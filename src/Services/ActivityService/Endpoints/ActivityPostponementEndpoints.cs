using System.Text.Json;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityActionHelpers;

namespace ActivityService.Endpoints;

internal static class ActivityPostponementEndpoints
{
    internal static void MapActivityPostponementEndpoints(this IEndpointRouteBuilder app)
    {
        var routes = SafeGroup(app, "/api/activities/{activityId:int}/postponement").WithTags("Activity postponement");
        routes.MapGet("", Read);
        routes.MapPost("", Postpone);
        routes.MapPost("/resume", Resume);
    }

    private static async Task<(ClubActivity Activity, Scope Scope)> Load(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var activity = await db.Activities.SingleOrDefaultAsync(x => x.Id == id, ct);
        Require(activity is not null, 404, "ACTIVITY_NOT_FOUND", "Activity does not exist.");
        var scope = await CampusActivityEndpoints.OrganizerScope(activity!, db, http, ct);
        Require(scope.Can("event.content.write") || scope.Reviewer, 403, "POSTPONEMENT_AUTHORITY_REQUIRED", "Current organization or campus review authority is required.");
        return (activity!, scope);
    }

    private static async Task<IResult> Read(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var (activity, scope) = await Load(activityId, db, http, ct);
        var history = await db.SessionAudits.AsNoTracking().Where(x => x.ActivityId == activityId && (x.Action == "POSTPONE" || x.Action == "RESUME"))
            .OrderByDescending(x => x.Id).Take(50).Select(x => new { x.Action, x.Reason, x.ActorId, x.CreatedAtUtc, x.Version }).ToListAsync(ct);
        return Results.Ok(new
        {
            activity.Version, activity.Status, activity.StartTimeUtc, activity.EndTimeUtc,
            canPostpone = activity.Status == ActivityStatuses.Scheduled && activity.StartTimeUtc > Now(http),
            canResume = scope.Reviewer && activity.Status == ActivityStatuses.Postponed,
            history
        });
    }

    private sealed record Input(Guid ExpectedVersion, string? Reason, DateTimeOffset? StartTimeUtc = null, DateTimeOffset? EndTimeUtc = null);
    private static Task<IResult> Postpone(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct) => Change(activityId, false, db, http, ct);
    private static Task<IResult> Resume(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct) => Change(activityId, true, db, http, ct);
    private static async Task<IResult> Change(int activityId, bool resume, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        await Load(activityId, db, http, ct);
        var input = await Body<Input>(http, ct); var reason = input.Reason?.Trim();
        Require(reason is { Length: >= 5 and <= 1000 }, 400, "REASON_REQUIRED", "Provide a reason of 5–1000 characters.");
        Require(resume || input.StartTimeUtc is null && input.EndTimeUtc is null, 400, "RESUME_REQUIRES_REVIEW", "Postpone without inventing a new date; campus review sets the resumed schedule.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            var (activity, scope) = await Load(activityId, db, http, ct);
            Require(activity.Version == input.ExpectedVersion && activity.Status == (resume ? ActivityStatuses.Postponed : ActivityStatuses.Scheduled),
                409, "STALE_ACTIVITY", "Reload the current activity version and state.");
            var now = Now(http);
            Require(resume || activity.StartTimeUtc > now, 409, "ACTIVITY_ALREADY_STARTED", "Only an activity that has not started may be postponed.");
            Require(!await db.ActivityAttendances.AnyAsync(x => x.ActivityId == activityId, ct)
                && !await db.Set<ActivityActionCompletion>().AnyAsync(x => x.ActivityId == activityId, ct)
                && !await db.Set<ActivityClosingReport>().AnyAsync(x => x.ActivityId == activityId, ct),
                409, "ACTIVITY_HISTORY_PROTECTED", "Recorded attendance, task completion and closing reports cannot be moved to another schedule.");
            var before = new { activity.Status, activity.StartTimeUtc, activity.EndTimeUtc, activity.Version };
            var sessions = await db.Sessions.Where(x => x.ActivityId == activityId && x.Status != "CANCELLED").ToListAsync(ct);
            if (resume)
            {
                Require(scope.Reviewer, 403, "CAMPUS_REVIEW_REQUIRED", "Campus review authority must confirm the resumed schedule.");
                Require(input.StartTimeUtc.HasValue && input.EndTimeUtc.HasValue && input.StartTimeUtc > now && input.EndTimeUtc > input.StartTimeUtc,
                    400, "INVALID_RESUME_DATES", "Choose a valid future start and end.");
                var semester = await http.RequestServices.GetRequiredService<SemesterCatalogClient>().ResolveAsync(activity.SemesterId ?? "", http, ct);
                Require(semester is { IsActive: true, StartDate: not null, EndDate: not null }
                    && input.StartTimeUtc >= semester.StartDate && input.EndTimeUtc < semester.EndDate.Value.AddDays(1),
                    400, "ACTIVITY_OUTSIDE_SEMESTER", "The resumed activity must fit its authoritative active semester.");
                var shift = input.StartTimeUtc!.Value - activity.StartTimeUtc;
                Require(sessions.All(x => x.Status == "SCHEDULED" && x.StartTimeUtc + shift >= input.StartTimeUtc
                    && x.EndTimeUtc + shift <= input.EndTimeUtc), 409, "SESSION_SCHEDULE_CONFLICT", "The resumed interval must contain every shifted session.");
                activity.StartTimeUtc = input.StartTimeUtc.Value; activity.EndTimeUtc = input.EndTimeUtc!.Value;
                activity.Status = ActivityStatuses.Scheduled; activity.Version = Guid.NewGuid();
                foreach (var session in sessions)
                {
                    session.StartTimeUtc += shift; session.EndTimeUtc += shift; session.OpensAt += shift; session.ClosesAt += shift;
                    session.Version = Guid.NewGuid(); session.UpdatedAtUtc = now;
                    session.ScoringSnapshotJson = ShiftSnapshot(session.ScoringSnapshotJson, activity.Version, session.Version, session.StartTimeUtc, session.EndTimeUtc) ?? "";
                }
                activity.ScoringSnapshotJson = ShiftSnapshot(activity.ScoringSnapshotJson, activity.Version, Guid.Empty, activity.StartTimeUtc, activity.EndTimeUtc);
            }
            else
            {
                activity.Status = ActivityStatuses.Postponed; activity.Version = Guid.NewGuid();
                foreach (var session in sessions) { session.Version = Guid.NewGuid(); session.UpdatedAtUtc = now; }
            }
            var execution = await db.Set<ActivityExecution>().SingleOrDefaultAsync(x => x.ActivityId == activityId, ct);
            if (execution is not null) { execution.PublishAtUtc = null; execution.Version++; execution.PublicationVersion++; }
            activity.CampusPublishAtUtc = null; activity.UpdatedAtUtc = now;
            // Venue/equipment capacity is released on postponement. Resumption requires
            // fresh bookings; an old confirmation never silently reserves a new time.
            foreach (var booking in await db.Set<ActivityBooking>().Where(x => x.Session.ActivityId == activityId && (x.Status == "PENDING" || x.Status == "CONFIRMED")).ToListAsync(ct))
            { booking.Status = "CANCELLED"; booking.Version = Guid.NewGuid(); booking.ProcessedAtUtc = now; booking.ProcessedByUserId = scope.Account.Id; }
            db.SessionAudits.Add(new ActivitySessionAudit
            {
                ActivityId = activityId, Action = resume ? "RESUME" : "POSTPONE", Reason = reason, ActorId = scope.Account.Id,
                Version = activity.Version, CreatedAtUtc = now, SnapshotJson = JsonSerializer.Serialize(new { before, after = new { activity.Status, activity.StartTimeUtc, activity.EndTimeUtc, activity.Version } }, Json)
            });
            var recipients = await db.ActivityParticipants.Where(x => x.ActivityId == activityId).Select(x => x.UserId).Distinct().ToArrayAsync(ct);
            db.AddOutboxMessage(new ExperienceNotificationEvent(Guid.NewGuid(), now, activity.ClubId, recipients,
                resume ? "ACTIVITY_RESUMED" : "ACTIVITY_POSTPONED", resume ? "Hoạt động đã có lịch tiếp tục" : "Hoạt động tạm hoãn", $"/activities/{activityId}"), ExperienceRoutingKeys.Notification);
            return Results.Ok(new { activity.Version, activity.Status, activity.StartTimeUtc, activity.EndTimeUtc });
        }, ct);
    }

    private static string? ShiftSnapshot(string? json, Guid activityVersion, Guid sessionVersion, DateTimeOffset start, DateTimeOffset end)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        var snapshot = JsonSerializer.Deserialize<ExperienceSourceSnapshot>(json, Json);
        Require(snapshot is not null, 409, "SCORING_SOURCE_INVALID", "The existing approved scoring source must be valid.");
        return JsonSerializer.Serialize(snapshot! with { ActivityVersion = activityVersion, SessionVersion = sessionVersion, StartTimeUtc = start, EndTimeUtc = end }, Json);
    }
}
