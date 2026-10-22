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
using ClubService.Services;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClubService.Endpoints;

public static class MembershipEndpoints
{
    public static void MapMembershipEndpoints(this WebApplication app)
    {
        var clubs = app.MapGroup("/api/clubs")
            .WithTags("Memberships")
            .RequireAuthorization(AuthPolicies.BusinessAccess);

        // POST /api/clubs/{id}/join - Join a club
        clubs.MapPost("/{id:int}/join", JoinClub)
            .WithName("JoinClub")
            .WithDescription("Request to join a club");

        // GET /api/clubs/{id}/memberships - Get club memberships
        clubs.MapGet("/{id:int}/memberships", GetClubMemberships)
            .WithName("GetClubMemberships")
            .WithDescription("Get all memberships for a club");

        // POST /api/clubs/memberships/{membershipId}/approve - Approve membership
        clubs.MapPost("/memberships/{membershipId:int}/approve", ApproveMembership)
            .WithName("ApproveMembership")
            .WithDescription("Approve a membership request");

        // POST /api/clubs/memberships/{membershipId}/reject - Reject membership
        clubs.MapPost("/memberships/{membershipId:int}/reject", RejectMembership)
            .WithName("RejectMembership")
            .WithDescription("Reject a membership request");

        // POST /api/clubs/{id}/treasurers - Assign treasurer
        clubs.MapPost("/{id:int}/treasurers", AssignTreasurer)
            .WithName("AssignTreasurer")
            .WithDescription("Assign a member as club treasurer");

        // POST /api/clubs/memberships/{membershipId}/member - Remove treasurer role
        clubs.MapPost("/memberships/{membershipId:int}/member", RemoveTreasurerRole)
            .WithName("RemoveTreasurerRole")
            .WithDescription("Remove treasurer role from a member");

        // DELETE /api/clubs/memberships/{membershipId} - Remove membership
        clubs.MapDelete("/memberships/{membershipId:int}", RemoveMembership)
            .WithName("RemoveMembership")
            .WithDescription("Remove a member from club");
    }

    private static async Task<IResult> JoinClub(
        int id,
        JoinClubRequest request,
        ClaimsPrincipal user,
        HttpContext http,
        ClubDbContext db,
        CancellationToken cancellationToken)
    {
        var club = await db.Clubs
            .Include(x => x.ManagerAssignments)
            .Include(x => x.Memberships)
            .FirstOrDefaultAsync(x => x.Id == id && x.IsActive, cancellationToken);

        if (club is null)
        {
            return Results.NotFound();
        }

        var userId = user.GetUserId();
        var profiles = http.RequestServices.GetService<CurrentUserProfileClient>();
        if (profiles is null) return ApiOperations.Error(503, "PROFILE_SERVICE_UNAVAILABLE", "The canonical account client is unconfigured.");
        InternalUserProfile? profile;
        try { profile = (await profiles.GetAsync([userId], cancellationToken)).GetValueOrDefault(userId); }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        { return ApiOperations.Error(503, "PROFILE_SERVICE_UNAVAILABLE", "The canonical account service is unavailable."); }
        if (profile is null || !profile.IsActive || profile.IsLocked) return Results.Forbid();
        if (club.CampusCode != CampusCodes.Global && club.CampusCode != profile.CampusCode) return Results.Forbid();
        request = request with
        {
            FullName = profile.DisplayName,
            Email = profile.Email,
            PhoneNumber = profile.PhoneNumber ?? string.Empty,
            Gender = request.Gender ?? string.Empty
        };
        if (club.ManagerAssignments.Any(x => x.ManagerUserId == userId && x.IsActive))
        {
            return Results.Conflict(new { message = "Club owner is already attached to this club." });
        }

        var validationError = ValidationExtensions.ValidateJoinRequest(request, club.Category);
        if (validationError is not null)
        {
            return Results.BadRequest(new { message = validationError });
        }

        var currentInvitation = await db.ClubMemberships.AsNoTracking().FirstOrDefaultAsync(x => x.ClubId == id
            && x.UserId == userId && x.Source == ClubMembershipSources.Invitation, cancellationToken);
        if (currentInvitation is not null)
        {
            var eligibility = await ClubRosterEligibility.Invitation(currentInvitation, club, http, cancellationToken);
            if (eligibility.Error is not null) return eligibility.Error;
        }
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            var existing = await db.ClubMemberships
                .FirstOrDefaultAsync(x => x.ClubId == id && x.UserId == userId, cancellationToken);

            if (existing is not null)
            {
                if (existing.Status == ClubMembershipStatuses.Pending &&
                    (!existing.AcceptedClubRules || !existing.CommittedToParticipate))
                {
                    var eligibility = await ClubRosterEligibility.Invitation(existing, club, http, cancellationToken);
                    if (eligibility.Error is not null) return eligibility.Error;
                    ClubRosterEligibility.Apply(existing, eligibility.Roster);
                    ClubMappers.ApplyJoinRequest(existing, request);
                    existing.ConcurrencyToken = Guid.NewGuid();
                    existing.ConsentedAtUtc = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                    existing.Club = club;
                    return Results.Ok(SelfMembershipApplicationEndpoints.SelfResponse(existing, userId));
                }

                if (existing.Status == ClubMembershipStatuses.Rejected)
                {
                    // Archive the rejected attempt before releasing the unique current-membership slot.
                    existing.IsDeleted = true;
                    existing.TreasurerSlot = null;
                    existing.DeletedAtUtc = DateTimeOffset.UtcNow;
                    existing.DeletedByUserId = userId;
                    await db.SaveChangesAsync(cancellationToken);
                }
                else return Results.Conflict(new { message = $"Membership request already exists with status {existing.Status}." });
            }
            var previousId = existing?.Id ?? await db.ClubMemberships.IgnoreQueryFilters()
                .Where(x => x.ClubId == id && x.UserId == userId).OrderByDescending(x => x.RequestedAtUtc).ThenByDescending(x => x.Id)
                .Select(x => (int?)x.Id).FirstOrDefaultAsync(cancellationToken);
            var membership = new ClubMembership
            {
                ClubId = id,
                UserId = userId,
                Role = ClubMemberRoles.Member,
                Status = ClubMembershipStatuses.Pending,
                Source = ClubMembershipSources.Application,
                ConsentedAtUtc = DateTimeOffset.UtcNow,
                PreviousApplicationId = previousId
            };

            ClubMappers.ApplyJoinRequest(membership, request);
            db.ClubMemberships.Add(membership);
            await db.SaveChangesAsync(cancellationToken);

            membership.Club = club;
            return Results.Created($"/api/clubs/memberships/{membership.Id}", SelfMembershipApplicationEndpoints.SelfResponse(membership, userId));
        }, cancellationToken, required: false);
    }

    private static async Task<IResult> GetClubMemberships(
        int id,
        ClaimsPrincipal user,
        ClubDbContext db,
        CancellationToken cancellationToken)
    {
        if (!user.IsSuperAdmin() && !await db.UserOwnsClubAsync(id, user.GetUserId(), cancellationToken))
        {
            return Results.Forbid();
        }

        var memberships = await db.ClubMemberships
            .AsNoTracking()
            .Include(x => x.Club)
            .Where(x => x.ClubId == id)
            .OrderBy(x => x.Status)
            .ThenBy(x => x.FullName)
            .ToListAsync(cancellationToken);

        return Results.Ok(memberships.Select(ClubMappers.ToMembershipResponse));
    }

    private static async Task<IResult> ApproveMembership(
        int membershipId,
        ReviewClubMembershipRequest request,
        ClaimsPrincipal user,
        ClubDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request.Note?.Trim().Length > 1000)
        {
            return Results.BadRequest(new { message = "The review note cannot exceed 1,000 characters." });
        }

        var membership = await db.ClubMemberships
            .Include(x => x.Club)
            .FirstOrDefaultAsync(x => x.Id == membershipId, cancellationToken);

        if (membership is null)
        {
            return Results.NotFound();
        }

        if (!user.IsSuperAdmin() && !await db.UserOwnsClubAsync(membership.ClubId, user.GetUserId(), cancellationToken))
        {
            return Results.Forbid();
        }

        var invitationEligibility = await ClubRosterEligibility.Invitation(membership, membership.Club, httpContext, cancellationToken);
        if (invitationEligibility.Error is not null) return invitationEligibility.Error;
        return await ApiOperations.ExecuteAsync(httpContext, db, request, async () =>
        {
            if (!user.IsSuperAdmin() && !await db.UserOwnsClubAsync(membership.ClubId, user.GetUserId(), cancellationToken)) return Results.Forbid();
            if (request.ExpectedVersion.HasValue && request.ExpectedVersion != membership.ConcurrencyToken)
                return ApiOperations.Error(409, "STALE_VERSION", "Reload the membership request.");
            if (membership.Status != ClubMembershipStatuses.Pending)
            {
                return Results.Conflict(new { message = "Only pending membership requests can be approved." });
            }

            if (!membership.AcceptedClubRules || !membership.CommittedToParticipate)
            {
                return Results.Conflict(new { message = "The invited member must personally accept the club rules before approval." });
            }
            var currentEligibility = await ClubRosterEligibility.Invitation(membership, membership.Club, httpContext, cancellationToken);
            if (currentEligibility.Error is not null) return currentEligibility.Error;
            ClubRosterEligibility.Apply(membership, currentEligibility.Roster);

            membership.Status = ClubMembershipStatuses.Approved;
            membership.Role = ClubMemberRoles.Member;
            membership.ReviewNote = request.Note?.Trim();
            membership.ReviewedAtUtc = DateTimeOffset.UtcNow;
            membership.ReviewedByUserId = user.GetUserId();

            db.AddOutboxMessage(new ClubAccessInvalidatedEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                membership.ClubId,
                [membership.UserId]), EventRoutingKeys.ClubAccessInvalidated);

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(ClubMappers.ToMembershipResponse(membership));
        }, cancellationToken, required: false);
    }

    private static async Task<IResult> RejectMembership(
        int membershipId,
        ReviewClubMembershipRequest request,
        ClaimsPrincipal user,
        ClubDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request.Note?.Trim().Length > 1000)
        {
            return Results.BadRequest(new { message = "The review note cannot exceed 1,000 characters." });
        }

        var membership = await db.ClubMemberships
            .Include(x => x.Club)
            .FirstOrDefaultAsync(x => x.Id == membershipId, cancellationToken);

        if (membership is null)
        {
            return Results.NotFound();
        }

        if (!user.IsSuperAdmin() && !await db.UserOwnsClubAsync(membership.ClubId, user.GetUserId(), cancellationToken))
        {
            return Results.Forbid();
        }

        return await ApiOperations.ExecuteAsync(httpContext, db, request, async () =>
        {
            if (!user.IsSuperAdmin() && !await db.UserOwnsClubAsync(membership.ClubId, user.GetUserId(), cancellationToken)) return Results.Forbid();
            if (request.ExpectedVersion.HasValue && request.ExpectedVersion != membership.ConcurrencyToken)
                return ApiOperations.Error(409, "STALE_VERSION", "Reload the membership request.");
            if (membership.Status != ClubMembershipStatuses.Pending)
            {
                return Results.Conflict(new { message = "Only pending membership requests can be rejected." });
            }

            membership.Status = ClubMembershipStatuses.Rejected;
            membership.Role = ClubMemberRoles.Member;
            membership.TreasurerSlot = null;
            membership.ReviewNote = request.Note?.Trim();
            membership.ReviewedAtUtc = DateTimeOffset.UtcNow;
            membership.ReviewedByUserId = user.GetUserId();

            db.AddOutboxMessage(new ClubAccessInvalidatedEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                membership.ClubId,
                [membership.UserId]), EventRoutingKeys.ClubAccessInvalidated);

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(ClubMappers.ToMembershipResponse(membership));
        }, cancellationToken, required: false);
    }


    private static async Task<IResult> AssignTreasurer(
        int id,
        AssignTreasurerRequest request,
        ClaimsPrincipal user,
        ClubDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!user.IsSuperAdmin() && !await db.UserOwnsClubAsync(id, user.GetUserId(), cancellationToken))
        {
            return Results.Forbid();
        }

        var club = await db.Clubs
            .Include(x => x.Memberships)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (club is null)
        {
            return Results.NotFound();
        }

        var membership = club.Memberships.FirstOrDefault(x => x.UserId == request.MemberUserId);
        if (membership is null || membership.Status != ClubMembershipStatuses.Approved)
        {
            return Results.BadRequest(new { message = "Treasurer must be an approved member of this club." });
        }

        var isActiveManager = await db.ClubManagerAssignments.AsNoTracking()
            .AnyAsync(x => x.ClubId == id && x.ManagerUserId == membership.UserId && x.IsActive, cancellationToken);

        var activeTreasurers = club.Memberships
            .Where(x => x.Role == ClubMemberRoles.Treasurer && x.Status == ClubMembershipStatuses.Approved && !x.IsDeleted)
            .ToList();

        var assignmentError = ClubMemberRoleRules.ValidateTreasurerAssignment(
            isActiveManager,
            membership.Role == ClubMemberRoles.Treasurer,
            activeTreasurers.Count);

        if (assignmentError is not null)
        {
            return Results.Conflict(new { message = assignmentError });
        }

        // Determine free slot (1 or 2)
        if (membership.Role != ClubMemberRoles.Treasurer || !membership.TreasurerSlot.HasValue)
        {
            var occupiedSlots = activeTreasurers
                .Where(x => x.Id != membership.Id && x.TreasurerSlot.HasValue)
                .Select(x => x.TreasurerSlot!.Value)
                .ToHashSet();

            if (!occupiedSlots.Contains(1))
            {
                membership.TreasurerSlot = 1;
            }
            else if (!occupiedSlots.Contains(2))
            {
                membership.TreasurerSlot = 2;
            }
            else
            {
                return Results.Conflict(new { message = "A club can have at most two treasurers." });
            }
        }

        ClubMemberRoleRules.ApplyTreasurerRole(membership, request.MemberName);
        club.ConcurrencyToken = Guid.NewGuid();

        db.AddOutboxMessage(new ClubAccessInvalidatedEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            id,
            [request.MemberUserId]), EventRoutingKeys.ClubAccessInvalidated);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { message = "A club can have at most two treasurers." });
        }

        membership.Club = club;
        return Results.Ok(ClubMappers.ToMembershipResponse(membership));
    }

    private static async Task<IResult> RemoveTreasurerRole(
        int membershipId,
        ClaimsPrincipal user,
        ClubDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var membership = await db.ClubMemberships
            .Include(x => x.Club)
            .FirstOrDefaultAsync(x => x.Id == membershipId, cancellationToken);

        if (membership is null)
        {
            return Results.NotFound();
        }

        if (!user.IsSuperAdmin() && !await db.UserOwnsClubAsync(membership.ClubId, user.GetUserId(), cancellationToken))
        {
            return Results.Forbid();
        }

        ClubMemberRoleRules.ApplyMemberRole(membership);
        membership.Club.ConcurrencyToken = Guid.NewGuid();
        db.AddOutboxMessage(new ClubAccessInvalidatedEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            membership.ClubId,
            [membership.UserId]), EventRoutingKeys.ClubAccessInvalidated);

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(ClubMappers.ToMembershipResponse(membership));
    }

    private static async Task<IResult> RemoveMembership(
        int membershipId,
        Guid? expectedVersion,
        ClaimsPrincipal user,
        ClubDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var membership = await db.ClubMemberships.IgnoreQueryFilters()
            .Include(x => x.Club)
            .FirstOrDefaultAsync(x => x.Id == membershipId, cancellationToken);

        if (membership is null)
        {
            return Results.NotFound();
        }

        if (!user.IsSuperAdmin() && !await db.UserOwnsClubAsync(membership.ClubId, user.GetUserId(), cancellationToken))
        {
            return Results.Forbid();
        }

        return await MemberManagementEndpoints.RemoveMember(membership.ClubId, membershipId, expectedVersion, db, user,
            httpContext, httpContext.RequestServices.GetRequiredService<ILogger<Program>>(), cancellationToken);
    }
}
