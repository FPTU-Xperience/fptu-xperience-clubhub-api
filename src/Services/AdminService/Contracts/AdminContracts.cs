using AdminService.Auditing;

namespace AdminService.Contracts;

public sealed record CurrentActorResponse(
    string SubjectId,
    int? UserId,
    string? Email,
    IReadOnlyCollection<string> Roles);

public sealed record CreateAuditEventRequest(
    string? Action,
    string? ResourceType,
    string? ResourceId,
    string? Outcome,
    IReadOnlyDictionary<string, string?>? Metadata);

public sealed record AuditEventResponse(
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
    IReadOnlyDictionary<string, string?> Metadata)
{
    public static AuditEventResponse From(AuditEvent value) => new(
        value.Id,
        value.ActorSubjectId,
        value.ActorEmail,
        value.ActorRoles,
        value.Action,
        value.ResourceType,
        value.ResourceId,
        value.CorrelationId,
        value.TimestampUtc,
        value.Outcome,
        value.Metadata);
}
