using System.Data;
using System.Text.Json;
using ActivityService.Data;
using ActivityService.Endpoints;
using ActivityService.Models;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Services;

public sealed class ActivityExperienceEventHandler(ActivityDbContext db, CurrentUserProfileClient users,
    ClubExperienceAuthorityClient clubs) : IExperienceEventHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public bool Handles(string route) => route is ExperienceRoutingKeys.ManualAttendanceDecision or ExperienceRoutingKeys.AttendanceRecognition;

    public async Task ProcessAsync(Guid eventId, string route, JsonElement payload, CancellationToken ct)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            if (await db.ExperienceInbox.AnyAsync(x => x.EventId == eventId, ct)) return;
            if (route == ExperienceRoutingKeys.ManualAttendanceDecision)
                await ManualAsync(payload.Deserialize<ManualAttendanceDecisionEvent>(JsonOptions)!, ct);
            else
            {
                var message = payload.Deserialize<AttendanceRecognitionEvent>(JsonOptions)!;
                var row = await db.ActivityAttendances.Include(x => x.Activity).SingleOrDefaultAsync(x => x.Id == message.AttendanceId, ct);
                if (row is not null)
                {
                    var cancelled = row.Activity.Status == ActivityStatuses.Cancelled || row.SessionId.HasValue
                        && await db.Sessions.AnyAsync(x => x.Id == row.SessionId && x.Status == "CANCELLED", ct);
                    // A delayed award receipt cannot restore a cancelled source.
                    if (!cancelled || message.Status is "REVOKED" or "FAILED") row.RecognitionStatus = message.Status;
                    row.ContributionId = message.ContributionId; row.Version = Guid.NewGuid();
                }
            }
            db.ExperienceInbox.Add(new ActivityExperienceInbox { EventId = eventId, ProcessedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct); if (transaction is not null) await transaction.CommitAsync(ct);
        });
    }

    private async Task ManualAsync(ManualAttendanceDecisionEvent message, CancellationToken ct)
    {
        var source = message.Source;
        var session = await db.Sessions.Include(x => x.Activity).SingleOrDefaultAsync(x => x.Id == source.SessionId && x.ActivityId == source.ActivityId, ct);
        var participant = await db.ActivityParticipants.SingleOrDefaultAsync(x => x.ActivityId == source.ActivityId && x.UserId == message.UserId, ct);
        var eligible = session is not null && await ActivityEligibility.ResolveAsync(session.Activity, message.UserId,
            session.StartTimeUtc, users, clubs, ct) is not null;
        if (session is null || participant is null || !eligible || session.Status == "CANCELLED" || session.Activity.Status == ActivityStatuses.Cancelled
            || !session.ManualConfirmationEnabled || session.Version != source.SessionVersion || message.UserId == message.VerifiedByUserId
            || session.StartTimeUtc > DateTimeOffset.UtcNow)
        {
            db.AddOutboxMessage(new AttendanceRecognitionEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, 0, null,
                message.RequestId, "FAILED", "STALE_SOURCE_OR_ELIGIBILITY"), ExperienceRoutingKeys.AttendanceRecognition); return;
        }
        var row = await db.ActivityAttendances.SingleOrDefaultAsync(x => x.SessionId == source.SessionId && x.UserId == message.UserId, ct);
        if (row is not null && row.VerificationStatus == "VERIFIED")
        {
            if (row.RecognitionStatus != "PENDING")
            {
                db.AddOutboxMessage(new AttendanceRecognitionEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, row.Id,
                    row.ContributionId, message.RequestId, row.RecognitionStatus, null), ExperienceRoutingKeys.AttendanceRecognition); return;
            }
        }
        else
        {
            row ??= new ActivityAttendance
            {
                ActivityId = source.ActivityId,
                SessionId = source.SessionId,
                UserId = message.UserId,
                FullName = participant.FullName,
                AttendanceDate = WeeklyAttendanceRules.GetVietnamDate(session.StartTimeUtc),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            if (row.Id == 0) db.ActivityAttendances.Add(row);
            row.Status = AttendanceStatuses.Present; row.VerificationStatus = "VERIFIED"; row.VerificationMethod = "MANUAL_REVIEW";
            row.CheckedInByUserId = message.VerifiedByUserId; row.CheckedInAtUtc = DateTimeOffset.UtcNow;
            row.Note = message.Reason; row.ManualRequestId = message.RequestId; row.RecognitionStatus = "PENDING"; row.Version = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
        }
        db.AddOutboxMessage(new AttendanceVerifiedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, message.UserId,
            row.Id, row.VerificationMethod!, row.VerificationMethod == "ROTATING_QR" ? null : row.CheckedInByUserId,
            ActivitySessionEndpoints.Source(session)), ExperienceRoutingKeys.AttendanceVerified);
    }
}
