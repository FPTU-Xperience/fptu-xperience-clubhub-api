using System.Text.Json;
using AdminService.Data;
using AdminService.Errors;
using AdminService.Observability;
using AdminService.Security;

namespace AdminService.Auditing;

public sealed class EfAuditService(
    AdminDbContext dbContext,
    ICurrentActor currentActor,
    ICorrelationContext correlationContext) : IAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AuditEvent> WriteAsync(
        AuditWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!currentActor.IsAuthenticated || string.IsNullOrWhiteSpace(currentActor.SubjectId))
        {
            throw new OperationForbiddenException("An authenticated actor is required for audit writes.");
        }

        var roles = currentActor.Roles.ToArray();
        var metadata = request.Metadata is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(request.Metadata, StringComparer.OrdinalIgnoreCase);
        var record = new AuditRecord
        {
            Id = Guid.NewGuid(),
            ActorSubjectId = currentActor.SubjectId,
            ActorEmail = currentActor.Email,
            ActorRolesJson = JsonSerializer.Serialize(roles, JsonOptions),
            Action = request.Action,
            ResourceType = request.ResourceType,
            ResourceId = request.ResourceId,
            CorrelationId = correlationContext.Id,
            TimestampUtc = DateTimeOffset.UtcNow,
            Outcome = request.Outcome,
            MetadataJson = JsonSerializer.Serialize(metadata, JsonOptions)
        };

        dbContext.AuditRecords.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToAuditEvent(record);
    }

    public static AuditEvent ToAuditEvent(AuditRecord record) => new(
        record.Id,
        record.ActorSubjectId,
        record.ActorEmail,
        JsonSerializer.Deserialize<string[]>(record.ActorRolesJson, JsonOptions) ?? [],
        record.Action,
        record.ResourceType,
        record.ResourceId,
        record.CorrelationId,
        record.TimestampUtc,
        record.Outcome,
        JsonSerializer.Deserialize<Dictionary<string, string?>>(record.MetadataJson, JsonOptions)
        ?? new Dictionary<string, string?>());
}
