using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using ClubService.Data;
using ClubService.Extensions;
using ClubService.Mappers;
using Microsoft.EntityFrameworkCore;

namespace ClubService.Endpoints;

public static class ClubPublicProfileEndpoints
{
    private static readonly HashSet<string> Fields = ["name", "category", "description", "contactEmail", "contactPhone", "scheduleLabel", "schedule", "expectedVersion"];

    public static void MapClubPublicProfileEndpoints(this WebApplication app) =>
        app.MapPatch("/api/clubs/{clubId:int}/public-profile", UpdateAsync)
            .RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Club public profile");

    private static async Task<IResult> UpdateAsync(int clubId, JsonElement request,
        ClubDbContext db, ClaimsPrincipal user, HttpContext http, CancellationToken cancellationToken)
    {
        if (!await db.CanManageMembershipsAsync(clubId, user, cancellationToken)) return Results.Forbid();
        var club = await db.Clubs.Include(x => x.ManagerAssignments).Include(x => x.Memberships)
            .SingleOrDefaultAsync(x => x.Id == clubId, cancellationToken);
        if (club is null) return Results.NotFound();
        if (!club.IsActive || club.DeletedAtUtc.HasValue)
            return ApiOperations.Error(409, "CLUB_INACTIVE", "An inactive club cannot be edited here.");
        if (request.ValueKind != JsonValueKind.Object || request.EnumerateObject().Any(x => !Fields.Contains(x.Name))
            || request.EnumerateObject().GroupBy(x => x.Name).Any(x => x.Count() != 1))
            return ApiOperations.Error(400, "INVALID_PROFILE_FIELDS", "Only the public text fields and expectedVersion are accepted.");
        if (!request.TryGetProperty("expectedVersion", out var version) || version.ValueKind != JsonValueKind.String || !version.TryGetGuid(out var expected))
            return ApiOperations.Error(400, "VERSION_REQUIRED", "Provide the club version.");
        if (request.TryGetProperty("schedule", out var structured))
        {
            if (request.TryGetProperty("scheduleLabel", out _)) return ApiOperations.Error(400, "CONFLICTING_SCHEDULE", "Provide structured schedule or scheduleLabel.");
            if (structured.ValueKind != JsonValueKind.Null)
            {
                var valid = structured.ValueKind == JsonValueKind.Object && structured.EnumerateObject().All(x =>
                    x.Name is "semesterId" or "weekdays" or "startTime" or "endTime" or "repeatWeekly" or "timezone")
                    && structured.EnumerateObject().GroupBy(x => x.Name).All(x => x.Count() == 1)
                    && structured.TryGetProperty("semesterId", out var semesterValue) && semesterValue.ValueKind == JsonValueKind.String
                    && structured.TryGetProperty("weekdays", out var weekdays) && weekdays.ValueKind == JsonValueKind.Array
                    && weekdays.GetArrayLength() is > 0 and <= 7 && weekdays.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var day) && day is >= 1 and <= 7)
                    && weekdays.EnumerateArray().Select(x => x.GetInt32()).Distinct().Count() == weekdays.GetArrayLength()
                    && structured.TryGetProperty("timezone", out var zone) && zone.ValueKind == JsonValueKind.String && zone.GetString() == "Asia/Ho_Chi_Minh"
                    && structured.TryGetProperty("repeatWeekly", out var repeat) && repeat.ValueKind == JsonValueKind.True
                    && structured.TryGetProperty("startTime", out var start) && Slot(start, out var startMinutes)
                    && structured.TryGetProperty("endTime", out var end) && Slot(end, out var endMinutes) && endMinutes > startMinutes;
                if (!valid) return ApiOperations.Error(400, "INVALID_SCHEDULE", "Use a canonical semester, unique weekdays, Asia/Ho_Chi_Minh and a weekly interval in 30-minute slots.");
                SemesterReference? semester;
                try { semester = await http.RequestServices.GetRequiredService<SemesterCatalogClient>().ResolveAsync(structured.GetProperty("semesterId").GetString()!, http, cancellationToken); }
                catch (HttpRequestException) { return ApiOperations.Error(503, "SEMESTER_SERVICE_UNAVAILABLE", "Semester catalog is unavailable."); }
                if (semester is null || semester.SemesterCode != structured.GetProperty("semesterId").GetString()) return ApiOperations.Error(400, "UNKNOWN_SEMESTER", "Use the canonical semester code.");
            }
        }

        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            if (expected != club.ConcurrencyToken) return ApiOperations.Error(409, "STALE_VERSION", "Reload the club before saving.");
            if (request.EnumerateObject().All(x => x.Name == "expectedVersion"))
                return ApiOperations.Error(400, "EMPTY_PATCH", "Provide a public text field.");
            foreach (var field in request.EnumerateObject().Where(x => x.Name != "expectedVersion"))
            {
                if (field.Name == "schedule")
                {
                    club.ScheduleJson = field.Value.ValueKind == JsonValueKind.Null ? null : field.Value.GetRawText();
                    club.ScheduleLabel = club.ScheduleJson is null ? null : $"Hằng tuần: {string.Join(", ", field.Value.GetProperty("weekdays").EnumerateArray().Select(x => x.GetInt32()))} | {field.Value.GetProperty("startTime").GetString()}–{field.Value.GetProperty("endTime").GetString()} (Asia/Ho_Chi_Minh)";
                    continue;
                }
                if (field.Value.ValueKind != JsonValueKind.String && !(field.Name == "scheduleLabel" && field.Value.ValueKind == JsonValueKind.Null))
                    return ApiOperations.Error(400, "INVALID_PROFILE_FIELD", "Text is required; only scheduleLabel can be null.");
                var value = field.Value.ValueKind == JsonValueKind.Null ? null : field.Value.GetString()!.Trim();
                var max = field.Name switch { "name" => 200, "category" => 40, "description" => 1000, "contactEmail" => 200, "contactPhone" => 20, _ => 500 };
                if (value?.Length > max || (field.Name is "name" or "category" && string.IsNullOrWhiteSpace(value))
                    || (field.Name == "contactEmail" && !string.IsNullOrEmpty(value) && !new EmailAddressAttribute().IsValid(value)))
                    return ApiOperations.Error(400, "INVALID_PROFILE_FIELD", "Check required text, lengths and email format.");
                switch (field.Name)
                {
                    case "name": club.Name = value!; break;
                    case "description": club.Description = value!; break;
                    case "contactEmail": club.ContactEmail = value!; break;
                    case "contactPhone": club.ContactPhone = value!; break;
                    case "scheduleLabel": club.ScheduleLabel = value; club.ScheduleJson = null; break;
                    case "category":
                        var code = value!.ToUpperInvariant();
                        if (!await db.ClubCategories.AnyAsync(x => x.Code == code, cancellationToken))
                            return ApiOperations.Error(400, "UNKNOWN_CATEGORY", "Choose a persisted category code.");
                        club.Category = code;
                        break;
                }
            }
            club.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ClubMappers.ToPublicDetailResponse(club));
        }, cancellationToken, required: false);
    }

    private static bool Slot(JsonElement value, out int minutes)
    {
        minutes = -1;
        if (value.ValueKind != JsonValueKind.String) return false;
        var text = value.GetString()!;
        if (text.Length != 5 || text[2] != ':' || !text.Where((_, i) => i != 2).All(c => c is >= '0' and <= '9')
            || !int.TryParse(text[..2], out var hour) || !int.TryParse(text[3..], out var minute)
            || hour is < 0 or > 24 || minute is not (0 or 30) || hour == 24 && minute != 0) return false;
        minutes = hour * 60 + minute; return true;
    }
}
