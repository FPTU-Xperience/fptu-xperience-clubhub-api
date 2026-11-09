using ClubReportHub.Shared.Rosters;
using ClubReportHub.Shared.Experience;
using ClubService.Services;
using System.Security.Claims;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Messaging;
using ClubService.Contracts;
using ClubService.Data;
using ClubService.Extensions;
using ClubService.Mappers;
using ClubService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClubService.Endpoints;

/// <summary>
/// Endpoints for club ownership transfer workflow.
/// Owner selects member -> Submit transfer request -> Admin approves/rejects
/// </summary>
public static class TransferEndpoints
{
    public static void MapTransferEndpoints(this WebApplication app)
    {
        var clubs = app.MapGroup("/api/clubs")
            .WithTags("Club Ownership Transfer")
            .RequireAuthorization(AuthPolicies.BusinessAccess);

        // Owner: Get list of approved members eligible for ownership transfer
        clubs.MapGet("/{clubId:int}/members-for-transfer", GetEligibleMembersForTransfer)
            .WithName("GetEligibleMembersForTransfer")
            .WithDescription("Get list of members eligible for ownership transfer");

        // Owner: Submit ownership transfer request
        clubs.MapPost("/{clubId:int}/transfer-ownership", SubmitTransferRequest)
            .WithName("SubmitTransferRequest")
            .WithDescription("Submit an ownership transfer request (Club owner only)");

        // Owner: Get transfer request for their club
        clubs.MapGet("/{clubId:int}/transfer-request", GetTransferRequest)
            .WithName("GetTransferRequest")
            .WithDescription("Get transfer request for a club (Club owner only)");

        // Admin: Get all transfer requests
        clubs.MapGet("/transfer-requests", GetAllTransferRequests)
            .WithName("GetAllTransferRequests")
            .WithDescription("Get all pending transfer requests (Admin only)")
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);

        // Admin: Approve transfer request
        clubs.MapPost("/transfer-requests/{requestId:int}/approve", ApproveTransferRequest).AddEndpointFilter<RosterExceptionFilter>()
            .WithName("ApproveTransferRequest")
            .WithDescription("Approve an ownership transfer request (Admin only)")
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);

        // Admin: Reject transfer request
        clubs.MapPost("/transfer-requests/{requestId:int}/reject", RejectTransferRequest)
            .WithName("RejectTransferRequest")
            .WithDescription("Reject an ownership transfer request (Admin only)")
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);
    }

    /// <summary>
    /// Owner: Get list of approved members eligible for ownership transfer.
    /// </summary>
    private static async Task<IResult> GetEligibleMembersForTransfer(
        int clubId,
        ClubDbContext db,
        HttpContext http,
        CancellationToken ct)
    {
        var userId = http.User.GetUserId();

        var isOwner = await db.ClubManagerAssignments.AnyAsync(
            x => x.ClubId == clubId && x.ManagerUserId == userId && x.IsActive, ct);

        if (!isOwner)
            return Results.Forbid();

        var members = await db.ClubMemberships
            .AsNoTracking()
            .Where(x => x.ClubId == clubId && x.Status == ClubMembershipStatuses.Approved)
            .OrderBy(x => x.FullName)
            .Select(x => new ClubMemberForTransferResponse(
                x.UserId, x.FullName, x.Email, x.Role,
                x.ReviewedAtUtc ?? x.RequestedAtUtc))
            .ToListAsync(ct);

        return Results.Ok(members);
    }

    /// <summary>
    /// Owner: Submit an ownership transfer request.
    /// </summary>
    private static async Task<IResult> SubmitTransferRequest(
        int clubId,
        TransferOwnershipRequest request,
        ClubDbContext db,
        HttpContext http,
        CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        var userName = http.User.GetDisplayName();

        var club = await db.Clubs.FindAsync(new object[] { clubId }, ct);
        if (club is null)
            return Results.NotFound(new { message = "Club not found" });

        if (!club.IsActive)
            return Results.BadRequest(new { message = "Club is inactive" });

        var isOwner = await db.ClubManagerAssignments.AnyAsync(
            x => x.ClubId == clubId && x.ManagerUserId == userId && x.IsActive, ct);

        if (!isOwner)
            return Results.Forbid();

        var newOwnerMembership = await db.ClubMemberships
            .FirstOrDefaultAsync(
                x => x.ClubId == clubId && x.UserId == request.NewOwnerUserId && x.Status == ClubMembershipStatuses.Approved,
                ct);

        if (newOwnerMembership is null)
            return Results.BadRequest(new { message = "New owner must be an approved club member" });

        if (request.NewOwnerUserId == userId)
            return Results.BadRequest(new { message = "Cannot transfer ownership to yourself" });

        var existingRequest = await db.ClubOwnershipTransfers
            .AnyAsync(x => x.ClubId == clubId && x.Status == ClubOwnershipTransferStatuses.Pending, ct);

        if (existingRequest)
            return Results.Conflict(new { message = "A pending transfer request already exists for this club" });

        var transferRequest = new ClubOwnershipTransfer
        {
            ClubId = clubId,
            CurrentOwnerUserId = userId,
            CurrentOwnerName = userName,
            NewOwnerUserId = request.NewOwnerUserId,
            NewOwnerName = newOwnerMembership.FullName,
            Reason = request.Reason,
            Status = ClubOwnershipTransferStatuses.Pending,
            RequestedAtUtc = DateTimeOffset.UtcNow
        };

        club.ConcurrencyToken = Guid.NewGuid();

        try
        {
            await db.SaveChangesAsync(ct);
            db.ClubOwnershipTransfers.Add(transferRequest);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { message = "A pending transfer request already exists for this club" });
        }

        return Results.Created(
            $"/api/clubs/transfer-requests/{transferRequest.Id}",
            ClubMappers.ToTransferRequestResponse(transferRequest, club.Name));
    }

    /// <summary>
    /// Owner: Get the transfer request for their club.
    /// </summary>
    private static async Task<IResult> GetTransferRequest(
        int clubId,
        ClubDbContext db,
        HttpContext http,
        CancellationToken ct)
    {
        var userId = http.User.GetUserId();

        var isOwner = await db.ClubManagerAssignments.AnyAsync(
            x => x.ClubId == clubId && x.ManagerUserId == userId && x.IsActive, ct);

        if (!isOwner)
            return Results.Forbid();

        var request = await db.ClubOwnershipTransfers
            .Where(x => x.ClubId == clubId)
            .OrderByDescending(x => x.RequestedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (request is null)
            return Results.Ok(new { message = "No transfer request found" });

        var club = await db.Clubs.FindAsync(new object[] { clubId }, ct);

        return Results.Ok(ClubMappers.ToTransferRequestResponse(request, club?.Name ?? string.Empty));
    }

    /// <summary>
    /// Admin: Get all transfer requests.
    /// </summary>
    private static async Task<IResult> GetAllTransferRequests(
        ClubDbContext db,
        HttpContext http,
        CancellationToken ct)
    {
        if (!http.User.IsStudentAffairsAdministrator())
            return Results.Forbid();

        var requests = await db.ClubOwnershipTransfers
            .AsNoTracking()
            .Include(x => x.Club)
            .OrderByDescending(x => x.RequestedAtUtc)
            .Select(x => ClubMappers.ToTransferRequestResponse(x, x.Club.Name))
            .ToListAsync(ct);

        return Results.Ok(requests);
    }

    /// <summary>
    /// Admin: Approve a transfer request and change club ownership.
    /// </summary>
    private static async Task<IResult> ApproveTransferRequest(
        int requestId,
        ApproveTransferRequest request,
        ClubDbContext db,
        HttpContext http,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (!http.User.IsStudentAffairsAdministrator()) return Results.Forbid();
        if (request.AdminNote?.Length > 1000) return ApiOperations.Error(400, "INVALID_REVIEW_NOTE", "The review note cannot exceed 1,000 characters.");
        async Task<bool> Reviewer(Club club)
        {
            if (http.User.IsSuperAdmin()) return true;
            var client = http.RequestServices.GetService<CurrentUserProfileClient>();
            if (client is null) return false;
            var account = (await client.GetAsync([http.User.GetUserId()], ct)).GetValueOrDefault(http.User.GetUserId());
            return account is { IsActive: true, IsLocked: false } && account.CampusCode == club.CampusCode;
        }
        var initial = await db.ClubOwnershipTransfers.AsNoTracking().Include(x => x.Club).SingleOrDefaultAsync(x => x.Id == requestId, ct);
        if (initial is null) return Results.NotFound(new { message = "Transfer request not found" });
        if (!await Reviewer(initial.Club)) return Results.Forbid();
        return await ApiOperations.ExecuteAsync(http, db, new { requestId, request.AdminNote }, async () =>
        {
            var transfer = await db.ClubOwnershipTransfers.Include(x => x.Club).SingleOrDefaultAsync(x => x.Id == requestId, ct);
            if (transfer is null) return ApiOperations.Error(404, "TRANSFER_NOT_FOUND", "Transfer request not found.");
            if (!await Reviewer(transfer.Club)) return ApiOperations.Error(403, "CAMPUS_REVIEW_REQUIRED", "Current CTSV campus authority is required.");
            if (transfer.Status != ClubOwnershipTransferStatuses.Pending || !transfer.Club.IsActive || transfer.Club.DeletedAtUtc.HasValue)
                return ApiOperations.Error(409, "TRANSFER_SOURCE_CHANGED", "Reload a pending transfer for an active club.");
            var actor = http.User.GetUserId();
            if (actor == transfer.CurrentOwnerUserId || actor == transfer.NewOwnerUserId)
                return ApiOperations.Error(403, "SELF_REVIEW_FORBIDDEN", "An independent CTSV reviewer is required.");
            if (await db.ClubManagerAssignments.AnyAsync(x => x.ManagerUserId == transfer.NewOwnerUserId && x.ClubId != transfer.ClubId && x.IsActive, ct))
                return ApiOperations.Error(409, "MANAGER_ALREADY_ASSIGNED", "The new owner is already an active manager of another club.");
            var current = await db.ClubManagerAssignments.SingleOrDefaultAsync(x => x.ClubId == transfer.ClubId && x.IsActive, ct);
            var member = await db.ClubMemberships.SingleOrDefaultAsync(x => x.ClubId == transfer.ClubId && x.UserId == transfer.NewOwnerUserId && x.Status == ClubMembershipStatuses.Approved, ct);
            if (current is null || current.ManagerUserId != transfer.CurrentOwnerUserId || member is null)
                return ApiOperations.Error(409, "TRANSFER_SOURCE_CHANGED", "The current owner or candidate membership has changed.");
            var profiles = http.RequestServices.GetService<CurrentUserProfileClient>();
            if (profiles is null) return ApiOperations.Error(503, "PROFILE_SERVICE_UNAVAILABLE", "Current account authority is unavailable.");
            var accounts = await profiles.GetAsync([transfer.NewOwnerUserId], ct);
            if (!accounts.TryGetValue(transfer.NewOwnerUserId, out var account) || account is not { IsActive: true, IsLocked: false }
                || transfer.Club.CampusCode != CampusCodes.Global && account.CampusCode != transfer.Club.CampusCode)
                return ApiOperations.Error(409, "OWNER_CANDIDATE_INELIGIBLE", "The candidate must remain an active approved campus member.");
            if (member.Role == ClubMemberRoles.Treasurer || (await ClubGovernancePolicy.GroupTreasurers(db, transfer.ClubId, http, ct)).Contains(member.UserId))
                return ApiOperations.Error(409, "OWNER_TREASURER_CONFLICT", "Revoke the candidate's treasurer duty before ownership transfer.");
            var now = http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
            current.IsActive = false; current.EndedAtUtc = now;
            db.ClubManagerAssignments.Add(new ClubManagerAssignment { ClubId = transfer.ClubId, ManagerUserId = member.UserId, ManagerName = account.DisplayName, AssignedAtUtc = now, IsActive = true });
            transfer.Status = ClubOwnershipTransferStatuses.Approved; transfer.AdminNote = request.AdminNote;
            transfer.ReviewedByUserId = actor; transfer.ReviewedAtUtc = now; transfer.ExecutedAtUtc = now; transfer.Club.ConcurrencyToken = Guid.NewGuid();
            var viceAppointments = await db.Set<ClubViceAppointment>().Where(x => x.ClubId == transfer.ClubId && x.UserId == member.UserId && x.Status == "APPROVED").ToArrayAsync(ct);
            foreach (var vice in viceAppointments)
            {
                vice.Status = "SUPERSEDED"; vice.Version = Guid.NewGuid();
                var payload = new { appointmentId = vice.Id, vice.MembershipId, vice.UserId, vice.Status, vice.From, vice.Until, ownershipTransferId = transfer.Id };
                db.Add(new ClubGovernanceLeadershipEvent { AppointmentId = vice.Id, ActorId = actor, Action = "LEADERSHIP_SUPERSEDED", Reason = "Approved ownership transfer", At = now, SnapshotJson = System.Text.Json.JsonSerializer.Serialize(payload) });
                ClubGovernanceAudit.Record(db, transfer.ClubId, "LEADERSHIP", "LEADERSHIP_SUPERSEDED", "Approved ownership transfer", payload, http, vice.MembershipId, vice.UserId);
            }
            ClubGovernanceAudit.Record(db, transfer.ClubId, "LEADERSHIP", "OWNER_TRANSFER_APPROVED", request.AdminNote?.Trim() ?? "", new
            { transferId = transfer.Id, before = new { userId = current.ManagerUserId, displayName = current.ManagerName }, after = new { userId = member.UserId, displayName = account.DisplayName } }, http, member.Id, member.UserId);
            db.AddOutboxMessage(new ClubAccessInvalidatedEvent(Guid.NewGuid(), now, transfer.ClubId, [transfer.CurrentOwnerUserId, transfer.NewOwnerUserId]), EventRoutingKeys.ClubAccessInvalidated);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Club {ClubId} ownership transferred from {OldOwner} to {NewOwner} by admin {AdminId}", transfer.ClubId, transfer.CurrentOwnerUserId, transfer.NewOwnerUserId, actor);
            return Results.Ok(ClubMappers.ToTransferRequestResponse(transfer, transfer.Club.Name));
        }, ct, required: false);
    }

    private static async Task<IResult> RejectTransferRequest(
        int requestId,
        ApproveTransferRequest request,
        ClubDbContext db,
        HttpContext http,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (!http.User.IsStudentAffairsAdministrator())
            return Results.Forbid();

        var reviewerUserId = http.User.GetUserId();

        var transferRequest = await db.ClubOwnershipTransfers
            .Include(x => x.Club)
            .FirstOrDefaultAsync(x => x.Id == requestId, ct);

        if (transferRequest is null)
            return Results.NotFound(new { message = "Transfer request not found" });

        if (transferRequest.Status != ClubOwnershipTransferStatuses.Pending)
            return Results.BadRequest(new { message = $"Request is already {transferRequest.Status}" });

        transferRequest.Status = ClubOwnershipTransferStatuses.Rejected;
        transferRequest.AdminNote = request.AdminNote;
        transferRequest.ReviewedByUserId = reviewerUserId;
        transferRequest.ReviewedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Club {ClubId} transfer request rejected by admin {AdminId}",
            transferRequest.ClubId, reviewerUserId);

        return Results.Ok(ClubMappers.ToTransferRequestResponse(transferRequest, transferRequest.Club.Name));
    }
}
