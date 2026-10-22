using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ClubReportHub.Shared.Schedules;

public sealed record AcademicCalendarReference(int Year, string Term, Guid SemesterKey, string SemesterId,
    string CampusCode, Guid Version, bool IsActive, bool CanWrite, string Availability,
    string TermStartDate, string Block10StartDate, string Block3StartDate);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AcademicCalendarInput(string TermStartDate, string Block10StartDate, string Block3StartDate,
    [property: JsonRequired] Guid ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StudyCellInput(string Subject, string? Room,
    [property: JsonRequired] Guid ExpectedVersion, [property: JsonRequired] Guid ExpectedCalendarVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StudyTemplateCell(string Day, string SlotId, string Subject, string? Room);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StudyImportInput(string Mode, IReadOnlyList<StudyTemplateCell> Entries,
    [property: JsonRequired] Guid ExpectedVersion, [property: JsonRequired] Guid ExpectedCalendarVersion);
public sealed record StudyCellResponse(int Week, string Day, string SlotId, string Subject, string Room);

public sealed class ScheduleException(int status, string code, string message, string? field = null) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string? Field { get; } = field;
}
public static class ScheduleRules
{
    public static readonly string[] Days = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];
    public static void Require(bool condition, int status, string code, string message, string? field = null)
    { if (!condition) throw new ScheduleException(status, code, message, field); }
    public static void Period(int year, string term) => Require(year is >= 2000 and <= 2100 && term is "SP" or "SU" or "FA",
        400, "INVALID_PERIOD", "Use year 2000–2100 and term SP/SU/FA.", "period");
    public static int Weeks(string block)
    {
        Require(block is "10W" or "3W", 400, "INVALID_BLOCK", "Use 10W or 3W.", "block");
        return block == "10W" ? 10 : 3;
    }
    public static int Slot(string block, string day, string slotId)
    {
        var weeks = Weeks(block);
        Require(Days.Contains(day), 400, "INVALID_DAY", "Use mon through sun.", "day");
        Require(slotId is { Length: 6 } && slotId.StartsWith("slot-", StringComparison.Ordinal)
            && slotId[5] >= '1' && slotId[5] <= (weeks == 10 ? '5' : '7'),
            400, "INVALID_SLOT", "Use a slot supported by the selected block.", "slotId");
        return slotId[5] - '0';
    }
    public static (string Subject, string Room) Text(string? subject, string? room)
    {
        var s = subject?.Trim() ?? ""; var r = room?.Trim() ?? "";
        Require(s.Length is >= 1 and <= 120 && !s.Any(char.IsControl), 400, "INVALID_SUBJECT", "Subject must contain 1–120 characters.", "subject");
        Require(r.Length <= 80 && !r.Any(char.IsControl), 400, "INVALID_ROOM", "Room must contain at most 80 characters.", "room");
        return (s, r);
    }
}
public sealed class ScheduleExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (ScheduleException e)
        {
            return Results.Json(new
            {
                e.Code,
                message = e.Message,
                traceId = context.HttpContext.TraceIdentifier,
                errors = e.Field is null ? new Dictionary<string, string[]>() : new() { [e.Field] = [e.Message] }
            }, statusCode: e.Status);
        }
        catch (BadHttpRequestException e) { return ApiOperations.Error(e.StatusCode, "INVALID_REQUEST", e.Message); }
        catch (JsonException) { return ApiOperations.Error(400, "INVALID_BODY", "Use the documented fields and types."); }
        catch (Exception e) when (SqlConflict(e)) { return ApiOperations.Error(409, "SCHEDULE_CHANGED", "A concurrent transaction won; reload before retrying."); }
        catch (HttpRequestException) { return ApiOperations.Error(503, "CALENDAR_AUTHORITY_UNAVAILABLE", "Calendar/account authority is unavailable."); }
        catch (TaskCanceledException) when (!context.HttpContext.RequestAborted.IsCancellationRequested)
        { return ApiOperations.Error(503, "CALENDAR_AUTHORITY_UNAVAILABLE", "Calendar/account authority timed out."); }
    }
    private static bool SqlConflict(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is Microsoft.Data.SqlClient.SqlException { Number: 1205 or 3960 }) return true;
        return false;
    }
}
public sealed class AcademicCalendarClient(HttpClient client, IConfiguration configuration)
{
    public async Task<AcademicCalendarReference> Read(int year, string term, HttpContext http, CancellationToken ct)
    {
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) throw new HttpRequestException("Calendar authority is unconfigured.");
        var campus = http.Request.Query["campusCode"].ToString();
        var query = string.IsNullOrEmpty(campus) ? "" : "?campusCode=" + Uri.EscapeDataString(campus);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/internal/academic-calendar/{year}/{term}" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", http.GetBearerToken());
        request.Headers.Add("X-Internal-Workflow-Token", secret);
        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.Forbidden)
        {
            JsonElement error;
            try { error = await response.Content.ReadFromJsonAsync<JsonElement>(ct); }
            catch (JsonException e) { throw new HttpRequestException("Invalid calendar authority response.", e); }
            if (error.ValueKind != JsonValueKind.Object || !error.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String
                || !error.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.String)
                throw new HttpRequestException("Invalid calendar authority response.");
            throw new ScheduleException((int)response.StatusCode, code.GetString()!, message.GetString()!);
        }
        response.EnsureSuccessStatusCode();
        var value = await response.Content.ReadFromJsonAsync<AcademicCalendarReference>(ct) ?? throw new HttpRequestException("Missing calendar source.");
        if (value.Year != year || value.Term != term || value.SemesterKey == Guid.Empty || value.Version == Guid.Empty
            || !CampusCodes.FiveCampuses.Contains(value.CampusCode)) throw new HttpRequestException("Invalid calendar source.");
        return value;
    }
}
