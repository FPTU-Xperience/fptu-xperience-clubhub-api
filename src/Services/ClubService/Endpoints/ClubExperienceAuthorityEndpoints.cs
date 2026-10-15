using System.Security.Cryptography;
using System.Text;
using ClubReportHub.Shared.Experience;
using ClubService.Data;
using Microsoft.EntityFrameworkCore;

namespace ClubService.Endpoints;

public static class ClubExperienceAuthorityEndpoints
{
    public static void MapClubExperienceAuthorityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/internal/experience/clubs/{clubId:int}", async (int clubId, int? userId, ClubDbContext db,
            HttpContext http, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
            var club = await db.Clubs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == clubId, ct);
            if (club is null) return Results.NotFound();
            var member = await db.ClubMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.ClubId == clubId && x.UserId == userId && x.Status == "Approved", ct);
            return Results.Ok(new ClubExperienceAuthority(club.Id, club.Name, club.CampusCode, club.IsActive && club.DeletedAtUtc == null,
                member is not null, member?.ReviewedAtUtc ?? member?.RequestedAtUtc));
        }).AllowAnonymous().ExcludeFromDescription();

        app.MapPost("/api/internal/experience/student/club-scope", async (StudentClubScopeRequest query, ClubDbContext db,
            HttpContext http, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
            if (query.UserId <= 0 || string.IsNullOrWhiteSpace(query.CampusCode) || query.CampusCode.Length > 40) return Results.BadRequest();
            return Results.Ok(await StudentScopeAsync(query, db, ct));
        }).AllowAnonymous().ExcludeFromDescription();

        app.MapPost("/api/internal/experience/student/counters", async (StudentCounterQuery query, ClubDbContext db,
            HttpContext http, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
            if (query.UserId <= 0 || query.Scope is not ("PERIOD" or "CUMULATIVE")) return Results.BadRequest();
            var rows = await db.ClubMemberships.AsNoTracking().Where(x => x.UserId == query.UserId && x.Status == "Approved"
                && x.Club.IsActive && x.Club.DeletedAtUtc == null).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.ClubId, x.ConcurrencyToken, ClubVersion = x.Club.ConcurrencyToken }).ToListAsync(ct);
            var count = rows.Select(x => x.ClubId).Distinct().Count();
            var fingerprint = $"{query.UserId}:" + string.Join("|", rows.Select(x => $"{x.Id}:{x.ClubId}:{x.ConcurrencyToken}:{x.ClubVersion}"));
            var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
            return Results.Ok(new StudentCounters(query.UserId, null, count, DateTimeOffset.UtcNow, version));
        }).AllowAnonymous().ExcludeFromDescription();
    }

    private static async Task<StudentClubScope> StudentScopeAsync(StudentClubScopeRequest request, ClubDbContext db, CancellationToken ct)
    {
        var clubs = await db.Clubs.AsNoTracking().Where(x => x.IsActive && x.DeletedAtUtc == null
            && (x.CampusCode == request.CampusCode || x.Memberships.Any(m => m.UserId == request.UserId && m.Status == "Approved")
                || x.ManagerAssignments.Any(m => m.ManagerUserId == request.UserId && m.IsActive)))
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.Category,
                x.CampusCode,
                x.ConcurrencyToken,
                Owner = x.ManagerAssignments.Any(m => m.ManagerUserId == request.UserId && m.IsActive)
            }).ToListAsync(ct);
        var ids = clubs.Select(x => x.Id).ToArray();
        var members = await db.ClubMemberships.AsNoTracking().Where(x => ids.Contains(x.ClubId)
            && x.UserId == request.UserId && x.Status == "Approved").ToDictionaryAsync(x => x.ClubId, ct);
        var items = clubs.OrderBy(x => x.Id).Select(x =>
        {
            var member = members.GetValueOrDefault(x.Id);
            return new StudentClubMetadata(x.Id, x.Code, x.Name, x.Category, x.CampusCode,
                member is not null, member is not null || x.Owner, member?.ReviewedAtUtc ?? member?.RequestedAtUtc,
                x.ConcurrencyToken, member?.ConcurrencyToken);
        }).ToArray();
        var fingerprint = string.Join("|", items.Select(x => $"{x.ClubId}:{x.Version}:{x.MembershipVersion}:{x.IsApprovedMember}:{x.CanViewPrivate}"));
        return new(items, DateTimeOffset.UtcNow, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint))));
    }
}
