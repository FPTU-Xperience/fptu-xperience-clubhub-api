using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Messaging;
using ClubService.Data;
using ClubService.Extensions;
using ClubService.Models;
using ClubService.Services;
using Microsoft.EntityFrameworkCore;

namespace ClubService.Endpoints;
public static class ClubRoleEndpoints
{
    public static void MapClubRoleEndpoints(this WebApplication app)
    {
        app.MapClubGovernance();
        var routes = app.MapGroup("/api/clubs/{clubId:int}/roles").RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Club roles");
        routes.MapGet("", ReadAsync);
        routes.MapPatch("/{memberId:int}", WriteAsync);
    }
    private static async Task<bool> CanManage(int clubId, HttpContext http, ClubDbContext db, CancellationToken ct) =>
        http.User.IsSuperAdmin() || await ClubGovernancePolicy.HasLeadership(db, clubId, http, ct);
    private static async Task<IResult> ReadAsync(int clubId, int? page, int? pageSize, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var club = await db.Clubs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == clubId && x.IsActive, ct);
        if (club is null) return Results.NotFound();
        var canManage = await CanManage(clubId, http, db, ct);
        var reviewer = http.User.IsStudentAffairsAdministrator() && (http.User.IsSuperAdmin() || http.User.GetCampus() == club.CampusCode);
        if (!canManage && !reviewer && !await db.ClubMemberships.AnyAsync(x => x.ClubId == clubId && x.UserId == http.User.GetUserId() && x.Status == ClubMembershipStatuses.Approved, ct)) return Results.Forbid();
        var owners = await db.ClubManagerAssignments.AsNoTracking().Where(x => x.ClubId == clubId && x.IsActive).Select(x => x.ManagerUserId).ToArrayAsync(ct);
        var legacyTreasurers = await db.ClubMemberships.Where(x => x.ClubId == clubId && x.Status == ClubMembershipStatuses.Approved && x.Role == ClubMemberRoles.Treasurer).Select(x => x.UserId).ToArrayAsync(ct);
        var groupTreasurers = await ClubGovernancePolicy.GroupTreasurers(db, clubId, http, ct);
        var groupGovernance = await db.Set<ClubGovernanceTerm>().AnyAsync(x => x.ClubId == clubId, ct);
        var count = legacyTreasurers.Concat(groupTreasurers).Distinct().Count();
        var query = db.ClubMemberships.AsNoTracking().Where(x => x.ClubId == clubId && x.Status == ClubMembershipStatuses.Approved);
        var total = await query.CountAsync(ct); var currentPage = Math.Max(page ?? 1, 1); var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var rows = await query.OrderBy(x => x.FullName).ThenBy(x => x.Id).Skip((int)Math.Min((long)(currentPage - 1) * size, int.MaxValue)).Take(size).ToListAsync(ct);
        IReadOnlyDictionary<int, InternalUserProfile> profiles = new Dictionary<int, InternalUserProfile>();
        var profileClient = http.RequestServices.GetService<CurrentUserProfileClient>();
        if (profileClient is not null)
            try { profiles = await profileClient.GetAsync(rows.Select(row => row.UserId), ct); }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
            { return ApiOperations.Error(503, "PROFILE_SERVICE_UNAVAILABLE", "Current member display names are unavailable."); }
        return Results.Ok(new
        {
            clubId,
            version = club.ConcurrencyToken,
            groupGovernance,
            capabilities = new { canManageRoles = canManage },
            supportedRoles = new[] { "CLUB_OWNER", ClubMemberRoles.Treasurer, ClubMemberRoles.Member },
            treasurerLimit = 2,
            treasurerCount = count,
            items = rows.Select(row => new
            {
                memberId = row.Id,
                row.UserId,
                displayName = profiles.TryGetValue(row.UserId, out var profile) && !string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.DisplayName.Trim() : row.FullName,
                joinedAtUtc = row.ReviewedAtUtc ?? row.RequestedAtUtc,
                role = ClubMemberRoleRules.ResolveDisplayRole(row.Role, owners.Contains(row.UserId)),
                version = row.ConcurrencyToken,
                canAssignTreasurer = canManage && !groupGovernance && !owners.Contains(row.UserId) && row.Role != ClubMemberRoles.Treasurer && (count < 2 || groupTreasurers.Contains(row.UserId)),
                canRevokeTreasurer = canManage && !owners.Contains(row.UserId) && row.Role == ClubMemberRoles.Treasurer
            }),
            page = currentPage,
            pageSize = size,
            totalItems = total,
            totalPages = (int)Math.Ceiling((decimal)total / size)
        });
    }
    private static async Task<IResult> WriteAsync(int clubId, int memberId, JsonElement request, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!await CanManage(clubId, http, db, ct)) return Results.Forbid();
        if (request.ValueKind != JsonValueKind.Object || request.EnumerateObject().Count() != 4 || request.EnumerateObject().Any(x => x.Name is not ("role" or "expectedVersion" or "expectedClubVersion" or "reason"))
            || !request.TryGetProperty("role", out var roleValue) || roleValue.ValueKind != JsonValueKind.String || roleValue.GetString() is not (ClubMemberRoles.Member or ClubMemberRoles.Treasurer)
            || !request.TryGetProperty("expectedVersion", out var versionValue) || versionValue.ValueKind != JsonValueKind.String || !Guid.TryParse(versionValue.GetString(), out var version)
            || !request.TryGetProperty("expectedClubVersion", out var clubValue) || clubValue.ValueKind != JsonValueKind.String || !Guid.TryParse(clubValue.GetString(), out var clubVersion)
            || !request.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(reason.GetString()) || reason.GetString()!.Length > 1000)
            return ApiOperations.Error(400, "INVALID_ROLE_CHANGE", "Provide a supported single role, current member/club versions and a reason. Ownership uses the separate succession workflow.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            if (!await CanManage(clubId, http, db, ct)) return ApiOperations.Error(403, "ROLE_AUTHORITY_REQUIRED", "Current scoped leadership is required.");
            var club = await db.Clubs.SingleOrDefaultAsync(x => x.Id == clubId && x.IsActive, ct);
            var row = await db.ClubMemberships.SingleOrDefaultAsync(x => x.Id == memberId && x.ClubId == clubId && x.Status == ClubMembershipStatuses.Approved, ct);
            if (club is null || row is null) return ApiOperations.Error(404, "ROLE_MEMBER_NOT_FOUND", "The current membership does not belong to this club.");
            if (row.ConcurrencyToken != version || club.ConcurrencyToken != clubVersion) return ApiOperations.Error(409, "STALE_ROLE", "Reload the current club and member roles.");
            if (await db.ClubManagerAssignments.AnyAsync(x => x.ClubId == clubId && x.ManagerUserId == row.UserId && x.IsActive, ct))
                return ApiOperations.Error(409, "OWNER_PROTECTED", "The active owner cannot be demoted or removed; use approved succession so the club keeps its owner.");
            var target = roleValue.GetString()!;
            if (target == row.Role) return ApiOperations.Error(409, "ROLE_UNCHANGED", "This member already has that role.");
            var previous = row.Role;
            if (target == ClubMemberRoles.Treasurer)
            {
                if (await db.Set<ClubGovernanceTerm>().AnyAsync(x => x.ClubId == clubId, ct))
                    return ApiOperations.Error(409, "GROUP_ASSIGNMENT_REQUIRED", "Assign treasurer duty through the current governance term. Existing legacy duties may still be revoked here.");
                var slots = await db.ClubMemberships.Where(x => x.ClubId == clubId && x.Status == ClubMembershipStatuses.Approved && x.Role == ClubMemberRoles.Treasurer).Select(x => x.TreasurerSlot).ToArrayAsync(ct);
                if (slots.Length >= 2) return ApiOperations.Error(409, "TREASURER_LIMIT", "A club can have at most two treasurers.");
                var groupTreasurers = await ClubGovernancePolicy.GroupTreasurers(db, clubId, http, ct);
                var legacyTreasurers = await db.ClubMemberships.Where(x => x.ClubId == clubId && x.Status == ClubMembershipStatuses.Approved && x.Role == ClubMemberRoles.Treasurer).Select(x => x.UserId).ToArrayAsync(ct);
                if (groupTreasurers.Concat(legacyTreasurers).Append(row.UserId).Distinct().Count() > 2)
                    return ApiOperations.Error(409, "TREASURER_LIMIT", "A club can have at most two unique duty holders across groups and legacy roles.");
                row.TreasurerSlot = !slots.Contains(1) ? 1 : 2; ClubMemberRoleRules.ApplyTreasurerRole(row, null);
            }
            else ClubMemberRoleRules.ApplyMemberRole(row);
            var legacyAudit = ClubGovernanceAudit.Record(db, clubId, "LEGACY_ROLE", "LEGACY_ROLE_CHANGED", reason.GetString()!.Trim(),
                new { membershipId = row.Id, row.UserId, displayName = row.FullName, before = previous, after = row.Role }, http, row.Id, row.UserId);
            var currentGovernance = await ClubGovernancePolicy.Current(db, clubId, http, ct);
            if (currentGovernance is not null)
            {
                var term = await db.Set<ClubGovernanceTerm>().SingleAsync(x => x.ClubId == clubId && x.SemesterId == currentGovernance.SemesterId, ct);
                var audit = await ClubGovernanceEndpoints.Append(term, currentGovernance, currentGovernance, "LEGACY_ROLE_CHANGED", reason.GetString()!, db, http, ct);
                legacyAudit.RevisionId = audit.Id;
                audit.MemberChangesJson = JsonSerializer.Serialize(new
                {
                    legacyRoleChange = new { membershipId = row.Id, row.UserId, displayName = row.FullName, before = previous, after = row.Role },
                    groups = ClubGovernancePolicy.Members(currentGovernance)
                }, ClubGovernancePolicy.Json);
                db.Add(new ClubGovernanceMemberChange
                {
                    RevisionId = audit.Id,
                    MembershipId = row.Id,
                    UserId = row.UserId,
                    DisplayName = row.FullName,
                    BeforeJson = JsonSerializer.Serialize(new { legacyRole = previous }),
                    AfterJson = JsonSerializer.Serialize(new { legacyRole = row.Role })
                });
            }
            club.ConcurrencyToken = Guid.NewGuid();
            db.AddOutboxMessage(new ClubAccessInvalidatedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, clubId, [row.UserId]), EventRoutingKeys.ClubAccessInvalidated);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { clubId, memberId, row.UserId, previousRole = previous, role = row.Role, version = row.ConcurrencyToken, clubVersion = club.ConcurrencyToken, reason = reason.GetString()!.Trim() });
        }, ct);
    }
}
