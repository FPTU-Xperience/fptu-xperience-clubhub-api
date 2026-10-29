using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityEndpointHelpers;

namespace ActivityService.Endpoints;

public static class ActivityVisibilityEndpoints
{
    public static void MapActivityVisibilityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/api/activities/{id:int}/visibility", async (int id, ActivityVisibilityInput input,
            ActivityDbContext db, HttpContext http, ClubAccessClient access, CancellationToken ct) =>
        {
            var activity = await db.Activities.SingleOrDefaultAsync(a => a.Id == id, ct);
            if (activity is null) return Results.NotFound();
            if (!await CanManageClubOrReviewAllAsync(activity.ClubId, http.User, access, http, ct, true)) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 1000)
                return ApiOperations.Error(400, "REASON_REQUIRED", "Provide a visibility change reason.");
            return await ApiOperations.ExecuteAsync(http, db, input, async () =>
            {
                if (activity.Version != input.ExpectedVersion)
                    return ApiOperations.Error(409, "STALE_VERSION", "Reload the activity.");
                var before = activity.IsInternal;
                activity.IsInternal = input.IsInternal;
                activity.DiscoveryVisibility = input.IsInternal ? "CLUB_ONLY" : "CAMPUS_PUBLIC";
                activity.Version = Guid.NewGuid(); activity.UpdatedAtUtc = ActivitySessionEndpoints.Now(http);
                db.SessionAudits.Add(new ActivitySessionAudit
                {
                    ActivityId = id,
                    Action = "VISIBILITY",
                    ActorId = http.User.GetUserId(),
                    Reason = input.Reason.Trim(),
                    Version = activity.Version,
                    CreatedAtUtc = activity.UpdatedAtUtc,
                    SnapshotJson = JsonSerializer.Serialize(new { previousIsInternal = before, activity.IsInternal })
                });
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { activity.Id, activity.IsInternal, activity.Version });
            }, ct);
        }).RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Activities");
    }
}
