using System.Text.Json;
using System.Text.Json.Serialization;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Messaging;
using ClubReportHub.Shared.Rosters;
using ClubService.Data;
using ClubService.Extensions;
using ClubService.Models;
using ClubService.Services;
using Microsoft.EntityFrameworkCore;

namespace ClubService.Endpoints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClubViceNominationInput(int MembershipId, Guid ExpectedClubVersion, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClubViceDecisionInput(Guid ExpectedVersion, string Reason, DateTimeOffset? EffectiveFromUtc = null, DateTimeOffset? EffectiveUntilUtc = null);

public static class ClubViceEndpoints
{
    internal static void MapClubVice(this IEndpointRouteBuilder app)
    {
        var routes = app.MapGroup("/api/clubs/{clubId:int}/governance/leadership-nominations")
            .RequireAuthorization(AuthPolicies.BusinessAccess).AddEndpointFilter<RosterExceptionFilter>().WithTags("Approved vice leadership");
        routes.MapGet("/", Read);
        routes.MapPost("/", Nominate);
        routes.MapPost("/{nominationId:guid}/approve", (int clubId, Guid nominationId, ClubViceDecisionInput input, ClubDbContext db, HttpContext http, CancellationToken ct) => Decide(clubId, nominationId, input, "APPROVED", db, http, ct));
        routes.MapPost("/{nominationId:guid}/reject", (int clubId, Guid nominationId, ClubViceDecisionInput input, ClubDbContext db, HttpContext http, CancellationToken ct) => Decide(clubId, nominationId, input, "REJECTED", db, http, ct));
        routes.MapPost("/{nominationId:guid}/revoke", (int clubId, Guid nominationId, ClubViceDecisionInput input, ClubDbContext db, HttpContext http, CancellationToken ct) => Decide(clubId, nominationId, input, "REVOKED", db, http, ct));
    }
    private static DateTimeOffset Now(HttpContext http) => http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
    private static bool Reason(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 500;
    private static async Task<InternalUserProfile?> Account(int userId, HttpContext http, CancellationToken ct)
    {
        var accounts = await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([userId], ct);
        return accounts.TryGetValue(userId, out var account) && account is { IsActive: true, IsLocked: false } ? account : null;
    }
    private static async Task<bool> Reviewer(Club club, HttpContext http, CancellationToken ct)
    {
        var account = await Account(http.User.GetUserId(), http, ct);
        return account is not null && (http.User.IsSuperAdmin() || (http.User.IsStudentAffairsAdministrator() && account.CampusCode == club.CampusCode));
    }
    private static object Snapshot(ClubViceAppointment row) => new
    {
        row.Id,
        row.ClubId,
        row.MembershipId,
        row.UserId,
        row.MemberName,
        row.ProposedBy,
        row.ProposedAt,
        row.Status,
        row.Version,
        effectiveFromUtc = row.From,
        effectiveUntilUtc = row.Until
    };
    private static void Audit(ClubViceAppointment row, string action, string reason, ClubDbContext db, HttpContext http)
    {
        db.Add(new ClubGovernanceLeadershipEvent { AppointmentId = row.Id, ActorId = http.User.GetUserId(), Action = action, Reason = reason.Trim(), At = Now(http), SnapshotJson = JsonSerializer.Serialize(Snapshot(row)) });
        ClubGovernanceAudit.Record(db, row.ClubId, "LEADERSHIP", action, reason.Trim(), Snapshot(row), http, row.MembershipId, row.UserId);
    }
    private static async Task<IResult> Read(int clubId, int? page, int? pageSize, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var club = await db.Clubs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == clubId && x.IsActive, ct); if (club is null) return Results.NotFound();
        var reviewer = await Reviewer(club, http, ct);
        if (!reviewer && (!await db.UserOwnsClubAsync(clubId, http.User.GetUserId(), ct) || await Account(http.User.GetUserId(), http, ct) is null)) return Results.Forbid();
        var query = db.Set<ClubViceAppointment>().AsNoTracking().Where(x => x.ClubId == clubId); var total = await query.CountAsync(ct);
        var current = Math.Clamp(page ?? 1, 1, 1_000_000); var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var rows = await query.OrderByDescending(x => x.ProposedAt).ThenBy(x => x.Id).Skip((current - 1) * size).Take(size).ToArrayAsync(ct);
        return Results.Ok(new
        {
            clubId,
            clubVersion = club.ConcurrencyToken,
            capabilities = new { canReview = reviewer, canNominate = await db.UserOwnsClubAsync(clubId, http.User.GetUserId(), ct) },
            items = rows.Select(Snapshot),
            page = current,
            pageSize = size,
            totalItems = total,
            totalPages = (int)Math.Ceiling(total / (double)size)
        });
    }
    private static async Task<IResult> Nominate(int clubId, ClubViceNominationInput input, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!await db.UserOwnsClubAsync(clubId, http.User.GetUserId(), ct) || await Account(http.User.GetUserId(), http, ct) is null) return Results.Forbid();
        if (!Reason(input.Reason) || input.MembershipId < 1) return ApiOperations.Error(400, "INVALID_NOMINATION", "Provide an approved membership and a reason up to 500 characters.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            var club = await db.Clubs.SingleOrDefaultAsync(x => x.Id == clubId && x.IsActive && x.DeletedAtUtc == null, ct);
            if (club is null || !await db.UserOwnsClubAsync(clubId, http.User.GetUserId(), ct) || await Account(http.User.GetUserId(), http, ct) is null)
                return ApiOperations.Error(403, "OWNER_REQUIRED", "The current active owner must propose leadership.");
            if (club.ConcurrencyToken != input.ExpectedClubVersion) return ApiOperations.Error(409, "STALE_NOMINATION", "Reload the current club version.");
            var member = await db.ClubMemberships.SingleOrDefaultAsync(x => x.Id == input.MembershipId && x.ClubId == clubId && x.Status == ClubMembershipStatuses.Approved, ct);
            var account = member is null ? null : await Account(member.UserId, http, ct);
            if (member is null || account is null || (club.CampusCode != CampusCodes.Global && account.CampusCode != club.CampusCode))
                return ApiOperations.Error(409, "MEMBERSHIP_CHANGED", "Select a current active approved campus member.");
            if (await db.UserOwnsClubAsync(clubId, member.UserId, ct)) return ApiOperations.Error(409, "OWNER_PROTECTED", "The active owner cannot be appointed as vice president.");
            var now = Now(http);
            if (await db.Set<ClubViceAppointment>().AnyAsync(x => x.ClubId == clubId && x.UserId == member.UserId
                && (x.Status == "PENDING" || (x.Status == "APPROVED" && x.Until > now)), ct)) return ApiOperations.Error(409, "NOMINATION_EXISTS", "This member already has a pending or unexpired vice appointment.");
            var row = new ClubViceAppointment
            {
                ClubId = clubId,
                MembershipId = member.Id,
                UserId = member.UserId,
                MemberName = member.FullName,
                ProposedBy = http.User.GetUserId(),
                ProposedAt = now
            };
            db.Add(row); Audit(row, "LEADERSHIP_NOMINATED", input.Reason, db, http); club.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/clubs/{clubId}/governance/leadership-nominations/{row.Id}", Snapshot(row));
        }, ct);
    }
    private static async Task<IResult> Decide(int clubId, Guid nominationId, ClubViceDecisionInput input, string target, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        var initialClub = await db.Clubs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == clubId && x.IsActive, ct);
        if (initialClub is null) return Results.NotFound(); if (!await Reviewer(initialClub, http, ct)) return Results.Forbid();
        if (!Reason(input.Reason)) return ApiOperations.Error(400, "REASON_REQUIRED", "Provide a reason up to 500 characters.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            var club = await db.Clubs.SingleOrDefaultAsync(x => x.Id == clubId && x.IsActive && x.DeletedAtUtc == null, ct);
            if (club is null || !await Reviewer(club, http, ct)) return ApiOperations.Error(403, "REVIEWER_REQUIRED", "Current CTSV campus authority is required.");
            var row = await db.Set<ClubViceAppointment>().SingleOrDefaultAsync(x => x.Id == nominationId && x.ClubId == clubId, ct);
            if (row is null) return ApiOperations.Error(404, "NOMINATION_NOT_FOUND", "The nomination does not belong to this club.");
            if (row.Version != input.ExpectedVersion || (target == "REVOKED" ? row.Status != "APPROVED" : row.Status != "PENDING"))
                return ApiOperations.Error(409, "STALE_NOMINATION", "Reload the current nomination decision.");
            if (http.User.GetUserId() == row.UserId || http.User.GetUserId() == row.ProposedBy) return ApiOperations.Error(403, "SELF_REVIEW_FORBIDDEN", "An independent CTSV reviewer is required.");
            if (target == "APPROVED")
            {
                var member = await db.ClubMemberships.SingleOrDefaultAsync(x => x.Id == row.MembershipId && x.UserId == row.UserId && x.ClubId == clubId && x.Status == ClubMembershipStatuses.Approved, ct);
                var account = await Account(row.UserId, http, ct);
                if (member is null || account is null || (club.CampusCode != CampusCodes.Global && account.CampusCode != club.CampusCode) || !await db.UserOwnsClubAsync(clubId, row.ProposedBy, ct)
                    || await Account(row.ProposedBy, http, ct) is null || await db.UserOwnsClubAsync(clubId, row.UserId, ct))
                    return ApiOperations.Error(409, "NOMINATION_SOURCE_CHANGED", "The proposer, candidate or current ownership has changed.");
                if (!input.EffectiveFromUtc.HasValue || !input.EffectiveUntilUtc.HasValue || input.EffectiveFromUtc >= input.EffectiveUntilUtc || input.EffectiveUntilUtc <= Now(http))
                    return ApiOperations.Error(400, "INVALID_APPOINTMENT_INTERVAL", "Provide a valid explicit UTC appointment interval ending in the future.");
                if (await db.Set<ClubViceAppointment>().AnyAsync(x => x.ClubId == clubId && x.UserId == row.UserId && x.Id != row.Id && x.Status == "APPROVED"
                    && x.From < input.EffectiveUntilUtc && input.EffectiveFromUtc < x.Until, ct)) return ApiOperations.Error(409, "APPOINTMENT_OVERLAP", "This member already has an overlapping approved appointment.");
                row.From = input.EffectiveFromUtc.Value.ToUniversalTime(); row.Until = input.EffectiveUntilUtc.Value.ToUniversalTime();
            }
            row.Status = target; row.Version = Guid.NewGuid(); club.ConcurrencyToken = Guid.NewGuid(); Audit(row, "LEADERSHIP_" + target, input.Reason, db, http);
            db.AddOutboxMessage(new ClubAccessInvalidatedEvent(Guid.NewGuid(), Now(http), clubId, [row.UserId]), EventRoutingKeys.ClubAccessInvalidated);
            await db.SaveChangesAsync(ct); return Results.Ok(Snapshot(row));
        }, ct);
    }
}
