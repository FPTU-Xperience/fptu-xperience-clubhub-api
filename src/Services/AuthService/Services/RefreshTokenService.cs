using System.Security.Cryptography;
using System.Text;
using System.Data;
using AuthService.Contracts;
using AuthService.Data;
using AuthService.Models;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AuthService.Services;

public enum RefreshTokenStatus
{
    Success,
    Invalid,
    Expired,
    RevokedWithinGracePeriod,
    CompromisedFamilyRevoked,
    AccountNotAllowed
}

public sealed record RotateRefreshTokenResult(
    RefreshTokenStatus Status,
    AuthResponse? Response = null,
    RefreshToken? NewToken = null,
    User? User = null);

public sealed class RefreshTokenService
{
    private readonly AuthDbContext _db;
    private readonly JwtTokenFactory _tokenFactory;
    private readonly JwtOptions _jwtOptions;
    private readonly IMemoryCache _cache;

    public const int RotationGracePeriodSeconds = 30;

    private sealed record CachedRotation(AuthResponse Response, int SecurityVersion, string FamilyId, string ReplacementTokenHash);

    public RefreshTokenService(
        AuthDbContext db,
        JwtTokenFactory tokenFactory,
        IOptions<JwtOptions> jwtOptions,
        IMemoryCache cache)
    {
        _db = db;
        _tokenFactory = tokenFactory;
        _jwtOptions = jwtOptions.Value;
        _cache = cache;
    }

    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async Task<RefreshToken> CreateRefreshTokenAsync(int userId)
    {
        var rawToken = GenerateSecureToken();
        var tokenHash = HashToken(rawToken);
        var familyId = Guid.NewGuid().ToString("N");

        var refreshToken = new RefreshToken
        {
            UserId = userId,
            Token = tokenHash,
            RawToken = rawToken,
            FamilyId = familyId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays)
        };

        _db.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync();

        return refreshToken;
    }

    public async Task<RefreshToken?> GetRefreshTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = HashToken(token);
        var query = _db.RefreshTokens
            .Include(x => x.User)
            .ThenInclude(x => x.UserRoles)
            .ThenInclude(x => x.Role);

        var refreshToken = await query.FirstOrDefaultAsync(x => x.Token == hash);
        if (refreshToken is null && !IsSha256HashFormat(token))
        {
            refreshToken = await query.FirstOrDefaultAsync(x => x.Token == token);
        }

        return refreshToken;
    }

    public async Task<RotateRefreshTokenResult> RotateRefreshTokenAsync(
        string rawRefreshToken,
        string? clientIp)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return new RotateRefreshTokenResult(RefreshTokenStatus.Invalid);
        }

        var tokenHash = HashToken(rawRefreshToken);
        var cacheKey = $"rotated_refresh:{tokenHash}";

        // A parallel tab can reuse the winning raw pair only while its session
        // and account still exist in their current authorized state.
        var cached = await TryGetCachedRotationAsync(cacheKey);
        if (cached is not null) return cached;

        var oldToken = await GetRefreshTokenAsync(rawRefreshToken);

        if (oldToken is null)
        {
            return new RotateRefreshTokenResult(RefreshTokenStatus.Invalid);
        }

        var currentUser = await _db.Users.AsNoTracking()
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == oldToken.UserId);
        if (currentUser is null || !CanIssueTokens(currentUser))
        {
            return new RotateRefreshTokenResult(RefreshTokenStatus.AccountNotAllowed, User: currentUser);
        }

        if (oldToken.IsExpired)
        {
            return new RotateRefreshTokenResult(RefreshTokenStatus.Expired, User: oldToken.User);
        }

        if (oldToken.IsRevoked)
        {
            var revokedAt = oldToken.RevokedAtUtc ?? DateTimeOffset.MinValue;
            var elapsed = DateTimeOffset.UtcNow - revokedAt;

            if (elapsed <= TimeSpan.FromSeconds(RotationGracePeriodSeconds))
            {
                // If not in cache after wait, the replacement is stored only as a hash and must never
                // be returned as a bearer credential. The client must use the winner's response.
                return await WaitForCachedRotationAsync(cacheKey)
                    ?? new RotateRefreshTokenResult(RefreshTokenStatus.Invalid, User: currentUser);
            }

            // Outside grace period: token reuse / replay detected! Revoke entire family!
            await RevokeFamilyAsync(oldToken.FamilyId, clientIp);
            return new RotateRefreshTokenResult(RefreshTokenStatus.CompromisedFamilyRevoked, User: oldToken.User);
        }

        // Generate new token
        var newRawToken = GenerateSecureToken();
        var newTokenHash = HashToken(newRawToken);
        var now = DateTimeOffset.UtcNow;

        // Revoke old token
        oldToken.RevokedAtUtc = now;
        oldToken.RevokedByIp = clientIp;
        oldToken.ReplacedByToken = newTokenHash;

        var rotatedToken = new RefreshToken
        {
            UserId = oldToken.UserId,
            Token = newTokenHash,
            RawToken = newRawToken,
            FamilyId = oldToken.FamilyId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(_jwtOptions.RefreshTokenExpirationDays)
        };

        _db.RefreshTokens.Add(rotatedToken);

        try
        {
            await _db.SaveChangesAsync();

            var response = CreateAuthResponse(currentUser, rotatedToken, newRawToken);
            _cache.Set(cacheKey, new CachedRotation(response, currentUser.SecurityVersion, oldToken.FamilyId, newTokenHash),
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(RotationGracePeriodSeconds),
                    Size = 1
                });

            return new RotateRefreshTokenResult(RefreshTokenStatus.Success, Response: response, NewToken: rotatedToken, User: currentUser);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await WaitForCachedRotationAsync(cacheKey)
                ?? new RotateRefreshTokenResult(RefreshTokenStatus.Invalid, User: currentUser);
        }
    }

    private static bool CanIssueTokens(User user) =>
        user.IsActive && !user.IsLocked && ActorAccountPolicy.HasValidActorConfiguration(user);

    private async Task<RotateRefreshTokenResult?> TryGetCachedRotationAsync(string cacheKey)
    {
        if (!_cache.TryGetValue(cacheKey, out CachedRotation? cached) || cached is null) return null;
        var replacement = await _db.RefreshTokens.AsNoTracking()
            .Include(x => x.User).ThenInclude(x => x.UserRoles).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Token == cached.ReplacementTokenHash);
        if (replacement is null || replacement.UserId != cached.Response.User.Id
            || replacement.FamilyId != cached.FamilyId || !replacement.IsActive)
        {
            _cache.Remove(cacheKey);
            return new RotateRefreshTokenResult(RefreshTokenStatus.Invalid);
        }
        var user = replacement.User;
        if (!CanIssueTokens(user))
        {
            _cache.Remove(cacheKey);
            return new RotateRefreshTokenResult(RefreshTokenStatus.AccountNotAllowed, User: user);
        }
        var roles = user.UserRoles.Select(x => x.Role.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (user.SecurityVersion != cached.SecurityVersion || !roles.SetEquals(cached.Response.User.Roles)
            || !string.Equals(user.CampusCode, cached.Response.User.CampusCode, StringComparison.Ordinal))
        {
            _cache.Remove(cacheKey);
            return new RotateRefreshTokenResult(RefreshTokenStatus.Invalid);
        }
        return new RotateRefreshTokenResult(RefreshTokenStatus.Success, Response: cached.Response);
    }

    private async Task<RotateRefreshTokenResult?> WaitForCachedRotationAsync(string cacheKey)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var result = await TryGetCachedRotationAsync(cacheKey);
            if (result is not null) return result;
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
        return null;
    }

    public async Task<RefreshToken?> RotateRefreshTokenAsync(
        RefreshToken oldToken,
        string? clientIp)
    {
        if (!oldToken.IsActive)
        {
            return null;
        }

        var newRawToken = GenerateSecureToken();
        var newTokenHash = HashToken(newRawToken);

        oldToken.RevokedAtUtc = DateTimeOffset.UtcNow;
        oldToken.RevokedByIp = clientIp;
        oldToken.ReplacedByToken = newTokenHash;

        var rotatedToken = new RefreshToken
        {
            UserId = oldToken.UserId,
            Token = newTokenHash,
            RawToken = newRawToken,
            FamilyId = oldToken.FamilyId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays)
        };

        _db.RefreshTokens.Add(rotatedToken);
        await _db.SaveChangesAsync();

        return rotatedToken;
    }

    public async Task RevokeFamilyAsync(string familyId, string? clientIp)
    {
        await using var transaction = _db.Database.IsRelational() && _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
        var tokens = await FamilyForUpdate(_db, familyId).ToListAsync();

        foreach (var token in tokens) _cache.Remove($"rotated_refresh:{token.Token}");
        if (_db.Database.IsRelational())
        {
            var revokedAt = DateTimeOffset.UtcNow;
            await _db.RefreshTokens.Where(x => x.FamilyId == familyId && x.RevokedAtUtc == null)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAtUtc, revokedAt)
                    .SetProperty(x => x.RevokedByIp, clientIp));
        }
        else
        {
            foreach (var token in tokens)
            {
                if (token.IsRevoked) continue;
                token.RevokedAtUtc = DateTimeOffset.UtcNow;
                token.RevokedByIp = clientIp;
            }
        }

        if (!_db.Database.IsRelational()) await _db.SaveChangesAsync();
        if (transaction is not null) await transaction.CommitAsync();
    }

    public async Task RevokeForUserAsync(int userId, string? clientIp = null)
    {
        var tokens = await _db.RefreshTokens
            .Where(x => x.UserId == userId)
            .ToListAsync();

        foreach (var token in tokens)
        {
            _cache.Remove($"rotated_refresh:{token.Token}");
            if (token.IsRevoked) continue;
            token.RevokedAtUtc = DateTimeOffset.UtcNow;
            token.RevokedByIp = clientIp;
        }

        await _db.SaveChangesAsync();
    }

    public async Task CleanupExpiredTokensAsync(int daysOld = 30)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-daysOld);
        var expiredTokens = await _db.RefreshTokens
            .Where(x => x.ExpiresAtUtc < cutoff && x.RevokedAtUtc != null)
            .ToListAsync();

        if (expiredTokens.Count > 0)
        {
            _db.RefreshTokens.RemoveRange(expiredTokens);
            await _db.SaveChangesAsync();
        }
    }

    public AuthResponse CreateAuthResponse(
        User user,
        RefreshToken refreshToken,
        string? rawRefreshToken = null,
        IEnumerable<string>? withRoles = null)
    {
        var roles = withRoles?.ToArray() ?? user.UserRoles.Select(x => x.Role.Name).OrderBy(x => x).ToArray();
        var token = _tokenFactory.CreateToken(user.Id, user.Username, user.FullName, roles, user.SecurityVersion, user.CampusCode);

        var clientRefreshToken = rawRefreshToken ?? refreshToken.RawToken ?? refreshToken.Token;

        return new AuthResponse(
            token.AccessToken,
            token.ExpiresAtUtc,
            new UserSummary(
                user.Id,
                user.Username,
                user.FullName,
                user.Email,
                roles,
                user.IsActive,
                user.IsLocked,
                user.CampusCode),
            clientRefreshToken,
            refreshToken.ExpiresAtUtc);
    }

    private static string GenerateSecureToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    // Call inside a serializable transaction. Update locks serialize grant creation/revocation
    // without the shared-range-lock upgrade deadlocks caused by two simultaneous redemptions.
    internal static IQueryable<RefreshToken> FamilyForUpdate(AuthDbContext db, string familyId) =>
        db.Database.IsSqlServer()
            ? db.RefreshTokens.FromSqlInterpolated($"SELECT * FROM [RefreshTokens] WITH (UPDLOCK, HOLDLOCK) WHERE [FamilyId] = {familyId}")
            : db.RefreshTokens.Where(x => x.FamilyId == familyId);

    private static bool IsSha256HashFormat(string value) =>
        value.Length == 64 && value.All(char.IsAsciiHexDigit);
}
