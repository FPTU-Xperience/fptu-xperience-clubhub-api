namespace ActivityService.Models;

public sealed class CampusActivityDuty
{
    public Guid Id { get; set; }
    public int ActivityId { get; set; }
    public int UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public string Responsibility { get; set; } = "";
    public Guid Version { get; set; }
    public int AssignedBy { get; set; }
    public DateTimeOffset AssignedAtUtc { get; set; }
    public int? ConfirmedBy { get; set; }
    public DateTimeOffset? ConfirmedAtUtc { get; set; }
}
