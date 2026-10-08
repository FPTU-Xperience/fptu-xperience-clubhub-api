namespace AdminService.Data;

public sealed class XpAnomaly
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Severity { get; set; } = "medium"; // "high", "medium", "low"

    public string Status { get; set; } = "open"; // "open", "resolved"

    public string Club { get; set; } = string.Empty;

    public string Student { get; set; } = string.Empty;

    public int StudentUserId { get; set; }

    public string Source { get; set; } = "Tự khai báo";

    public string Evidence { get; set; } = string.Empty;

    public int Amount { get; set; }

    public int Count { get; set; } = 1;

    public string SemesterCode { get; set; } = "FALL2026";

    public string CampusCode { get; set; } = "HAN";

    public string? Decision { get; set; } // "keep", "adjust", "revoke"

    public int? Adjustment { get; set; }

    public string? Reason { get; set; }

    public string? ResolvedByName { get; set; }

    public DateTimeOffset? ResolvedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
