using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Endpoints;

internal static class ActivityActionHelpers
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal const string ScoringStatus = "DEFERRED_BY_PRODUCT_OWNER";
    internal sealed class WorkflowException(int status, string code, string message) : Exception(message)
    {
        internal int Status { get; } = status;
        internal string Code { get; } = code;
    }
    internal static void Require(bool valid, int status, string code, string message)
    {
        if (!valid) throw new WorkflowException(status, code, message);
    }
    internal static RouteGroupBuilder SafeGroup(IEndpointRouteBuilder app, string path)
    {
        var group = app.MapGroup(path).RequireAuthorization(AuthPolicies.BusinessAccess);
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (WorkflowException e) { return ApiOperations.Error(e.Status, e.Code, e.Message); }
            catch (JsonException) { return ApiOperations.Error(400, "INVALID_BODY", "Use the documented JSON fields and types."); }
            catch (Exception e) when (IsSqlWriteConflict(e))
            { return ApiOperations.Error(409, "CONCURRENT_UPDATE", "Another operation won the transaction. Reload before retrying."); }
            catch (HttpRequestException) { return ApiOperations.Error(503, "AUTHORITY_UNAVAILABLE", "A required authority could not be verified. Retry later."); }
            catch (TaskCanceledException) when (!context.HttpContext.RequestAborted.IsCancellationRequested)
            { return ApiOperations.Error(503, "AUTHORITY_UNAVAILABLE", "An authority timed out."); }
        });
        return group;
    }
    private static bool IsSqlWriteConflict(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is Microsoft.Data.SqlClient.SqlException { Number: 1205 or 3960 }) return true;
        return false;
    }
    internal static async Task<T> Body<T>(HttpContext http, CancellationToken ct)
    {
        const int limit = 256 * 1024;
        Require(http.Request.ContentType?.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase) == true,
            415, "JSON_REQUIRED", "Use application/json.");
        Require(http.Request.ContentLength is null or <= limit, 413, "PAYLOAD_TOO_LARGE", "The maximum payload is 256 KiB.");
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = await http.Request.Body.ReadAsync(bytes, ct)) > 0)
        {
            Require(buffer.Length + count <= limit, 413, "PAYLOAD_TOO_LARGE", "The maximum payload is 256 KiB.");
            buffer.Write(bytes, 0, count);
        }
        using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 20 });
        UniqueFields(document.RootElement);
        if (typeof(T) == typeof(ActivityPublicationInput))
            Require(document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.EnumerateObject().Any(x => x.Name.Equals("isPublished", StringComparison.OrdinalIgnoreCase)),
                400, "PUBLICATION_FLAG_REQUIRED", "Provide isPublished explicitly.");
        return document.RootElement.Deserialize<T>(Json) ?? throw new JsonException();
    }
    private static void UniqueFields(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in value.EnumerateObject())
            {
                Require(names.Add(field.Name), 400, "DUPLICATE_FIELD", "Duplicate JSON fields are not accepted.");
                UniqueFields(field.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) UniqueFields(item);
    }
    internal static DateTimeOffset Now(HttpContext http) =>
        http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;

    internal sealed record Scope(InternalUserProfile Account, ClubExperienceAuthority Club, bool Manager, bool Reviewer);
    internal static async Task<Scope> Authorize(int clubId, HttpContext http, CancellationToken ct)
    {
        var actor = http.User.GetUserId();
        var account = (await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([actor], ct)).GetValueOrDefault(actor);
        Require(account is { IsActive: true, IsLocked: false }, 403, "ACCOUNT_UNAVAILABLE", "An active account is required.");
        var club = await http.RequestServices.GetRequiredService<ClubExperienceAuthorityClient>().GetAsync(clubId, actor, ct);
        Require(club is { IsActive: true }, 404, "CLUB_UNAVAILABLE", "The club is unavailable.");
        Require(http.User.IsInRole(AuthRoles.Admin) || account!.CampusCode == club!.CampusCode,
            403, "CAMPUS_SCOPE_DENIED", "This club belongs to another campus.");
        var scopes = await http.RequestServices.GetRequiredService<ClubAccessClient>()
            .GetMyAccessAsync(http.GetBearerToken(), ct, true);
        var manager = scopes.Any(x => x.ClubId == clubId && x.CanManage);
        return new(account!, club!, manager,
            http.User.IsInRole(AuthRoles.Admin) || http.User.IsInRole(AuthRoles.StudentAffairsAdmin));
    }
    internal static void Manager(Scope scope) => Require(scope.Manager, 403, "MANAGEMENT_REQUIRED", "Current club management is required.");
    internal static bool Mutable(ClubActivity activity, HttpContext http) =>
        activity.Status == ActivityStatuses.Scheduled && activity.EndTimeUtc > Now(http);
    internal static void Expected(int expected, int actual) =>
        Require(expected > 0 && expected == actual && actual < int.MaxValue, 409, "STALE_VERSION", "Reload the current version before retrying.");

    internal static ActivityApplicationInput Normalize(ActivityApplicationInput value) => value with
    {
        Period = value.Period?.Trim(),
        Title = value.Title?.Trim() ?? "",
        Description = value.Description?.Trim() ?? "",
        Objective = value.Objective?.Trim() ?? "",
        Location = value.Location?.Trim() ?? "",
        ExpectedVersion = null,
        Actions = value.Actions?.Select(x => x is null ? throw new JsonException() : x with { Name = x.Name?.Trim() ?? "" }).ToArray() ?? []
    };
    internal static void Validate(ActivityApplicationInput value, bool submit, HttpContext http)
    {
        Require(value.ClubId > 0 && !string.IsNullOrWhiteSpace(value.Period) && value.Period.Length <= 40
            && value.Title?.Length <= 200 && value.Description?.Length <= 2000 && value.Objective?.Length <= 2000
            && value.Location?.Length <= 200 && ActivityScales.All.Contains(value.Scale)
            && value.Actions is { Count: <= 100 }, 400, "INVALID_APPLICATION", "Check content lengths, semester, scale and action count.");
        Require(!value.StartTimeUtc.HasValue || !value.EndTimeUtc.HasValue || value.EndTimeUtc > value.StartTimeUtc,
            400, "INVALID_SCHEDULE", "The activity must end after it starts.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in value.Actions!)
        {
            Require(!string.IsNullOrWhiteSpace(action.ClientId) && action.ClientId.Length <= 80 && ids.Add(action.ClientId)
                && action.Name?.Length <= 200 && action.BasePoints is > 0 and <= 100000
                && decimal.Round(action.BasePoints, 4) == action.BasePoints
                && ContributionRoles.All.Contains(action.ContributionRole)
                && action.ScaleSource is "ACTIVITY" or "OVERRIDE"
                && (action.ScaleSource == "ACTIVITY" ? action.Scale is null : ActivityScales.All.Contains(action.Scale))
                && action.ExpectedPeople is >= 1 and <= 100000 && action.ExpectedOccurrencesPerPerson is >= 1 and <= 1000
                && action.MaxAwardsPerPerson >= action.ExpectedOccurrencesPerPerson && action.MaxAwardsPerPerson <= 1000
                && action.MaxAwardCount >= (long)action.ExpectedPeople * action.ExpectedOccurrencesPerPerson
                && action.MaxAwardCount <= 1000000 && action.VerificationMethod is "ATTENDANCE" or "WORK_REVIEW"
                && action.CompletionMode == "FULL_ONLY" && action.StackingRule == "ADDITIVE",
                400, "INVALID_ACTION", "Use distinct action IDs, canonical codes, four-decimal positive points and bounded counts covering the plan.");
        }
        if (value.Recruitment is { } recruitment)
        {
            Require(recruitment.Visibility is "PUBLIC" or "PRIVATE" && recruitment.Eligibility is "SCHOOL_STUDENTS" or "CLUB_MEMBERS"
                && (recruitment.Visibility != "PRIVATE" || recruitment.Eligibility == "CLUB_MEMBERS")
                && recruitment.Positions is { Count: > 0 and <= 100 }, 400, "INVALID_RECRUITMENT", "Check recruitment scope and positions.");
            var positions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var position in recruitment.Positions!)
            {
                Require(position is not null && ids.Contains(position.ActionClientId) && positions.Add(position.ActionClientId)
                    && position.Capacity > 0 && position.Capacity <= value.Actions.Single(x => x.ClientId == position.ActionClientId).ExpectedPeople,
                    400, "INVALID_POSITION", "Positions must reference distinct actions within planned capacity.");
            }
        }
        if (submit)
            Require(!string.IsNullOrWhiteSpace(value.Title) && !string.IsNullOrWhiteSpace(value.Description)
                && !string.IsNullOrWhiteSpace(value.Objective) && !string.IsNullOrWhiteSpace(value.Location)
                && value.IndependentActionsConfirmed && value.Actions.Count > 0
                && value.Actions.All(x => !string.IsNullOrWhiteSpace(x.Name))
                && value.StartTimeUtc > Now(http) && value.EndTimeUtc > value.StartTimeUtc,
                400, "INCOMPLETE_APPLICATION", "Complete the dossier, independent actions and future schedule before submission.");
    }
    internal static async Task<SemesterReference> Semester(ActivityApplicationInput input, HttpContext http, CancellationToken ct)
    {
        var semester = await http.RequestServices.GetRequiredService<SemesterCatalogClient>().ResolveAsync(input.Period!, http, ct);
        Require(semester is { IsActive: true, StartDate: not null, EndDate: not null }, 409, "SEMESTER_UNAVAILABLE", "Use an active authoritative semester with dates.");
        Require((!input.StartTimeUtc.HasValue || input.StartTimeUtc >= semester!.StartDate)
            && (!input.EndTimeUtc.HasValue || input.EndTimeUtc < semester!.EndDate!.Value.AddDays(1)),
            400, "OUTSIDE_SEMESTER", "The activity must fit the authoritative semester bounds.");
        return semester!;
    }
    internal static void History(ActivityDbContext db, ActivityApplication value, HttpContext http, string? reason = null) =>
        db.Add(new ActivityApplicationHistory
        {
            ApplicationId = value.Id,
            Version = value.Version,
            Revision = value.Revision,
            Status = value.Status,
            ContentJson = value.ContentJson,
            ActorId = http.User.GetUserId(),
            Reason = reason,
            CreatedAt = Now(http)
        });
    internal static void Audit(ActivityDbContext db, ActivityExecution value, HttpContext http, string action, object snapshot) =>
        db.Add(new ActivityExecutionAudit
        {
            ActivityId = value.ActivityId,
            Version = value.Version,
            ActorId = http.User.GetUserId(),
            Action = action,
            SnapshotJson = JsonSerializer.Serialize(snapshot, Json),
            CreatedAt = Now(http)
        });
    internal static async Task<object> ApplicationResponse(ActivityDbContext db, ActivityApplication row, Scope scope, CancellationToken ct)
    {
        var content = JsonSerializer.Deserialize<ActivityApplicationInput>(row.ContentJson, Json)!;
        var activityId = await db.Set<ActivityExecution>().Where(x => x.ApplicationId == row.Id).Select(x => (int?)x.ActivityId).SingleOrDefaultAsync(ct);
        return new
        {
            row.Id,
            row.Version,
            row.Revision,
            row.Status,
            row.ClubId,
            content.Period,
            content.Title,
            content.Description,
            content.Objective,
            content.Location,
            content.StartTimeUtc,
            content.EndTimeUtc,
            content.Scale,
            content.IsInternal,
            content.IndependentActionsConfirmed,
            content.Actions,
            content.Recruitment,
            activityId,
            expectedRawXp = (decimal?)null,
            scoringStatus = ScoringStatus,
            canAwardPoints = false,
            capabilities = new { canEdit = scope.Manager && row.Status is "DRAFT" or "REVISION_REQUESTED", canSubmit = scope.Manager && row.Status is "DRAFT" or "REVISION_REQUESTED", canReview = scope.Reviewer && row.CreatedBy != scope.Account.Id && row.Status == "SUBMITTED" },
            row.CreatedAt,
            row.UpdatedAt
        };
    }
}
