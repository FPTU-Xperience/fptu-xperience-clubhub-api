using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using AdminService.Data;
using AdminService.Errors;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Services;

internal sealed class RawFundingException(string code, string message, int status = 409) : ApplicationExceptionBase(message, code, status);

internal static class RawFundingLedger
{
    private sealed record Totals(decimal Granted, decimal Available, decimal Reserved, decimal Spent);
    internal static async Task<bool> CanRead(VerifiedContribution contribution, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!http.User.IsInRole(AuthRoles.Admin) && !http.User.IsInRole(AuthRoles.StudentAffairsAdmin)) return false;
        var id = SourceReservation(contribution); if (id is null) return false;
        var reservation = await db.Set<RawFundingReservation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (reservation is null) return false;
        var actorId = http.User.GetUserId();
        var actor = (await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([actorId], ct)).GetValueOrDefault(actorId);
        return actor is { IsActive: true, IsLocked: false } && (http.User.IsInRole(AuthRoles.Admin) || actor.CampusCode == reservation.CampusCode);
    }
    internal static void Require([DoesNotReturnIf(false)] bool condition, string code, string message, int status = 409)
    { if (!condition) throw new RawFundingException(code, message, status); }

    internal static async Task<RawFundingAccount> Account(AdminDbContext db, int clubId, string campus, string semester, CancellationToken ct)
    {
        var row = await db.Set<RawFundingAccount>().SingleOrDefaultAsync(x => x.ClubId == clubId && x.CampusCode == campus && x.SemesterId == semester, ct);
        Require(row is not null, "RAW_FUNDING_NOT_GRANTED", "The organization has no raw-point grant for this semester.");
        await Reconcile(db, row!, ct); return row!;
    }

    internal static async Task Reconcile(AdminDbContext db, RawFundingAccount row, CancellationToken ct)
    {
        var query = db.Set<RawFundingEntry>().AsNoTracking().Where(x => x.ClubId == row.ClubId && x.CampusCode == row.CampusCode && x.SemesterId == row.SemesterId);
        Totals totals;
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var entries = await query.Select(x => new { x.GrantedDelta, x.AvailableDelta, x.ReservedDelta, x.SpentDelta }).ToArrayAsync(ct);
            totals = new(entries.Sum(x => x.GrantedDelta), entries.Sum(x => x.AvailableDelta), entries.Sum(x => x.ReservedDelta), entries.Sum(x => x.SpentDelta));
        }
        else totals = await query.GroupBy(x => x.ClubId).Select(g => new Totals(g.Sum(x => x.GrantedDelta), g.Sum(x => x.AvailableDelta), g.Sum(x => x.ReservedDelta), g.Sum(x => x.SpentDelta)))
            .SingleOrDefaultAsync(ct) ?? new(0, 0, 0, 0);
        Require(totals.Granted == row.Granted && totals.Available == row.Available
            && totals.Reserved == row.Reserved && totals.Spent == row.Spent
            && row.Available >= 0 && row.Reserved >= 0 && row.Spent >= 0 && row.Granted == row.Available + row.Reserved + row.Spent,
            "RAW_RECONCILIATION_REQUIRED", "Raw funding projection does not match its immutable ledger.", 503);
    }

    internal static void Append(AdminDbContext db, RawFundingAccount account, string key, string kind,
        decimal granted, decimal available, decimal reserved, decimal spent, int? actor, string reason,
        RawFundingReservation? reservation = null, ExperienceLedgerEffect? effect = null)
    {
        Require(account.Available + available >= 0 && account.Reserved + reserved >= 0 && account.Spent + spent >= 0
            && account.Granted + granted is >= 0 and <= 1_000_000_000_000
            && granted == available + reserved + spent, "RAW_BUDGET_EXCEEDED", "The operation exceeds available or reserved raw points.");
        account.Granted += granted; account.Available += available; account.Reserved += reserved; account.Spent += spent; account.Version = Guid.NewGuid();
        db.Add(new RawFundingEntry
        {
            ClubId = account.ClubId, CampusCode = account.CampusCode, SemesterId = account.SemesterId, OperationKey = key,
            Kind = kind, GrantedDelta = granted, AvailableDelta = available, ReservedDelta = reserved, SpentDelta = spent,
            ActorId = actor, RecordedAt = DateTimeOffset.UtcNow, Reason = reason, ReservationId = reservation?.Id,
            ContributionId = effect?.ContributionId, ExperienceEffect = effect
        });
    }

    internal static async Task<RawFundingReservation> Reserve(AdminDbContext db, int clubId, string campus, string semester,
        string sourceKey, string sourceVersion, Guid policyVersion, decimal budget, int actor, CancellationToken ct)
    {
        var existing = await db.Set<RawFundingReservation>().SingleOrDefaultAsync(x => x.SourceKey == sourceKey && x.SourceVersion == sourceVersion, ct);
        if (existing is not null)
        {
            Require(existing.Status == "RESERVED" && existing.ClubId == clubId && existing.CampusCode == campus
                && existing.SemesterId == semester && existing.PolicyVersion == policyVersion && existing.Budget == budget,
                "RAW_RESERVATION_CHANGED", "The funding source was refunded, settled or changed; submit a new source revision.");
            return existing;
        }
        Require(budget > 0 && budget <= 1_000_000_000 && decimal.Round(budget, 4) == budget && sourceKey.Length is > 0 and <= 160 && sourceVersion.Length is > 0 and <= 100,
            "INVALID_RAW_BUDGET", "Provide a positive four-decimal raw budget and versioned source.", 400);
        var account = await Account(db, clubId, campus, semester, ct);
        var row = new RawFundingReservation { ClubId = clubId, CampusCode = campus, SemesterId = semester, SourceKey = sourceKey,
            SourceVersion = sourceVersion, PolicyVersion = policyVersion, Budget = budget, ReservedBy = actor, ReservedAt = DateTimeOffset.UtcNow };
        db.Add(row);
        Append(db, account, $"reserve:{row.Id:N}", "RESERVE", 0, -budget, budget, 0, actor, "Gate 1 raw-point reservation", row);
        return row;
    }

    internal static Guid? SourceReservation(VerifiedContribution row)
    {
        if (row.ActivityId is null) return null; // External declarations and club credits never debit the activity pool.
        try
        {
            if (string.IsNullOrWhiteSpace(row.SourceSnapshotJson)) return null;
            using var source = JsonDocument.Parse(row.SourceSnapshotJson);
            return source.RootElement.ValueKind == JsonValueKind.Object && source.RootElement.TryGetProperty("fundingReservationId", out var id)
                && id.ValueKind == JsonValueKind.String && id.TryGetGuid(out var value) ? value : null;
        }
        catch (JsonException) { return null; } // Older records do not have a funding snapshot.
    }

    internal static async Task Charge(AdminDbContext db, VerifiedContribution contribution, ExperienceLedgerEffect effect, CancellationToken ct)
    {
        var source = SourceReservation(contribution);
        if (source is null)
        {
            Require(contribution.ActivityId is null || !await db.Set<RawFundingReservation>().AnyAsync(x => x.ActivityId == contribution.ActivityId, ct),
                "FUNDING_SNAPSHOT_MISSING", "The funded activity is missing its immutable funding reference.", 503);
            return; // Historical/unfunded activities retain their original ledger; no inferred retroactive charge.
        }
        var row = await db.Set<RawFundingReservation>().SingleOrDefaultAsync(x => x.Id == source, ct);
        Require(row is not null && row.Status != "REFUNDED" && row.PolicyVersion == effect.PolicyVersion && row.SemesterId == effect.SemesterId
            && (row.ClubId == 0 || row.ClubId == contribution.ClubId)
            && (row.ActivityId is null || row.ActivityId == contribution.ActivityId), "RAW_RESERVATION_INVALID", "The award must match its original raw funding and policy.");
        row!.ActivityId ??= contribution.ActivityId;
        var key = $"effect:{effect.ContributionId:N}:{effect.RevisionId:N}:{effect.EffectType}";
        Require(!await db.Set<RawFundingEntry>().AnyAsync(x => x.OperationKey == key, ct), "RAW_EFFECT_ALREADY_APPLIED", "The raw effect was already posted.");
        var delta = effect.Amount;
        Require(delta <= 0 || row.Status == "RESERVED", "RAW_SETTLEMENT_CLOSED", "Reopen an authorized raw budget before increasing a settled award.");
        Require(row.Spent + delta >= 0 && row.Spent + delta <= row.Budget, "RAW_BUDGET_EXCEEDED", "Verified awards exceed the approved raw budget.");
        var account = await Account(db, row.ClubId, row.CampusCode, row.SemesterId, ct);
        Append(db, account, key, delta >= 0 ? "AWARD" : "REVERSAL", 0,
            row.Status == "SETTLED" ? -delta : 0, row.Status == "RESERVED" ? -delta : 0, delta,
            effect.VerifiedByUserId, effect.Reason, row, effect);
        row.Spent += delta; row.Version = Guid.NewGuid();
    }

    internal static async Task Settle(AdminDbContext db, RawFundingReservation row, int actor, string reason, CancellationToken ct)
    {
        Require(row.Status == "RESERVED", "RAW_SETTLEMENT_CLOSED", "Only a reserved activity can be settled.");
        var account = await Account(db, row.ClubId, row.CampusCode, row.SemesterId, ct);
        var unused = row.Budget - row.Spent;
        Append(db, account, $"settle:{row.Id:N}:{row.Version:N}", "SETTLE", 0, unused, -unused, 0, actor, reason, row);
        row.Status = "SETTLED"; row.Version = Guid.NewGuid();
    }

    internal static async Task Refund(AdminDbContext db, RawFundingReservation row, int? actor, string reason, CancellationToken ct)
    {
        if (row.Status == "REFUNDED") return;
        Require(row.Spent == 0, "RAW_REVERSAL_REQUIRED", "Reverse issued XP effects before refunding the activity budget.");
        var account = await Account(db, row.ClubId, row.CampusCode, row.SemesterId, ct);
        var amount = row.Status == "RESERVED" ? row.Budget : 0;
        Append(db, account, $"refund:{row.Id:N}", "REFUND", 0, amount, -amount, 0, actor, reason, row);
        row.Status = "REFUNDED"; row.Version = Guid.NewGuid();
    }
}
