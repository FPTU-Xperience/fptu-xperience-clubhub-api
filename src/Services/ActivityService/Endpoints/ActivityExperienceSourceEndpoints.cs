using ActivityService.Data;
using ActivityService.Models;
using ActivityService.Services;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Endpoints;

public static class ActivityExperienceSourceEndpoints
{
    public static void MapActivityExperienceSourceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/internal/experience/activities/{activityId:int}/scope", async (
            int activityId, ActivityDbContext db, HttpContext http, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
            var row = await db.Activities.AsNoTracking().Where(x => x.Id == activityId)
                .Select(x => new ActivityExperienceScope(x.Id, x.ClubId)).SingleOrDefaultAsync(ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        }).AllowAnonymous().ExcludeFromDescription();
        app.MapGet("/api/internal/experience/activities/{activityId:int}/sessions/{sessionId:int}", async (
            int activityId, int sessionId, int userId, ActivityDbContext db, HttpContext http,
            IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
            var session = await db.Sessions.AsNoTracking().Include(x => x.Activity).SingleOrDefaultAsync(
                x => x.Id == sessionId && x.ActivityId == activityId, cancellationToken);
            if (session is null) return Results.NotFound();
            var source = ActivitySessionEndpoints.Source(session);
            if (session.Activity.PublicationStatus is "CANCELLED" or "DRAFT" || session.Activity.Status == ActivityStatuses.Cancelled)
                source = source with { Status = "CANCELLED" };
            var registered = await db.ActivityParticipants.AnyAsync(x => x.ActivityId == activityId && x.UserId == userId, cancellationToken);
            try
            {
                if (registered) registered = await ActivityEligibility.ResolveAsync(session.Activity, userId, session.StartTimeUtc,
                    http.RequestServices.GetRequiredService<CurrentUserProfileClient>(), http.RequestServices.GetRequiredService<ClubExperienceAuthorityClient>(), cancellationToken) is not null;
            }
            catch (HttpRequestException) { return ApiOperations.Error(503, "ELIGIBILITY_AUTHORITY_UNAVAILABLE", "Current source eligibility is unavailable."); }
            var verified = await db.ActivityAttendances.AnyAsync(x => x.SessionId == sessionId && x.UserId == userId
                && x.VerificationStatus == "VERIFIED", cancellationToken);
            return Results.Ok(new ActivitySourceContext(source, registered, verified));
        }).AllowAnonymous().ExcludeFromDescription();
    }
}
