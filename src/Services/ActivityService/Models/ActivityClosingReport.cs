namespace ActivityService.Models;

public sealed class ActivityClosingReport
{
    public int ActivityId { get; set; }
    public Guid Version { get; set; }
    public string Status { get; set; } = "SUBMITTED";
    public string Summary { get; set; } = "";
    public string Improvements { get; set; } = "";
    public int SubmittedBy { get; set; }
    public DateTimeOffset SubmittedAtUtc { get; set; }
    public int? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
}

public sealed class ActivityClosingPolicy
{
    public long Id { get; set; }
    public string CampusCode { get; set; } = "";
    public int ReportDays { get; set; }
    public int UpdatedBy { get; set; }
    public DateTimeOffset EffectiveAtUtc { get; set; }
}
