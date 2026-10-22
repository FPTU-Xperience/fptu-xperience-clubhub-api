using AuthService.Data;
using AuthService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Schedules;
using Microsoft.EntityFrameworkCore;
using static ClubReportHub.Shared.Schedules.ScheduleRules;

namespace AuthService.Endpoints;

public static class StudyScheduleEndpoints
{
    public static void MapStudyScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/users/me/study-schedule/{year}/{term}/{block}")
            .RequireAuthorization(AuthPolicies.AllActors).AddEndpointFilter<ScheduleExceptionFilter>().WithTags("Private study schedule");
        api.MapGet("", Get);
        api.MapPut("/{week}/{day}/{slotId}", Put);
        api.MapDelete("/{week}/{day}/{slotId}", Delete);
        api.MapPost("/bulk-import", Import);
    }
    private static async Task<int> Actor(HttpContext http, AuthDbContext db, CancellationToken ct)
    {
        var id = http.User.GetUserId();
        Require(await db.Users.AsNoTracking().AnyAsync(x => x.Id == id && x.IsActive && !x.IsLocked, ct),
            403, "ACCOUNT_INELIGIBLE", "An active account is required.");
        return id;
    }
    private static async Task<AcademicCalendarReference> Source(int year, string term, string block, HttpContext http,
        AcademicCalendarClient calendars, CancellationToken ct, Guid? expected = null)
    {
        Period(year, term); Weeks(block);
        var calendar = await calendars.Read(year, term, http, ct);
        if (expected.HasValue)
        {
            Require(calendar.IsActive && calendar.CanWrite, 409, "SEMESTER_READ_ONLY", "Inactive or stale calendar periods are read-only.");
            Require(calendar.Version == expected, 409, "CALENDAR_CHANGED", "Reload the current calendar before writing.");
        }
        return calendar;
    }
    private static Task<StudyScheduleBlock?> Header(AuthDbContext db, int user, int year, string term, string block, CancellationToken ct) =>
        db.Set<StudyScheduleBlock>().SingleOrDefaultAsync(x => x.UserId == user && x.Year == year && x.Term == term && x.Block == block, ct);
    private static async Task<StudyCellResponse[]> Cells(AuthDbContext db, StudyScheduleBlock? row, CancellationToken ct)
    {
        var cells = row is null ? [] : await db.Set<StudyScheduleCell>().Where(x => x.BlockId == row.Id).ToArrayAsync(ct);
        return cells.OrderBy(x => x.Week).ThenBy(x => Array.IndexOf(Days, x.Day)).ThenBy(x => x.SlotNumber)
            .Select(x => new StudyCellResponse(x.Week, x.Day, "slot-" + x.SlotNumber, x.Subject, x.Room)).ToArray();
    }
    private static async Task<IResult> Get(int year, string term, string block, HttpContext http, AuthDbContext db, AcademicCalendarClient calendars, CancellationToken ct)
    {
        var user = await Actor(http, db, ct); var calendar = await Source(year, term, block, http, calendars, ct);
        var row = await Header(db, user, year, term, block, ct); http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new
        {
            year,
            term,
            block,
            version = row?.Version ?? Guid.Empty,
            calendarVersion = calendar.Version,
            calendar = new { calendar.TermStartDate, calendar.Block10StartDate, calendar.Block3StartDate },
            calendar.SemesterKey,
            calendar.SemesterId,
            calendar.CampusCode,
            calendar.Availability,
            canWrite = calendar.CanWrite,
            entries = await Cells(db, row, ct)
        });
    }
    private static void Cell(int week, string block, string day, string slotId)
    { Require(week >= 1 && week <= Weeks(block), 400, "INVALID_WEEK", "Use a block-local week.", "week"); Slot(block, day, slotId); }
    private static StudyScheduleBlock Create(AuthDbContext db, int user, int year, string term, string block)
    { var row = new StudyScheduleBlock { UserId = user, Year = year, Term = term, Block = block }; db.Add(row); return row; }
    private static void Touch(StudyScheduleBlock row, AcademicCalendarReference source, HttpContext http)
    {
        row.Version = Guid.NewGuid(); row.SemesterKey = source.SemesterKey; row.CalendarVersion = source.Version;
        row.UpdatedAtUtc = http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
    }
    private static async Task<IResult> Put(int year, string term, string block, int week, string day, string slotId,
        HttpContext http, AuthDbContext db, AcademicCalendarClient calendars, CancellationToken ct)
    {
        Period(year, term); Cell(week, block, day, slotId);
        var user = await Actor(http, db, ct); var input = await BoundedJsonRequest.ReadAsync<StudyCellInput>(http, 8192, ct);
        var text = Text(input.Subject, input.Room); await Source(year, term, block, http, calendars, ct, input.ExpectedCalendarVersion);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await Actor(http, db, ct); var source = await Source(year, term, block, http, calendars, ct, input.ExpectedCalendarVersion);
            var row = await Header(db, user, year, term, block, ct);
            Require((row?.Version ?? Guid.Empty) == input.ExpectedVersion, 409, "SCHEDULE_CHANGED", "Reload this period before writing.");
            row ??= Create(db, user, year, term, block);
            var number = Slot(block, day, slotId);
            var cell = await db.Set<StudyScheduleCell>().SingleOrDefaultAsync(x => x.BlockId == row.Id && x.Week == week && x.Day == day && x.SlotNumber == number, ct);
            if (cell is null) { cell = new StudyScheduleCell { BlockId = row.Id, Week = week, Day = day, SlotNumber = number }; db.Add(cell); }
            Touch(row, source, http); cell.Subject = text.Subject; cell.Room = text.Room; cell.UpdatedAtUtc = row.UpdatedAtUtc;
            return Results.Ok(new { week, day, slotId, subject = cell.Subject, room = cell.Room, version = row.Version, calendarVersion = source.Version });
        }, ct);
    }
    private static async Task<IResult> Delete(int year, string term, string block, int week, string day, string slotId,
        Guid expectedVersion, Guid expectedCalendarVersion, HttpContext http, AuthDbContext db, AcademicCalendarClient calendars, CancellationToken ct)
    {
        Period(year, term); Cell(week, block, day, slotId); var user = await Actor(http, db, ct);
        await Source(year, term, block, http, calendars, ct, expectedCalendarVersion);
        return await ApiOperations.ExecuteAsync(http, db, new { expectedVersion, expectedCalendarVersion }, async () =>
        {
            await Actor(http, db, ct); var source = await Source(year, term, block, http, calendars, ct, expectedCalendarVersion);
            var row = await Header(db, user, year, term, block, ct);
            Require((row?.Version ?? Guid.Empty) == expectedVersion, 409, "SCHEDULE_CHANGED", "Reload this period before writing.");
            if (row is null) return Results.NoContent();
            var number = Slot(block, day, slotId);
            var cell = await db.Set<StudyScheduleCell>().SingleOrDefaultAsync(x => x.BlockId == row.Id && x.Week == week && x.Day == day && x.SlotNumber == number, ct);
            if (cell is not null) { db.Remove(cell); Touch(row, source, http); }
            return Results.NoContent();
        }, ct);
    }
    private static async Task<IResult> Import(int year, string term, string block, HttpContext http, AuthDbContext db,
        AcademicCalendarClient calendars, CancellationToken ct)
    {
        Period(year, term); var weeks = Weeks(block); var user = await Actor(http, db, ct);
        var input = await BoundedJsonRequest.ReadAsync<StudyImportInput>(http, 65536, ct);
        Require(input.Mode is "skip" or "replace" && input.Entries is { Count: > 0 } && input.Entries.Count <= (weeks == 10 ? 35 : 49),
            400, "INVALID_TEMPLATE", "Use skip/replace and a nonempty bounded weekly template.", "entries");
        var seen = new HashSet<(string Day, int Slot)>();
        var template = input.Entries.Select(x =>
        {
            Require(x is not null, 400, "INVALID_TEMPLATE", "Template rows cannot be null.", "entries");
            var slot = Slot(block, x!.Day, x.SlotId); var text = Text(x.Subject, x.Room);
            Require(seen.Add((x.Day, slot)), 400, "DUPLICATE_TEMPLATE_CELL", "A day/slot may appear only once.", "entries");
            return (x.Day, Slot: slot, text.Subject, text.Room);
        }).ToArray();
        await Source(year, term, block, http, calendars, ct, input.ExpectedCalendarVersion);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await Actor(http, db, ct); var source = await Source(year, term, block, http, calendars, ct, input.ExpectedCalendarVersion);
            var row = await Header(db, user, year, term, block, ct);
            Require((row?.Version ?? Guid.Empty) == input.ExpectedVersion, 409, "SCHEDULE_CHANGED", "Reload this period before importing.");
            row ??= Create(db, user, year, term, block);
            var cells = await db.Set<StudyScheduleCell>().Where(x => x.BlockId == row.Id).ToDictionaryAsync(x => (x.Week, x.Day, x.SlotNumber), ct);
            var imported = 0; var skipped = 0; var replaced = 0;
            var now = http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
            for (var week = 1; week <= weeks; week++)
                foreach (var item in template)
                {
                    if (cells.TryGetValue((week, item.Day, item.Slot), out var cell))
                    {
                        if (input.Mode == "skip") { skipped++; continue; }
                        replaced++;
                    }
                    else
                    {
                        cell = new StudyScheduleCell { BlockId = row.Id, Week = week, Day = item.Day, SlotNumber = item.Slot };
                        db.Add(cell); cells.Add((week, item.Day, item.Slot), cell); imported++;
                    }
                    cell.Subject = item.Subject; cell.Room = item.Room; cell.UpdatedAtUtc = now;
                }
            if (imported + replaced != 0) Touch(row, source, http);
            var entries = cells.Values.OrderBy(x => x.Week).ThenBy(x => Array.IndexOf(Days, x.Day)).ThenBy(x => x.SlotNumber)
                .Select(x => new StudyCellResponse(x.Week, x.Day, "slot-" + x.SlotNumber, x.Subject, x.Room)).ToArray();
            return Results.Ok(new { imported, skipped, replaced, entries, version = row.Version, calendarVersion = source.Version });
        }, ct);
    }
}
