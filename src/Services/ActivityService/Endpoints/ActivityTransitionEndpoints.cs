using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityEndpointHelpers;

namespace ActivityService.Endpoints;

internal static class ActivityTransitionEndpoints
{
    internal static async Task<IResult> ApplyAsync(int id, bool cancel, ActivityDbContext db, HttpContext http,
        ClubAccessClient access, CancellationToken ct)
    {
        var row = await db.Activities.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return Results.NotFound();
        if (!await CanManageClubOrReviewAllAsync(row.ClubId, http.User, access, http, ct, true)) return Results.Forbid();
        SessionDecisionRequest? input;
        try { input = await http.Request.ReadFromJsonAsync<SessionDecisionRequest>(ct); }
        catch (JsonException) { return ApiOperations.Error(400, "VERSION_AND_REASON_REQUIRED", "Provide expectedVersion and reason."); }
        if (input is null || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 1000)
            return ApiOperations.Error(400, "VERSION_AND_REASON_REQUIRED", "Provide expectedVersion and reason.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            if (row.Version != input.ExpectedVersion || row.Status == ActivityStatuses.Cancelled)
                return ApiOperations.Error(409, "STALE_ACTIVITY", "Reload the activity.");
            var sessions = await db.Sessions.Where(x => x.ActivityId == id).ToListAsync(ct);
            if (!cancel && (row.Status != ActivityStatuses.Scheduled || row.PublicationStatus is not ("PUBLISHED" or "CLOSED")
                || row.EndTimeUtc > ActivitySessionEndpoints.Now(http) || sessions.Any(x => x.Status == "SCHEDULED")))
                return ApiOperations.Error(409, "ACTIVITY_NOT_FINISHED", "Close all sessions and wait until the activity ends before completing it.");
            row.Status = cancel ? ActivityStatuses.Cancelled : ActivityStatuses.Completed;
            row.PublicationStatus = cancel ? "CANCELLED" : "COMPLETED"; row.Version = Guid.NewGuid(); row.UpdatedAtUtc = ActivitySessionEndpoints.Now(http);
            if (cancel)
            {
                foreach (var session in sessions.Where(x => x.Status != "CANCELLED"))
                { session.Status = "CANCELLED"; session.Version = Guid.NewGuid(); ActivitySessionEndpoints.Audit(db, session, http, "CANCEL", input.Reason); }
                foreach (var attendance in await db.ActivityAttendances.Where(x => x.ActivityId == id && x.VerificationStatus == "VERIFIED").ToListAsync(ct))
                { attendance.VerificationStatus = "REVOKED"; attendance.RecognitionStatus = "REVOCATION_PENDING"; attendance.Version = Guid.NewGuid(); }
                db.AddOutboxMessage(new ExperienceSourceCancelledEvent(Guid.NewGuid(), row.UpdatedAtUtc, id, null, row.ClubId,
                    http.User.GetUserId(), input.Reason.Trim()), ExperienceRoutingKeys.SourceCancelled);
            }
            db.SessionAudits.Add(new ActivitySessionAudit
            {
                ActivityId = id,
                Action = cancel ? "CANCEL" : "COMPLETE",
                ActorId = http.User.GetUserId(),
                Version = row.Version,
                Reason = input.Reason.Trim(),
                CreatedAtUtc = row.UpdatedAtUtc,
                SnapshotJson = JsonSerializer.Serialize(ToResponse(row), ActivitySessionEndpoints.JsonOptions)
            });
            await db.SaveChangesAsync(ct); return Results.Ok(ToResponse(row));
        }, ct);
    }
}
