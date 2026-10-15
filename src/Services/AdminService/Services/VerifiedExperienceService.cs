using System.Text.Json;
using AdminService.Contracts;
using AdminService.Data;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Services;

internal static class VerifiedExperienceService
{
    internal static string Json<T>(T value) => JsonSerializer.Serialize(value, ExperienceAuthorization.JsonOptions);
    internal static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, ExperienceAuthorization.JsonOptions)
        ?? throw new InvalidOperationException("A persisted experience snapshot is missing.");
    internal static string ClubSourceKey(int activityId, int sessionId) => $"CLUB:{activityId}:SESSION:{sessionId}";
    internal static ContributionResponse Response(VerifiedContribution row) => new(row.Id, row.UserId,
        row.SourceType, row.SourceId, row.ClubId, row.ActivityId, row.SessionId, row.SemesterId, row.OccurredAt,
        row.Title, row.Message, row.Status, row.Version, row.CurrentRevisionId,
        Read<ContributionAttributes>(row.ProposedAttributesJson), row.EffectiveAttributesJson is null ? null : Read<ContributionAttributes>(row.EffectiveAttributesJson),
        Read<List<Guid>>(row.EvidenceRefsJson), row.RawPoints, row.Status == "REVOKED" ? "REVOKED" : row.EffectiveAttributesJson is null ? "NOT_REQUESTED" : "RECORDED", row.UpdatedAtUtc)
    {
        SourceKey = row.SourceKey,
        CalculationBreakdown = StudentCalculationService.Current(row)
    };

    internal static ContributionRevision Revision(AdminDbContext db, VerifiedContribution row, string status,
        ContributionAttributes attributes, int? actor, string reason, IReadOnlyList<Guid>? evidence = null)
    {
        row.RevisionOrdinal++;
        var revision = new ContributionRevision
        {
            ContributionId = row.Id,
            Ordinal = row.RevisionOrdinal,
            Status = status,
            AttributesJson = Json(attributes),
            EvidenceRefsJson = evidence is null ? row.EvidenceRefsJson : Json(evidence),
            ActorId = actor,
            Reason = reason,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        row.CurrentRevisionId = revision.Id; row.Version = Guid.NewGuid(); row.UpdatedAtUtc = revision.CreatedAtUtc;
        db.ContributionRevisions.Add(revision);
        return revision;
    }

    internal static async Task<bool> OwnsEvidenceAsync(AdminDbContext db, int userId, IReadOnlyList<Guid>? ids, CancellationToken cancellationToken)
    {
        if (ids is null || ids.Count > 10 || ids.Distinct().Count() != ids.Count) return false;
        return await db.ContributionEvidence.CountAsync(x => x.OwnerUserId == userId && ids.Contains(x.Id), cancellationToken) == ids.Count;
    }

    internal static bool SameBlueprint(ContributionAttributes proposed, ContributionAttributes source) =>
        proposed.Tier == source.Tier && proposed.Scale == source.Scale &&
        Json(proposed.PillarWeights.OrderBy(x => x.Pillar, StringComparer.Ordinal)) == Json(source.PillarWeights.OrderBy(x => x.Pillar, StringComparer.Ordinal));

    internal static async Task<ApprovedExperiencePolicySnapshot?> PolicyAsync(AdminDbContext db, Guid version,
        string semesterId, string environment, CancellationToken cancellationToken)
    {
        var row = await db.ApprovedExperiencePolicies.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == version && x.SemesterId == semesterId && x.Environment == environment && x.Scope == "PERIOD", cancellationToken);
        return row is null ? null : Read<ApprovedExperiencePolicySnapshot>(row.SnapshotJson);
    }

    internal static async Task AppendEffectAsync(AdminDbContext db, VerifiedContribution row, ContributionRevision revision,
        ContributionAttributes? before, ContributionAttributes after, ApprovedExperiencePolicySnapshot policy,
        string effectType, Guid verificationId, int? verifiedBy, string method, string reason, Guid? eventId,
        CancellationToken cancellationToken, bool revoke = false)
    {
        var oldPoints = row.RawPoints;
        var newPoints = revoke ? 0m : ExperienceScoringCalculator.CalculateRawPoints(after.Tier, after.Role, after.Scale, after.Result);
        var oldAllocations = before is null ? new Dictionary<string, decimal>() : VerifiedExperienceRules.Allocate(oldPoints, before.PillarWeights, policy.Policy.Precision);
        var newAllocations = revoke ? after.PillarWeights.ToDictionary(x => x.Pillar, _ => 0m)
            : VerifiedExperienceRules.Allocate(newPoints, after.PillarWeights, policy.Policy.Precision);
        var effect = new ExperienceLedgerEffect
        {
            ContributionId = row.Id,
            RevisionId = revision.Id,
            UserId = row.UserId,
            SemesterId = row.SemesterId,
            EffectType = effectType,
            Amount = newPoints - oldPoints,
            VerificationId = verificationId,
            VerifiedByUserId = verifiedBy,
            VerificationMethod = method,
            PolicyVersion = policy.Policy.PolicyVersion,
            FormulaVersion = policy.Policy.FormulaVersion,
            OccurredAt = row.OccurredAt,
            RecordedAt = DateTimeOffset.UtcNow,
            Reason = reason,
            EventId = eventId,
            CalculationSnapshotJson = Json(new LedgerCalculationSnapshot(
                StudentCalculationService.Build(after, policy, newPoints, verificationId, "IMMUTABLE_EFFECT_SNAPSHOT")
                    ?? throw new InvalidOperationException("Unsupported contribution formula."),
                oldPoints, newPoints - oldPoints, oldAllocations, after))
        };
        db.ExperienceLedgerEffects.Add(effect);
        row.RawPoints = newPoints; row.Status = revoke ? "REVOKED" : "APPROVED";
        row.EffectiveAttributesJson = Json(after); row.PolicySnapshotJson = Json(policy);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var pillar in oldAllocations.Keys.Union(newAllocations.Keys, StringComparer.Ordinal))
        {
            db.ExperienceLedgerAllocations.Add(new ExperienceLedgerAllocation
            {
                EffectId = effect.Id,
                Pillar = pillar,
                Amount = newAllocations.GetValueOrDefault(pillar) - oldAllocations.GetValueOrDefault(pillar)
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    internal static void Notify(AdminDbContext db, VerifiedContribution row, string kind, string title) =>
        db.AddOutboxMessage(new ExperienceNotificationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, row.ClubId ?? 0,
            [row.UserId], kind, title, $"/contributions/{row.Id}"), ExperienceRoutingKeys.Notification);
}
