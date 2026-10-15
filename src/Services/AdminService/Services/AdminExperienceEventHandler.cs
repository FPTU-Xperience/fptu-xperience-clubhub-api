using System.Data;
using System.Text.Json;
using AdminService.Data;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using static AdminService.Services.VerifiedExperienceService;

namespace AdminService.Services;

public sealed class AdminExperienceEventHandler(AdminDbContext db, IConfiguration configuration) : IExperienceEventHandler
{
    public bool Handles(string route) => route is ExperienceRoutingKeys.AttendanceVerified or ExperienceRoutingKeys.SourceCancelled or ExperienceRoutingKeys.AttendanceRecognition;

    public async Task ProcessAsync(Guid eventId, string route, JsonElement payload, CancellationToken ct)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            if (await db.ExperienceInbox.AnyAsync(x => x.EventId == eventId, ct)) return;
            if (route == ExperienceRoutingKeys.AttendanceVerified)
                await AttendanceAsync(payload.Deserialize<AttendanceVerifiedEvent>(ExperienceAuthorization.JsonOptions)!, ct);
            else if (route == ExperienceRoutingKeys.SourceCancelled)
                await CancelAsync(payload.Deserialize<ExperienceSourceCancelledEvent>(ExperienceAuthorization.JsonOptions)!, ct);
            else
            {
                var receipt = payload.Deserialize<AttendanceRecognitionEvent>(ExperienceAuthorization.JsonOptions)!;
                if (receipt.ManualRequestId.HasValue)
                {
                    var row = await db.ManualAttendanceRequests.SingleOrDefaultAsync(x => x.Id == receipt.ManualRequestId, ct);
                    if (row is not null && row.Status == "APPROVED_PENDING")
                    {
                        row.Status = receipt.Status == "RECORDED" ? "APPROVED" : "FAILED"; row.RecognitionStatus = receipt.Status;
                        row.ContributionId = receipt.ContributionId; row.FailureCode = receipt.FailureCode; row.Version = Guid.NewGuid();
                    }
                }
            }
            db.ExperienceInbox.Add(new ExperienceInbox { EventId = eventId, RoutingKey = route, ProcessedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct); if (transaction is not null) await transaction.CommitAsync(ct);
        });
    }

    private async Task AttendanceAsync(AttendanceVerifiedEvent message, CancellationToken ct)
    {
        var source = message.Source;
        if (message.VerificationMethod is not ("ROTATING_QR" or "MANUAL_REVIEW")
            || message.VerificationMethod == "MANUAL_REVIEW" && (message.VerifiedByUserId is null || message.VerifiedByUserId == message.UserId))
            throw new InvalidOperationException("Attendance has no valid verification authority.");
        var manual = await db.ManualAttendanceRequests.SingleOrDefaultAsync(x => x.SessionId == source.SessionId && x.UserId == message.UserId && x.Status == "APPROVED_PENDING", ct);
        // Direct manual attendance writes cannot mint points on a role check or
        // a stale cross-service capability read. Award authority lives here.
        if (message.VerificationMethod == "MANUAL_REVIEW" && manual is null)
        {
            var grant = await ExperienceAuthorization.FindAsync(db, message.VerifiedByUserId!.Value,
                "CLUB", source.ClubId, "attendance.verify", ct);
            if (grant is null) { Receipt(message, null, "FAILED", "VERIFICATION_ASSIGNMENT_REVOKED", null); return; }
            grant.Version = Guid.NewGuid();
        }
        var cancelled = await db.InvalidatedExperienceSources.AnyAsync(x => x.ActivityId == source.ActivityId && (x.SessionId == null || x.SessionId == source.SessionId), ct);
        var policy = await PolicyAsync(db, source.Policy.PolicyVersion, source.SemesterId,
            configuration["Experience:Environment"] ?? "PROJECT", ct);
        if (cancelled || policy is null)
        { Receipt(message, null, "FAILED", cancelled ? "SOURCE_CANCELLED" : "POLICY_UNAVAILABLE", manual?.Id); return; }
        var key = ClubSourceKey(source.ActivityId, source.SessionId);
        var row = await db.Contributions.SingleOrDefaultAsync(x => x.UserId == message.UserId && x.SourceKey == key, ct);
        if (row is not null && row.EffectiveAttributesJson is not null)
        { Receipt(message, row.Id, row.Status == "REVOKED" ? "REVOKED" : "RECORDED", null, manual?.Id); return; }
        var attributes = source.Attributes with { Role = ContributionRoles.Participant, Result = BonusResults.None };
        if (VerifiedExperienceRules.Validate(attributes) is not null) throw new InvalidOperationException("Invalid published scoring source.");
        if (row is null)
        {
            row = new VerifiedContribution
            {
                UserId = message.UserId,
                ClubId = source.ClubId,
                ActivityId = source.ActivityId,
                SessionId = source.SessionId,
                SourceId = $"{source.ActivityId}/{source.SessionId}",
                SourceKey = key,
                SemesterId = source.SemesterId,
                Title = source.Title,
                ProposedAttributesJson = Json(attributes),
                SourceSnapshotJson = Json(source),
                OccurredAt = source.StartTimeUtc,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.Contributions.Add(row);
        }
        var revision = Revision(db, row, "APPROVED", attributes, message.VerifiedByUserId, "Verified attendance.");
        row.AttendanceId = message.AttendanceId;
        await AppendEffectAsync(db, row, revision, null, attributes, policy, "AWARD", message.EventId,
            message.VerifiedByUserId, message.VerificationMethod, "Verified attendance.", message.EventId, ct);
        Receipt(message, row.Id, "RECORDED", null, manual?.Id);
        Notify(db, row, "EXPERIENCE_RECORDED", "Điểm trải nghiệm đã được ghi nhận");
    }

    private void Receipt(AttendanceVerifiedEvent source, Guid? contribution, string status, string? failure, Guid? manual) =>
        db.AddOutboxMessage(new AttendanceRecognitionEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, source.AttendanceId,
            contribution, manual, status, failure), ExperienceRoutingKeys.AttendanceRecognition);

    private async Task CancelAsync(ExperienceSourceCancelledEvent message, CancellationToken ct)
    {
        db.InvalidatedExperienceSources.Add(new InvalidatedExperienceSource
        {
            ActivityId = message.ActivityId,
            SessionId = message.SessionId,
            ActorId = message.ActorId,
            Reason = message.Reason,
            InvalidatedAt = message.OccurredAtUtc
        });
        var rows = await db.Contributions.Where(x => x.ActivityId == message.ActivityId && (message.SessionId == null || x.SessionId == message.SessionId)).ToListAsync(ct);
        foreach (var row in rows)
        {
            if (row.Status == "APPROVED" && row.PolicySnapshotJson is not null && row.EffectiveAttributesJson is not null)
            {
                var attributes = Read<ContributionAttributes>(row.EffectiveAttributesJson);
                var revision = Revision(db, row, "REVOKED", attributes, message.ActorId, message.Reason);
                await AppendEffectAsync(db, row, revision, attributes, attributes, Read<AdminService.Contracts.ApprovedExperiencePolicySnapshot>(row.PolicySnapshotJson),
                    "REVERSAL", message.EventId, message.ActorId, "SOURCE_INVALIDATION", message.Reason, message.EventId, ct, revoke: true);
                if (row.AttendanceId.HasValue)
                    db.AddOutboxMessage(new AttendanceRecognitionEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, row.AttendanceId.Value,
                        row.Id, null, "REVOKED", "SOURCE_CANCELLED"), ExperienceRoutingKeys.AttendanceRecognition);
            }
            else if (row.Status is "SUBMITTED" or "REVISION_REQUESTED") { row.Status = "INVALIDATED"; row.Version = Guid.NewGuid(); }
            Notify(db, row, "SOURCE_CANCELLED", "Buổi hoạt động đã bị hủy");
        }
    }
}
