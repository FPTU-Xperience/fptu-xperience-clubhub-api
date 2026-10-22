using AdminService.Data;
using AdminService.Security;
using AdminService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Profiles;
using ClubReportHub.Shared.Rosters;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Endpoints;

public static class AcademicProfileEndpoints
{
    public static void MapAcademicProfileEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/internal/academic-profiles/me", Read).RequireAuthorization(AdminPolicies.AnyActor)
            .AddEndpointFilter<RosterExceptionFilter>().ExcludeFromDescription();
    private static async Task<IResult> Read(HttpContext http, AdminDbContext db, SchoolAccountClient accounts, IConfiguration configuration, CancellationToken ct)
    {
        if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
        var id = http.User.GetUserId(); var account = (await accounts.UsersAsync([id], ct)).SingleOrDefault();
        if (account is not { IsActive: true, IsLocked: false }) return Results.Forbid();
        IResult Result(AcademicStudentProfile academic) => Results.Ok(new AcademicProfileEnvelope(id, academic));
        http.Response.Headers.CacheControl = "no-store";
        if (!account.IsStudent) return Result(new("NOT_APPLICABLE"));
        if (!account.IsSchoolEmail || !CampusCodes.FiveCampuses.Contains(account.CampusCode)) return Result(new("NO_CANONICAL_SCHOOL_IDENTITY"));
        var semesters = await db.Semesters.AsNoTracking().Where(x => x.IsActive).Take(2).ToArrayAsync(ct);
        if (semesters.Length != 1) return Result(new(semesters.Length == 0 ? "SEMESTER_UNCONFIGURED" : "SEMESTER_AMBIGUOUS"));
        var semester = semesters[0]; var roster = await SemesterRosterWorkflow.Current(semester, account.CampusCode, db, ct);
        if (roster is null) return Result(new("ROSTER_UNAVAILABLE", SemesterKey: semester.Id, SemesterId: semester.SemesterCode));
        var row = await db.Set<SemesterRosterRow>().AsNoTracking().SingleOrDefaultAsync(x => x.RevisionId == roster.RevisionId && x.UserId == id, ct);
        var revision = await db.Set<SemesterRosterRevision>().AsNoTracking().SingleAsync(x => x.Id == roster.RevisionId, ct);
        if (row is null) return Result(new("NOT_IN_ROSTER", Source: revision.Source, SemesterKey: semester.Id, SemesterId: semester.SemesterCode, RosterRevisionId: revision.Id, SourceVersion: roster.Version));
        if (!row.Email.Equals(account.Email, StringComparison.OrdinalIgnoreCase) || row.CampusCode != account.CampusCode || string.IsNullOrWhiteSpace(row.StudentCode))
            return Result(new("IDENTITY_CHANGED", SemesterKey: semester.Id, SemesterId: semester.SemesterCode));
        return Result(new(row.Active ? "AVAILABLE" : "ROSTER_INACTIVE", row.StudentCode, revision.Source, semester.Id, semester.SemesterCode, revision.Id, roster.Version));
    }
}
