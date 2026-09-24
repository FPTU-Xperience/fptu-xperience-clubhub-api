using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Messaging;
using ClubService.Contracts;
using ClubService.Data;
using ClubService.Extensions;
using ClubService.Infrastructure;
using ClubService.Mappers;
using ClubService.Models;
using ClubService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClubService.Endpoints;

public static class MemberManagementEndpoints
{
    public static void MapMemberManagementEndpoints(this WebApplication app)
    {
        var clubs = app.MapGroup("/api/clubs")
            .WithTags("Member Management")
            .RequireAuthorization(AuthPolicies.BusinessAccess);

        // GET /api/clubs/{clubId}/members - List members with pagination
        clubs.MapGet("/{clubId:int}/members", ListClubMembers)
            .WithName("ListClubMembers")
            .WithDescription("List all members of a club with pagination and filtering");

        clubs.MapPost("/{clubId:int}/members", InviteClubMember)
            .WithName("InviteClubMember")
            .WithDescription("Create a pending invitation; the member must accept the club rules through /join before approval");

        // GET /api/clubs/{clubId}/members/{memberId} - Get member details
        clubs.MapGet("/{clubId:int}/members/{memberId:int}", GetMemberDetails)
            .WithName("GetMemberDetails")
            .WithDescription("Get detailed information about a specific member");

        clubs.MapPut("/{clubId:int}/members/{memberId:int}", UpdateMemberProfile)
            .WithName("UpdateClubMemberProfile")
            .WithDescription("Update member contact/profile fields; role, identity, consent and membership status cannot be changed here");

        // DELETE /api/clubs/{clubId}/members/{memberId} - Remove member from club
        clubs.MapDelete("/{clubId:int}/members/{memberId:int}", RemoveMember)
            .WithName("RemoveMember")
            .WithDescription("Remove a member from the club");

        // GET /api/clubs/{clubId}/member-roster - Get member roster
        clubs.MapGet("/{clubId:int}/member-roster", GetMemberRoster)
            .WithName("GetMemberRoster")
            .WithDescription("Get member roster for a club");

        // POST /api/clubs/{clubId}/member-roster/resolve - Resolve roster members
        clubs.MapPost("/{clubId:int}/member-roster/resolve", ResolveRosterMembers)
            .WithName("ResolveRosterMembers")
            .WithDescription("Resolve specific members from the roster");
    }

    private static async Task<IResult> InviteClubMember(
        int clubId,
        JsonElement request,
        ClaimsPrincipal user,
        ClubDbContext db,
        CancellationToken cancellationToken)
    {
        if (request.ValueKind != JsonValueKind.Object ||
            !request.TryGetProperty("userId", out var userIdValue) ||
            !request.TryGetProperty("fullName", out var fullNameValue) ||
            fullNameValue.ValueKind != JsonValueKind.String ||
            !request.TryGetProperty("role", out var roleValue) ||
            roleValue.ValueKind != JsonValueKind.String)
        {
            return Results.BadRequest(new { message = "userId, fullName and role are required." });
        }

        var parsedNumber = 0;
        var validUserId = (userIdValue.ValueKind == JsonValueKind.Number &&
                userIdValue.TryGetInt32(out parsedNumber) && parsedNumber > 0) ||
            (userIdValue.ValueKind == JsonValueKind.String &&
                int.TryParse(userIdValue.GetString(), out parsedNumber) && parsedNumber > 0);
        var fullName = fullNameValue.GetString()?.Trim();
        if (!validUserId || string.IsNullOrWhiteSpace(fullName) || fullName.Length > 200 ||
            roleValue.GetString() != AuthRoles.ClubMember)
        {
            return Results.BadRequest(new { message = "Invitation requires a positive userId, a valid name and CLUB_MEMBER role." });
        }

        var club = await db.Clubs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == clubId && x.IsActive, cancellationToken);
        if (club is null) return Results.NotFound();
        if (!await db.CanManageMembershipsAsync(clubId, user, cancellationToken))
            return Results.Forbid();
        if (await db.ClubManagerAssignments.AnyAsync(x =>
                x.ClubId == clubId && x.ManagerUserId == parsedNumber && x.IsActive, cancellationToken))
            return Results.Conflict(new { message = "The club manager is already attached to this club." });
        if (await db.ClubMemberships.IgnoreQueryFilters().AnyAsync(x =>
                x.ClubId == clubId && x.UserId == parsedNumber, cancellationToken))
            return Results.Conflict(new { message = "Membership or invitation already exists for this user." });

        var membership = new ClubMembership
        {
            ClubId = clubId,
            UserId = parsedNumber,
            FullName = fullName,
            Role = ClubMemberRoles.Member,
            Status = ClubMembershipStatuses.Pending,
            AcceptedClubRules = false,
            CommittedToParticipate = false
        };
        db.ClubMemberships.Add(membership);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { message = "Membership or invitation already exists for this user." });
        }

        return Results.Created($"/api/clubs/memberships/{membership.Id}", new
        {
            membership.Id,
            membership.ClubId,
            membership.UserId,
            membership.FullName,
            role = AuthRoles.ClubMember,
            membership.Status,
            membership.RequestedAtUtc
        });
    }

    private static async Task<IResult> ListClubMembers(
        int clubId,
        string? search,
        string? status,
        string? role,
        string? sortBy,
        string? sortDirection,
        int? page,
        int? pageSize,
        ClubDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!await db.CanManageMembershipsAsync(clubId, user, cancellationToken))
            return Results.Forbid();

        if (!await db.Clubs.AnyAsync(x => x.Id == clubId, cancellationToken))
            return Results.NotFound(new { message = "Club not found." });

        var actualPage = Math.Max(1, page ?? 1);
        var actualPageSize = Math.Clamp(pageSize ?? 10, 1, 100);

        var query = ClubMemberQuery.ApplyFilters(
            db.ClubMemberships.AsNoTracking().Where(x => x.ClubId == clubId),
            search, status, role);
        query = ClubMemberQuery.ApplySort(query, sortBy, sortDirection);

        var totalItems = await query.CountAsync(cancellationToken);
        var memberships = await query.Skip((actualPage - 1) * actualPageSize).Take(actualPageSize).ToListAsync(cancellationToken);

        var memberUserIds = memberships.Select(x => x.UserId).ToArray();
        var activeManagerUserIds = await db.ClubManagerAssignments.AsNoTracking()
            .Where(x => x.ClubId == clubId && x.IsActive && memberUserIds.Contains(x.ManagerUserId))
            .Select(x => x.ManagerUserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var statisticsClient = httpContext.RequestServices.GetService<ActivityStatisticsClient>();
        IReadOnlyDictionary<int, ActivityMemberStatistics> statistics;
        try
        {
            statistics = statisticsClient is not null
                ? await statisticsClient.GetBatchAsync(
                    clubId,
                    memberships.Select(x => new ActivityMemberInput(x.UserId, x.ReviewedAtUtc ?? x.RequestedAtUtc)).ToArray(),
                    httpContext.GetBearerToken(),
                    cancellationToken)
                : new Dictionary<int, ActivityMemberStatistics>();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            var logger = httpContext.RequestServices.GetService<ILogger<ActivityStatisticsClient>>();
            logger?.LogWarning(exception, "Activity statistics are temporarily unavailable for club {ClubId}. Falling back to default statistics.", clubId);
            statistics = new Dictionary<int, ActivityMemberStatistics>();
        }

        var items = memberships.Select(x =>
        {
            var item = statistics.GetValueOrDefault(x.UserId, new ActivityMemberStatistics(x.UserId, 0, 0, 0));
            return new ClubMemberListItemResponse(
                x.Id, x.ClubId, x.UserId, x.FullName, x.Email, x.PhoneNumber,
                ClubMemberRoleRules.ResolveDisplayRole(x.Role, activeManagerUserIds.Contains(x.UserId)),
                x.Status,
                x.ReviewedAtUtc ?? x.RequestedAtUtc,
                new MemberParticipationResponse(item.EligibleActivities, item.AttendedActivities, item.ParticipationRate));
        }).ToArray();

        return Results.Ok(new PagedClubMembersResponse(
            items, actualPage, actualPageSize, totalItems,
            totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)actualPageSize)));
    }

    private static async Task<IResult> GetMemberDetails(
        int clubId,
        int memberId,
        int? historyPage,
        int? historyPageSize,
        ClubDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!await db.CanManageMembershipsAsync(clubId, user, cancellationToken))
            return Results.Forbid();

        var membership = await db.ClubMemberships.AsNoTracking()
            .Include(x => x.Club)
            .FirstOrDefaultAsync(x => x.Id == memberId && x.ClubId == clubId, cancellationToken);

        if (membership is null)
            return Results.NotFound(new { message = "Member not found in this club." });

        var actualHistoryPage = Math.Max(1, historyPage ?? 1);
        var actualHistoryPageSize = Math.Clamp(historyPageSize ?? 10, 1, 100);

        var statisticsClient = httpContext.RequestServices.GetService<ActivityStatisticsClient>();
        ActivityStatisticsDetail detail;
        try
        {
            detail = statisticsClient is not null
                ? await statisticsClient.GetDetailAsync(
                    clubId,
                    new ActivityStatisticsDetailQuery(
                        membership.UserId,
                        membership.ReviewedAtUtc ?? membership.RequestedAtUtc,
                        actualHistoryPage,
                        actualHistoryPageSize),
                    httpContext.GetBearerToken(),
                    cancellationToken)
                : new ActivityStatisticsDetail(new ActivityMemberStatistics(membership.UserId, 0, 0, 0), Array.Empty<ActivityHistoryItem>(), actualHistoryPage, actualHistoryPageSize, 0, 0);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            var logger = httpContext.RequestServices.GetService<ILogger<ActivityStatisticsClient>>();
            logger?.LogWarning(exception, "Activity statistics detail is temporarily unavailable for member {MemberId} in club {ClubId}. Falling back to default statistics.", memberId, clubId);
            detail = new ActivityStatisticsDetail(new ActivityMemberStatistics(membership.UserId, 0, 0, 0), Array.Empty<ActivityHistoryItem>(), actualHistoryPage, actualHistoryPageSize, 0, 0);
        }

        return Results.Ok(new ClubMemberDetailResponse(
            ClubMappers.ToMembershipResponse(membership),
            membership.ReviewedAtUtc ?? membership.RequestedAtUtc,
            new MemberParticipationResponse(
                detail.Statistics.EligibleActivities,
                detail.Statistics.AttendedActivities,
                detail.Statistics.ParticipationRate),
            detail.Items.Select(x => new MemberActivityHistoryItemResponse(
                x.ActivityId, x.Title, x.StartTimeUtc, x.ActivityStatus, x.AttendanceStatus)).ToArray(),
            actualHistoryPage,
            actualHistoryPageSize,
            detail.TotalItems,
            detail.TotalPages));
    }

    private static async Task<IResult> UpdateMemberProfile(
        int clubId,
        int memberId,
        UpdateClubMemberProfileRequest request,
        ClubDbContext db,
        ClaimsPrincipal user,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (!await db.CanManageMembershipsAsync(clubId, user, cancellationToken))
            return Results.Forbid();

        var membership = await db.ClubMemberships.Include(x => x.Club)
            .FirstOrDefaultAsync(x => x.Id == memberId && x.ClubId == clubId, cancellationToken);
        if (membership is null)
            return Results.NotFound(new { message = "Member not found in this club." });

        var fullName = request.FullName?.Trim();
        var email = request.Email?.Trim();
        var phone = request.PhoneNumber?.Trim();
        var address = request.Address?.Trim();
        var gender = request.Gender?.Trim();
        if (fullName is null && email is null && phone is null && address is null && gender is null && request.DateOfBirth is null)
            return Results.BadRequest(new { message = "Provide at least one profile field to update." });

        if (fullName is { Length: 0 or > 200 }
            || email is { Length: 0 or > 255 }
            || (email is not null && !new EmailAddressAttribute().IsValid(email))
            || phone is { Length: 0 or > 40 }
            || address is { Length: > 500 }
            || gender is { Length: > 20 }
            || (request.DateOfBirth.HasValue && (request.DateOfBirth.Value == DateOnly.MinValue
                || request.DateOfBirth.Value > DateOnly.FromDateTime(DateTime.UtcNow))))
        {
            return Results.BadRequest(new { message = "Invalid profile field: check name, email, phone, date of birth and field lengths." });
        }

        if (fullName is not null) membership.FullName = fullName;
        if (email is not null) membership.Email = email;
        if (phone is not null) membership.PhoneNumber = phone;
        if (address is not null) membership.Address = address;
        if (gender is not null) membership.Gender = gender;
        if (request.DateOfBirth.HasValue) membership.DateOfBirth = request.DateOfBirth.Value;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Membership {MembershipId} profile in club {ClubId} was updated by user {UserId}", memberId, clubId, user.GetUserId());
        return Results.Ok(ClubMappers.ToMembershipResponse(membership));
    }

    private static async Task<IResult> RemoveMember(
        int clubId,
        int memberId,
        ClubDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (!await db.CanManageMembershipsAsync(clubId, user, cancellationToken))
            return Results.Forbid();

        var membership = await db.ClubMemberships
            .FirstOrDefaultAsync(x => x.Id == memberId && x.ClubId == clubId, cancellationToken);

        if (membership is null)
            return Results.NotFound(new { message = "Member not found in this club." });

        var isActiveManager = await db.ClubManagerAssignments.AsNoTracking()
            .AnyAsync(x => x.ClubId == clubId && x.ManagerUserId == membership.UserId && x.IsActive, cancellationToken);

        var removalError = ClubMemberRoleRules.ValidateRemoval(isActiveManager);
        if (removalError is not null)
            return Results.Conflict(new { message = removalError });

        membership.IsDeleted = true;
        membership.DeletedAtUtc = DateTimeOffset.UtcNow;
        membership.DeletedByUserId = user.GetUserId();
        membership.Status = ClubMembershipStatuses.Inactive;
        membership.TreasurerSlot = null;

        db.AddOutboxMessage(new ClubAccessInvalidatedEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            clubId,
            [membership.UserId]), EventRoutingKeys.ClubAccessInvalidated);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Membership {MembershipId} was soft-deleted from club {ClubId} by user {UserId}",
            memberId, clubId, user.GetUserId());

        return Results.NoContent();
    }

    private static async Task<IResult> GetMemberRoster(
        int clubId,
        DateTimeOffset joinedOnOrBefore,
        string? search,
        int page,
        int pageSize,
        ClubDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await db.CanManageMembershipsAsync(clubId, user, cancellationToken))
            return Results.Forbid();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.ClubMemberships.AsNoTracking()
            .Where(x => x.ClubId == clubId
                && x.Status == ClubMembershipStatuses.Approved
                && (x.ReviewedAtUtc ?? x.RequestedAtUtc) <= joinedOnOrBefore);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.FullName.Contains(term) || x.Email.Contains(term) || x.PhoneNumber.Contains(term));
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(x => x.FullName)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ClubMemberRosterItemResponse(
                x.Id, x.UserId, x.FullName, x.Email, x.PhoneNumber,
                x.Role, x.Status, x.ReviewedAtUtc ?? x.RequestedAtUtc))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PagedClubMemberRosterResponse(
            rows, page, pageSize, totalItems,
            totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize)));
    }

    private static async Task<IResult> ResolveRosterMembers(
        int clubId,
        ResolveClubMemberRosterRequest request,
        ClubDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await db.CanManageMembershipsAsync(clubId, user, cancellationToken))
            return Results.Forbid();

        var memberIds = request.MemberIds?.Where(id => id > 0).Distinct().ToArray() ?? [];
        var userIds = request.UserIds?.Where(id => id > 0).Distinct().ToArray() ?? [];

        if (memberIds.Length == 0 && userIds.Length == 0)
        {
            return Results.BadRequest(new { message = "Provide between 1 and 500 unique member IDs or user IDs." });
        }

        if (memberIds.Length > 500 || userIds.Length > 500)
        {
            return Results.BadRequest(new { message = "Cannot query more than 500 IDs at once." });
        }

        var cutoff = request.JoinedOnOrBefore ?? DateTimeOffset.MaxValue;
        var query = db.ClubMemberships.AsNoTracking()
            .Where(x => x.ClubId == clubId
                && x.Status == ClubMembershipStatuses.Approved
                && (x.ReviewedAtUtc ?? x.RequestedAtUtc) <= cutoff);

        // SEC-F11: Filter exclusively by active approved club memberships
        if (memberIds.Length > 0 && userIds.Length > 0)
        {
            query = query.Where(x => memberIds.Contains(x.Id) || userIds.Contains(x.UserId));
        }
        else if (memberIds.Length > 0)
        {
            query = query.Where(x => memberIds.Contains(x.Id));
        }
        else
        {
            query = query.Where(x => userIds.Contains(x.UserId));
        }

        var rows = await query
            .OrderBy(x => x.FullName)
            .Select(x => new ClubMemberRosterItemResponse(
                x.Id, x.UserId, x.FullName, x.Email, x.PhoneNumber,
                x.Role, x.Status, x.ReviewedAtUtc ?? x.RequestedAtUtc))
            .ToListAsync(cancellationToken);

        return Results.Ok(rows);
    }
}
