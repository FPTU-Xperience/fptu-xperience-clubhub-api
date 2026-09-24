using System.Security.Claims;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClubReportHub.Shared.Auth;
using FinanceService.Data;
using FinanceService.Extensions;
using FinanceService.Mappers;
using FinanceService.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceService.Endpoints;

public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/transactions", GetTransactions);
        group.MapPost("/transactions", CreateManualAdjustment)
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);
    }

    private static async Task<IResult> CreateManualAdjustment(
        JsonElement request,
        FinanceDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (request.ValueKind != JsonValueKind.Object ||
            !request.TryGetProperty("clubId", out var clubValue) ||
            !request.TryGetProperty("type", out var typeValue) ||
            !request.TryGetProperty("amount", out var amountValue) ||
            !request.TryGetProperty("description", out var descriptionValue) ||
            !request.TryGetProperty("reason", out var reasonValue) ||
            !request.TryGetProperty("idempotencyKey", out var keyValue) ||
            typeValue.ValueKind != JsonValueKind.String ||
            descriptionValue.ValueKind != JsonValueKind.String ||
            reasonValue.ValueKind != JsonValueKind.String ||
            keyValue.ValueKind != JsonValueKind.String ||
            amountValue.ValueKind != JsonValueKind.Number ||
            !amountValue.TryGetDecimal(out var amount))
            return Results.BadRequest(new { message = "A manual adjustment requires clubId, type, amount, description, reason and idempotencyKey." });

        var clubId = 0;
        var validClubId = (clubValue.ValueKind == JsonValueKind.Number &&
                clubValue.TryGetInt32(out clubId) && clubId > 0) ||
            (clubValue.ValueKind == JsonValueKind.String &&
                int.TryParse(clubValue.GetString(), out clubId) && clubId > 0);
        var description = descriptionValue.GetString()?.Trim() ?? string.Empty;
        var reason = reasonValue.GetString()?.Trim() ?? string.Empty;
        var key = keyValue.GetString()?.Trim() ?? string.Empty;
        if (!validClubId || !string.Equals(typeValue.GetString(), "adjustment", StringComparison.OrdinalIgnoreCase) ||
            amount == 0 || amount is < -1_000_000_000m or > 1_000_000_000m ||
            decimal.Round(amount, 2) != amount ||
            description.Length is < 1 or > 1000 || reason.Length is < 10 or > 1000 ||
            !Regex.IsMatch(key, "^[A-Za-z0-9][A-Za-z0-9._:-]{7,99}$"))
            return Results.BadRequest(new { message = "Only a bounded signed adjustment with a reason and a valid idempotency key is allowed." });

        var actorId = user.GetUserId();
        var existing = await db.ManualFinanceAdjustments.AsNoTracking()
            .Include(x => x.FinanceTransaction)
            .FirstOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
        if (existing is not null)
            return Matches(existing, clubId, amount, description, reason, actorId)
                ? Results.Ok(ToResponse(existing))
                : Results.Conflict(new { message = "This idempotency key belongs to a different adjustment." });

        if (!await db.BudgetProposals.AnyAsync(x => x.ClubId == clubId, cancellationToken) &&
            !await db.FinanceTransactions.AnyAsync(x => x.ClubId == clubId, cancellationToken))
            return Results.NotFound(new { message = "No finance history exists for this club." });

        var row = new FinanceTransaction
        {
            ClubId = clubId,
            Amount = amount,
            Type = TransactionTypes.ManualAdjustment,
            Description = description
        };
        var adjustment = new ManualFinanceAdjustment
        {
            FinanceTransaction = row,
            IdempotencyKey = key,
            CreatedByUserId = actorId,
            Reason = reason
        };
        db.ManualFinanceAdjustments.Add(adjustment);
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            existing = await db.ManualFinanceAdjustments.AsNoTracking()
                .Include(x => x.FinanceTransaction)
                .FirstOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
            if (existing is null) throw;
            return Matches(existing, clubId, amount, description, reason, actorId)
                ? Results.Ok(ToResponse(existing))
                : Results.Conflict(new { message = "This idempotency key belongs to a different adjustment." });
        }

        return Results.Created("/api/finance/transactions", ToResponse(adjustment));
    }

    private static bool Matches(ManualFinanceAdjustment adjustment, int clubId, decimal amount,
        string description, string reason, int actorId) =>
        adjustment.CreatedByUserId == actorId && adjustment.Reason == reason &&
        adjustment.FinanceTransaction.ClubId == clubId &&
        adjustment.FinanceTransaction.Amount == amount &&
        adjustment.FinanceTransaction.Description == description;

    private static object ToResponse(ManualFinanceAdjustment adjustment) => new
    {
        adjustment.FinanceTransaction.Id,
        adjustment.FinanceTransaction.ClubId,
        adjustment.FinanceTransaction.Type,
        adjustment.FinanceTransaction.Amount,
        adjustment.FinanceTransaction.Description,
        adjustment.FinanceTransaction.TransactionDateUtc,
        adjustment.CreatedByUserId,
        adjustment.Reason
    };

    private static async Task<IResult> GetTransactions(
        int? clubId,
        FinanceDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        CancellationToken cancellationToken)
    {
        var query = db.FinanceTransactions.AsNoTracking();
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

            query = query.Where(x => allowedClubIds.Contains(x.ClubId));
        }

        if (clubId.HasValue)
        {
            query = query.Where(x => x.ClubId == clubId);
        }

        var rows = await query.OrderByDescending(x => x.TransactionDateUtc).Take(100).ToListAsync(cancellationToken);
        return Results.Ok(rows.Select(FinanceMappers.ToFinanceTransactionResponse));
    }
}
