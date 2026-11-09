using System.Security.Cryptography;
using System.Text;
using AdminService.Data;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Services;

// The approved 1:1 credit bridge is a projection of immutable verified XP effects.
// It never copies/mints a second award, rewrites XP, or imports sample wallets.
internal static class ClubCreditLedger
{
    internal const string Policy = "CLUB_VERIFIED_XP_1_TO_1_V1";
    internal sealed record CreditEffect(long Id, decimal Amount, DateTimeOffset RecordedAt, string SemesterId, Guid ContributionId);
    internal sealed record Balance(decimal Earned, decimal Revoked, decimal Spent, decimal Available, long SourceRevision, Guid Version);
    internal static IQueryable<CreditEffect> Source(AdminDbContext db, int userId, int clubId) =>
        from effect in db.ExperienceLedgerEffects.AsNoTracking()
        join contribution in db.Contributions.AsNoTracking() on effect.ContributionId equals contribution.Id
        where effect.UserId == userId && contribution.UserId == userId && contribution.ClubId == clubId
            && contribution.SourceType == "CLUB" && effect.PolicyVersion != Guid.Empty && effect.VerificationId != Guid.Empty
        select new CreditEffect(effect.Id, effect.Amount, effect.RecordedAt, effect.SemesterId, effect.ContributionId);

    internal static Balance Calculate(int userId, int clubId, IReadOnlyList<CreditEffect> effects, ClubCreditWallet? wallet)
    {
        var revision = effects.Count == 0 ? 0 : effects.Max(x => x.Id);
        var earned = effects.Where(x => x.Amount > 0).Sum(x => x.Amount);
        var revoked = -effects.Where(x => x.Amount < 0).Sum(x => x.Amount);
        var spent = wallet?.Spent ?? 0;
        var fingerprint = FormattableString.Invariant($"{Policy}:{userId}:{clubId}:{revision}:{wallet?.Version ?? Guid.Empty:D}");
        var version = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)).AsSpan(0, 16));
        return new(earned, revoked, spent, earned - revoked - spent, revision, version);
    }

    internal static async Task<bool> Reconciles(AdminDbContext db, int userId, int clubId, decimal spent, CancellationToken ct)
    {
        var query = db.Set<ClubRewardRedemption>().Where(x => x.UserId == userId && x.ClubId == clubId);
        var total = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
            ? (await query.Select(x => x.Amount).ToArrayAsync(ct)).Sum()
            : await query.SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        return total == spent;
    }
}
