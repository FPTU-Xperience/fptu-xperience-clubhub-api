namespace AdminService.Data;

public sealed class XpLedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int StudentUserId { get; set; }

    public string StudentName { get; set; } = string.Empty;

    public string Type { get; set; } = "award"; // "award", "adjust", "revoke"

    public string Source { get; set; } = string.Empty;

    public string Actor { get; set; } = "System";

    public int Amount { get; set; }

    public int RubricVersion { get; set; } = 1;

    public string Reason { get; set; } = string.Empty;

    public string PillarCategory { get; set; } = "Community";

    public string SemesterCode { get; set; } = "FALL2026";

    public string CampusCode { get; set; } = "HAN";

    public Guid? RelatedAnomalyId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
