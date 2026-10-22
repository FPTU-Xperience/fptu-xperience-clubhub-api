using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdminService.Data;
using AdminService.Security;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Schedules;
using Microsoft.EntityFrameworkCore;
using static ClubReportHub.Shared.Schedules.ScheduleRules;

namespace AdminService.Endpoints;

public static class AcademicCalendarEndpoints
{
    public static void MapAcademicCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/academic-calendar/{year}/{term}").RequireAuthorization(AdminPolicies.AnyActor)
            .AddEndpointFilter<ScheduleExceptionFilter>().WithTags("Academic calendar");
        api.MapGet("", Get);
        api.MapPut("", Put).RequireAuthorization(AdminPolicies.BackofficeUser);
        app.MapGet("/api/internal/academic-calendar/{year}/{term}", InternalGet).RequireAuthorization(AdminPolicies.AnyActor)
            .AddEndpointFilter<ScheduleExceptionFilter>().ExcludeFromDescription();
    }
    private static async Task<string> Campus(string? campusCode, HttpContext http, CurrentUserProfileClient profiles, CancellationToken ct)
    {
        var user = (await profiles.GetAsync([http.User.GetUserId()], ct)).GetValueOrDefault(http.User.GetUserId());
        Require(user is { IsActive: true, IsLocked: false }, 403, "ACCOUNT_INELIGIBLE", "An active account is required.");
        var campus = campusCode ?? user!.CampusCode;
        Require(CampusCodes.FiveCampuses.Contains(campus), 400, "INVALID_CAMPUS", "Use a canonical campus.", "campusCode");
        Require(http.User.IsInRole(AuthRoles.Admin) || campus == user!.CampusCode,
            403, "CAMPUS_SCOPE_DENIED", "Calendar access is limited to your current campus.");
        return campus;
    }
    private static async Task<IResult> Get(int year, string term, string? campusCode, HttpContext http, AdminDbContext db,
        CurrentUserProfileClient profiles, IConfiguration configuration, CancellationToken ct)
    {
        Period(year, term); var campus = await Campus(campusCode, http, profiles, ct);
        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok((await State(year, term, campus, db, configuration, ct)).Response);
    }
    private static Task<IResult> InternalGet(int year, string term, string? campusCode, HttpContext http, AdminDbContext db,
        CurrentUserProfileClient profiles, IConfiguration configuration, CancellationToken ct) =>
        VerifiedExperienceRules.InternalRequest(http, configuration) ? Get(year, term, campusCode, http, db, profiles, configuration, ct) : Task.FromResult<IResult>(Results.Forbid());
    private static async Task<IResult> Put(int year, string term, string? campusCode, HttpContext http, AdminDbContext db,
        CurrentUserProfileClient profiles, IConfiguration configuration, CancellationToken ct)
    {
        Period(year, term); var campus = await Campus(campusCode, http, profiles, ct);
        var input = await BoundedJsonRequest.ReadAsync<AcademicCalendarInput>(http, 8192, ct);
        var before = await State(year, term, campus, db, configuration, ct);
        Require(before.Semester.IsActive, 409, "SEMESTER_INACTIVE", "Inactive semesters are read-only.");
        return await ApiOperations.ExecuteAsync(http, db, new { input, campus }, async () =>
        {
            await Campus(campus, http, profiles, ct);
            var state = await State(year, term, campus, db, configuration, ct, true);
            Require(state.Semester.IsActive, 409, "SEMESTER_INACTIVE", "Inactive semesters are read-only.");
            Require(input.ExpectedVersion == state.Response.Version, 409, "CALENDAR_CHANGED", "Reload the current calendar version.");
            Require(Valid(input.TermStartDate, input.Block10StartDate, input.Block3StartDate, state.Semester),
                400, "INVALID_CALENDAR_DATES", "Use the semester start date, Monday block starts, 10W then 3W, all inside the semester.", "dates");
            var row = state.Row;
            if (row is null) { row = new AcademicCalendarConfiguration { SemesterId = state.Semester.Id, CampusCode = campus }; db.Add(row); }
            row.TermStartDate = input.TermStartDate; row.Block10StartDate = input.Block10StartDate; row.Block3StartDate = input.Block3StartDate;
            row.Version = Guid.NewGuid(); row.UpdatedByUserId = http.User.GetUserId();
            row.UpdatedAtUtc = http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
            var response = Project(year, term, campus, state.Semester, row);
            db.Add(new AcademicCalendarRevision
            {
                CalendarId = row.Id,
                Version = row.Version,
                ActorId = row.UpdatedByUserId,
                AtUtc = row.UpdatedAtUtc,
                SnapshotJson = JsonSerializer.Serialize(response, BoundedJsonRequest.Options)
            });
            return Results.Ok(response);
        }, ct);
    }
    private sealed record CalendarState(Semester Semester, AcademicCalendarConfiguration? Row, AcademicCalendarReference Response);
    private static async Task<CalendarState> State(int year, string term, string campus, AdminDbContext db, IConfiguration configuration,
        CancellationToken ct, bool tracked = false)
    {
        var semesters = await db.Semesters.AsNoTracking().ToArrayAsync(ct);
        var matches = semesters.Where(s => int.TryParse(configuration[$"Workspace:SemesterAliases:{s.Id:D}:Year"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var aliasYear) && aliasYear == year
            && configuration[$"Workspace:SemesterAliases:{s.Id:D}:Term"] == term).ToArray();
        Require(matches.Length != 0, 404, "SEMESTER_UNCONFIGURED", "This year/term has no canonical semester alias.");
        Require(matches.Length == 1, 409, "SEMESTER_ALIAS_AMBIGUOUS", "This year/term has multiple semester aliases.");
        var semester = matches[0];
        var query = db.Set<AcademicCalendarConfiguration>().Where(x => x.SemesterId == semester.Id && x.CampusCode == campus);
        var row = await (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct);
        return new(semester, row, Project(year, term, campus, semester, row));
    }
    private static AcademicCalendarReference Project(int year, string term, string campus, Semester semester, AcademicCalendarConfiguration? row)
    {
        var configured = row is not null; var valid = configured && Valid(row!.TermStartDate, row.Block10StartDate, row.Block3StartDate, semester);
        var bounds = semester.StartDate.HasValue && semester.EndDate >= semester.StartDate;
        var basis = FormattableString.Invariant($"{year}|{term}|{campus}|{semester.Id:D}|{semester.SemesterCode}|{semester.StartDate:O}|{semester.EndDate:O}|{semester.IsActive}|{row?.Version:D}");
        var version = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(basis)).AsSpan(0, 16));
        return new(year, term, semester.Id, semester.SemesterCode, campus, version, semester.IsActive,
            semester.IsActive && bounds && (!configured || valid), !configured ? "UNCONFIGURED" : valid ? "AVAILABLE" : "STALE_CONFIGURATION",
            row?.TermStartDate ?? "", row?.Block10StartDate ?? "", row?.Block3StartDate ?? "");
    }
    private static bool Valid(string? term, string? block10, string? block3, Semester semester)
    {
        static bool Date(string? input, out DateOnly date) => DateOnly.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        return Date(term, out var t) && Date(block10, out var a) && Date(block3, out var b)
            && semester.StartDate.HasValue && semester.EndDate.HasValue
            && t == DateOnly.FromDateTime(semester.StartDate.Value.Date) && t <= a
            && a.DayOfWeek == DayOfWeek.Monday && b.DayOfWeek == DayOfWeek.Monday
            && a.Year <= 9998 && b.Year <= 9998 && b >= a.AddDays(70)
            && b.AddDays(20) <= DateOnly.FromDateTime(semester.EndDate.Value.Date);
    }
}
