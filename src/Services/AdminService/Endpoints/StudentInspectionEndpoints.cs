using AdminService.Data;
using AdminService.Security;
using AdminService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Rosters;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Endpoints;

public static class StudentInspectionEndpoints
{
    public static void MapStudentInspectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/student-affairs").RequireAuthorization(AdminPolicies.BackofficeUser)
            .AddEndpointFilter<RosterExceptionFilter>().WithTags("Student inspection");
        group.MapGet("/campuses", Campuses);
        group.MapGet("/students/{studentId:int}/profile", Profile);
    }

    private static async Task<IResult> Campuses(HttpContext http, SchoolAccountClient accounts, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var actor = (await accounts.UsersAsync([http.User.GetUserId()], ct)).SingleOrDefault();
        if (actor is not { IsActive: true, IsLocked: false }) return Results.Forbid();
        var admin = http.User.IsInRole(AuthRoles.Admin);
        var codes = admin ? CampusCodes.FiveCampuses : CampusCodes.FiveCampuses.Where(x => x == actor.CampusCode).ToArray();
        if (!admin && codes.Length == 0) return Results.Forbid();
        return Results.Ok(new
        {
            actorId = actor.UserId,
            campusCode = actor.CampusCode,
            canSelectCampus = admin,
            semesterScope = "GLOBAL",
            campuses = codes.Select(code => new { code, name = CampusCodes.GetDisplayName(code) })
        });
    }

    private static async Task<IResult> Profile(int studentId, string? semester, HttpContext http, AdminDbContext db,
        SchoolAccountClient accounts, ClubExperienceAuthorityClient clubs, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var users = (await accounts.UsersAsync([http.User.GetUserId(), studentId], ct)).ToDictionary(x => x.UserId);
        var actor = users.GetValueOrDefault(http.User.GetUserId()); var student = users.GetValueOrDefault(studentId);
        if (actor is not { IsActive: true, IsLocked: false }) return Results.Forbid();
        if (student is not { IsStudent: true }) return Results.NotFound();
        if (!http.User.IsInRole(AuthRoles.Admin) && (student.CampusCode != actor.CampusCode || !CampusCodes.FiveCampuses.Contains(actor.CampusCode)))
            return Results.Forbid();

        string? studentCode = null, semesterCode = null;
        Guid? rosterVersion = null;
        var academicAvailability = "SEMESTER_REQUIRED";
        if (!string.IsNullOrWhiteSpace(semester))
        {
            var period = await SemesterRosterWorkflow.Semester(semester, db, ct); semesterCode = period.SemesterCode;
            var roster = await SemesterRosterWorkflow.Current(period, student.CampusCode, db, ct);
            academicAvailability = "ROSTER_UNAVAILABLE";
            if (roster is not null)
            {
                rosterVersion = roster.Version;
                var row = await db.Set<SemesterRosterRow>().AsNoTracking().SingleOrDefaultAsync(x => x.RevisionId == roster.RevisionId && x.UserId == studentId, ct);
                academicAvailability = row is null ? "NOT_IN_ROSTER" : !row.Active ? "ROSTER_INACTIVE"
                    : !row.Email.Equals(student.Email, StringComparison.OrdinalIgnoreCase) || row.CampusCode != student.CampusCode ? "IDENTITY_CHANGED" : "AVAILABLE";
                if (academicAvailability == "AVAILABLE") studentCode = row!.StudentCode;
            }
        }
        object membership;
        try
        {
            var scope = await clubs.StudentScopeAsync(studentId, student.CampusCode, ct);
            if (scope.Clubs is null || scope.Clubs.Select(x => x.ClubId).Distinct().Count() != scope.Clubs.Count
                || scope.Clubs.Any(x => x.ClubId <= 0 || x.IsApprovedMember && x.MembershipVersion is null)
                || string.IsNullOrWhiteSpace(scope.Version)) throw new HttpRequestException("Invalid membership authority.");
            membership = new
            {
                availability = "AVAILABLE",
                scope.AsOf,
                scope.Version,
                items = scope.Clubs.Where(x => x.IsApprovedMember).Select(x => new { x.ClubId, code = x.ClubCode, name = x.ClubName, x.JoinedAt }).ToArray()
            };
        }
        catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException
            || error is TaskCanceledException && !http.RequestAborted.IsCancellationRequested)
        {
            membership = new { availability = "AUTHORITY_UNAVAILABLE", items = Array.Empty<object>() };
        }
        return Results.Ok(new
        {
            studentId,
            fullName = student.FullName,
            student.Email,
            student.CampusCode,
            semesterCode,
            studentCode,
            academicAvailability,
            rosterVersion,
            major = (string?)null,
            cohort = (string?)null,
            majorAvailability = "NO_AUTHORITATIVE_SOURCE",
            cohortAvailability = "NO_AUTHORITATIVE_SOURCE",
            membership
        });
    }
}
