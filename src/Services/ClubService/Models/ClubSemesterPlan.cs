namespace ClubService.Models;

public sealed class ClubSemesterPlan
{
    public int Id { get; set; }
    public int ClubId { get; set; }
    public string SemesterId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int TargetActivityCount { get; set; }
    public int TargetSessionCount { get; set; }
    public int TargetUniqueParticipantCount { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public Guid Version { get; set; } = Guid.NewGuid();
    public int CreatedBy { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ClubPlanRevision
{
    public long Id { get; set; }
    public int PlanId { get; set; }
    public Guid Version { get; set; }
    public int ActorId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
}
