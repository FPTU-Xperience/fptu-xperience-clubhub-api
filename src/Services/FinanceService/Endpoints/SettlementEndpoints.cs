using System.Security.Claims;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using FinanceService.Contracts;
using FinanceService.Data;
using FinanceService.Extensions;
using FinanceService.Mappers;
using FinanceService.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceService.Endpoints;

public static class SettlementEndpoints
{
    public static void MapSettlementEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/settlements", GetSettlements);
        group.MapGet("/settlements/{id:int}", GetSettlement);
        group.MapPost("/proposals/{id:int}/settlements", CreateSettlement);
        group.MapPost("/settlements/{id:int}/submit", SubmitSettlement);
        group.MapPost("/settlements/{id:int}/review", ReviewSettlement)
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);
        group.MapPost("/settlements/{id:int}/approve", ApproveSettlement)
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);
        group.MapPost("/settlements/{id:int}/reject", RejectSettlement)
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);
    }

    private static async Task<IResult> SubmitSettlement(
        int id,
        FinanceDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        CancellationToken cancellationToken)
    {
        var settlement = await db.Settlements.AsNoTracking().Include(x => x.BudgetProposal)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (settlement is null) return Results.NotFound();
        if (!user.IsFinanceReviewer() &&
            !await clubAccess.CanAccessFinanceClubAsync(settlement.BudgetProposal.ClubId, httpContext, cancellationToken))
            return Results.Forbid();
        if (settlement.Status != FinanceStatuses.Submitted ||
            settlement.BudgetProposal.Status != FinanceStatuses.Approved)
            return Results.Conflict(new { message = "This settlement is no longer awaiting review." });
        return Results.Ok(new { success = true, status = "pending_review" });
    }

    private static async Task<IResult> ReviewSettlement(
        int id,
        JsonElement request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (request.ValueKind != JsonValueKind.Object ||
            !request.TryGetProperty("review", out var reviewValue) ||
            reviewValue.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(reviewValue.GetString()) ||
            reviewValue.GetString()!.Trim().Length > 1000)
            return Results.BadRequest(new { message = "A review note of at most 1,000 characters is required." });
        var review = reviewValue.GetString()!.Trim();
        var settlement = await db.Settlements.Include(x => x.BudgetProposal)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (settlement is null) return Results.NotFound();
        if (settlement.Status != FinanceStatuses.Submitted ||
            settlement.BudgetProposal.Status != FinanceStatuses.Approved)
            return Results.Conflict(new { message = "Only a submitted settlement can receive a review note." });
        if (settlement.BudgetProposal.ProposedByUserId == user.GetUserId())
            return Results.BadRequest(new { message = "The proposal creator cannot review its settlement." });
        if (settlement.ReviewNote == review && settlement.ReviewedByUserId == user.GetUserId())
            return Results.Ok(new { success = true });
        settlement.ReviewNote = review;
        settlement.ReviewedByUserId = user.GetUserId();
        settlement.ReviewedAtUtc = DateTimeOffset.UtcNow;
        settlement.BudgetProposal.Version++;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { message = "Settlement changed while it was being reviewed." });
        }
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> GetSettlement(
        int id,
        FinanceDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        CancellationToken cancellationToken)
    {
        var settlement = await db.Settlements.AsNoTracking()
            .Include(x => x.BudgetProposal)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (settlement is null)
        {
            return Results.NotFound();
        }

        if (!user.IsFinanceReviewer()
            && !await clubAccess.CanAccessFinanceClubAsync(settlement.BudgetProposal.ClubId, httpContext, cancellationToken))
        {
            return Results.Forbid();
        }

        return Results.Ok(FinanceMappers.ToSettlementResponse(settlement));
    }

    private static async Task<IResult> GetSettlements(
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

        var baseQuery = db.Settlements.AsNoTracking();
        if (!user.IsFinanceReviewer())
        {
            var allowedClubIds = await clubAccess.GetFinanceClubIdsAsync(httpContext, cancellationToken);
            if (allowedClubIds.Count == 0)
            {
                return Results.Forbid();
            }

            baseQuery = baseQuery.Where(x => allowedClubIds.Contains(x.BudgetProposal.ClubId));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            baseQuery = baseQuery.Where(x => x.Status == status);
        }

        var total = await baseQuery.CountAsync(cancellationToken);
        var rows = await baseQuery
            .OrderByDescending(x => x.SubmittedAtUtc)
            .Skip(skip)
            .Take(actualPageSize)
            .Include(x => x.BudgetProposal)
            .ToListAsync(cancellationToken);
        return Results.Ok(new { total, page = actualPage, pageSize = actualPageSize, items = rows.Select(FinanceMappers.ToSettlementResponse) });
    }

    private static async Task<IResult> CreateSettlement(
        int id,
        CreateSettlementRequest request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        CancellationToken cancellationToken)
    {
        var proposal = await db.BudgetProposals.Include(x => x.Settlements).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (proposal is null)
        {
            return Results.NotFound();
        }

        if (!await clubAccess.CanManageFinanceClubAsync(proposal.ClubId, httpContext, cancellationToken))
        {
            return Results.Forbid();
        }

        if (proposal.Status != FinanceStatuses.Approved)
        {
            return Results.BadRequest(new { message = "Only approved budget proposals can be settled." });
        }

        if (request.TotalSpent <= 0)
        {
            return Results.BadRequest(new { message = "Total spent must be greater than zero." });
        }

        // Validate receipt URL is a valid HTTPS URL
        if (!Uri.TryCreate(request.ReceiptUrl, UriKind.Absolute, out var receiptUri))
        {
            return Results.BadRequest(new { message = "Receipt URL must be a valid URL." });
        }
        if (!receiptUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = "Receipt URL must use HTTPS." });
        }

        if (request.TotalSpent > 1_000_000_000)
        {
            return Results.BadRequest(new { message = "Total spent exceeds maximum allowed amount." });
        }

        // Validate settlement doesn't exceed approved budget
        if (proposal.ApprovedAmount.HasValue && request.TotalSpent > proposal.ApprovedAmount.Value)
        {
            return Results.BadRequest(new { message = $"Total spent ({request.TotalSpent:N0}) exceeds approved amount ({proposal.ApprovedAmount:N0})." });
        }

        if (proposal.Settlements.Any(x => x.Status == FinanceStatuses.Submitted || x.Status == FinanceStatuses.Approved))
        {
            return Results.Conflict(new { message = "This proposal already has an active settlement." });
        }

        var settlement = new Settlement
        {
            BudgetProposalId = id,
            TotalSpent = request.TotalSpent,
            ReceiptUrl = request.ReceiptUrl.Trim()
        };
        proposal.Version++;
        proposal.Settlements.Add(settlement);
        db.FinanceTransactions.Add(new FinanceTransaction
        {
            ClubId = proposal.ClubId,
            Amount = request.TotalSpent,
            Type = TransactionTypes.SettlementSubmitted,
            Description = $"Đã nộp quyết toán cho {proposal.Title}",
            ReferenceId = proposal.Id
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { message = "This proposal already has an active settlement." });
        }

        return Results.Ok(FinanceMappers.ToBudgetProposalResponse(proposal));
    }

    private static async Task<IResult> ApproveSettlement(
        int id,
        ReviewSettlementRequest request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var settlement = await db.Settlements.Include(x => x.BudgetProposal)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (settlement is null)
        {
            return Results.NotFound();
        }

        if (settlement.Status != FinanceStatuses.Submitted
            || settlement.BudgetProposal.Status != FinanceStatuses.Approved)
        {
            return Results.Conflict(new { message = "Only submitted settlements on approved proposals can be approved." });
        }

        if (settlement.BudgetProposal.ProposedByUserId == user.GetUserId())
        {
            return Results.BadRequest(new { message = "The proposal creator cannot approve its settlement." });
        }

        var note = request.Note?.Trim();
        if (note?.Length > 1000)
        {
            return Results.BadRequest(new { message = "Review note must be at most 1000 characters." });
        }

        settlement.Status = FinanceStatuses.Approved;
        settlement.ReviewedByUserId = user.GetUserId();
        settlement.ReviewedAtUtc = DateTimeOffset.UtcNow;
        settlement.ReviewNote = string.IsNullOrWhiteSpace(note) ? "Quyết toán đã được phê duyệt." : note;
        settlement.BudgetProposal.Status = FinanceStatuses.Settled;
        settlement.BudgetProposal.Version++;
        db.FinanceTransactions.Add(new FinanceTransaction
        {
            ClubId = settlement.BudgetProposal.ClubId,
            Amount = settlement.TotalSpent,
            Type = TransactionTypes.SettlementApproved,
            Description = $"Đã phê duyệt quyết toán cho {settlement.BudgetProposal.Title}",
            ReferenceId = settlement.BudgetProposalId
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { message = "This settlement was reviewed concurrently. Refresh and try again." });
        }

        return Results.Ok(FinanceMappers.ToSettlementResponse(settlement));
    }

    private static async Task<IResult> RejectSettlement(
        int id,
        ReviewSettlementRequest request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var settlement = await db.Settlements.Include(x => x.BudgetProposal)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (settlement is null)
        {
            return Results.NotFound();
        }

        if (settlement.Status != FinanceStatuses.Submitted
            || settlement.BudgetProposal.Status != FinanceStatuses.Approved)
        {
            return Results.Conflict(new { message = "Only submitted settlements on approved proposals can be rejected." });
        }

        if (settlement.BudgetProposal.ProposedByUserId == user.GetUserId())
        {
            return Results.BadRequest(new { message = "The proposal creator cannot reject its settlement." });
        }

        var note = request.Note?.Trim();
        if (string.IsNullOrWhiteSpace(note) || note.Length > 1000)
        {
            return Results.BadRequest(new { message = "A rejection reason of at most 1000 characters is required." });
        }

        settlement.Status = FinanceStatuses.Rejected;
        settlement.ReviewedByUserId = user.GetUserId();
        settlement.ReviewedAtUtc = DateTimeOffset.UtcNow;
        settlement.ReviewNote = note;
        settlement.BudgetProposal.Version++;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { message = "This settlement was reviewed concurrently. Refresh and try again." });
        }

        return Results.Ok(FinanceMappers.ToSettlementResponse(settlement));
    }
}
