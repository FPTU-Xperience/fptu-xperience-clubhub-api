using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Rosters;
using ClubService.Data;
using ClubService.Extensions;
using ClubService.Models;
using Microsoft.EntityFrameworkCore;

namespace ClubService.Endpoints;

public static class ClubRosterEligibilityEndpoints
{
    public static void MapClubRosterEligibilityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/clubs/{clubId:int}/eligible-members", async (int clubId, string? semesterId, string? search, int? page, int? pageSize,
            HttpContext http, ClubDbContext db, SemesterRosterClient roster, CancellationToken ct) =>
        {
            var club = await db.Clubs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == clubId && x.IsActive && x.DeletedAtUtc == null, ct);
            if (club is null) return Results.NotFound();
            if (!await db.CanManageMembershipsAsync(clubId, http.User, ct)) return Results.Forbid();
            if (!http.User.IsSuperAdmin() && club.CampusCode != CampusCodes.Global && club.CampusCode != http.User.GetCampus()) return Results.Forbid();
            var p = page ?? 1; var size = pageSize ?? 10; var text = search?.Trim();
            RosterErrors.Check(!string.IsNullOrWhiteSpace(semesterId) && semesterId.Length <= 40 && text?.Length is >= 2 and <= 100
                && p is >= 1 and <= 100000 && size is >= 1 and <= 50, 400, "INVALID_ELIGIBILITY_QUERY", "Select a semester, search 2–100 characters and pageSize 1–50.");
            var campus = club.CampusCode == CampusCodes.Global ? http.User.GetCampus() : club.CampusCode;
            var excluded = await db.ClubMemberships.IgnoreQueryFilters().Where(x => x.ClubId == clubId && !x.IsDeleted)
                .Select(x => x.UserId).Union(db.ClubManagerAssignments.Where(x => x.ClubId == clubId && x.IsActive).Select(x => x.ManagerUserId)).ToArrayAsync(ct);
            var result = await roster.LookupAsync(new(semesterId!, campus, text, p, size, excluded), ct);
            RosterErrors.Check(result.Availability == "AVAILABLE", 409, result.Availability, "A committed active-semester roster is required.");
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new
            {
                result.SemesterId,
                result.SemesterKey,
                result.CampusCode,
                rosterVersion = result.Version,
                rosterRevisionId = result.RevisionId,
                result.Items,
                result.Page,
                result.PageSize,
                result.TotalItems,
                result.TotalPages
            });
        }).RequireAuthorization(AuthPolicies.BusinessAccess).AddEndpointFilter<RosterExceptionFilter>().WithTags("Club member invitations");
    }
}

internal static class ClubRosterEligibility
{
    internal static async Task<(RosterLookupResult? Roster, IResult? Error)> Check(Club club, int userId, string? semesterId,
        string campus, HttpContext http, CancellationToken ct)
    {
        if (!club.IsActive || club.DeletedAtUtc.HasValue)
            return (null, ApiOperations.Error(409, "CLUB_UNAVAILABLE", "The club is inactive."));
        if (string.IsNullOrWhiteSpace(semesterId) || semesterId.Length > 40)
            return (null, ApiOperations.Error(400, "SEMESTER_REQUIRED", "Select the invitation semester."));
        var client = http.RequestServices.GetService<SemesterRosterClient>();
        if (client is null) return (null, ApiOperations.Error(503, "ROSTER_AUTHORITY_UNAVAILABLE", "The semester roster authority is unconfigured."));
        try
        {
            var result = await client.LookupAsync(new(semesterId, campus, UserId: userId), ct);
            if (result.Availability != "AVAILABLE") return (null, ApiOperations.Error(409, result.Availability, "A committed active-semester roster is required."));
            if (result.Items.SingleOrDefault(x => x.UserId == userId) is not { Eligible: true })
                return (null, ApiOperations.Error(422, "STUDENT_NOT_ELIGIBLE", "This account is not currently eligible in the semester roster."));
            return (result, null);
        }
        catch (RosterException e) { return (null, ApiOperations.Error(e.Status, e.Code, e.Message)); }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        { return (null, ApiOperations.Error(503, "ROSTER_AUTHORITY_UNAVAILABLE", "The roster/account authority could not be verified.")); }
    }
    internal static async Task<(RosterLookupResult? Roster, IResult? Error)> Invitation(ClubMembership membership,
        Club club, HttpContext http, CancellationToken ct)
    {
        if (membership.Source != ClubMembershipSources.Invitation) return (null, null);
        if (string.IsNullOrEmpty(membership.SemesterId) || string.IsNullOrEmpty(membership.RosterCampusCode))
            return (null, ApiOperations.Error(409, "INVITATION_REQUIRES_RECONCILIATION", "This legacy invitation has no authoritative semester. Ask the manager to reconcile it."));
        return await Check(club, membership.UserId, membership.SemesterId, membership.RosterCampusCode, http, ct);
    }
    internal static void Apply(ClubMembership membership, RosterLookupResult? roster)
    {
        if (roster is null) return;
        membership.SemesterId = roster.SemesterId; membership.RosterCampusCode = roster.CampusCode;
        membership.RosterRevisionId = roster.RevisionId; membership.RosterVersion = roster.Version;
    }
}
