namespace ActivityService.Models;

public sealed class ActivitySession
{
    public int Id { get; set; }
    public int ActivityId { get; set; }
    public ClubActivity Activity { get; set; } = null!;
    public Guid SeriesId { get; set; }
    public string SemesterId { get; set; } = string.Empty;
    public DateTimeOffset StartTimeUtc { get; set; }
    public DateTimeOffset EndTimeUtc { get; set; }
    public string Timezone { get; set; } = "Asia/Ho_Chi_Minh";
    public string Location { get; set; } = string.Empty;
    public string Mode { get; set; } = "ONSITE";
    public string Status { get; set; } = "SCHEDULED";
    public DateTimeOffset OpensAt { get; set; }
    public DateTimeOffset ClosesAt { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? RadiusMeters { get; set; }
    public bool ManualConfirmationEnabled { get; set; }
    public string ScoringSnapshotJson { get; set; } = string.Empty;
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public int CreatedByUserId { get; set; }
}

public sealed class ActivitySessionAudit
{
    public long Id { get; set; }
    public int ActivityId { get; set; }
    public int? SessionId { get; set; }
    public string Action { get; set; } = string.Empty;
    public int ActorId { get; set; }
    public string? Reason { get; set; }
    public Guid Version { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
}

public sealed class ActivityExperienceInbox
{
    public Guid EventId { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}
