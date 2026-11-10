namespace ActivityService.Models;

public sealed class ActivityRecapFeedback
{
    public int ActivityId { get; set; }
    public int UserId { get; set; }
    public Guid Version { get; set; }
    public string Strengths { get; set; } = "";
    public string Weaknesses { get; set; } = "";
    public string Suggestions { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
