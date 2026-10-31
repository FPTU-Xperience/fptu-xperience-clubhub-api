namespace ActivityService.Models;

public sealed class ClubActivity
{
    public int Id { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public string PublicationStatus { get; set; } = "LEGACY";
    public string DiscoveryVisibility { get; set; } = "CLUB_ONLY";
    public bool IsInternal { get; set; } = true;
    public string? SemesterId { get; set; }
    public string EffortTier { get; set; } = "T1";
    public string Scale { get; set; } = "Club";
    public string PillarWeightsJson { get; set; } = "[]";
    public Guid? PolicyVersion { get; set; }
    public string? ScoringSnapshotJson { get; set; }
    public int? Capacity { get; set; }
    public int RegisteredCount { get; set; }
    public DateTimeOffset? RegistrationOpensAt { get; set; }
    public DateTimeOffset? RegistrationClosesAt { get; set; }
    public string Eligibility { get; set; } = "CLUB_MEMBER";
    public string? EvidenceRequirements { get; set; }
    public string? CoverImageUrl { get; set; }
    public int? SourceReportId { get; set; }
    public int? SourceReportDetailId { get; set; }
    public int ClubId { get; set; }
    public string ClubName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset StartTimeUtc { get; set; }
    public DateTimeOffset EndTimeUtc { get; set; }
    public string MeetingDaysCsv { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Status { get; set; } = ActivityStatuses.Scheduled;
    public int CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ActivityParticipant> Participants { get; set; } = [];
    public ICollection<ActivityAttendance> Attendances { get; set; } = [];
}
