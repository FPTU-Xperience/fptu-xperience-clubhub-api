using AdminService.Contracts;
using AdminService.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Services;

internal static class StudentQuestService
{
    internal static async Task<string?> ValidateAsync(QuestCriteria criteria, string semester, string environment,
        AdminDbContext db, CancellationToken ct)
    {
        if (!RadarPillars.CoreSix.Contains(criteria.TargetPillar) || criteria.EligibleActivityIds is not { Count: > 0 and <= 100 }
            || criteria.EligibleActivityIds.Any(x => x <= 0) || criteria.EligibleActivityIds.Distinct().Count() != criteria.EligibleActivityIds.Count
            || criteria.EligibleSessionIds is { Count: 0 or > 100 } || criteria.EligibleSessionIds?.Any(x => x <= 0) == true
            || criteria.EligibleSessionIds?.Distinct().Count() != criteria.EligibleSessionIds?.Count
            || criteria.MinimumVerifiedContributions is < 1 or > 100 || criteria.MinimumAllocatedPoints < 0
            || criteria.RuleVersion != "LOW_AXIS_QUEST_V1") return "INVALID_QUEST_CRITERIA";
        return await db.ApprovedExperiencePolicies.AsNoTracking().AnyAsync(x => x.Id == criteria.PolicyVersion
            && x.Scope == "PERIOD" && x.SemesterId == semester && x.Environment == environment, ct)
            ? null : "SCORE_POLICY_UNAPPROVED";
    }

    internal static JoinedQuestCriteria? Snapshot(QuestParticipant participant) => participant.CriteriaSnapshotJson is null ? null
        : VerifiedExperienceService.Read<JoinedQuestCriteria>(participant.CriteriaSnapshotJson);

    internal static async Task<(IReadOnlyList<Guid> Ids, decimal Points, long Revision, bool SourcesAvailable)> ProofAsync(
        QuestParticipant participant, AdminDbContext db, StudentExperienceSourceClient sources, CancellationToken ct, IReadOnlyList<Guid>? requested = null)
    {
        var snapshot = Snapshot(participant);
        if (snapshot is null) return ([], 0, 0, true);
        var criteria = snapshot.Criteria;
        var criterionPolicy = await db.ApprovedExperiencePolicies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == criteria.PolicyVersion
            && x.Scope == "PERIOD" && x.SemesterId == snapshot.SemesterId, ct);
        if (criterionPolicy is null) return ([], 0, 0, true);
        var rows = await db.Contributions.AsNoTracking().Where(x => x.UserId == participant.StudentUserId && x.SourceType == "CLUB"
            && x.Status == "APPROVED" && x.SemesterId == snapshot.SemesterId && x.ActivityId != null && x.SessionId != null
            && criteria.EligibleActivityIds.Contains(x.ActivityId.Value) && x.OccurredAt >= participant.JoinedAtUtc
            && (snapshot.Deadline == null || x.OccurredAt <= snapshot.Deadline)
            && !db.InvalidatedExperienceSources.Any(s => s.ActivityId == x.ActivityId && (s.SessionId == null || s.SessionId == x.SessionId)))
            .ToListAsync(ct);
        var sourceIds = rows.Select(x => x.Id).ToArray();
        var effects = await db.ExperienceLedgerEffects.AsNoTracking().Where(x => x.UserId == participant.StudentUserId
            && sourceIds.Contains(x.ContributionId)).ToListAsync(ct);
        var effectIds = effects.Select(x => x.Id).ToArray();
        var allocations = await db.ExperienceLedgerAllocations.AsNoTracking().Where(x => effectIds.Contains(x.EffectId)
            && x.Pillar == criteria.TargetPillar).ToListAsync(ct);
        var byEffect = effects.ToDictionary(x => x.Id, x => x.ContributionId);
        var net = allocations.GroupBy(x => byEffect[x.EffectId]).ToDictionary(x => x.Key, x => x.Sum(a => a.Amount));
        var qualified = rows.Where(x => (requested is null || requested.Contains(x.Id))
            && (criteria.EligibleSessionIds is null || criteria.EligibleSessionIds.Contains(x.SessionId!.Value))
            && net.GetValueOrDefault(x.Id) > 0 && x.PolicySnapshotJson is not null)
            .DistinctBy(x => x.SourceKey).ToArray();
        var approvedPolicyIds = await db.ApprovedExperiencePolicies.AsNoTracking().Where(x => x.Scope == "PERIOD"
            && x.SemesterId == snapshot.SemesterId && x.Environment == criterionPolicy.Environment).Select(x => x.Id).ToListAsync(ct);
        qualified = qualified.Where(x => TrustedPolicy(x, approvedPolicyIds, criterionPolicy.Environment)).ToArray();
        var active = new HashSet<StudentSourceReference>();
        try
        {
            foreach (var batch in qualified.Select(x => new StudentSourceReference(x.ActivityId!.Value, x.SessionId!.Value)).Distinct().Chunk(100))
                active.UnionWith((await sources.SourceStatusAsync(new(batch), ct)).ActiveSources);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        { return ([], 0, effects.Select(x => x.Id).DefaultIfEmpty(0).Max(), false); }
        qualified = qualified.Where(x => active.Contains(new(x.ActivityId!.Value, x.SessionId!.Value))).ToArray();
        return (qualified.Select(x => x.Id).OrderBy(x => x).ToArray(),
            qualified.Sum(x => net[x.Id]), effects.Select(x => x.Id).DefaultIfEmpty(0).Max(), true);
    }

    private static bool TrustedPolicy(VerifiedContribution contribution, IReadOnlyCollection<Guid> approvedIds, string environment)
    {
        try
        {
            var policy = VerifiedExperienceService.Read<ApprovedExperiencePolicySnapshot>(contribution.PolicySnapshotJson!);
            return policy.Scope == "PERIOD" && policy.Policy.SemesterId == contribution.SemesterId
                && policy.Policy.Environment == environment && policy.Policy.ApprovalStatus == "APPROVED"
                && policy.Policy.FormulaVersion == "P_B_R_Q_K_V1" && approvedIds.Contains(policy.Policy.PolicyVersion);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return false; }
    }

    internal static async Task<QuestParticipantResponse> ResponseAsync(QuestParticipant participant, AdminDbContext db,
        StudentExperienceSourceClient sources, CancellationToken ct)
    {
        var proof = await ProofAsync(participant, db, sources, ct);
        var snapshot = Snapshot(participant);
        var met = snapshot is not null && proof.Ids.Count >= snapshot.Criteria.MinimumVerifiedContributions
            && proof.Points >= snapshot.Criteria.MinimumAllocatedPoints;
        return QuestParticipantResponse.From(participant) with
        {
            LinkedContributionIds = proof.Ids,
            VerifiedContributionCount = proof.Ids.Count,
            EffectiveStatus = !proof.SourcesAvailable ? "UNAVAILABLE" : participant.Status == "Completed" ? met ? "COMPLETED" : "REQUIRES_REVALIDATION" : met ? "READY_TO_COMPLETE" : "JOINED",
            ProgressAvailable = snapshot is not null && proof.SourcesAvailable,
            LedgerRevision = proof.Revision
        };
    }
}

