namespace AdminService.Auditing;

public static class AuditOutcomes
{
    public const string Succeeded = "SUCCEEDED";
    public const string Failed = "FAILED";
    public const string Denied = "DENIED";
}

public sealed record AuditWriteRequest(
    string Action,
    string ResourceType,
    string? ResourceId = null,
    string Outcome = AuditOutcomes.Succeeded,
    IReadOnlyDictionary<string, string?>? Metadata = null);

public sealed record AuditEvent(
    Guid Id,
    string ActorSubjectId,
    string? ActorEmail,
    IReadOnlyCollection<string> ActorRoles,
    string Action,
    string ResourceType,
    string? ResourceId,
    string CorrelationId,
    DateTimeOffset TimestampUtc,
    string Outcome,
    IReadOnlyDictionary<string, string?> Metadata);

public interface IAuditService
{
    Task<AuditEvent> WriteAsync(
        AuditWriteRequest request,
        CancellationToken cancellationToken = default);
}
