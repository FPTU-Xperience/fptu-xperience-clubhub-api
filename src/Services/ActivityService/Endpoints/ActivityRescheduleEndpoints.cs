using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityActionHelpers;

namespace ActivityService.Endpoints;

internal static class ActivityRescheduleEndpoints
{
    internal static void MapActivityRescheduleEndpoints(this IEndpointRouteBuilder app)
    {
        var activities = SafeGroup(app, "/api/activities").WithTags("Activity rescheduling");
        activities.MapGet("/{activityId:int}/reschedule-requests", List);
        activities.MapPost("/{activityId:int}/reschedule-requests", Create);
        activities.MapPost("/{activityId:int}/reschedule-requests/{requestId:guid}/review", Review);
    }

    private static async Task<IResult> List(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var (activity, _, scope) = await Load(activityId, db, http, ct);
        Require(scope.Manager || scope.Reviewer, 403, "RESCHEDULE_ACCESS_DENIED", "Current club management or review authority is required.");
        var now = Now(http);
        var items = await db.Set<ActivityRescheduleRequest>().AsNoTracking()
            .Where(r => r.ActivityId == activityId).ToListAsync(ct);
        var canChange = await IsBeforeOnAir(activity, db, now, ct);
        return Results.Ok(new
        {
            canRequest = scope.Manager && canChange,
            canReview = scope.Reviewer && canChange,
            items = items.OrderByDescending(r => r.RequestedAtUtc).Take(50).Select(r => Response(r, now))
        });
    }

    private static async Task<IResult> Create(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var (activity, _, scope) = await Load(activityId, db, http, ct);
        Manager(scope);
        var input = await Body<RescheduleInput>(http, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(activity).ReloadAsync(ct);
            var now = Now(http);
            Require(await IsBeforeOnAir(activity, db, now, ct), 409, "RESCHEDULE_LOCKED", "Rescheduling closes 48 hours before the activity starts or when it goes public.");
            Require(input.ExpectedActivityVersion == activity.Version, 409, "STALE_VERSION", "Reload the activity.");
            var reason = input.Reason?.Trim();
            Require(reason is { Length: > 0 and <= 2000 }, 400, "RESCHEDULE_REASON_REQUIRED", "Provide a reason of at most 2000 characters.");
            await ValidateDates(activity, input.StartTimeUtc, input.EndTimeUtc, db, http, ct);
            Require(!await db.Set<ActivityRescheduleRequest>().AnyAsync(r => r.ActivityId == activityId && r.Status == "PENDING", ct),
                409, "RESCHEDULE_ALREADY_PENDING", "This activity already has a pending request.");
            var row = new ActivityRescheduleRequest
            {
                Id = Guid.NewGuid(), ActivityId = activityId, Version = Guid.NewGuid(),
                RequestedBy = scope.Account.Id, RequestedAtUtc = now,
                OriginalStartTimeUtc = activity.StartTimeUtc, OriginalEndTimeUtc = activity.EndTimeUtc,
                StartTimeUtc = input.StartTimeUtc, EndTimeUtc = input.EndTimeUtc,
                ReviewDeadlineUtc = activity.StartTimeUtc.AddDays(-2), Reason = reason!
            };
            db.Add(row);
            return Results.Ok(Response(row, now));
        }, ct);
    }

    private static async Task<IResult> Review(int activityId, Guid requestId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var (activity, execution, scope) = await Load(activityId, db, http, ct);
        Require(scope.Reviewer, 403, "RESCHEDULE_REVIEW_DENIED", "Current campus review authority is required.");
        var input = await Body<RescheduleReview>(http, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            var row = await db.Set<ActivityRescheduleRequest>().SingleOrDefaultAsync(r => r.ActivityId == activityId && r.Id == requestId, ct);
            if (row is null) return Results.NotFound();
            var now = Now(http);
            Require(scope.Account.Id != row.RequestedBy, 403, "INDEPENDENT_REVIEW_REQUIRED", "The requester cannot review their own request.");
            Require(row.Status == "PENDING" && now < row.ReviewDeadlineUtc, 409, "RESCHEDULE_EXPIRED_OR_CLOSED", "The request is expired or already reviewed.");
            Require(row.Version == input.ExpectedVersion, 409, "STALE_VERSION", "Reload the request.");
            var note = input.Note?.Trim();
            Require(input.Decision is "APPROVED" or "REJECTED" && (note?.Length ?? 0) <= 2000,
                400, "INVALID_RESCHEDULE_DECISION", "Choose approval or rejection and use a note of at most 2000 characters.");
            if (input.Decision == "APPROVED")
            {
                await db.Entry(activity).ReloadAsync(ct);
                await db.Entry(execution).ReloadAsync(ct);
                Require(await IsBeforeOnAir(activity, db, now, ct)
                    && activity.StartTimeUtc == row.OriginalStartTimeUtc && activity.EndTimeUtc == row.OriginalEndTimeUtc,
                    409, "RESCHEDULE_LOCKED", "The activity went public, its dates changed, or its review period ended.");
                await ValidateDates(activity, row.StartTimeUtc, row.EndTimeUtc, db, http, ct);
                Require(Now(http) < row.ReviewDeadlineUtc, 409, "RESCHEDULE_EXPIRED_OR_CLOSED", "The request expired during review.");
                activity.StartTimeUtc = row.StartTimeUtc;
                activity.EndTimeUtc = row.EndTimeUtc;
                activity.UpdatedAtUtc = now;
                activity.Version = Guid.NewGuid();
                execution.Version++;
                execution.PublicationVersion++;
                Audit(db, execution, http, "RESCHEDULE_APPROVED", new { requestId, row.StartTimeUtc, row.EndTimeUtc });
            }
            row.Status = input.Decision!;
            row.Version = Guid.NewGuid();
            row.ReviewedBy = scope.Account.Id;
            row.ReviewedAtUtc = now;
            row.ReviewNote = note;
            return Results.Ok(Response(row, now));
        }, ct);
    }

    private static async Task<(ClubActivity Activity, ActivityExecution Execution, Scope Scope)> Load(
        int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var activity = await db.Activities.SingleOrDefaultAsync(a => a.Id == activityId, ct);
        Require(activity is not null, 404, "ACTIVITY_NOT_FOUND", "Activity does not exist.");
        var scope = await Authorize(activity!.ClubId, http, ct);
        var execution = await db.Set<ActivityExecution>().SingleOrDefaultAsync(e => e.ActivityId == activityId, ct);
        Require(execution is not null, 409, "ACTIVITY_PLAN_REQUIRED", "This activity does not have an approved opening plan.");
        return (activity, execution!, scope);
    }

    private static async Task<bool> IsBeforeOnAir(ClubActivity activity, ActivityDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        if (activity.Status != ActivityStatuses.Scheduled || activity.PublicationStatus != "DRAFT"
            || now >= activity.StartTimeUtc.AddDays(-2)) return false;
        var history = await db.Set<ActivityExecutionAudit>().AsNoTracking()
            .Where(a => a.ActivityId == activity.Id && (a.Action == "PUBLICATION" || a.Action == "AUTO_PUBLISH"))
            .Select(a => new { a.Action, a.SnapshotJson }).ToListAsync(ct);
        return !history.Any(a => a.Action == "AUTO_PUBLISH"
            || JsonSerializer.Deserialize<ActivityPublicationInput>(a.SnapshotJson, Json) is { IsPublished: true, PublishAtUtc: null });
    }

    private static async Task ValidateDates(ClubActivity activity, DateTimeOffset start, DateTimeOffset end,
        ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var semester = activity.SemesterId is { } code
            ? await http.RequestServices.GetRequiredService<SemesterCatalogClient>().ResolveAsync(code, http, ct) : null;
        Require(semester is { IsActive: true, StartDate: not null, EndDate: not null }
            && start > Now(http) && end > start && start >= semester.StartDate && end < semester.EndDate.Value.AddDays(1)
            && (start != activity.StartTimeUtc || end != activity.EndTimeUtc),
            400, "INVALID_RESCHEDULE_DATES", "Choose different future dates within the active semester.");
        Require(activity.RegistrationClosesAt is null || activity.RegistrationClosesAt <= start,
            409, "RESCHEDULE_REGISTRATION_CONFLICT", "The current registration window must close by the proposed start.");
        var sessions = await db.Sessions.AsNoTracking().Where(s => s.ActivityId == activity.Id && s.Status != "CANCELLED")
            .Select(s => new { s.StartTimeUtc, s.EndTimeUtc, s.OpensAt, s.ClosesAt }).ToListAsync(ct);
        Require(!sessions.Any(s => s.StartTimeUtc < start || s.EndTimeUtc > end || s.OpensAt < start || s.ClosesAt > end),
            409, "RESCHEDULE_SESSION_CONFLICT", "All sessions and attendance windows must fit in the proposed dates.");
    }

    private static object Response(ActivityRescheduleRequest row, DateTimeOffset now) => new
    {
        row.Id, row.Version, row.OriginalStartTimeUtc, row.OriginalEndTimeUtc, row.StartTimeUtc, row.EndTimeUtc,
        row.Reason, row.RequestedAtUtc, row.ReviewDeadlineUtc, row.ReviewedAtUtc, row.ReviewNote,
        Status = row.Status == "PENDING" && now >= row.ReviewDeadlineUtc ? "EXPIRED" : row.Status
    };

    private sealed record RescheduleInput(Guid ExpectedActivityVersion, DateTimeOffset StartTimeUtc, DateTimeOffset EndTimeUtc, string? Reason);
    private sealed record RescheduleReview(Guid ExpectedVersion, string? Decision, string? Note);
}
