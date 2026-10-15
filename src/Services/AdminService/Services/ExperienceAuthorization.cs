using System.Security.Claims;
using System.Text.Json;
using AdminService.Data;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Services;

internal static class ExperienceAuthorization
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal static readonly string[] Capabilities = ["contribution.review", "attendance.verify", "experience.adjust"];

    internal static async Task<VerificationAssignment?> FindAsync(AdminDbContext db, int actor,
        string sourceType, int? clubId, string capability, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var grants = await db.VerificationAssignments.Where(x => x.UserId == actor && x.SourceType == sourceType
            && x.ClubId == clubId && !x.IsRevoked && x.StartsAt <= now && (x.ExpiresAt == null || x.ExpiresAt > now))
            .ToListAsync(cancellationToken);
        return grants.FirstOrDefault(x => (JsonSerializer.Deserialize<string[]>(x.CapabilitiesJson, JsonOptions) ?? []).Contains(capability));
    }

    internal static async Task<HashSet<int>> ClubsAsync(AdminDbContext db, int actor, string capability, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var grants = await db.VerificationAssignments.AsNoTracking().Where(x => x.UserId == actor && x.SourceType == "CLUB"
            && x.ClubId != null && !x.IsRevoked && x.StartsAt <= now && (x.ExpiresAt == null || x.ExpiresAt > now))
            .ToListAsync(cancellationToken);
        return grants.Where(x => (JsonSerializer.Deserialize<string[]>(x.CapabilitiesJson, JsonOptions) ?? []).Contains(capability))
            .Select(x => x.ClubId!.Value).ToHashSet();
    }

    internal static void Audit(AdminDbContext db, ClaimsPrincipal user, string action, string resourceType,
        string resourceId, object metadata, string? correlation = null) => db.AuditRecords.Add(new AuditRecord
        {
            Id = Guid.NewGuid(),
            ActorSubjectId = user.GetUserId().ToString(),
            ActorRolesJson = JsonSerializer.Serialize(
            user.FindAll(ClaimTypes.Role).Select(x => x.Value), JsonOptions),
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            CorrelationId = correlation ?? Guid.NewGuid().ToString("N"),
            TimestampUtc = DateTimeOffset.UtcNow,
            Outcome = "SUCCEEDED",
            MetadataJson = JsonSerializer.Serialize(metadata, JsonOptions)
        });
}
