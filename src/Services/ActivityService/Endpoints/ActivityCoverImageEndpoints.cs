using System.Text.Json.Serialization;
using ActivityService.Data;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityEndpointHelpers;

namespace ActivityService.Endpoints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActivityCoverImageRequest(string? CoverImageUrl, Guid ExpectedVersion);
public static class ActivityCoverImageEndpoints
{
    public static void MapActivityCoverImageEndpoints(this IEndpointRouteBuilder app) =>
        app.MapPut("/api/activities/{activityId:int}/cover-image", async (int activityId, ActivityCoverImageRequest request,
            ActivityDbContext db, ClubAccessClient access, [Microsoft.AspNetCore.Mvc.FromServices] MediaAttachmentClient media, HttpContext http, CancellationToken ct) =>
        {
            var activity = await db.Activities.SingleOrDefaultAsync(x => x.Id == activityId, ct);
            if (activity is null) return Results.NotFound();
            if (!await CanManageClubOrReviewAllAsync(activity.ClubId, http.User, access, http, ct, true)) return Results.Forbid();
            return await ApiOperations.ExecuteAsync(http, db, request, async () =>
            {
                if (request.ExpectedVersion != activity.Version) return ApiOperations.Error(409, "STALE_ACTIVITY", "Reload the activity.");
                var image = await media.VerifyAsync(request.CoverImageUrl, activity.CoverImageUrl, "activity-cover", activityId, http, ct);
                if (image.StatusCode != 200) return ApiOperations.Error(image.StatusCode, image.Code!, "The cover could not be attached.");
                activity.CoverImageUrl = image.PublicUrl; activity.Version = Guid.NewGuid(); await db.SaveChangesAsync(ct);
                return Results.Ok(ToResponse(activity));
            }, ct, required: false);
        }).RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Activity public images");
}
