namespace ActivityService.Models;

public sealed class ActivityParticipant
{
    [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public int Id { get; set; }
    public int ActivityId { get; set; }
    public ClubActivity Activity { get; set; } = null!;
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string AttendanceStatus { get; set; } = AttendanceStatuses.Registered;
    public DateTimeOffset RegisteredAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
