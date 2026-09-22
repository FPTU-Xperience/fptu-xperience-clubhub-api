using System.Security.Claims;
using ActivityService.Contracts;
using ActivityService.Infrastructure;
using ActivityService.Services;
using ClubReportHub.Shared.Auth;
using static ActivityService.Endpoints.ActivityEndpointHelpers;

namespace ActivityService.Endpoints;

public static class MemberStatisticsEndpoints
{
    public static IEndpointRouteBuilder MapMemberStatisticsEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/activities/clubs/{clubId:int}/member-statistics",
            async (
                int clubId,
                MemberStatisticsQuery request,
                MemberActivityStatisticsService statistics,
                ClubMemberRosterClient rosterClient,
                ClaimsPrincipal user,
                HttpContext httpContext,
                ClubAccessClient clubAccess,
                CancellationToken cancellationToken) =>
            {
                var canManage =
                    await CanManageClubOrReviewAllAsync(
                        clubId,
                        user,
                        clubAccess,
                        httpContext,
                        cancellationToken);

                if (!canManage)
                {
                    return Results.Forbid();
                }

                if (request.Members is null
                    || request.Members.Count > 500
                    || request.Members.Any(x => x.UserId <= 0))
                {
                    return Results.BadRequest(new
                    {
                        message =
                            "Provide between 0 and 500 valid members."
                    });
                }

                if (request.Members.Count == 0)
                {
                    return Results.Ok(Array.Empty<MemberStatisticsResponse>());
                }

                // SEC-F11: Resolve requested members from authoritative ClubService source of truth
                var requestedUserIds = request.Members.Select(x => x.UserId).Distinct().ToArray();
                IReadOnlyCollection<ClubMemberRosterItem> resolvedMembers;
                try
                {
                    resolvedMembers = await rosterClient.ResolveByUserIdsAsync(
                        clubId,
                        requestedUserIds,
                        httpContext.GetBearerToken(),
                        cancellationToken);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    return Results.Problem(
                        $"Unable to communicate with Club Service to verify members: {ex.Message}",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                // Reject/omit non-members and use server-authoritative JoinedAtUtc instead of client date
                var authoritativeInputs = resolvedMembers
                    .Select(m => new MemberStatisticsInput(m.UserId, m.JoinedAtUtc))
                    .ToArray();

                if (authoritativeInputs.Length == 0)
                {
                    return Results.Ok(Array.Empty<MemberStatisticsResponse>());
                }

                var result = await statistics.GetBatchAsync(
                    clubId,
                    authoritativeInputs,
                    DateTimeOffset.UtcNow,
                    cancellationToken);

                return Results.Ok(
                    result.Values.OrderBy(x => x.UserId));
            })
            .RequireAuthorization(AuthPolicies.BusinessAccess)
            .WithTags("Member Activity Statistics");

        app.MapPost(
            "/api/activities/clubs/{clubId:int}/member-statistics/detail",
            async (
                int clubId,
                MemberStatisticsDetailQuery request,
                MemberActivityStatisticsService statistics,
                ClubMemberRosterClient rosterClient,
                ClaimsPrincipal user,
                HttpContext httpContext,
                ClubAccessClient clubAccess,
                CancellationToken cancellationToken) =>
            {
                var canManage =
                    await CanManageClubOrReviewAllAsync(
                        clubId,
                        user,
                        clubAccess,
                        httpContext,
                        cancellationToken);

                if (!canManage)
                {
                    return Results.Forbid();
                }

                if (request.UserId <= 0
                    || request.Page < 1
                    || request.PageSize is < 1 or > 100)
                {
                    return Results.BadRequest(new
                    {
                        message =
                            "The member and pagination values are invalid."
                    });
                }

                // SEC-F11: Verify that request.UserId is an active member of clubId in ClubService
                IReadOnlyCollection<ClubMemberRosterItem> resolvedMembers;
                try
                {
                    resolvedMembers = await rosterClient.ResolveByUserIdsAsync(
                        clubId,
                        [request.UserId],
                        httpContext.GetBearerToken(),
                        cancellationToken);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    return Results.Problem(
                        $"Unable to communicate with Club Service to verify member: {ex.Message}",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                var resolvedMember = resolvedMembers.FirstOrDefault(x => x.UserId == request.UserId);
                if (resolvedMember is null)
                {
                    return Results.NotFound(new
                    {
                        message = "Member not found in this club or not an active member."
                    });
                }

                // Use authoritative JoinedAtUtc from ClubService instead of client-supplied date
                var authoritativeQuery = request with { JoinedAtUtc = resolvedMember.JoinedAtUtc };

                var result = await statistics.GetDetailAsync(
                    clubId,
                    authoritativeQuery,
                    DateTimeOffset.UtcNow,
                    cancellationToken);

                return Results.Ok(result);
            })
            .RequireAuthorization(AuthPolicies.BusinessAccess)
            .WithTags("Member Activity Statistics");

        return app;
    }
}
