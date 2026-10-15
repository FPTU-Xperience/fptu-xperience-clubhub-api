using System.Data;
using AdminService.Contracts;
using AdminService.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Services;

internal static class StudentExperienceReadService
{
    internal static Task<RadarReadResult> ReadAsync(AdminDbContext db, int userId, string? semester,
        string? scope, DateTimeOffset? requestedAsOf, string name, string email, string environment, CancellationToken ct)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(() => ReadCoreAsync(db, userId, semester, scope,
            requestedAsOf, name, email, environment, ct));

    private static async Task<RadarReadResult> ReadCoreAsync(AdminDbContext db, int userId, string? semester,
        string? requestedScope, DateTimeOffset? requestedAsOf, string name, string email, string environment, CancellationToken ct)
    {
        var scope = string.IsNullOrWhiteSpace(requestedScope) ? "PERIOD" : requestedScope.Trim().ToUpperInvariant();
        var now = DateTimeOffset.UtcNow; var asOf = requestedAsOf?.ToUniversalTime() ?? now;
        if (scope is not ("PERIOD" or "CUMULATIVE") || scope == "CUMULATIVE" && !string.IsNullOrWhiteSpace(semester))
            return new(null, 400, "INVALID_RADAR_SCOPE", "Use PERIOD with a semester, or CUMULATIVE without a semester.");
        if (asOf > now) return new(null, 400, "INVALID_AS_OF", "A future snapshot is not available.");
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        Semester? period = null;
        if (scope == "PERIOD")
        {
            if (!string.IsNullOrWhiteSpace(semester))
            {
                var id = Guid.TryParse(semester, out var parsed) ? parsed : Guid.Empty;
                var code = semester.Trim().ToUpperInvariant();
                period = await db.Semesters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id || x.SemesterCode == code, ct);
                if (period is null) return new(null, 400, "INVALID_SEMESTER", "The requested semester is not configured.");
            }
            else
            {
                var active = await db.Semesters.AsNoTracking().Where(x => x.IsActive).Take(2).ToListAsync(ct);
                if (active.Count != 1) return new(null, 400, "SEMESTER_REQUIRED", "Select a semester; the active semester is missing or ambiguous.");
                period = active[0];
            }
        }
        var policies = db.ApprovedExperiencePolicies.AsNoTracking().Where(x => x.Scope == scope && x.Environment == environment
            && x.ApprovedAt <= asOf && (x.EffectiveAt == null || x.EffectiveAt <= asOf));
        if (period is not null) policies = policies.Where(x => x.SemesterId == period.SemesterCode);
        var policyRow = await policies.OrderByDescending(x => x.EffectiveAt ?? x.ApprovedAt).ThenByDescending(x => x.ApprovedAt)
            .ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (policyRow is null) return new(null, 503, "SCORE_POLICY_UNAPPROVED", "This scope requires an approved policy for the configured environment.");
        var policy = VerifiedExperienceService.Read<ApprovedExperiencePolicySnapshot>(policyRow.SnapshotJson);
        if (policy.Scope != scope || policy.IndexFormulaVersion != "ERI_MEAN_PIELOU_SATURATED_V1"
            || RadarPillars.AllSeven.Any(p => !policy.TauByPillar.TryGetValue(p, out var tau) || tau <= 0))
            return new(null, 503, "SCORE_POLICY_UNSUPPORTED", "The approved index policy cannot be evaluated by this server.");
        var effects = db.ExperienceLedgerEffects.AsNoTracking().Where(x => x.UserId == userId && x.RecordedAt <= asOf);
        if (period is not null) effects = effects.Where(x => x.SemesterId == period.SemesterCode);
        var revision = await effects.Select(x => (long?)x.Id).MaxAsync(ct) ?? 0;
        effects = effects.Where(x => x.Id <= revision);
        var allocated = from allocation in db.ExperienceLedgerAllocations.AsNoTracking()
                        join effect in effects on allocation.EffectId equals effect.Id
                        select new { effect.ContributionId, allocation.Pillar, allocation.Amount };
        Dictionary<string, decimal> raw;
        List<Guid> recognized;
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            // SQLite stores decimals as text. Keep exact decimal arithmetic instead of casting to binary floating point.
            var values = await allocated.ToListAsync(ct);
            raw = values.GroupBy(x => x.Pillar).ToDictionary(x => x.Key, x => x.Sum(a => a.Amount));
            var amounts = await effects.Select(x => new { x.ContributionId, x.Amount }).ToListAsync(ct);
            recognized = amounts.GroupBy(x => x.ContributionId).Where(x => x.Sum(e => e.Amount) > 0).Select(x => x.Key).ToList();
        }
        else
        {
            raw = await allocated.GroupBy(x => x.Pillar).Select(x => new { Pillar = x.Key, Amount = x.Sum(a => a.Amount) })
                .ToDictionaryAsync(x => x.Pillar, x => x.Amount, ct);
            recognized = await effects.GroupBy(x => x.ContributionId).Where(x => x.Sum(e => e.Amount) > 0)
                .Select(x => x.Key).ToListAsync(ct);
        }
        var latest = await effects.GroupBy(x => x.ContributionId)
            .Select(x => x.OrderByDescending(e => e.Id).First()).ToListAsync(ct);
        var revisionIds = latest.Where(x => recognized.Contains(x.ContributionId)).Select(x => x.RevisionId).ToArray();
        var leaderRevisions = await db.ContributionRevisions.AsNoTracking().Where(x => revisionIds.Contains(x.Id)
            && x.AttributesJson.ToUpper().Contains("LEADER")).ToListAsync(ct);
        var leaderIds = leaderRevisions.Where(x => VerifiedExperienceService.Read<ContributionAttributes>(x.AttributesJson).Role
                .Equals(ContributionRoles.Leader, StringComparison.OrdinalIgnoreCase)).Select(x => x.ContributionId).ToArray();
        var leaderValues = await allocated.Where(x => leaderIds.Contains(x.ContributionId)).ToListAsync(ct);
        var leaderPillars = leaderValues.GroupBy(x => new { x.ContributionId, x.Pillar }).Where(x => x.Sum(a => a.Amount) > 0)
            .Select(x => x.Key.Pillar).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var radar = ExperienceIndexCalculator.ComputeRadar(raw, policy.TauByPillar, leaderPillars,
            policy.ThresholdStarter, policy.ThresholdPractitioner, policy.ThresholdLeader,
            policy.MinPillarScoreAllRounder, policy.MinJAllRounder);
        var canonical = db.Contributions.AsNoTracking().Where(x => x.UserId == userId && recognized.Contains(x.Id));
        var clubCount = await canonical.CountAsync(x => x.SourceType == "CLUB", ct);
        var externalCount = await canonical.CountAsync(x => x.SourceType != "CLUB", ct);
        var legacyCount = await db.SelfDeclarations.AsNoTracking().CountAsync(x => x.StudentId == userId
            && !db.Contributions.Any(c => c.Id == x.Id), ct);
        var items = RadarPillars.AllSeven.Select(p =>
        {
            var item = radar.Pillars[p];
            return new PillarRadarItemResponse(p, item.RawPointsSum, item.Tau,
                Math.Round(item.SaturatedScore, 2, MidpointRounding.AwayFromZero), item.MasteryTier, item.HasLeaderProof);
        }).ToArray();
        var response = new StudentRadarResponse(userId, name, email, period?.SemesterCode ?? string.Empty,
            Math.Round(radar.D, 2, MidpointRounding.AwayFromZero), Math.Round(radar.J, 4, MidpointRounding.AwayFromZero),
            Math.Round(radar.M, 4, MidpointRounding.AwayFromZero), Math.Round(radar.ERI, 2, MidpointRounding.AwayFromZero),
            radar.ProfileTitle, externalCount, items)
        {
            Scope = scope,
            SemesterId = period?.SemesterCode,
            PolicyVersion = policyRow.Id,
            FormulaVersion = policy.IndexFormulaVersion,
            ContributionFormulaVersion = policy.Policy.FormulaVersion,
            LedgerBasis = "VERIFIED_NET_LEDGER",
            IsAuthoritative = true,
            LedgerRevision = revision,
            AsOf = asOf,
            Version = $"{scope}:{period?.SemesterCode ?? "ALL"}:{policyRow.Id:N}:{revision}",
            RadarAxes = items.Where(x => RadarPillars.CoreSix.Contains(x.Pillar)).ToArray(),
            PlusOne = items.Single(x => x.Pillar == RadarPillars.PlusOne),
            ApprovedClubContributionsCount = clubCount,
            LegacyPreviewCount = legacyCount
        };
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new(response);
    }
}

