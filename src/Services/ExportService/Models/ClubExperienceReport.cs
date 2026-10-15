namespace ExportService.Models;

public sealed class ClubExperienceReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ClubId { get; set; }
    public string SemesterId { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public int CreatedBy { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string SnapshotJson { get; set; } = "";
    public string SnapshotHash { get; set; } = "";
    public byte[]? PdfBytes { get; set; }
    public string? FailureCode { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
