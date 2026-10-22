using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Rosters;
using ClubService.Data;
using ClubService.Models;
using Microsoft.EntityFrameworkCore;

namespace ClubService.Endpoints;

public static class ClubWorkspaceCatalogEndpoints
{
    public static void MapClubWorkspaceCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/clubs/{clubId:int}").RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Club workspace catalog");
        group.MapGet("/workspace-semesters", (int clubId, ClubDbContext db, WorkspaceCatalogClient catalog, HttpContext http, CancellationToken ct)
            => ReadAsync(clubId, "workspace", null, db, catalog, http, ct));
        group.MapGet("/invitation-semesters", (int clubId, ClubDbContext db, WorkspaceCatalogClient catalog, HttpContext http, CancellationToken ct)
            => ReadAsync(clubId, "invitation", null, db, catalog, http, ct));
        group.MapGet("/experience-policies", (int clubId, string? semesterId, ClubDbContext db, WorkspaceCatalogClient catalog, HttpContext http, CancellationToken ct)
            => ReadAsync(clubId, "policy", semesterId, db, catalog, http, ct));
    }

    private static async Task<IResult> ReadAsync(int clubId, string projection, string? semesterId, ClubDbContext db,
        WorkspaceCatalogClient catalog, HttpContext http, CancellationToken ct)
    {
        var club = await db.Clubs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == clubId && x.IsActive && x.DeletedAtUtc == null, ct);
        if (club is null) return Results.NotFound();
        var admin = http.User.IsInRole(AuthRoles.Admin);
        if (!admin && club.CampusCode != CampusCodes.Global && http.User.GetCampus() != club.CampusCode) return Results.Forbid();
        var actor = http.User.GetUserId();
        var manager = await db.ClubManagerAssignments.AsNoTracking().AnyAsync(x => x.ClubId == clubId && x.ManagerUserId == actor && x.IsActive, ct);
        var backoffice = admin || http.User.IsInRole(AuthRoles.StudentAffairsAdmin);
        var member = projection == "workspace" && await db.ClubMemberships.AsNoTracking().AnyAsync(x => x.ClubId == clubId
            && x.UserId == actor && x.Status == ClubMembershipStatuses.Approved, ct);
        if (!backoffice && !manager && !member) return Results.Forbid();
        if (projection == "policy" && (string.IsNullOrWhiteSpace(semesterId) || semesterId.Length > 40))
            return ApiOperations.Error(400, "SEMESTER_REQUIRED", "Select a canonical semester code or ID.");
        WorkspaceCatalog result;
        try { result = await catalog.ReadAsync(http, ct); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        { return ApiOperations.Error(503, "WORKSPACE_CATALOG_UNAVAILABLE", "The authoritative semester/policy catalog is unavailable."); }
        http.Response.Headers.CacheControl = "no-store";
        if (projection == "policy")
        {
            var semester = result.Semesters.SingleOrDefault(x => x.Code.Equals(semesterId, StringComparison.OrdinalIgnoreCase)
                || x.Id.ToString().Equals(semesterId, StringComparison.OrdinalIgnoreCase));
            if (semester is null) return Results.NotFound();
            var items = result.Policies.Where(x => x.Policy.SemesterId == semester.Code).ToArray();
            return Results.Ok(new
            {
                clubId,
                semesterId = semester.Code,
                semesterKey = semester.Id,
                result.Environment,
                items,
                availability = items.Length == 0 ? "NO_APPROVED_POLICY" : "AVAILABLE",
                result.AsOf
            });
        }
        IReadOnlyList<RosterSummary> rosterSummaries = [];
        if (projection == "invitation" && http.RequestServices.GetService<SemesterRosterClient>() is { } rosterClient)
        {
            try { rosterSummaries = await rosterClient.SummariesAsync(club.CampusCode == CampusCodes.Global ? http.User.GetCampus() : club.CampusCode, ct); }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            { return ApiOperations.Error(503, "ROSTER_AUTHORITY_UNAVAILABLE", "The invitation roster catalog is unavailable."); }
        }
        return Results.Ok(new
        {
            clubId,
            items = result.Semesters.Select(x => new
            {
                x.Id,
                x.Code,
                x.Label,
                x.IsCurrent,
                editable = x.Editable && (manager || backoffice),
                canInvite = projection == "invitation" && (manager || backoffice) && x.IsCurrent
                    && rosterSummaries.Any(r => r.SemesterKey == x.Id && r.ActiveCount > 0),
                invitationAvailability = rosterSummaries.Any(r => r.SemesterKey == x.Id)
                    ? rosterSummaries.Any(r => r.SemesterKey == x.Id && r.ActiveCount > 0) ? "AVAILABLE" : "EMPTY" : "ROSTER_UNAVAILABLE",
                x.StartUtc,
                x.EndUtc,
                x.ReportPeriod,
                invitationCode = x.Code,
                semesterId = x.Code,
                semesterKey = x.Id,
                x.Year,
                x.Term,
                x.EditAvailability,
                x.MappingAvailability
            }).ToArray(),
            result.CurrentAvailability,
            result.AsOf,
            result.Timezone,
            result.StartInclusive,
            result.EndInclusive
        });
    }
}
