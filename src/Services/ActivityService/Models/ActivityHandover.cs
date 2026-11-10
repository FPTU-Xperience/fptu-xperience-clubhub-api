namespace ActivityService.Models;

public sealed class ActivityHandover
{
    public int ActivityId { get; set; }
    public int ClubId { get; set; }
    public string ClubName { get; set; } = "";
    public Guid Version { get; set; }
    public string Status { get; set; } = "PENDING";
    public int OfferedBy { get; set; }
    public DateTimeOffset OfferedAtUtc { get; set; }
    public int? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
}
