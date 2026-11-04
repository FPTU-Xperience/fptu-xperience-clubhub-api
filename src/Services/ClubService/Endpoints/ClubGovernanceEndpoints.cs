using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Messaging;
using ClubReportHub.Shared.Rosters;
using ClubService.Data;
using ClubService.Extensions;
using ClubService.Models;
using ClubService.Services;
using Microsoft.EntityFrameworkCore;

namespace ClubService.Endpoints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GovernanceInitializeInput(Guid ExpectedClubVersion, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GovernanceGroupInput(Guid ExpectedVersion, string Reason, string? Name = null, string? Description = null,
    string? PresetCode = null, string[]? PermissionCodes = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GovernanceDecisionInput(Guid ExpectedVersion, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GovernanceAssignmentInput(Guid ExpectedVersion, string Reason, string Operation, Guid GroupId, int[] MembershipIds, Guid? FromGroupId = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GovernanceReportInput(Guid ExpectedVersion, string Reason, Guid SourceRevisionId, string ExpectedLeadershipDigest, Guid ExpectedClubVersion);

public static class ClubGovernanceEndpoints
{
    public static void MapClubGovernance(this IEndpointRouteBuilder app)
    {
        app.MapClubVice();
        var group = app.MapGroup("/api/clubs/{clubId:int}/governance").RequireAuthorization(AuthPolicies.BusinessAccess)
            .AddEndpointFilter<RosterExceptionFilter>().WithTags("Club governance");
        group.MapGet("/permission-catalog", Catalog);
        group.MapGet("/eligible-members", EligibleMembers);
        group.MapGet("/history", History);
        group.MapGet("/terms/{semesterId:guid}", Read);
        group.MapGet("/terms/{semesterId:guid}/eligible-members", EligibleMembers);
        group.MapPost("/terms/{semesterId:guid}/initialize", Initialize);
        group.MapPost("/terms/{semesterId:guid}/groups", CreateGroup);
        group.MapPatch("/terms/{semesterId:guid}/groups/{groupId:guid}", UpdateGroup);
        group.MapPost("/terms/{semesterId:guid}/groups/{groupId:guid}/archive", ArchiveGroup);
        group.MapPost("/terms/{semesterId:guid}/assignment-changes", Assign);
        group.MapGet("/terms/{semesterId:guid}/revisions", Revisions);
        group.MapGet("/terms/{semesterId:guid}/revisions/{revisionId:guid}", Revision);
        group.MapGet("/terms/{semesterId:guid}/report-submissions", Reports);
        group.MapGet("/terms/{semesterId:guid}/report-submissions/{submissionId:guid}", Report);
        group.MapPost("/terms/{semesterId:guid}/report-submissions", SubmitReport);
    }
    private static DateTimeOffset Now(HttpContext http) => http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
    private static bool Reason(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 500;
    private static string Digest(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, ClubGovernancePolicy.Json))));
    private static async Task<object> LegacyTreasurers(int clubId, ClubDbContext db, CancellationToken ct) =>
        await db.ClubMemberships.AsNoTracking().Where(x => x.ClubId == clubId && x.Status == ClubMembershipStatuses.Approved && x.Role == ClubMemberRoles.Treasurer)
            .OrderBy(x => x.UserId).Select(x => new { membershipId = x.Id, x.UserId, displayName = x.FullName, source = "LEGACY_ROLE" }).ToArrayAsync(ct);
    private static IResult Error(int status, string code, string message) => ApiOperations.Error(status, code, message);
    private static async Task<bool> Active(HttpContext http, CancellationToken ct)
    {
        var users = await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([http.User.GetUserId()], ct);
        return users.TryGetValue(http.User.GetUserId(), out var actor) && actor is { IsActive: true, IsLocked: false };
    }
    private static async Task<bool> Manage(int clubId, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!await Active(http, ct)) return false;
        if (http.User.IsSuperAdmin() || await db.UserOwnsClubAsync(clubId, http.User.GetUserId(), ct)) return true;
        if (!await ClubGovernancePolicy.IsVice(db, clubId, http.User.GetUserId(), Now(http), ct)) return false;
        var accounts = await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([http.User.GetUserId()], ct);
        var campus = await db.Clubs.Where(x => x.Id == clubId).Select(x => x.CampusCode).SingleAsync(ct);
        return campus == CampusCodes.Global || accounts[http.User.GetUserId()].CampusCode == campus;
    }
    private static async Task<Club?> Visible(int clubId, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        var accounts = await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([http.User.GetUserId()], ct);
        if (!accounts.TryGetValue(http.User.GetUserId(), out var account) || account is not { IsActive: true, IsLocked: false }) return null;
        var club = await db.Clubs.SingleOrDefaultAsync(x => x.Id == clubId && x.IsActive && x.DeletedAtUtc == null, ct);
        if (club is null) return null;
        if (http.User.IsSuperAdmin() || (http.User.IsStudentAffairsAdministrator() && account.CampusCode == club.CampusCode)
            || await db.UserOwnsClubAsync(clubId, http.User.GetUserId(), ct)
            || await db.ClubMemberships.AnyAsync(x => x.ClubId == clubId && x.UserId == http.User.GetUserId() && x.Status == ClubMembershipStatuses.Approved, ct)) return club;
        return null;
    }
    private static async Task<WorkspaceSemesterMetadata?> Period(Guid semesterId, HttpContext http, CancellationToken ct) =>
        (await http.RequestServices.GetRequiredService<WorkspaceCatalogClient>().ReadAsync(http, ct)).Semesters.SingleOrDefault(x => x.Id == semesterId);
    private static async Task<IResult> Catalog(int clubId, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (await Visible(clubId, db, http, ct) is null) return Results.Forbid();
        var owner = await db.UserOwnsClubAsync(clubId, http.User.GetUserId(), ct);
        return Results.Ok(new
        {
            clubId,
            actorId = http.User.GetUserId(),
            version = ClubGovernancePolicy.CatalogVersion,
            permissions = ClubGovernancePolicy.Delegable.Select(code => new { code, delegable = true, dutyCode = ClubGovernancePolicy.FinancialDuty([code]) ? "TREASURER" : null }),
            presets = ClubGovernancePolicy.Presets.Select(p => new { code = p.Key, permissionCodes = p.Value }),
            constraints = new { maxTreasurers = 2, ownerCanBeTreasurer = false, multipleGroupsPerMember = true },
            unsupportedCapabilities = new[] { "club.declarations.process" },
            capabilities = new { canManage = await Manage(clubId, db, http, ct), canNominateVice = owner, canTransferOwner = owner }
        });
    }
    private static async Task<object> Leadership(int clubId, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        var owners = await db.ClubManagerAssignments.AsNoTracking().Where(x => x.ClubId == clubId && x.IsActive)
            .OrderBy(x => x.ManagerUserId)
            .Select(x => new { userId = x.ManagerUserId, displayName = x.ManagerName }).ToArrayAsync(ct);
        var now = Now(http);
        var vice = await db.Set<ClubViceAppointment>().AsNoTracking().Where(x => x.ClubId == clubId && x.Status == "APPROVED" && x.From <= now && now < x.Until
            && db.ClubMemberships.Any(m => m.Id == x.MembershipId && m.UserId == x.UserId && m.Status == ClubMembershipStatuses.Approved))
            .OrderBy(x => x.UserId).ThenBy(x => x.Id).Select(x => new { appointmentId = x.Id, x.MembershipId, x.UserId, displayName = x.MemberName, effectiveFromUtc = x.From, effectiveUntilUtc = x.Until }).ToArrayAsync(ct);
        return new { owners, vicePresidents = vice };
    }
    private static async Task<IResult> Read(int clubId, Guid semesterId, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var club = await Visible(clubId, db, http, ct); if (club is null) return Results.Forbid();
        var period = await Period(semesterId, http, ct); if (period is null) return Results.NotFound();
        var term = await db.Set<ClubGovernanceTerm>().AsNoTracking().SingleOrDefaultAsync(x => x.ClubId == clubId && x.SemesterId == semesterId, ct);
        var revision = term is null ? null : await db.Set<ClubGovernanceRevision>().AsNoTracking().SingleAsync(x => x.Id == term.RevisionId && x.TermId == term.Id, ct);
        var latest = term is null ? null : await db.Set<ClubGovernanceReport>().AsNoTracking().Where(x => x.TermId == term.Id).OrderByDescending(x => x.Number).FirstOrDefaultAsync(ct);
        var canManage = await Manage(clubId, db, http, ct);
        var leadership = await Leadership(clubId, db, http, ct);
        var legacyTreasurers = await LegacyTreasurers(clubId, db, ct);
        var legacyChanged = latest is not null && (!JsonSerializer.Deserialize<JsonElement>(latest.SnapshotJson).TryGetProperty("legacyTreasurers", out var capturedLegacy)
            || capturedLegacy.GetRawText() != JsonSerializer.Serialize(legacyTreasurers, ClubGovernancePolicy.Json));
        return Results.Ok(new
        {
            clubId,
            actorId = http.User.GetUserId(),
            semesterId,
            semesterCode = period.Code,
            semester = period,
            clubVersion = club.ConcurrencyToken,
            initialized = term is not null,
            termId = term?.Id,
            version = term?.Version,
            revisionId = term?.RevisionId,
            sequence = term?.Sequence,
            configuration = revision is null ? null : ClubGovernancePolicy.Parse(revision),
            leadership,
            legacyTreasurers,
            leadershipDigest = Digest(leadership),
            reportNeedsUpdate = latest is not null && (latest.SourceRevisionId != term!.RevisionId || latest.LeadershipDigest != Digest(leadership) || legacyChanged),
            capabilities = new
            {
                canConfigure = canManage && ClubGovernancePolicy.Active(period, Now(http)),
                canReadHistory = true,
                canSubmitReport = canManage && term is not null && ClubGovernancePolicy.Active(period, Now(http))
            }
        });
    }
    private static async Task<IResult> EligibleMembers(int clubId, Guid? semesterId, string? search, int? page, int? pageSize, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (await Visible(clubId, db, http, ct) is null || !await Manage(clubId, db, http, ct)) return Results.Forbid();
        if (semesterId.HasValue && await Period(semesterId.Value, http, ct) is null) return Results.NotFound();
        var club = await db.Clubs.AsNoTracking().SingleAsync(x => x.Id == clubId, ct);
        var memberships = await db.ClubMemberships.AsNoTracking().Where(x => x.ClubId == clubId && x.Status == ClubMembershipStatuses.Approved).ToArrayAsync(ct);
        var profiles = await ClubGovernancePolicy.Profiles(memberships.Select(x => x.UserId), http, ct);
        var eligible = memberships.Where(m => profiles.TryGetValue(m.UserId, out var p) && p is { IsActive: true, IsLocked: false }
            && (club.CampusCode == CampusCodes.Global || p.CampusCode == club.CampusCode))
            .Select(m => new { memberId = m.Id, m.UserId, displayName = profiles[m.UserId].DisplayName, version = m.ConcurrencyToken })
            .Where(x => string.IsNullOrWhiteSpace(search) || x.displayName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.displayName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.memberId).ToArray();
        var current = Math.Clamp(page ?? 1, 1, 1_000_000); var size = Math.Clamp(pageSize ?? 20, 1, 100);
        return Results.Ok(new
        {
            clubId,
            semesterId,
            items = eligible.Skip((current - 1) * size).Take(size),
            page = current,
            pageSize = size,
            totalItems = eligible.Length,
            totalPages = (int)Math.Ceiling(eligible.Length / (double)size)
        });
    }
    internal static async Task<ClubGovernanceRevision> Append(ClubGovernanceTerm term, GovernanceConfiguration before, GovernanceConfiguration after,
        string action, string reason, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        var users = await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([http.User.GetUserId()], ct);
        var revision = new ClubGovernanceRevision
        {
            TermId = term.Id,
            Sequence = ++term.Sequence,
            PreviousId = term.RevisionId == Guid.Empty ? null : term.RevisionId,
            ActorId = http.User.GetUserId(),
            ActorName = users.GetValueOrDefault(http.User.GetUserId())?.DisplayName ?? "",
            Action = action,
            Reason = reason.Trim(),
            At = Now(http),
            ConfigurationJson = JsonSerializer.Serialize(after, ClubGovernancePolicy.Json),
            MemberChangesJson = JsonSerializer.Serialize(new { before = ClubGovernancePolicy.Members(before), after = ClubGovernancePolicy.Members(after) }, ClubGovernancePolicy.Json)
        };
        term.RevisionId = revision.Id; term.Version = Guid.NewGuid(); db.Add(revision);
        var beforeGroups = before.Groups.ToDictionary(x => x.Id); var afterGroups = after.Groups.ToDictionary(x => x.Id);
        revision.AffectedGroupIdsJson = JsonSerializer.Serialize(beforeGroups.Keys.Concat(afterGroups.Keys).Distinct().Where(id =>
            !beforeGroups.ContainsKey(id) || !afterGroups.ContainsKey(id) || JsonSerializer.Serialize(beforeGroups[id], ClubGovernancePolicy.Json) != JsonSerializer.Serialize(afterGroups[id], ClubGovernancePolicy.Json)).Order().ToArray());
        static Dictionary<int, (GovernanceMemberSnapshot Member, string Json)> Snapshots(GovernanceConfiguration configuration) =>
            ClubGovernancePolicy.Members(configuration).ToDictionary(m => m.MembershipId, m => (m, JsonSerializer.Serialize(new
            {
                m.MembershipId,
                m.UserId,
                m.DisplayName,
                m.Permissions,
                groups = configuration.Groups.Where(g => m.Groups.Contains(g.Id)).OrderBy(g => g.Id).Select(g => new { g.Id, g.Name, g.PermissionCodes })
            }, ClubGovernancePolicy.Json)));
        var previousMembers = Snapshots(before); var nextMembers = Snapshots(after);
        foreach (var id in previousMembers.Keys.Concat(nextMembers.Keys).Distinct())
        {
            var previous = previousMembers.GetValueOrDefault(id); var next = nextMembers.GetValueOrDefault(id);
            if (previous.Json == next.Json) continue;
            var member = next.Member ?? previous.Member;
            db.Add(new ClubGovernanceMemberChange
            {
                RevisionId = revision.Id,
                MembershipId = id,
                UserId = member.UserId,
                DisplayName = member.DisplayName,
                BeforeJson = previous.Json,
                AfterJson = next.Json
            });
        }
        var affected = ClubGovernancePolicy.Members(before).Select(x => x.UserId).Concat(ClubGovernancePolicy.Members(after).Select(x => x.UserId)).Distinct().ToArray();
        if (affected.Length > 0) db.AddOutboxMessage(new ClubAccessInvalidatedEvent(Guid.NewGuid(), Now(http), term.ClubId, affected), EventRoutingKeys.ClubAccessInvalidated);
        return revision;
    }
    private static async Task<IResult> Initialize(int clubId, Guid semesterId, GovernanceInitializeInput input, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!await Manage(clubId, db, http, ct)) return Results.Forbid();
        if (!Reason(input.Reason)) return Error(400, "REASON_REQUIRED", "Provide a reason up to 500 characters.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            var club = await Visible(clubId, db, http, ct); var period = await Period(semesterId, http, ct);
            if (club is null || !await Manage(clubId, db, http, ct)) return Error(403, "GOVERNANCE_AUTHORITY_REQUIRED", "Current leadership is required.");
            if (period is null || !ClubGovernancePolicy.Active(period, Now(http))) return Error(409, "TERM_NOT_CONFIGURABLE", "Select the authoritative current editable semester.");
            if (club.ConcurrencyToken != input.ExpectedClubVersion || await db.Set<ClubGovernanceTerm>().AnyAsync(x => x.ClubId == clubId && x.SemesterId == semesterId, ct))
                return Error(409, "STALE_GOVERNANCE", "Reload current governance and club version.");
            var term = new ClubGovernanceTerm { ClubId = clubId, SemesterId = semesterId }; db.Add(term);
            var configuration = ClubGovernancePolicy.Defaults(period);
            await Append(term, configuration with { Groups = [] }, configuration, "TERM_INITIALIZED", input.Reason, db, http, ct);
            club.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(ct);
            return Results.Ok(new { clubId, semesterId, termId = term.Id, version = term.Version, revisionId = term.RevisionId });
        }, ct);
    }
    private sealed class MutationRejected(int status, string code, string message) : Exception(message)
    { public int Status { get; } = status; public string Code { get; } = code; }
    private static void Reject(int status, string code, string message) => throw new MutationRejected(status, code, message);
    private static async Task<IResult> Mutate(int clubId, Guid semesterId, Guid expectedVersion, string reason, object input, string action,
        Func<GovernanceConfiguration, Task<GovernanceConfiguration>> change, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!await Manage(clubId, db, http, ct)) return Results.Forbid();
        if (!Reason(reason)) return Error(400, "REASON_REQUIRED", "Provide a reason up to 500 characters.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            var club = await Visible(clubId, db, http, ct); var period = await Period(semesterId, http, ct);
            if (club is null || !await Manage(clubId, db, http, ct)) return Error(403, "GOVERNANCE_AUTHORITY_REQUIRED", "Current leadership is required.");
            if (period is null || !ClubGovernancePolicy.Active(period, Now(http))) return Error(409, "TERM_NOT_CONFIGURABLE", "Only the authoritative current semester can grant rights.");
            var term = await db.Set<ClubGovernanceTerm>().SingleOrDefaultAsync(x => x.ClubId == clubId && x.SemesterId == semesterId, ct);
            if (term is null || term.Version != expectedVersion) return Error(409, "STALE_GOVERNANCE", "Reload the current term revision.");
            var revision = await db.Set<ClubGovernanceRevision>().AsNoTracking().SingleAsync(x => x.Id == term.RevisionId && x.TermId == term.Id, ct);
            var before = ClubGovernancePolicy.Parse(revision);
            GovernanceConfiguration after;
            try
            {
                after = (await change(before)) with { StartsAt = period.StartUtc!.Value, EndsAt = period.EndUtc!.Value };
                var groups = after.Groups.Where(x => !x.Archived).ToArray();
                if (groups.Length > 50 || groups.Select(x => x.Name.Trim().ToUpperInvariant()).Distinct().Count() != groups.Length)
                    Reject(400, "INVALID_GROUP_NAMES", "Use unique group names and at most 50 active groups.");
                var memberships = groups.Where(x => !x.Protected).SelectMany(x => x.Assignments).Select(x => x.MembershipId).Distinct().ToArray();
                var eligible = await db.ClubMemberships.Where(x => x.ClubId == clubId && memberships.Contains(x.Id) && x.Status == ClubMembershipStatuses.Approved).ToDictionaryAsync(x => x.Id, ct);
                after = after with
                {
                    Groups = after.Groups.Select(g => g.Protected ? g : g with
                    { Assignments = g.Assignments.Where(a => eligible.TryGetValue(a.MembershipId, out var member) && member.UserId == a.UserId).ToArray() }).ToArray()
                };
                groups = after.Groups.Where(x => !x.Archived).ToArray();
                var holders = groups.Where(x => !x.Protected && ClubGovernancePolicy.FinancialDuty(x.PermissionCodes)).SelectMany(x => x.Assignments).Select(x => x.UserId)
                    .Concat(await db.ClubMemberships.Where(x => x.ClubId == clubId && x.Status == ClubMembershipStatuses.Approved && x.Role == ClubMemberRoles.Treasurer).Select(x => x.UserId).ToArrayAsync(ct)).Distinct().ToArray();
                if (holders.Length > 2) Reject(409, "TREASURER_LIMIT", "A club can have at most two unique treasurer duty holders across groups and legacy roles.");
                if (await db.ClubManagerAssignments.AnyAsync(x => x.ClubId == clubId && x.IsActive && holders.Contains(x.ManagerUserId), ct))
                    Reject(409, "OWNER_PROTECTED", "The owner cannot also hold treasurer duty.");
            }
            catch (MutationRejected error) { return Error(error.Status, error.Code, error.Message); }
            await Append(term, before, after, action, reason, db, http, ct);
            club.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(ct);
            return Results.Ok(new { clubId, semesterId, termId = term.Id, version = term.Version, revisionId = term.RevisionId, configuration = after });
        }, ct);
    }
    private static GovernanceGroup GroupDraft(GovernanceGroupInput input, GovernanceGroup? previous = null)
    {
        var name = (input.Name ?? previous?.Name ?? "").Trim(); var description = (input.Description ?? previous?.Description ?? "").Trim();
        var preset = input.PresetCode ?? previous?.PresetCode ?? "CUSTOM";
        var codes = input.PermissionCodes ?? (input.PresetCode is not null && ClubGovernancePolicy.Presets.TryGetValue(preset, out var defaults) ? defaults : previous?.PermissionCodes ?? []);
        if (name.Length is < 1 or > 80 || description.Length > 300 || !ClubGovernancePolicy.Presets.ContainsKey(preset)
            || codes.Length > ClubGovernancePolicy.Delegable.Length || codes.Distinct().Count() != codes.Length || codes.Any(x => !ClubGovernancePolicy.Delegable.Contains(x)))
            Reject(400, "INVALID_GROUP", "Provide a valid name, preset and allowed specialist permissions.");
        return new(previous?.Id ?? Guid.NewGuid(), name, description, preset, ClubGovernancePolicy.FinancialDuty(codes) ? "TREASURER" : null,
            false, codes.Order(StringComparer.Ordinal).ToArray(), previous?.Assignments ?? []);
    }
    private static GovernanceGroup EditableGroup(GovernanceConfiguration config, Guid groupId)
    {
        var group = config.Groups.SingleOrDefault(x => x.Id == groupId && !x.Archived);
        if (group is null) Reject(404, "GROUP_NOT_FOUND", "The group does not belong to this term.");
        if (group!.Protected) Reject(403, "LEADERSHIP_PROTECTED", "Use CTSV-approved leadership workflows.");
        return group;
    }
    private static Task<IResult> CreateGroup(int clubId, Guid semesterId, GovernanceGroupInput input, ClubDbContext db, HttpContext http, CancellationToken ct) =>
        Mutate(clubId, semesterId, input.ExpectedVersion, input.Reason, input, "GROUP_CREATED", config =>
            Task.FromResult(config with { Groups = [.. config.Groups, GroupDraft(input)] }), db, http, ct);
    private static Task<IResult> UpdateGroup(int clubId, Guid semesterId, Guid groupId, GovernanceGroupInput input, ClubDbContext db, HttpContext http, CancellationToken ct) =>
        Mutate(clubId, semesterId, input.ExpectedVersion, input.Reason, input, "GROUP_UPDATED", config =>
        {
            var group = EditableGroup(config, groupId); var updated = GroupDraft(input, group);
            return Task.FromResult(config with { Groups = config.Groups.Select(x => x.Id == groupId ? updated : x).ToArray() });
        }, db, http, ct);
    private static Task<IResult> ArchiveGroup(int clubId, Guid semesterId, Guid groupId, GovernanceDecisionInput input, ClubDbContext db, HttpContext http, CancellationToken ct) =>
        Mutate(clubId, semesterId, input.ExpectedVersion, input.Reason, input, "GROUP_ARCHIVED", config =>
        {
            var group = EditableGroup(config, groupId);
            return Task.FromResult(config with { Groups = config.Groups.Select(x => x.Id == group.Id ? x with { Archived = true, Assignments = [] } : x).ToArray() });
        }, db, http, ct);
    private static Task<IResult> Assign(int clubId, Guid semesterId, GovernanceAssignmentInput input, ClubDbContext db, HttpContext http, CancellationToken ct) =>
        Mutate(clubId, semesterId, input.ExpectedVersion, input.Reason, input, "ASSIGNMENT_" + input.Operation, async config =>
        {
            var target = EditableGroup(config, input.GroupId);
            if (input.MembershipIds is null || input.MembershipIds.Length is < 1 or > 100 || input.MembershipIds.Distinct().Count() != input.MembershipIds.Length
                || input.Operation is not ("ADD" or "REMOVE" or "TRANSFER")) Reject(400, "INVALID_ASSIGNMENT_CHANGE", "Use an explicit operation and unique membership IDs.");
            var source = input.Operation == "TRANSFER" ? EditableGroup(config, input.FromGroupId ?? Guid.Empty) : target;
            if (input.Operation == "REMOVE")
            {
                if (input.MembershipIds!.Any(id => !source.Assignments.Any(a => a.MembershipId == id))) Reject(409, "ASSIGNMENT_CHANGED", "Reload the source assignments.");
                return config with { Groups = config.Groups.Select(g => g.Id == target.Id ? g with { Assignments = g.Assignments.Where(a => !input.MembershipIds!.Contains(a.MembershipId)).ToArray() } : g).ToArray() };
            }
            var members = await db.ClubMemberships.Where(x => x.ClubId == clubId && input.MembershipIds!.Contains(x.Id) && x.Status == ClubMembershipStatuses.Approved).ToArrayAsync(ct);
            if (members.Length != input.MembershipIds!.Length) Reject(409, "MEMBERSHIP_CHANGED", "Only approved club memberships can receive assignments.");
            var profiles = await ClubGovernancePolicy.Profiles(members.Select(x => x.UserId), http, ct);
            var campus = await db.Clubs.Where(x => x.Id == clubId).Select(x => x.CampusCode).SingleAsync(ct);
            if (members.Any(m => !profiles.TryGetValue(m.UserId, out var p) || p is not { IsActive: true, IsLocked: false }
                || (campus != CampusCodes.Global && p.CampusCode != campus))) Reject(409, "MEMBERSHIP_CHANGED", "Select active eligible club members.");
            if (input.Operation == "TRANSFER" && source.Id == target.Id) Reject(400, "INVALID_TRANSFER", "Choose a different source group.");
            if (input.Operation is "REMOVE" or "TRANSFER" && members.Any(m => !source.Assignments.Any(a => a.MembershipId == m.Id))) Reject(409, "ASSIGNMENT_CHANGED", "Reload the source assignments.");
            if (input.Operation is "ADD" or "TRANSFER" && members.Any(m => target.Assignments.Any(a => a.MembershipId == m.Id))) Reject(409, "ASSIGNMENT_EXISTS", "This membership already belongs to the target group.");
            return config with
            {
                Groups = config.Groups.Select(g =>
            {
                var assignments = g.Assignments;
                if (g.Id == source.Id && input.Operation is "REMOVE" or "TRANSFER") assignments = assignments.Where(a => !input.MembershipIds.Contains(a.MembershipId)).ToArray();
                if (g.Id == target.Id && input.Operation is "ADD" or "TRANSFER") assignments = [.. assignments, .. members.Select(m => new GovernanceAssignment(Guid.NewGuid(), m.Id, m.UserId, m.FullName))];
                return g with { Assignments = assignments };
            }).ToArray()
            };
        }, db, http, ct);
    private static async Task<IResult> Revisions(int clubId, Guid semesterId, int? page, int? pageSize, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store"; if (await Visible(clubId, db, http, ct) is null) return Results.Forbid();
        var term = await db.Set<ClubGovernanceTerm>().AsNoTracking().SingleOrDefaultAsync(x => x.ClubId == clubId && x.SemesterId == semesterId, ct);
        var query = db.Set<ClubGovernanceRevision>().AsNoTracking().Where(x => term != null && x.TermId == term.Id);
        var total = await query.CountAsync(ct); var current = Math.Clamp(page ?? 1, 1, 1_000_000); var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var items = await query.OrderByDescending(x => x.Sequence).Skip((current - 1) * size).Take(size)
            .Select(x => new { x.Id, x.Sequence, x.PreviousId, x.ActorId, x.ActorName, x.Action, x.Reason, x.At, x.MemberChangesJson }).ToArrayAsync(ct);
        return Results.Ok(new { clubId, semesterId, items, page = current, pageSize = size, totalItems = total, totalPages = (int)Math.Ceiling(total / (double)size) });
    }
    private static async Task<IResult> History(int clubId, Guid? semesterId, int? membershipId, Guid? groupId, string? eventType,
        DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize, bool? includeUnmapped, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (await Visible(clubId, db, http, ct) is null) return Results.Forbid();
        if (from > to || eventType?.Length > 50 || membershipId is <= 0) return Error(400, "INVALID_HISTORY_FILTER", "Use valid history filters and date ranges.");
        var query = db.Set<ClubGovernanceRevision>().AsNoTracking().Where(r => db.Set<ClubGovernanceTerm>().Any(t => t.Id == r.TermId && t.ClubId == clubId && (!semesterId.HasValue || t.SemesterId == semesterId)));
        if (membershipId.HasValue) query = query.Where(r => db.Set<ClubGovernanceMemberChange>().Any(c => c.RevisionId == r.Id && c.MembershipId == membershipId));
        if (groupId.HasValue) { var needle = "\"" + groupId.Value.ToString() + "\""; query = query.Where(r => r.AffectedGroupIdsJson.Contains(needle)); }
        if (!string.IsNullOrWhiteSpace(eventType)) query = query.Where(x => x.Action == eventType);
        if (from.HasValue) query = query.Where(x => x.At >= from); if (to.HasValue) query = query.Where(x => x.At <= to);
        var global = db.Set<ClubGovernanceAuditEvent>().AsNoTracking().Where(x => x.ClubId == clubId && x.RevisionId == null && (!semesterId.HasValue || includeUnmapped == true) && !groupId.HasValue);
        if (membershipId.HasValue) global = global.Where(x => x.MembershipId == membershipId);
        if (!string.IsNullOrWhiteSpace(eventType)) global = global.Where(x => x.Action == eventType);
        if (from.HasValue) global = global.Where(x => x.At >= from); if (to.HasValue) global = global.Where(x => x.At <= to);
        var combined = query.Select(x => new { x.Id, Source = "REVISION", TermId = (Guid?)x.TermId, x.Sequence, x.Action, x.ActorId, x.ActorName, x.At, x.Reason, PayloadJson = "{}" })
            .Concat(global.Select(x => new { x.Id, x.Source, TermId = (Guid?)null, Sequence = 0, x.Action, x.ActorId, x.ActorName, x.At, x.Reason, x.PayloadJson }));
        var total = await combined.CountAsync(ct); var current = Math.Clamp(page ?? 1, 1, 1_000_000); var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var rows = await combined.OrderByDescending(x => x.At).ThenBy(x => x.Source).ThenByDescending(x => x.Sequence).ThenBy(x => x.Id).Skip((current - 1) * size).Take(size).ToArrayAsync(ct);
        var ids = rows.Where(x => x.Source == "REVISION").Select(x => x.Id).ToArray();
        var changes = await db.Set<ClubGovernanceMemberChange>().AsNoTracking().Where(x => ids.Contains(x.RevisionId) && (!membershipId.HasValue || x.MembershipId == membershipId)).ToArrayAsync(ct);
        return Results.Ok(new
        {
            clubId,
            semesterId,
            items = rows.Select(r => new
            {
                r.Id,
                r.Source,
                r.TermId,
                r.Sequence,
                r.Action,
                r.ActorId,
                r.ActorName,
                r.At,
                r.Reason,
                details = r.Source == "REVISION" ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(r.PayloadJson),
                memberChanges = changes.Where(c => c.RevisionId == r.Id).Select(c => new
                {
                    c.MembershipId,
                    c.UserId,
                    c.DisplayName,
                    before = c.BeforeJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(c.BeforeJson),
                    after = c.AfterJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(c.AfterJson)
                })
            }),
            page = current,
            pageSize = size,
            totalItems = total,
            totalPages = (int)Math.Ceiling(total / (double)size)
        });
    }
    private static async Task<IResult> Revision(int clubId, Guid semesterId, Guid revisionId, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store"; if (await Visible(clubId, db, http, ct) is null) return Results.Forbid();
        var row = await db.Set<ClubGovernanceRevision>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == revisionId
            && db.Set<ClubGovernanceTerm>().Any(t => t.Id == x.TermId && t.ClubId == clubId && t.SemesterId == semesterId), ct);
        return row is null ? Results.NotFound() : Results.Ok(new { clubId, semesterId, revision = row, configuration = ClubGovernancePolicy.Parse(row) });
    }
    private static async Task<IResult> Reports(int clubId, Guid semesterId, int? page, int? pageSize, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store"; if (await Visible(clubId, db, http, ct) is null) return Results.Forbid();
        var query = db.Set<ClubGovernanceReport>().AsNoTracking().Where(x => db.Set<ClubGovernanceTerm>().Any(t => t.Id == x.TermId && t.ClubId == clubId && t.SemesterId == semesterId));
        var total = await query.CountAsync(ct); var current = Math.Clamp(page ?? 1, 1, 1_000_000); var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var items = await query.OrderByDescending(x => x.Number).Skip((current - 1) * size).Take(size)
            .Select(x => new { x.Id, x.Number, x.SourceRevisionId, x.ActorId, x.At, x.Reason }).ToArrayAsync(ct);
        return Results.Ok(new { clubId, semesterId, items, page = current, pageSize = size, totalItems = total, totalPages = (int)Math.Ceiling(total / (double)size) });
    }
    private static async Task<IResult> Report(int clubId, Guid semesterId, Guid submissionId, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store"; if (await Visible(clubId, db, http, ct) is null) return Results.Forbid();
        var row = await db.Set<ClubGovernanceReport>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == submissionId
            && db.Set<ClubGovernanceTerm>().Any(t => t.Id == x.TermId && t.ClubId == clubId && t.SemesterId == semesterId), ct);
        return row is null ? Results.NotFound() : Results.Ok(new { clubId, semesterId, submission = row });
    }
    private static async Task<IResult> SubmitReport(int clubId, Guid semesterId, GovernanceReportInput input, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!await Manage(clubId, db, http, ct)) return Results.Forbid();
        if (!Reason(input.Reason)) return Error(400, "REASON_REQUIRED", "Provide a reason up to 500 characters.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            var club = await Visible(clubId, db, http, ct); var period = await Period(semesterId, http, ct);
            if (club is null || !await Manage(clubId, db, http, ct)) return Error(403, "GOVERNANCE_AUTHORITY_REQUIRED", "Current leadership is required.");
            if (period is null || !ClubGovernancePolicy.Active(period, Now(http))) return Error(409, "TERM_NOT_CONFIGURABLE", "Reporting is available only in the current editable term.");
            var term = await db.Set<ClubGovernanceTerm>().SingleOrDefaultAsync(x => x.ClubId == clubId && x.SemesterId == semesterId, ct);
            if (term is null || term.Version != input.ExpectedVersion || term.RevisionId != input.SourceRevisionId) return Error(409, "REPORT_SOURCE_CHANGED", "Reload and review the current configuration before submission.");
            if (club.ConcurrencyToken != input.ExpectedClubVersion) return Error(409, "REPORT_SOURCE_CHANGED", "Club authority changed after preview. Reload before submitting.");
            var revision = await db.Set<ClubGovernanceRevision>().AsNoTracking().SingleAsync(x => x.Id == term.RevisionId && x.TermId == term.Id, ct);
            var number = (await db.Set<ClubGovernanceReport>().Where(x => x.TermId == term.Id).Select(x => (int?)x.Number).MaxAsync(ct) ?? 0) + 1;
            var legacyTreasurers = await LegacyTreasurers(clubId, db, ct);
            var leadership = await Leadership(clubId, db, http, ct);
            if (input.ExpectedLeadershipDigest != Digest(leadership)) return Error(409, "REPORT_SOURCE_CHANGED", "Leadership changed after preview. Reload before submitting the board report.");
            var report = new ClubGovernanceReport
            {
                TermId = term.Id,
                Number = number,
                SourceRevisionId = revision.Id,
                ActorId = http.User.GetUserId(),
                At = Now(http),
                Reason = input.Reason.Trim(),
                LeadershipDigest = Digest(leadership),
                SnapshotJson = JsonSerializer.Serialize(new { configuration = ClubGovernancePolicy.Parse(revision), leadership, legacyTreasurers, catalogVersion = ClubGovernancePolicy.CatalogVersion }, ClubGovernancePolicy.Json)
            };
            db.Add(report); club.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/clubs/{clubId}/governance/terms/{semesterId}/report-submissions/{report.Id}", new { clubId, semesterId, report.Id, report.Number, status = "SUBMITTED", permissionsChanged = false });
        }, ct);
    }
}
