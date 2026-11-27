namespace ActivityService.Models;

public sealed class ActivityFundingCandidate
{
    public int ActivityId { get; set; }
    public int UserId { get; set; }
    public string SourceKey { get; set; } = "";
}
