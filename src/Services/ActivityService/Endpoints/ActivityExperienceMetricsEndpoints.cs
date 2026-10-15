using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityEndpointHelpers;

namespace ActivityService.Endpoints;

public static class ActivityExperienceMetricsEndpoints
{
    public static void MapActivityExperienceMetricsEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/internal/experience/clubs/{clubId:int}/metrics", async (int clubId, string semesterId,
            ActivityDbContext db, HttpContext http, ClubAccessClient access, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
            if (!await CanManageClubOrReviewAllAsync(clubId, http.User, access, http, ct, true)) return Results.Forbid();
            var activities = db.Activities.Where(x => x.ClubId == clubId && x.SemesterId == semesterId
                && x.PublicationStatus != "DRAFT" && x.PublicationStatus != "LEGACY" && x.Status != ActivityStatuses.Cancelled);
            var ids = activities.Select(x => x.Id);
            var sessions = db.Sessions.Where(x => ids.Contains(x.ActivityId) && x.Status != "CANCELLED");
            var sessionIds = sessions.Select(x => x.Id);
            var attendance = db.ActivityAttendances.Where(x => x.SessionId != null && sessionIds.Contains(x.SessionId.Value) && x.VerificationStatus == "VERIFIED");
            var now = ActivitySessionEndpoints.Now(http);
            var metrics = new ActivityExperienceMetrics(await activities.CountAsync(ct),
                await activities.CountAsync(x => x.Status == ActivityStatuses.Completed, ct), await sessions.CountAsync(ct),
                await sessions.CountAsync(x => x.Status == "CLOSED" && x.EndTimeUtc <= now, ct), await attendance.CountAsync(ct),
                await attendance.Select(x => x.UserId).Distinct().CountAsync(ct), await attendance.CountAsync(x => x.RecognitionStatus == "PENDING", ct));
            var current = await (from a in attendance
                                 join s in db.Sessions on a.SessionId equals s.Id
                                 select new VerifiedParticipation(a.UserId, s.StartTimeUtc)).ToListAsync(ct);
            var users = current.Select(x => x.UserId).Distinct().ToArray();
            var global = await (from a in db.ActivityAttendances
                                where a.VerificationStatus == "VERIFIED" && users.Contains(a.UserId)
                                join s in db.Sessions on a.SessionId equals s.Id
                                where s.Status != "CANCELLED" && s.Activity.Status != ActivityStatuses.Cancelled
                                select new VerifiedParticipation(a.UserId, s.StartTimeUtc)).ToListAsync(ct);
            return Results.Ok(new InternalActivityExperienceMetrics(metrics, current,
                global.GroupBy(x => x.UserId).Select(x => new VerifiedParticipation(x.Key, x.Min(r => r.OccurredAt))).ToArray()));
        }).RequireAuthorization(AuthPolicies.BusinessAccess).ExcludeFromDescription();
}
