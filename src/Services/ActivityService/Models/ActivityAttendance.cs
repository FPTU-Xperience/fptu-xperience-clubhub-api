namespace ActivityService.Models;

public sealed class ActivityAttendance
{
    public int Id { get; set; }
    public int? SessionId { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public string VerificationStatus { get; set; } = "UNVERIFIED";
    public string? VerificationMethod { get; set; }
    public string RecognitionStatus { get; set; } = "NOT_REQUESTED";
    public Guid? ContributionId { get; set; }
    public Guid? ManualRequestId { get; set; }
    public int ActivityId { get; set; }
    public ClubActivity Activity { get; set; } = null!;
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateOnly AttendanceDate { get; set; }
    public string Status { get; set; } = AttendanceStatuses.NotMarked;
    public string? Note { get; set; }
    public DateTimeOffset? CheckedInAtUtc { get; set; }
    public int? CheckedInByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
