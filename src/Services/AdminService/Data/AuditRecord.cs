namespace AdminService.Data;

public sealed class AuditRecord
{
    public Guid Id { get; set; }
    public required string ActorSubjectId { get; set; }
    public string? ActorEmail { get; set; }
    public required string ActorRolesJson { get; set; }
    public required string Action { get; set; }
    public required string ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public required string CorrelationId { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public required string Outcome { get; set; }
    public required string MetadataJson { get; set; }
}
