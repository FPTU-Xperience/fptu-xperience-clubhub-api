using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Auth;
using ClubService.Contracts;
using ClubService.Data;
using ClubService.Models;
using Microsoft.EntityFrameworkCore;

namespace ClubService.Services;

internal static class ClubGovernanceAccess
{
    internal static async Task<ClubAccessResponse> Project(ClubAccessResponse access, int actor, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        var now = http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        if (!await db.Clubs.AnyAsync(x => x.Id == access.ClubId && x.IsActive && x.DeletedAtUtc == null, ct)) return access;
        var vice = await db.Set<ClubViceAppointment>().AsNoTracking().Where(x => x.ClubId == access.ClubId && x.UserId == actor
            && x.Status == "APPROVED" && x.From <= now && now < x.Until).ToArrayAsync(ct);
        var config = await ClubGovernancePolicy.Current(db, access.ClubId, http, ct);
        if (config is null && vice.Length == 0) return access;
        var users = await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([actor], ct);
        if (!users.TryGetValue(actor, out var account) || account is not { IsActive: true, IsLocked: false })
            return access with { Capabilities = [], IsVicePresident = false, GroupIds = [] };
        var membershipIds = await db.ClubMemberships.AsNoTracking().Where(x => x.ClubId == access.ClubId && x.UserId == actor
            && x.Status == ClubMembershipStatuses.Approved).Select(x => x.Id).ToArrayAsync(ct);
        var campus = await db.Clubs.Where(x => x.Id == access.ClubId).Select(x => x.CampusCode).SingleAsync(ct);
        var inCampus = campus == CampusCodes.Global || account.CampusCode == campus;
        var groups = config?.Groups.Where(x => inCampus && !x.Archived && !x.Protected && x.Assignments.Any(a => a.UserId == actor && membershipIds.Contains(a.MembershipId))).ToArray() ?? [];
        var isVice = inCampus && vice.Any(x => membershipIds.Contains(x.MembershipId));
        var permissions = groups.SelectMany(x => x.PermissionCodes).Concat(access.IsTreasurer ? ClubGovernancePolicy.Presets["TREASURER"] : [])
            .Concat(isVice ? ClubGovernancePolicy.Delegable : []).Where(ClubGovernancePolicy.Delegable.Contains).Distinct().Order(StringComparer.Ordinal).ToArray();
        var groupIds = groups.Select(x => x.Id).ToArray();
        var treasurerIds = access.TreasurerUserIds.Concat(await ClubGovernancePolicy.GroupTreasurers(db, access.ClubId, http, ct)).Distinct().ToArray();
        var expiry = config?.EndsAt;
        if (isVice)
        {
            var viceExpiry = vice.Where(x => membershipIds.Contains(x.MembershipId)).Max(x => x.Until)!.Value;
            expiry = expiry.HasValue && expiry < viceExpiry ? expiry : viceExpiry;
        }
        var term = config is null ? null : await db.Set<ClubGovernanceTerm>().AsNoTracking().SingleAsync(x => x.ClubId == access.ClubId && x.SemesterId == config.SemesterId, ct);
        return access with
        {
            IsTreasurer = access.IsTreasurer || treasurerIds.Contains(actor),
            TreasurerUserIds = treasurerIds,
            Capabilities = permissions,
            AccessValidUntilUtc = expiry,
            IsVicePresident = isVice,
            GovernanceRevisionId = term?.RevisionId,
            GroupIds = groupIds
        };
    }
}
