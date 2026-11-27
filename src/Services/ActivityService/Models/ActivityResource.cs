namespace ActivityService.Models;

public sealed class ActivityResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ActivityId { get; set; }
    public string Kind { get; set; } = "EMBED";
    public string Title { get; set; } = string.Empty;
    public string Visibility { get; set; } = "CLUB";
    public string Url { get; set; } = string.Empty;
    public string Provider { get; set; } = "EXTERNAL";
    public string? FileName { get; set; }
    public string? MimeType { get; set; }
    public long? SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public int Position { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public int UploadedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? DetachedAtUtc { get; set; }
}

// Stored separately so gallery/history queries never load file contents.
public sealed class ActivityResourceFile
{
    public Guid ResourceId { get; set; }
    public byte[] Bytes { get; set; } = [];
}

public sealed class ActivityResourceAudit
{
    public long Id { get; set; }
    public int ActivityId { get; set; }
    public Guid ResourceId { get; set; }
    public string Action { get; set; } = string.Empty;
    public int ActorId { get; set; }
    public string? Reason { get; set; }
    public string? BeforeJson { get; set; }
    public string AfterJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
