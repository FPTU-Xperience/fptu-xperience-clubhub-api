using AdminService.Contracts;
using AdminService.Data;
using ClubReportHub.Shared.Experience;

namespace AdminService.Services;

internal static class StudentCalculationService
{
    internal static CalculationBreakdown? Build(ContributionAttributes attributes, ApprovedExperiencePolicySnapshot policy,
        decimal points, Guid? verificationId, string provenance)
    {
        if (policy.Policy.FormulaVersion != "P_B_R_Q_K_V1") return null;
        var tier = ExperienceScoringCalculator.GetTierBasePoints(attributes.Tier);
        var role = ExperienceScoringCalculator.GetRoleMultiplier(attributes.Role);
        var scale = ExperienceScoringCalculator.GetScaleMultiplier(attributes.Scale);
        return new(tier, role, scale, Math.Min(role * scale, 8m),
            ExperienceScoringCalculator.GetBonusResultMultiplier(attributes.Result), points,
            attributes.PillarWeights, VerifiedExperienceRules.Allocate(points, attributes.PillarWeights, policy.Policy.Precision),
            policy.Policy.PolicyVersion, policy.Policy.FormulaVersion, verificationId, provenance);
    }

    internal static CalculationBreakdown? Current(VerifiedContribution row)
    {
        if (row.EffectiveAttributesJson is null || row.PolicySnapshotJson is null) return null;
        return Build(VerifiedExperienceService.Read<ContributionAttributes>(row.EffectiveAttributesJson),
            VerifiedExperienceService.Read<ApprovedExperiencePolicySnapshot>(row.PolicySnapshotJson),
            row.RawPoints, null, "RECONSTRUCTED_FROM_FROZEN_VERIFIED_STATE");
    }

    internal static object Effect(ExperienceLedgerEffect effect, ContributionRevision? revision,
        ApprovedExperiencePolicySnapshot? policy, IReadOnlyDictionary<string, decimal> allocations,
        decimal beforeRawPoints, IReadOnlyDictionary<string, decimal> beforeAllocations)
    {
        LedgerCalculationSnapshot? snapshot = effect.CalculationSnapshotJson is null ? null
            : VerifiedExperienceService.Read<LedgerCalculationSnapshot>(effect.CalculationSnapshotJson);
        var provenance = snapshot is null ? "HISTORICAL_LEDGER_WITHOUT_CALCULATION_SNAPSHOT" : "IMMUTABLE_EFFECT_SNAPSHOT";
        if (snapshot is null && revision is not null && policy is not null)
        {
            snapshot = Reconstruct(effect, revision, policy, allocations, beforeRawPoints, beforeAllocations);
            if (snapshot is not null) provenance = "RECONSTRUCTED_FROM_IMMUTABLE_LEDGER_AND_REVISION";
        }
        // Historical amounts are facts; an old formula is never re-evaluated with a newer policy.
        return new
        {
            effect.Id,
            effect.ContributionId,
            effect.RevisionId,
            effect.EffectType,
            deltaAmount = effect.Amount,
            effect.PolicyVersion,
            effect.FormulaVersion,
            effect.VerificationId,
            effect.VerificationMethod,
            effect.OccurredAt,
            effect.RecordedAt,
            effect.Reason,
            allocatedDelta = allocations,
            calculationBreakdown = snapshot?.After,
            beforeRawPoints = snapshot?.BeforeRawPoints,
            beforeAllocations = snapshot?.BeforeAllocations,
            verifiedAttributes = snapshot?.VerifiedAttributes ?? (revision is null ? null
                : VerifiedExperienceService.Read<ContributionAttributes>(revision.AttributesJson)),
            provenance,
            calculationAvailable = snapshot is not null,
            policyAvailable = policy is not null
        };
    }

    private static LedgerCalculationSnapshot? Reconstruct(ExperienceLedgerEffect effect, ContributionRevision revision,
        ApprovedExperiencePolicySnapshot policy, IReadOnlyDictionary<string, decimal> deltaAllocations,
        decimal beforeRawPoints, IReadOnlyDictionary<string, decimal> beforeAllocations)
    {
        if (policy.Policy.PolicyVersion != effect.PolicyVersion || policy.Policy.FormulaVersion != effect.FormulaVersion
            || effect.FormulaVersion != "P_B_R_Q_K_V1") return null;
        var attributes = VerifiedExperienceService.Read<ContributionAttributes>(revision.AttributesJson);
        if (VerifiedExperienceRules.Validate(attributes) is not null) return null;
        var afterRaw = beforeRawPoints + effect.Amount;
        var expected = revision.Status == "REVOKED" ? 0m
            : ExperienceScoringCalculator.CalculateRawPoints(attributes.Tier, attributes.Role, attributes.Scale, attributes.Result);
        if (afterRaw != expected) return null;
        var breakdown = Build(attributes, policy, afterRaw, effect.VerificationId, "RECONSTRUCTED_FROM_IMMUTABLE_LEDGER_AND_REVISION");
        if (breakdown is null || beforeAllocations.Keys.Union(deltaAllocations.Keys).Union(breakdown.AllocatedPoints.Keys)
            .Any(p => beforeAllocations.GetValueOrDefault(p) + deltaAllocations.GetValueOrDefault(p) != breakdown.AllocatedPoints.GetValueOrDefault(p)))
            return null;
        return new(breakdown, beforeRawPoints, effect.Amount, beforeAllocations, attributes);
    }
}

