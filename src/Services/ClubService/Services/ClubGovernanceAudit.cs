using System.Security.Claims;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubService.Data;
using ClubService.Models;

namespace ClubService.Services;

internal static class ClubGovernanceAudit
{
    internal static ClubGovernanceAuditEvent Record(ClubDbContext db, int clubId, string source, string action, string reason, object payload,
        HttpContext http, int? membershipId = null, int? userId = null)
    {
        var row = new ClubGovernanceAuditEvent
        {
            ClubId = clubId,
            Source = source,
            Action = action,
            Reason = reason,
            ActorId = http.User.GetUserId(),
            ActorName = http.User.FindFirstValue(ClaimTypes.Name) ?? "",
            MembershipId = membershipId,
            UserId = userId,
            At = http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow,
            PayloadJson = JsonSerializer.Serialize(payload, ClubGovernancePolicy.Json)
        };
        db.Add(row); return row;
    }
}
