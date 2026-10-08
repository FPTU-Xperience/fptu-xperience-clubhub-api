namespace AdminService.Data;

public sealed class PlatformSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GoogleDomain { get; set; } = "fpt.edu.vn";
    public string? TimetableUrl { get; set; }
    public bool InAppNotifications { get; set; } = true;
    public bool EmailNotifications { get; set; } = true;
    public string Digest { get; set; } = "weekly";
    public int RateLimit { get; set; } = 100;
    public int Retention { get; set; } = 365;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? UpdatedBy { get; set; }
}
