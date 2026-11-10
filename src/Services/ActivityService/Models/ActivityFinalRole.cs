namespace ActivityService.Models;

public sealed class ActivityFinalRole
{
    public int ActivityId { get; set; }
    public int UserId { get; set; }
    public Guid Version { get; set; }
    public string Role { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int ConfirmedBy { get; set; }
    public DateTimeOffset ConfirmedAtUtc { get; set; }
}
