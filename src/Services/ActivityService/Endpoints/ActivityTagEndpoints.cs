using System.Text.Json.Serialization;
using ActivityService.Data;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityEndpointHelpers;

namespace ActivityService.Endpoints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActivityTagUpdate(Guid ExpectedVersion, DiscoveryTagSelection? TagSelection);

internal static class ActivityTagEndpoints
{
    internal static async Task<IResult> UpdateAsync(int id, ActivityTagUpdate request,
        ActivityDbContext db, ClubAccessClient access, HttpContext http, CancellationToken ct)
    {
        if (request.TagSelection is null || !DiscoveryTags.TryNormalize(request.TagSelection, true, out var tags))
            return ApiOperations.Error(400, "INVALID_TAGS", DiscoveryTags.InvalidMessage);
        var activity = await db.Activities.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (activity is null) return Results.NotFound();
        if (!await CanManageClubOrReviewAllAsync(activity.ClubId, http.User, access, http, ct, true, "event.content.write"))
            return Results.Forbid();
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            if (request.ExpectedVersion != activity.Version)
                return ApiOperations.Error(409, "STALE_ACTIVITY", "Reload the activity.");
            var encoded = DiscoveryTags.Store(tags);
            if (activity.TagSelectionJson != encoded)
            {
                activity.TagSelectionJson = encoded;
                activity.Version = Guid.NewGuid();
                activity.UpdatedAtUtc = ActivitySessionEndpoints.Now(http);
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { activity.Version, TagSelection = tags });
        }, ct, required: false);
    }
}
