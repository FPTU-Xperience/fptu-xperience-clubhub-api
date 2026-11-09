using System.Text.Json;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Auth;
using ClubService.Extensions;
using ClubService.Data;
using ClubService.Models;
using Microsoft.EntityFrameworkCore;

namespace ClubService.Services;

public sealed record GovernanceAssignment(Guid Id, int MembershipId, int UserId, string DisplayName, bool IsGroupLead = false);
public sealed record GovernanceGroup(Guid Id, string Name, string Description, string PresetCode, string? DutyCode,
    bool Protected, string[] PermissionCodes, GovernanceAssignment[] Assignments, bool Archived = false);
public sealed record GovernanceConfiguration(Guid SemesterId, string SemesterCode, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    GovernanceGroup[] Groups, string CatalogVersion = ClubGovernancePolicy.CatalogVersion);
public sealed record GovernanceMemberSnapshot(int MembershipId, int UserId, string DisplayName, Guid[] Groups, string[] Permissions);

public static class ClubGovernancePolicy
{
    public const string CatalogVersion = "CLUB_GROUPS_V1";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly string[] Delegable = ["club.profile.write", "club.media.write", "event.content.write", "event.staff.assign",
        "event.registrations.manage", "club.members.recruit", "club.reports.draft.write", "club.finance.read", "club.finance.draft.write", "club.finance.submit"];
    public static readonly IReadOnlyDictionary<string, string[]> Presets = new Dictionary<string, string[]>
    {
        ["CONTENT"] = ["club.profile.write", "club.media.write", "event.content.write"],
        ["EVENT"] = ["event.staff.assign", "event.registrations.manage"],
        ["HR"] = ["club.members.recruit", "club.profile.write"],
        ["SECRETARY"] = ["club.reports.draft.write"],
        ["TREASURER"] = ["club.finance.read", "club.finance.draft.write", "club.finance.submit"],
        ["CUSTOM"] = []
    };
    public static bool FinancialDuty(IEnumerable<string> codes) => codes.Any(x => x is "club.finance.draft.write" or "club.finance.submit");
    public static async Task<bool> HasLeadership(ClubDbContext db, int clubId, HttpContext http, CancellationToken ct)
    {
        var actor = http.User.GetUserId();
        if (await db.UserOwnsClubAsync(clubId, actor, ct)) return true;
        var now = http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        if (!await IsVice(db, clubId, actor, now, ct)) return false;
        var accounts = await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([actor], ct);
        var campus = await db.Clubs.Where(x => x.Id == clubId && x.IsActive && x.DeletedAtUtc == null).Select(x => x.CampusCode).SingleOrDefaultAsync(ct);
        return campus is not null && accounts.TryGetValue(actor, out var account) && account is { IsActive: true, IsLocked: false }
            && (campus == CampusCodes.Global || account.CampusCode == campus);
    }
    public static async Task<IReadOnlyDictionary<int, InternalUserProfile>> Profiles(IEnumerable<int> ids, HttpContext http, CancellationToken ct)
    {
        var result = new Dictionary<int, InternalUserProfile>();
        var client = http.RequestServices.GetRequiredService<CurrentUserProfileClient>();
        foreach (var batch in ids.Distinct().Chunk(100))
            foreach (var row in await client.GetAsync(batch, ct)) result.Add(row.Key, row.Value);
        return result;
    }
    public static GovernanceConfiguration Parse(ClubGovernanceRevision revision) =>
        JsonSerializer.Deserialize<GovernanceConfiguration>(revision.ConfigurationJson, Json) ?? throw new InvalidOperationException("Missing persisted governance configuration.");
    public static async Task<bool> CanPerform(ClubDbContext db, int clubId, string permission, HttpContext http, CancellationToken ct)
    {
        if (!Delegable.Contains(permission)) return false;
        var actor = http.User.GetUserId();
        if (await db.UserOwnsClubAsync(clubId, actor, ct)) return true;
        var now = http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var isVice = await IsVice(db, clubId, actor, now, ct);
        var config = await Current(db, clubId, http, ct);
        if (!isVice && config is null) return false;
        var accounts = await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([actor], ct);
        var campus = await db.Clubs.Where(x => x.Id == clubId && x.IsActive && x.DeletedAtUtc == null).Select(x => x.CampusCode).SingleOrDefaultAsync(ct);
        if (campus is null || !accounts.TryGetValue(actor, out var account) || account is not { IsActive: true, IsLocked: false }
            || (campus != CampusCodes.Global && account.CampusCode != campus)) return false;
        if (isVice) return true;
        var memberships = await db.ClubMemberships.Where(x => x.ClubId == clubId && x.UserId == actor && x.Status == ClubMembershipStatuses.Approved).Select(x => x.Id).ToArrayAsync(ct);
        return config!.Groups.Any(g => !g.Archived && !g.Protected && g.PermissionCodes.Contains(permission)
            && g.Assignments.Any(a => a.UserId == actor && memberships.Contains(a.MembershipId)));
    }
    public static async Task<bool> HasWritableSpecialization(ClubDbContext db, int clubId, HttpContext http, CancellationToken ct)
    {
        var actor = http.User.GetUserId(); var config = await Current(db, clubId, http, ct);
        var permission = config?.Groups.Where(g => !g.Archived && !g.Protected && g.Assignments.Any(a => a.UserId == actor))
            .SelectMany(g => g.PermissionCodes).FirstOrDefault(code => code != "club.finance.read");
        return permission is not null && await CanPerform(db, clubId, permission, http, ct);
    }
    public static GovernanceConfiguration Defaults(WorkspaceSemesterMetadata period) => new(period.Id, period.Code, period.StartUtc!.Value, period.EndUtc!.Value,
    [
        new(Guid.NewGuid(), "Chủ nhiệm", "", "CLUB_OWNER", null, true, [], []),
        new(Guid.NewGuid(), "Phó chủ nhiệm", "", "VICE_PRESIDENT", null, true, [], []),
        ..Presets.Where(x => x.Key != "CUSTOM").Select(x => new GovernanceGroup(Guid.NewGuid(), x.Key switch
        { "HR" => "Nhân sự", "SECRETARY" => "Thư ký", "TREASURER" => "Thủ quỹ", "CONTENT" => "Content", _ => "Event" }, "", x.Key,
            FinancialDuty(x.Value) ? "TREASURER" : null, false, x.Value, []))
    ]);
    public static bool Active(WorkspaceSemesterMetadata period, DateTimeOffset now) => period.IsCurrent && period.Editable
        && period.StartUtc.HasValue && period.EndUtc.HasValue && period.StartUtc <= now && now <= period.EndUtc;
    public static GovernanceMemberSnapshot[] Members(GovernanceConfiguration config) => config.Groups.Where(x => !x.Archived && !x.Protected)
        .SelectMany(g => g.Assignments.Select(a => (Group: g, Assignment: a))).GroupBy(x => x.Assignment.MembershipId)
        .Select(rows => new GovernanceMemberSnapshot(rows.Key, rows.First().Assignment.UserId, rows.First().Assignment.DisplayName,
            rows.Select(x => x.Group.Id).Order().ToArray(), rows.SelectMany(x => x.Group.PermissionCodes).Distinct().Order(StringComparer.Ordinal).ToArray())).ToArray();
    public static async Task<GovernanceConfiguration?> Current(ClubDbContext db, int clubId, HttpContext http, CancellationToken ct)
    {
        var terms = await db.Set<ClubGovernanceTerm>().AsNoTracking().Where(x => x.ClubId == clubId).ToListAsync(ct);
        if (terms.Count == 0) return null;
        var catalog = await http.RequestServices.GetRequiredService<WorkspaceCatalogClient>().ReadAsync(http, ct);
        var now = http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var periods = catalog.Semesters.Where(x => Active(x, now)).ToArray();
        if (periods.Length != 1) return null;
        var term = terms.SingleOrDefault(x => x.SemesterId == periods[0].Id);
        if (term is null) return null;
        var revision = await db.Set<ClubGovernanceRevision>().AsNoTracking().SingleAsync(x => x.Id == term.RevisionId && x.TermId == term.Id, ct);
        return Parse(revision) with { StartsAt = periods[0].StartUtc!.Value, EndsAt = periods[0].EndUtc!.Value };
    }
    public static Task<bool> IsVice(ClubDbContext db, int clubId, int actor, DateTimeOffset now, CancellationToken ct) =>
        db.Set<ClubViceAppointment>().AnyAsync(x => x.ClubId == clubId && x.UserId == actor && x.Status == "APPROVED"
            && x.From <= now && now < x.Until && db.ClubMemberships.Any(m => m.Id == x.MembershipId && m.UserId == actor
                && m.ClubId == clubId && m.Status == ClubMembershipStatuses.Approved), ct);
    public static async Task<int[]> GroupTreasurers(ClubDbContext db, int clubId, HttpContext http, CancellationToken ct)
    {
        var config = await Current(db, clubId, http, ct); if (config is null) return [];
        var ids = config.Groups.Where(x => !x.Archived && !x.Protected && FinancialDuty(x.PermissionCodes)).SelectMany(x => x.Assignments).Select(x => x.MembershipId).Distinct().ToArray();
        return await db.ClubMemberships.Where(x => ids.Contains(x.Id) && x.ClubId == clubId && x.Status == ClubMembershipStatuses.Approved).Select(x => x.UserId).Distinct().ToArrayAsync(ct);
    }
}
