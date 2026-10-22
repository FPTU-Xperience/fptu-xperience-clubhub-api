using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubService.Contracts;
using ClubService.Data;
using ClubService.Mappers;
using ClubService.Models;
using Microsoft.EntityFrameworkCore;

namespace ClubService.Endpoints;

public static class SelfMembershipApplicationEndpoints
{
    public static void MapSelfMembershipApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/clubs/me/membership-applications")
            .RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("My membership applications");
        api.MapGet("/", ListAsync);
        api.MapPost("/{applicationId:int}/withdraw", WithdrawAsync);
    }

    internal static ClubMembershipResponse SelfResponse(ClubMembership row, int actor) => ClubMappers.ToMembershipResponse(row) with
    {
        CanWithdraw = row.UserId == actor && !row.IsDeleted && row.Status == ClubMembershipStatuses.Pending && row.Source == ClubMembershipSources.Application,
        CanAcceptInvitation = row.UserId == actor && !row.IsDeleted && row.Status == ClubMembershipStatuses.Pending
            && row.Source == ClubMembershipSources.Invitation && (!row.AcceptedClubRules || !row.CommittedToParticipate)
            && row.Club.IsActive && row.Club.DeletedAtUtc is null
    };

    private static async Task<IResult> ListAsync(ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        var actor = http.User.GetUserId();
        var rows = await db.ClubMemberships.IgnoreQueryFilters().AsNoTracking().Include(x => x.Club)
            .Where(x => x.UserId == actor).OrderByDescending(x => x.RequestedAtUtc).ThenByDescending(x => x.Id).ToListAsync(ct);
        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(rows.Select(row =>
        {
            var self = SelfResponse(row, actor);
            return new
            {
                applicationId = row.Id,
                club = new { clubId = row.ClubId, clubCode = row.Club.Code, row.Club.Name, row.Club.LogoUrl },
                row.Reason,
                status = row.Status.ToUpperInvariant(),
                row.Source,
                self.CanWithdraw,
                self.CanAcceptInvitation,
                version = row.ConcurrencyToken,
                row.SemesterId,
                invitedAt = row.InvitedAtUtc,
                consentedAt = row.ConsentedAtUtc,
                withdrawnAt = row.WithdrawnAtUtc,
                row.PreviousApplicationId,
                row.RequestedAtUtc,
                row.ReviewedAtUtc
            };
        }).ToArray());
    }

    private static async Task<IResult> WithdrawAsync(int applicationId, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        var actor = http.User.GetUserId();
        var row = await db.ClubMemberships.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == applicationId && x.UserId == actor, ct);
        if (row is null) return Results.NotFound();
        if (http.Request.ContentLength is > 0 || await http.Request.Body.ReadAsync(new byte[1], ct) > 0)
            return ApiOperations.Error(400, "BODY_NOT_ALLOWED", "Withdrawal does not accept a body.");
        return await ApiOperations.ExecuteAsync(http, db, new { applicationId }, async () =>
        {
            await db.Entry(row).ReloadAsync(ct);
            if (row.Status == ClubMembershipStatuses.Withdrawn && row.Source == ClubMembershipSources.Application)
                return Results.Ok(WithdrawalResponse(row));
            if (row.IsDeleted || row.Status != ClubMembershipStatuses.Pending || row.Source != ClubMembershipSources.Application)
                return ApiOperations.Error(409, "APPLICATION_NOT_WITHDRAWABLE", "Only your own pending application can be withdrawn.");
            var previousVersion = row.ConcurrencyToken;
            row.Status = ClubMembershipStatuses.Withdrawn;
            row.WithdrawnAtUtc = http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
            row.WithdrawnByUserId = actor;
            row.IsDeleted = true;
            row.DeletedAtUtc = row.WithdrawnAtUtc;
            row.DeletedByUserId = actor;
            row.TreasurerSlot = null;
            await db.SaveChangesAsync(ct);
            db.MembershipActions.Add(new ClubMembershipAction
            {
                MembershipId = row.Id,
                ActorUserId = actor,
                Action = "WITHDRAW",
                PreviousStatus = ClubMembershipStatuses.Pending,
                Status = row.Status,
                PreviousVersion = previousVersion,
                Version = row.ConcurrencyToken,
                OccurredAtUtc = row.WithdrawnAtUtc.Value
            });
            return Results.Ok(WithdrawalResponse(row));
        }, ct, required: false);
    }

    private static object WithdrawalResponse(ClubMembership row) => new
    { applicationId = row.Id, row.ClubId, status = "WITHDRAWN", version = row.ConcurrencyToken, canWithdraw = false, withdrawnAt = row.WithdrawnAtUtc };
}
