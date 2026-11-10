namespace ActivityService.Models;

public sealed class ActivityRescheduleRequest
{
    public Guid Id { get; set; }
    public int ActivityId { get; set; }
    public Guid Version { get; set; }
    public int RequestedBy { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset OriginalStartTimeUtc { get; set; }
    public DateTimeOffset OriginalEndTimeUtc { get; set; }
    public DateTimeOffset StartTimeUtc { get; set; }
    public DateTimeOffset EndTimeUtc { get; set; }
    public DateTimeOffset ReviewDeadlineUtc { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "PENDING";
    public int? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
}
