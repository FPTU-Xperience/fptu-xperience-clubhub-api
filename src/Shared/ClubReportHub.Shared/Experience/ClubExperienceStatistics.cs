namespace ClubReportHub.Shared.Experience;

public sealed record ActivityExperienceMetrics(int PublishedActivityCount, int CompletedActivityCount,
    int SessionCount, int CompletedSessionCount, int VerifiedAttendanceCount, int UniqueVerifiedParticipantCount,
    int PendingRecognitionCount);
public sealed record ClubExperienceTargets(Guid? PlanVersion, int TargetActivityCount, int TargetSessionCount,
    int TargetUniqueParticipantCount);
public sealed record TargetProgress(int Actual, int Target, decimal? Ratio)
{ public bool TargetReached => Actual >= Target; }
public sealed record ClubExperienceStatistics(int SchemaVersion, int ClubId, string ClubName, string SemesterId,
    DateTimeOffset GeneratedAt, string LedgerBasis, ActivityExperienceMetrics Activities, ClubExperienceTargets? Plan,
    IReadOnlyDictionary<string, TargetProgress> Progress, int ApprovedContributionCount, int FirstTimeParticipantCount,
    IReadOnlyDictionary<string, int> EffectiveRoleCounts, IReadOnlyDictionary<string, decimal> NetPointsByPillar,
    IReadOnlyList<Guid> PolicyVersions)
{
    public string LedgerRevision { get; init; } = "";
    public DateTimeOffset AsOf => GeneratedAt;
    public Guid? PlanVersion => Plan?.PlanVersion;
    public int ActivityCount => Activities.PublishedActivityCount;
    public int SessionCount => Activities.CompletedSessionCount;
    public int UniqueParticipantCount => Activities.UniqueVerifiedParticipantCount;
    public object RoleDistribution => new { unit = "CONTRIBUTIONS", total = EffectiveRoleCounts.Values.Sum(), counts = EffectiveRoleCounts };
    public object PillarDistribution => new { unit = "RAW_POINTS", byPillar = NetPointsByPillar };
    public ClubExperienceTargets? Targets => Plan;
    public string DefinitionsVersion => "CLUB_EXPERIENCE_PROJECT_V1";
}
public sealed record VerifiedParticipation(int UserId, DateTimeOffset OccurredAt);
public sealed record InternalActivityExperienceMetrics(ActivityExperienceMetrics Metrics,
    IReadOnlyList<VerifiedParticipation> CurrentParticipation, IReadOnlyList<VerifiedParticipation> FirstVerifiedInProject);
