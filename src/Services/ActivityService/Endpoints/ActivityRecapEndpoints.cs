using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Data;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityActionHelpers;

namespace ActivityService.Endpoints;

internal static class ActivityRecapEndpoints
{
    private const int PageSize = 20;

    internal static void MapActivityRecapEndpoints(this IEndpointRouteBuilder app)
    {
        var routes = SafeGroup(app, "/api/activities").WithTags("Activity recap");
        routes.MapGet("/{activityId:int}/recap", Get);
        routes.MapPut("/{activityId:int}/recap/my-feedback", Save);
    }

    private static async Task<IResult> Get(int activityId, int? page, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var (activity, scope) = await Load(activityId, db, http, ct);
        Require(scope.Club.IsApprovedMember || scope.Manager, 403, "RECAP_ACCESS_DENIED", "Only current club members and managers can access recap.");
        var currentPage = page ?? 1;
        Require(currentPage is >= 1 and <= 10000, 400, "INVALID_RECAP_PAGE", "Choose a valid recap page.");
        var available = activity.Status == ActivityStatuses.Completed;
        var canSubmit = available && scope.Club.IsApprovedMember && !await IsClosed(activityId, db, ct);
        var feedback = db.Set<ActivityRecapFeedback>().AsNoTracking().Where(x => x.ActivityId == activityId);
        var own = available && scope.Club.IsApprovedMember
            ? await feedback.SingleOrDefaultAsync(x => x.UserId == scope.Account.Id, ct) : null;
        var rows = available && scope.Manager
            ? await feedback.OrderBy(x => x.UserId).Skip((currentPage - 1) * PageSize).Take(PageSize + 1).ToListAsync(ct)
            : [];
        return Results.Ok(new
        {
            available,
            canSubmit,
            canReview = available && scope.Manager,
            myFeedback = own is null ? null : Response(own),
            items = rows.Take(PageSize).Select(Response),
            hasMore = rows.Count > PageSize
        });
    }

    private static async Task<IResult> Save(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var (activity, scope) = await Load(activityId, db, http, ct);
        Require(scope.Club.IsApprovedMember, 403, "RECAP_MEMBERSHIP_REQUIRED", "An approved club membership is required.");
        var input = await Body<FeedbackInput>(http, ct);
        var strengths = input.Strengths?.Trim() ?? "";
        var weaknesses = input.Weaknesses?.Trim() ?? "";
        var suggestions = input.Suggestions?.Trim() ?? "";
        Require(strengths.Length <= 2000 && weaknesses.Length <= 2000 && suggestions.Length <= 2000
            && strengths.Length + weaknesses.Length + suggestions.Length > 0,
            400, "INVALID_RECAP_FEEDBACK", "Provide feedback in at least one field, up to 2000 characters per field.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(activity).ReloadAsync(ct);
            Require(activity.Status == ActivityStatuses.Completed, 409, "RECAP_NOT_OPEN", "Recap opens after the activity is completed.");
            Require(!await IsClosed(activityId, db, ct), 409, "RECAP_CLOSED", "The closing report was approved or finalized.");
            var own = await db.Set<ActivityRecapFeedback>().SingleOrDefaultAsync(x => x.ActivityId == activityId && x.UserId == scope.Account.Id, ct);
            Require(own?.Version == input.ExpectedVersion, 409, "STALE_RECAP_FEEDBACK", "Reload your feedback before saving.");
            if (own is null)
            {
                own = new ActivityRecapFeedback { ActivityId = activityId, UserId = scope.Account.Id };
                db.Add(own);
            }
            own.Strengths = strengths;
            own.Weaknesses = weaknesses;
            own.Suggestions = suggestions;
            own.UpdatedAtUtc = Now(http);
            own.Version = Guid.NewGuid();
            return Results.Ok(Response(own));
        }, ct);
    }

    private static async Task<(ClubActivity Activity, Scope Scope)> Load(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var activity = await db.Activities.SingleOrDefaultAsync(x => x.Id == id, ct);
        Require(activity is not null, 404, "ACTIVITY_NOT_FOUND", "Activity does not exist.");
        var scope = await CampusActivityEndpoints.OrganizerScope(activity!, db, http, ct);
        return (activity!, scope);
    }

    private static object Response(ActivityRecapFeedback row) =>
        new { row.Version, row.Strengths, row.Weaknesses, row.Suggestions, row.UpdatedAtUtc };

    private static Task<bool> IsClosed(int activityId, ActivityDbContext db, CancellationToken ct) =>
        db.Set<ActivityClosingReport>().AnyAsync(x => x.ActivityId == activityId && (x.Status == "APPROVED" || x.Status == "FINALIZED"), ct);

    private sealed record FeedbackInput(Guid? ExpectedVersion, string? Strengths, string? Weaknesses, string? Suggestions);
}
