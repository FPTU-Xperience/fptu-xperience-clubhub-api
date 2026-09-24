using System.Security.Claims;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Messaging;
using ClubReportHub.Shared.Tracing;
using FinanceService.Clients;
using FinanceService.Contracts;
using FinanceService.Data;
using FinanceService.Extensions;
using FinanceService.Mappers;
using FinanceService.Models;
using FinanceService.Services;
using Microsoft.EntityFrameworkCore;

namespace FinanceService.Endpoints;

public static class ProposalEndpoints
{
    public static void MapProposalEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/proposals", GetProposals);
        group.MapGet("/proposals/{id:int}", GetProposalById);
        group.MapPost("/proposals", CreateProposal);
        group.MapPost("/proposals/{id:int}/submit", SubmitProposal);
        group.MapPost("/proposals/{id:int}/manager-review", ManagerReviewProposal);
        group.MapPost("/proposals/{id:int}/review", ReviewProposal)
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);
        group.MapPost("/proposals/{id:int}/manager-approve", ManagerApproveProposal);
        group.MapPost("/proposals/{id:int}/manager-reject", ManagerRejectProposal);
        group.MapPost("/proposals/{id:int}/approve", ApproveProposal)
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);
        group.MapPost("/proposals/{id:int}/reject", RejectProposal)
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);
    }

    private static async Task<IResult> SubmitProposal(
        int id,
        FinanceDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        CancellationToken cancellationToken)
    {
        var proposal = await db.BudgetProposals.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (proposal is null) return Results.NotFound();
        if (!user.IsFinanceReviewer() && proposal.ProposedByUserId != user.GetUserId() &&
            !await clubAccess.CanAccessFinanceClubAsync(proposal.ClubId, httpContext, cancellationToken))
            return Results.Forbid();
        if (proposal.Status != FinanceStatuses.Submitted)
            return Results.Conflict(new { message = "This proposal is no longer awaiting manager review." });
        return Results.Ok(new { success = true, status = "pending_manager_review" });
    }

    private static async Task<IResult> ManagerReviewProposal(
        int id,
        JsonElement request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        FutureEventReportClient futureEventReports,
        IConfiguration config,
        CancellationToken cancellationToken)
    {
        if (!TryReadReview(request, out var review))
            return Results.BadRequest(new { message = "A review note of at most 1,000 characters is required." });
        var proposal = await db.BudgetProposals.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (proposal is null) return Results.NotFound();
        if (proposal.SourceReportId.HasValue &&
            !await ValidateCombinedReportWorkflowAsync(proposal, httpContext, futureEventReports, config, cancellationToken))
            return Results.BadRequest(new { message = "Review this budget together with its future event report." });
        if (proposal.Status != FinanceStatuses.Submitted)
            return Results.Conflict(new { message = "Only submitted proposals can receive a manager review note." });
        var access = (await clubAccess.GetMyAccessAsync(httpContext.GetBearerToken(), cancellationToken))
            .FirstOrDefault(x => x.ClubId == proposal.ClubId && x.IsManager);
        if (access is null) return Results.Forbid();
        if (proposal.ProposedByUserId == user.GetUserId())
            return Results.BadRequest(new { message = "The proposal creator cannot review their own proposal." });
        if (proposal.ManagerReviewNote == review && proposal.ManagerReviewedByUserId == user.GetUserId())
            return Results.Ok(new { success = true });
        proposal.ManagerReviewNote = review;
        proposal.ManagerReviewedByUserId = user.GetUserId();
        proposal.ManagerReviewedAtUtc = DateTimeOffset.UtcNow;
        proposal.Version++;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { message = "Proposal changed while it was being reviewed." });
        }
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> ReviewProposal(
        int id,
        JsonElement request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        FutureEventReportClient futureEventReports,
        IConfiguration config,
        CancellationToken cancellationToken)
    {
        if (!TryReadReview(request, out var review))
            return Results.BadRequest(new { message = "A review note of at most 1,000 characters is required." });
        var proposal = await db.BudgetProposals.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (proposal is null) return Results.NotFound();
        if (proposal.SourceReportId.HasValue &&
            !await ValidateCombinedReportWorkflowAsync(proposal, httpContext, futureEventReports, config, cancellationToken))
            return Results.BadRequest(new { message = "Review this budget together with its future event report." });
        if (proposal.Status != FinanceStatuses.ManagerApproved)
            return Results.Conflict(new { message = "The club manager must approve this proposal before final review." });
        if (proposal.ProposedByUserId == user.GetUserId())
            return Results.BadRequest(new { message = "The proposal creator cannot review their own proposal." });
        if (proposal.ReviewNote == review && proposal.ReviewedByUserId == user.GetUserId())
            return Results.Ok(new { success = true });
        proposal.ReviewNote = review;
        proposal.ReviewedByUserId = user.GetUserId();
        proposal.ReviewedAtUtc = DateTimeOffset.UtcNow;
        proposal.Version++;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { message = "Proposal changed while it was being reviewed." });
        }
        return Results.Ok(new { success = true });
    }

    private static bool TryReadReview(JsonElement request, out string review)
    {
        review = string.Empty;
        if (request.ValueKind != JsonValueKind.Object ||
            !request.TryGetProperty("review", out var value) || value.ValueKind != JsonValueKind.String)
            return false;
        review = value.GetString()?.Trim() ?? string.Empty;
        return review.Length is > 0 and <= 1000;
    }

    private static async Task<IResult> GetProposals(
        int? clubId,
        string? status,
        int? page,
        int? pageSize,
        FinanceDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        CancellationToken cancellationToken)
    {
        var actualPage = Math.Max(page ?? 1, 1);
        var actualPageSize = pageSize is <= 0 ? 20 : Math.Min(pageSize ?? 20, 100);
        var skip = (int)Math.Min((long)(actualPage - 1) * actualPageSize, int.MaxValue);

        var baseQuery = db.BudgetProposals.AsNoTracking();
        if (!user.IsFinanceReviewer())
        {
            var allowedClubIds = await clubAccess.GetFinanceClubIdsAsync(httpContext, cancellationToken);
            if (allowedClubIds.Count == 0)
            {
                return Results.Forbid();
            }

            if (clubId.HasValue && !allowedClubIds.Contains(clubId.Value))
            {
                return Results.Forbid();
            }

            baseQuery = baseQuery.Where(x => allowedClubIds.Contains(x.ClubId));
        }

        if (clubId.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.ClubId == clubId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            baseQuery = baseQuery.Where(x => x.Status == status);
        }

        var total = await baseQuery.CountAsync(cancellationToken);
        var rows = await baseQuery
            .OrderByDescending(x => x.ProposedAtUtc)
            .Skip(skip)
            .Take(actualPageSize)
            .Include(x => x.Settlements)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return Results.Ok(new { total, page = actualPage, pageSize = actualPageSize, items = rows.Select(FinanceMappers.ToBudgetProposalResponse) });
    }

    private static async Task<IResult> GetProposalById(
        int id,
        FinanceDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        CancellationToken cancellationToken)
    {
        var proposal = await db.BudgetProposals
            .AsNoTracking()
            .Include(x => x.Settlements)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (proposal is null)
        {
            return Results.NotFound();
        }

        if (!user.IsFinanceReviewer() && !await clubAccess.CanAccessFinanceClubAsync(proposal.ClubId, httpContext, cancellationToken))
        {
            return Results.Forbid();
        }

        return Results.Ok(FinanceMappers.ToBudgetProposalResponse(proposal));
    }

    private static async Task<IResult> CreateProposal(
        CreateBudgetProposalRequest request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        ActivityCatalogClient activityCatalog,
        FutureEventReportClient futureEventReports,
        CancellationToken cancellationToken)
    {
        if (request.RequestedAmount <= 0)
        {
            return Results.BadRequest(new { message = "Requested amount must be greater than zero." });
        }

        var access = (await clubAccess.GetMyAccessAsync(httpContext.GetBearerToken(), cancellationToken))
            .FirstOrDefault(x => x.ClubId == request.ClubId && x.CanManageFinance);
        if (access is null)
        {
            return Results.Forbid();
        }

        if (request.ActivityId.HasValue && request.SourceReportId.HasValue)
        {
            return Results.BadRequest(new { message = "A proposal can link either an existing activity or a future event report, not both." });
        }

        var title = request.Title?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;
        FutureEventReportSnapshot? sourceReport = null;
        if (request.SourceReportId.HasValue)
        {
            sourceReport = await futureEventReports.GetAsync(
                request.SourceReportId.Value,
                httpContext.GetBearerToken(),
                cancellationToken);
            if (sourceReport is null || sourceReport.ClubId != request.ClubId)
            {
                return Results.BadRequest(new { message = "The selected future event report does not belong to this club." });
            }

            if (!string.Equals(sourceReport.ReportType, "FUTURE_EVENT", StringComparison.OrdinalIgnoreCase)
                || sourceReport.Details.Count != 1)
            {
                return Results.BadRequest(new { message = "Only a future event report awaiting its budget can be linked." });
            }

            title = sourceReport.Details.Single().ActivityName.Trim();
            var existingProposal = await db.BudgetProposals
                .Include(x => x.Settlements)
                .FirstOrDefaultAsync(x => x.SourceReportId == sourceReport.Id, cancellationToken);
            if (existingProposal is not null)
            {
                if (existingProposal.Status == FinanceStatuses.Rejected
                    && string.Equals(sourceReport.Status, "Awaiting Finance", StringComparison.OrdinalIgnoreCase))
                {
                    existingProposal.Title = title;
                    existingProposal.Description = description;
                    existingProposal.RequestedAmount = request.RequestedAmount;
                    existingProposal.ApprovedAmount = null;
                    existingProposal.Status = FinanceStatuses.Submitted;
                    existingProposal.ProposedByUserId = user.GetUserId();
                    existingProposal.ProposedAtUtc = DateTimeOffset.UtcNow;
                    existingProposal.ManagerReviewedByUserId = null;
                    existingProposal.ManagerReviewedAtUtc = null;
                    existingProposal.ManagerReviewNote = null;
                    existingProposal.ReviewedByUserId = null;
                    existingProposal.ReviewedAtUtc = null;
                    existingProposal.ReviewNote = null;
                    await db.SaveChangesAsync(cancellationToken);
                }

                if (sourceReport.BudgetProposalId != existingProposal.Id)
                {
                    var linked = await futureEventReports.LinkBudgetAsync(
                        sourceReport.Id,
                        existingProposal.Id,
                        existingProposal.RequestedAmount,
                        existingProposal.Description,
                        httpContext.GetBearerToken(),
                        cancellationToken);
                    if (!linked)
                    {
                        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                    }
                }
                return Results.Ok(FinanceMappers.ToBudgetProposalResponse(existingProposal));
            }

            if (!string.Equals(sourceReport.Status, "Awaiting Finance", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { message = "This future event report is no longer awaiting a budget." });
            }
        }

        if (request.ActivityId.HasValue)
        {
            var activity = await activityCatalog.GetAsync(
                request.ActivityId.Value,
                httpContext.GetBearerToken(),
                cancellationToken);
            if (activity is null || activity.ClubId != request.ClubId)
            {
                return Results.BadRequest(new { message = "The selected activity does not belong to this club." });
            }

            if (!BudgetProposalActivityRules.CanLink(activity.MeetingDays, activity.Title, activity.Description))
            {
                return Results.BadRequest(new { message = "Weekly, monthly, or attendance activities cannot be linked to a budget proposal." });
            }

            if (string.Equals(activity.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { message = "A cancelled activity cannot be linked to a budget proposal." });
            }

            title = activity.Title.Trim();
        }

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description))
        {
            return Results.BadRequest(new { message = "Proposal title and description are required." });
        }

        var proposal = new BudgetProposal
        {
            ClubId = request.ClubId,
            ClubName = access.ClubName,
            ActivityId = request.ActivityId,
            SourceReportId = request.SourceReportId,
            Title = title,
            Description = description,
            RequestedAmount = request.RequestedAmount,
            ProposedByUserId = user.GetUserId()
        };

        db.BudgetProposals.Add(proposal);

        if (sourceReport is null)
        {
            db.AddOutboxMessage(new BudgetProposalSubmittedEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                proposal.Id,
                proposal.ClubId,
                proposal.ClubName,
                proposal.RequestedAmount,
                proposal.ProposedByUserId,
                "ManagerReview",
                access.ManagerUserIds), EventRoutingKeys.BudgetProposalSubmitted, httpContext.GetCorrelationId());
        }

        await db.SaveChangesAsync(cancellationToken);

        if (sourceReport is not null)
        {
            var linked = await futureEventReports.LinkBudgetAsync(
                sourceReport.Id,
                proposal.Id,
                proposal.RequestedAmount,
                proposal.Description,
                httpContext.GetBearerToken(),
                cancellationToken);
            if (!linked)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }

        return Results.Created($"/api/finance/proposals/{proposal.Id}", FinanceMappers.ToBudgetProposalResponse(proposal));
    }

    private static async Task<bool> ValidateCombinedReportWorkflowAsync(
        BudgetProposal proposal,
        HttpContext httpContext,
        FutureEventReportClient futureEventReports,
        IConfiguration config,
        CancellationToken cancellationToken)
    {
        if (!proposal.SourceReportId.HasValue)
        {
            return true;
        }

        if (!httpContext.IsCombinedReportWorkflow(config))
        {
            return false;
        }

        var bearerToken = httpContext.GetBearerToken();
        if (string.IsNullOrEmpty(bearerToken))
        {
            return true;
        }

        var sourceReport = await futureEventReports.GetAsync(
            proposal.SourceReportId.Value,
            bearerToken,
            cancellationToken);

        if (sourceReport is null)
        {
            return httpContext.IsCombinedReportWorkflow(config);
        }

        if (sourceReport.ClubId != proposal.ClubId)
        {
            return false;
        }

        if (sourceReport.BudgetProposalId.HasValue && sourceReport.BudgetProposalId.Value != proposal.Id)
        {
            return false;
        }

        return true;
    }

    private static async Task<IResult> ManagerApproveProposal(
        int id,
        ReviewBudgetProposalRequest request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        FutureEventReportClient futureEventReports,
        IConfiguration config,
        CancellationToken cancellationToken)
    {
        var proposal = await db.BudgetProposals.Include(x => x.Settlements)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (proposal is null) return Results.NotFound();

        if (proposal.SourceReportId.HasValue && !await ValidateCombinedReportWorkflowAsync(proposal, httpContext, futureEventReports, config, cancellationToken))
        {
            return Results.BadRequest(new { message = "Review this budget together with its future event report." });
        }
        if (proposal.SourceReportId.HasValue && proposal.Status == FinanceStatuses.ManagerApproved)
        {
            return Results.Ok(FinanceMappers.ToBudgetProposalResponse(proposal));
        }

        if (!BudgetProposalReviewRules.CanManagerReview(proposal.Status))
        {
            return Results.BadRequest(new { message = "Only proposals awaiting club owner review can be approved." });
        }

        var access = (await clubAccess.GetMyAccessAsync(httpContext.GetBearerToken(), cancellationToken))
            .FirstOrDefault(x => x.ClubId == proposal.ClubId && x.IsManager);
        if (access is null) return Results.Forbid();
        if (proposal.ProposedByUserId == user.GetUserId())
        {
            return Results.BadRequest(new { message = "The proposal creator cannot approve their own proposal." });
        }

        proposal.Status = FinanceStatuses.ManagerApproved;
        proposal.ManagerReviewedByUserId = user.GetUserId();
        proposal.ManagerReviewedAtUtc = DateTimeOffset.UtcNow;
        proposal.ManagerReviewNote = string.IsNullOrWhiteSpace(request.Note)
            ? "Đã được chủ nhiệm câu lạc bộ phê duyệt."
            : request.Note.Trim();
        proposal.Version++;

        if (!proposal.SourceReportId.HasValue)
        {
            db.AddOutboxMessage(new BudgetProposalSubmittedEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                proposal.Id,
                proposal.ClubId,
                proposal.ClubName,
                proposal.RequestedAmount,
                proposal.ProposedByUserId,
                "FinalReview"), EventRoutingKeys.BudgetProposalSubmitted, httpContext.GetCorrelationId());
        }

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(FinanceMappers.ToBudgetProposalResponse(proposal));
    }

    private static async Task<IResult> ManagerRejectProposal(
        int id,
        ReviewBudgetProposalRequest request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        FutureEventReportClient futureEventReports,
        IConfiguration config,
        CancellationToken cancellationToken)
    {
        var proposal = await db.BudgetProposals.Include(x => x.Settlements)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (proposal is null) return Results.NotFound();

        if (proposal.SourceReportId.HasValue && !await ValidateCombinedReportWorkflowAsync(proposal, httpContext, futureEventReports, config, cancellationToken))
        {
            return Results.BadRequest(new { message = "Review this budget together with its future event report." });
        }
        if (proposal.SourceReportId.HasValue && proposal.Status == FinanceStatuses.Rejected)
        {
            return Results.Ok(FinanceMappers.ToBudgetProposalResponse(proposal));
        }

        if (!BudgetProposalReviewRules.CanManagerReview(proposal.Status))
        {
            return Results.BadRequest(new { message = "Only proposals awaiting club owner review can be rejected." });
        }

        var canManage = (await clubAccess.GetMyAccessAsync(httpContext.GetBearerToken(), cancellationToken))
            .Any(x => x.ClubId == proposal.ClubId && x.IsManager);
        if (!canManage) return Results.Forbid();
        if (proposal.ProposedByUserId == user.GetUserId())
        {
            return Results.BadRequest(new { message = "The proposal creator cannot reject their own proposal." });
        }

        proposal.Status = FinanceStatuses.Rejected;
        proposal.ManagerReviewedByUserId = user.GetUserId();
        proposal.ManagerReviewedAtUtc = DateTimeOffset.UtcNow;
        proposal.ManagerReviewNote = string.IsNullOrWhiteSpace(request.Note)
            ? "Đã bị chủ nhiệm câu lạc bộ từ chối."
            : request.Note.Trim();
        proposal.Version++;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(FinanceMappers.ToBudgetProposalResponse(proposal));
    }

    private static async Task<IResult> ApproveProposal(
        int id,
        ReviewBudgetProposalRequest request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        FutureEventReportClient futureEventReports,
        IConfiguration config,
        CancellationToken cancellationToken)
    {
        var proposal = await db.BudgetProposals.Include(x => x.Settlements).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (proposal is null)
        {
            return Results.NotFound();
        }

        if (proposal.SourceReportId.HasValue && !await ValidateCombinedReportWorkflowAsync(proposal, httpContext, futureEventReports, config, cancellationToken))
        {
            return Results.BadRequest(new { message = "Review this budget together with its future event report." });
        }
        if (proposal.SourceReportId.HasValue && proposal.Status == FinanceStatuses.Approved)
        {
            return Results.Ok(FinanceMappers.ToBudgetProposalResponse(proposal));
        }

        if (!BudgetProposalReviewRules.CanFinalReview(proposal.Status))
        {
            return Results.BadRequest(new { message = "The club owner must approve this proposal before final approval." });
        }

        if (proposal.ProposedByUserId == user.GetUserId())
        {
            return Results.BadRequest(new { message = "The proposal creator cannot approve their own proposal." });
        }

        var approvedAmount = request.ApprovedAmount ?? proposal.RequestedAmount;
        if (approvedAmount <= 0)
        {
            return Results.BadRequest(new { message = "Approved amount must be greater than zero." });
        }

        proposal.Status = FinanceStatuses.Approved;
        proposal.ApprovedAmount = approvedAmount;
        proposal.ReviewedByUserId = user.GetUserId();
        proposal.ReviewedAtUtc = DateTimeOffset.UtcNow;
        proposal.ReviewNote = string.IsNullOrWhiteSpace(request.Note) ? "Ngân sách đã được phê duyệt." : request.Note.Trim();
        proposal.Version++;
        db.FinanceTransactions.Add(new FinanceTransaction
        {
            ClubId = proposal.ClubId,
            Amount = approvedAmount,
            Type = TransactionTypes.BudgetApproved,
            Description = proposal.Title,
            ReferenceId = proposal.Id
        });

        db.AddOutboxMessage(new BudgetApprovedEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            proposal.Id,
            proposal.ClubId,
            proposal.ClubName,
            approvedAmount,
            user.GetUserId(),
            proposal.ProposedByUserId), EventRoutingKeys.BudgetApproved, httpContext.GetCorrelationId());

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(FinanceMappers.ToBudgetProposalResponse(proposal));
    }

    private static async Task<IResult> RejectProposal(
        int id,
        ReviewBudgetProposalRequest request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        FutureEventReportClient futureEventReports,
        IConfiguration config,
        CancellationToken cancellationToken = default)
    {
        var proposal = await db.BudgetProposals.Include(x => x.Settlements).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (proposal is null)
        {
            return Results.NotFound();
        }

        if (proposal.SourceReportId.HasValue && !await ValidateCombinedReportWorkflowAsync(proposal, httpContext, futureEventReports, config, cancellationToken))
        {
            return Results.BadRequest(new { message = "Review this budget together with its future event report." });
        }
        if (proposal.SourceReportId.HasValue && proposal.Status == FinanceStatuses.Rejected)
        {
            return Results.Ok(FinanceMappers.ToBudgetProposalResponse(proposal));
        }

        if (!BudgetProposalReviewRules.CanFinalReview(proposal.Status))
        {
            return Results.BadRequest(new { message = "The club owner must review this proposal before final rejection." });
        }

        if (proposal.ProposedByUserId == user.GetUserId())
        {
            return Results.BadRequest(new { message = "The proposal creator cannot reject their own proposal." });
        }

        proposal.Status = FinanceStatuses.Rejected;
        proposal.ReviewedByUserId = user.GetUserId();
        proposal.ReviewedAtUtc = DateTimeOffset.UtcNow;
        proposal.ReviewNote = string.IsNullOrWhiteSpace(request.Note) ? "Ngân sách đã bị từ chối." : request.Note.Trim();
        proposal.Version++;
        await db.SaveChangesAsync();
        return Results.Ok(FinanceMappers.ToBudgetProposalResponse(proposal));
    }
}
