using System.Security.Claims;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Messaging;
using ClubService.Contracts;
using ClubService.Data;
using ClubService.Mappers;
using ClubService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClubService.Endpoints;

public static class ManagerEndpoints
{
    public static void MapManagerEndpoints(this WebApplication app)
    {
        var clubs = app.MapGroup("/api/clubs")
            .WithTags("Club Managers")
            .RequireAuthorization(AuthPolicies.BusinessAccess);

        // POST /api/clubs/{id}/managers - Assign manager to club (Admin only)
        clubs.MapPost("/{id:int}/managers", AssignManager)
            .WithName("AssignManager")
            .WithDescription("Assign a manager to a club (Admin only)")
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);
    }

    private static async Task<IResult> AssignManager(
        int id,
        AssignManagerRequest request,
        ClubDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var club = await db.Clubs
            .Include(x => x.ManagerAssignments)
            .Include(x => x.Memberships)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (club is null)
        {
            return Results.NotFound();
        }

        var managesAnotherClub = await db.ClubManagerAssignments
            .AsNoTracking()
            .AnyAsync(x => x.ManagerUserId == request.ManagerUserId && x.ClubId != id && x.IsActive, cancellationToken);

        if (managesAnotherClub)
        {
            return Results.Conflict(new { message = "Each club owner can manage one club only." });
        }

        var affectedUserIds = club.ManagerAssignments
            .Where(x => x.IsActive)
            .Select(x => x.ManagerUserId)
            .Append(request.ManagerUserId)
            .Distinct()
            .ToArray();

        club.ConcurrencyToken = Guid.NewGuid();

        foreach (var assignment in club.ManagerAssignments.Where(x => x.IsActive))
        {
            assignment.IsActive = false;
            assignment.EndedAtUtc = DateTimeOffset.UtcNow;
        }

        db.ClubManagerAssignments.Add(new ClubManagerAssignment
        {
            ClubId = id,
            ManagerUserId = request.ManagerUserId,
            ManagerName = request.ManagerName.Trim(),
            IsActive = true
        });

        var existingMembership = club.Memberships.FirstOrDefault(x => x.UserId == request.ManagerUserId);
        if (existingMembership is null)
        {
            club.Memberships.Add(new ClubMembership
            {
                ClubId = id,
                UserId = request.ManagerUserId,
                FullName = request.ManagerName.Trim(),
                Role = ClubMemberRoles.Member,
                Status = ClubMembershipStatuses.Approved,
                ReviewedAtUtc = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existingMembership.FullName = request.ManagerName.Trim();
            existingMembership.Status = ClubMembershipStatuses.Approved;
            existingMembership.ReviewedAtUtc = DateTimeOffset.UtcNow;
        }

        db.AddOutboxMessage(new ClubAccessInvalidatedEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            id,
            affectedUserIds), EventRoutingKeys.ClubAccessInvalidated);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { message = "Each club owner can manage one club only and each club can have only one active owner." });
        }

        var updated = await db.Clubs
            .AsNoTracking()
            .Include(x => x.ManagerAssignments)
            .Include(x => x.Memberships)
            .FirstAsync(x => x.Id == id, cancellationToken);

        return Results.Ok(ClubMappers.ToResponse(updated));
    }
}
