using System.Text.Json;
using System.Text.Json.Serialization;
using ClubReportHub.Shared.Data;
using Microsoft.AspNetCore.Http;

namespace ClubReportHub.Shared.Rosters;

public sealed record SchoolAccount(int UserId, string Email, string FullName, string CampusCode,
    bool IsActive, bool IsLocked, bool IsStudent, bool IsSchoolEmail);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchoolAccountQuery(string[]? Emails = null, int[]? UserIds = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RosterRowInput(string Email, string StudentCode, string CampusCode, string Status, int? UserId = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RosterPreviewInput(int SchemaVersion, IReadOnlyList<RosterRowInput> Rows,
    [property: JsonRequired] Guid ExpectedRosterVersion, string Mode = "replace");
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RosterCommitInput([property: JsonRequired] Guid ExpectedRosterVersion);
public sealed record RosterCandidate(int UserId, string Email, string StudentCode, string CampusCode, bool Active);
public sealed record RosterImportError(int Row, string Field, string Code, string Message);
public sealed record RosterLookupQuery(string SemesterId, string CampusCode, string? Search = null,
    int Page = 1, int PageSize = 20, int[]? ExcludedUserIds = null, int? UserId = null, bool MetadataOnly = false);
public sealed record EligibleRosterStudent(int UserId, string FullName, string StudentCode, bool IsActive, bool IsLocked, bool Eligible);
public sealed record RosterLookupResult(Guid SemesterKey, string SemesterId, string CampusCode, string Availability,
    Guid Version, Guid? RevisionId, IReadOnlyList<EligibleRosterStudent> Items, int Page, int PageSize, int TotalItems, int TotalPages);
public sealed record RosterSummary(Guid SemesterKey, string SemesterId, Guid Version, Guid RevisionId, int ActiveCount);

public sealed class RosterException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public static class RosterErrors
{
    public static void Check(bool condition, int status, string code, string message)
    { if (!condition) throw new RosterException(status, code, message); }
}
public sealed class RosterExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (RosterException e) { return ApiOperations.Error(e.Status, e.Code, e.Message); }
        catch (BadHttpRequestException e) { return ApiOperations.Error(e.StatusCode, "INVALID_REQUEST", e.Message); }
        catch (JsonException) { return ApiOperations.Error(400, "INVALID_BODY", "Use the documented JSON fields and types."); }
        catch (Exception e) when (SqlConflict(e))
        { return ApiOperations.Error(409, "ROSTER_CHANGED", "A concurrent transaction won. Reload the roster before retrying."); }
        catch (System.Text.DecoderFallbackException) { return ApiOperations.Error(400, "INVALID_UTF8", "Use a UTF-8 file."); }
        catch (HttpRequestException) { return ApiOperations.Error(503, "ROSTER_AUTHORITY_UNAVAILABLE", "Account or roster authority is unavailable."); }
        catch (TaskCanceledException) when (!context.HttpContext.RequestAborted.IsCancellationRequested)
        { return ApiOperations.Error(503, "ROSTER_AUTHORITY_UNAVAILABLE", "An authority timed out."); }
    }
    private static bool SqlConflict(Exception error)
    {
        for (Exception? value = error; value is not null; value = value.InnerException)
            if (value is Microsoft.Data.SqlClient.SqlException { Number: 1205 or 3960 }) return true;
        return false;
    }
}
