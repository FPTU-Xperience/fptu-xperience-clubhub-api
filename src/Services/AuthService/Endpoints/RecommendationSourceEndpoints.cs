using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuthService.Data;
using AuthService.Models;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Schedules;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Endpoints;

public static class RecommendationSourceEndpoints
{
    public static void MapRecommendationSourceEndpoints(this IEndpointRouteBuilder app) =>
        app.MapPost("/api/internal/recommendation/students", ExportAsync).AllowAnonymous().ExcludeFromDescription();

    private static async Task<IResult> ExportAsync(RecommendationExportQuery query, AuthDbContext db,
        HttpContext http, IConfiguration configuration, CancellationToken ct)
    {
        if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
        if (query.AfterUserId < 0 || query.Limit is < 1 or > 100 || query.Calendars is null || query.Calendars.Count > 20)
            return Results.BadRequest();
        var users = await db.Users.AsNoTracking().Where(x => x.Id > query.AfterUserId && x.IsActive && !x.IsLocked)
            .OrderBy(x => x.Id).Take(query.Limit).ToArrayAsync(ct);
        var ids = users.Select(x => x.Id).ToArray();
        var profiles = await db.UserProfiles.AsNoTracking().Where(x => ids.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, ct);
        var keys = query.Calendars.Select(x => x.SemesterKey).Distinct().ToArray();
        var blocks = await db.Set<StudyScheduleBlock>().AsNoTracking().Where(x => ids.Contains(x.UserId) && keys.Contains(x.SemesterKey)).ToArrayAsync(ct);
        var blockIds = blocks.Select(x => x.Id).ToArray();
        var cells = await db.Set<StudyScheduleCell>().AsNoTracking().Where(x => blockIds.Contains(x.BlockId)).ToArrayAsync(ct);
        var items = users.Select(user =>
        {
            var profile = profiles.GetValueOrDefault(user.Id);
            var own = blocks.Where(x => x.UserId == user.Id).OrderBy(x => x.Id).ToArray();
            var busy = new List<RecommendationBusyInterval>();
            // Both blocks and their exact calendar versions are necessary to claim
            // known availability. Partial/stale schedules remain explicitly unknown.
            var known = own.Any(x => x.Block == "10W") && own.Any(x => x.Block == "3W");
            foreach (var block in own)
            {
                var calendar = query.Calendars.SingleOrDefault(x => x.SemesterKey == block.SemesterKey && x.CampusCode == user.CampusCode);
                if (calendar is null || calendar.Version != block.CalendarVersion) { known = false; continue; }
                var startText = block.Block == "10W" ? calendar.Block10StartDate : calendar.Block3StartDate;
                if (!DateOnly.TryParseExact(startText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                { known = false; continue; }
                foreach (var cell in cells.Where(x => x.BlockId == block.Id))
                    busy.Add(ToBusy(block.Block, start, cell));
            }
            var scheduleVersion = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Join("|", own.Select(x => $"{x.Id}:{x.Version}:{x.CalendarVersion}")) + ":" +
                string.Join("|", query.Calendars.Where(x => x.CampusCode == user.CampusCode).Select(x => x.Version)))));
            return new RecommendationStudentSource(user.Id, user.CampusCode,
                $"{user.SecurityVersion}:{user.CampusCode}:{user.IsActive}:{user.IsLocked}", profile?.Version ?? Guid.Empty,
                JsonSerializer.Deserialize<string[]>(profile?.InterestsJson ?? "[]") ?? [], profile?.DeclaredMajor,
                scheduleVersion, known ? "KNOWN" : "UNKNOWN", busy);
        }).ToArray();
        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new RecommendationStudentPage(items, users.LastOrDefault()?.Id ?? query.AfterUserId));
    }

    internal static RecommendationBusyInterval ToBusy(string block, DateOnly firstMonday, StudyScheduleCell cell)
    {
        // Same slot definitions as the student timetable UI, campus timezone UTC+7.
        int[] starts = block == "10W" ? [420, 570, 750, 900, 1050] : [420, 525, 630, 750, 855, 960, 1065];
        int[] ends = block == "10W" ? [555, 705, 885, 1035, 1185] : [510, 615, 720, 840, 945, 1050, 1155];
        var day = Array.IndexOf(ScheduleRules.Days, cell.Day);
        if (day < 0 || cell.Week < 1 || cell.Week > ScheduleRules.Weeks(block) || cell.SlotNumber < 1 || cell.SlotNumber > starts.Length)
            throw new InvalidOperationException("Invalid stored timetable cell.");
        var date = firstMonday.AddDays((cell.Week - 1) * 7 + day).ToDateTime(TimeOnly.MinValue);
        return new(new DateTimeOffset(date.AddMinutes(starts[cell.SlotNumber - 1]), TimeSpan.FromHours(7)).ToUniversalTime(),
            new DateTimeOffset(date.AddMinutes(ends[cell.SlotNumber - 1]), TimeSpan.FromHours(7)).ToUniversalTime());
    }
}
