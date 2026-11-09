using System.Text.Json;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Endpoints;
internal static class ActivityRegistrationCancellation
{
    internal static async Task<IResult> CancelAsync(int id, JsonElement request, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        if (request.ValueKind != JsonValueKind.Object || request.EnumerateObject().Any(x => x.Name != "expectedVersion")
            || request.EnumerateObject().Count() != 1 || !request.TryGetProperty("expectedVersion", out var value)
            || value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var expected))
            return ApiOperations.Error(400, "INVALID_CANCELLATION", "Provide only the current registration version; cancellation always uses your own account.");
        var actor = http.User.GetUserId();
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            var activity = await db.Activities.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (activity is null) return Results.NotFound();
            var row = await db.ActivityParticipants.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ActivityId == id && x.UserId == actor, ct);
            if (row is null) return Results.NotFound();
            var now = ActivitySessionEndpoints.Now(http);
            if (row.Version != expected || row.CancelledAtUtc is not null) return ApiOperations.Error(409, "STALE_REGISTRATION", "Reload your current registration before cancelling.");
            if (activity.Status != ActivityStatuses.Scheduled || now >= activity.StartTimeUtc
                || await db.ActivityAttendances.AnyAsync(x => x.ActivityId == id && x.UserId == actor, ct))
                return ApiOperations.Error(409, "CANCELLATION_CLOSED", "Cancellation is only available before the activity starts and before attendance exists.");
            row.CancelledAtUtc = now; row.Version = Guid.NewGuid();
            var activeCount = await db.ActivityParticipants.CountAsync(x => x.ActivityId == id, ct);
            activity.RegisteredCount = Math.Max(0, activeCount - 1); activity.Version = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { activityId = id, userId = actor, isRegistered = false, registrationVersion = row.Version, cancelledAtUtc = row.CancelledAtUtc, registeredAtUtc = row.RegisteredAtUtc });
        }, ct);
    }
}
