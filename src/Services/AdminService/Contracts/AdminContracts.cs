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
    IReadOnlyDictionary<string, string?> Metadata,
    DateTimeOffset Timestamp,
    string Actor,
    string Detail,
    string Area)
{
    public static AuditEventResponse From(AuditEvent value)
    {
        var actor = !string.IsNullOrWhiteSpace(value.ActorEmail)
            ? value.ActorEmail
            : (value.ActorRoles.Count > 0 ? string.Join(", ", value.ActorRoles) : value.ActorSubjectId);

        var actionUpper = value.Action.ToUpperInvariant();
        var resourceUpper = value.ResourceType.ToUpperInvariant();
        string area;
        if (actionUpper.Contains("QUEST") ||
            actionUpper.Contains("ANOMALY") ||
            actionUpper.Contains("BENCHMARK") ||
            actionUpper.Contains("DECLARATION") ||
            resourceUpper.Contains("QUEST") ||
            resourceUpper.Contains("ANOMALY") ||
            resourceUpper.Contains("BENCHMARK") ||
            resourceUpper.Contains("DECLARATION") ||
            value.ActorRoles.Any(r => r.Contains("AFFAIRS", StringComparison.OrdinalIgnoreCase)))
        {
            area = "affairs";
        }
        else if (actionUpper.Contains("SETTING") ||
                 actionUpper.Contains("USER") ||
                 actionUpper.Contains("ACCOUNT") ||
                 actionUpper.Contains("ROLE") ||
                 actionUpper.Contains("SECURITY") ||
                 value.ActorRoles.Any(r => r.Contains("ADMIN", StringComparison.OrdinalIgnoreCase)))
        {
            area = "admin";
        }
        else
        {
            area = "system";
        }

        var detail = value.Metadata != null && value.Metadata.TryGetValue("reason", out var reason) && !string.IsNullOrWhiteSpace(reason)
            ? reason
            : (value.ResourceId != null ? $"{value.Action} on {value.ResourceType} ({value.ResourceId})" : $"{value.Action} on {value.ResourceType}");

        return new(
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
            value.Metadata ?? new Dictionary<string, string?>(),
            value.TimestampUtc,
            actor,
            detail,
            area);
    }
}

