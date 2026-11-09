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

        var oldToken = await GetRefreshTokenAsync(rawRefreshToken);

        if (oldToken is null)
        {
            return new RotateRefreshTokenResult(RefreshTokenStatus.Invalid);
        }

        if (!oldToken.User.IsActive || oldToken.User.IsLocked || !ActorAccountPolicy.HasValidActorConfiguration(oldToken.User))
        {
            return new RotateRefreshTokenResult(RefreshTokenStatus.AccountNotAllowed, User: oldToken.User);
        }

        if (oldToken.IsExpired)
        {
            return new RotateRefreshTokenResult(RefreshTokenStatus.Expired, User: oldToken.User);
        }

        if (oldToken.IsRevoked)
        {
            var activeExpiries = await _db.RefreshTokens.AsNoTracking()
                .Where(x => x.UserId == oldToken.UserId && x.FamilyId == oldToken.FamilyId && x.RevokedAtUtc == null)
                .Select(x => x.ExpiresAtUtc).ToListAsync();
            if (oldToken.ReplacedByToken is null || !activeExpiries.Any(expiry => expiry > DateTimeOffset.UtcNow))
                return new RotateRefreshTokenResult(RefreshTokenStatus.Invalid, User: oldToken.User);
            var revokedAt = oldToken.RevokedAtUtc ?? DateTimeOffset.MinValue;
            var elapsed = DateTimeOffset.UtcNow - revokedAt;

            if (elapsed <= TimeSpan.FromSeconds(RotationGracePeriodSeconds))
            {
                // A concurrent request on this same instance may have just completed rotation.
                // Give the winner a short window to populate the cache if it hasn't yet:
                for (var attempt = 0; attempt < 30; attempt++)
                {
                    var concurrentWinner = await GetActiveCachedRotationAsync(cacheKey);
                    if (concurrentWinner is not null)
                    {
                        return new RotateRefreshTokenResult(RefreshTokenStatus.Success, Response: concurrentWinner);
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(50));
                }

                // If not in cache after wait, the replacement is stored only as a hash and must never
                // be returned as a bearer credential. The client must use the winner's response.
                return new RotateRefreshTokenResult(RefreshTokenStatus.Invalid, User: oldToken.User);
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

            var response = CreateAuthResponse(oldToken.User, rotatedToken, newRawToken);
            _cache.Set(cacheKey, response, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(RotationGracePeriodSeconds),
                Size = 1
            });

            return new RotateRefreshTokenResult(RefreshTokenStatus.Success, Response: response, NewToken: rotatedToken, User: oldToken.User);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another concurrent request committed the rotation at this exact instant!
            // Give the winning request a short window to populate this process-local cache.
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var concurrentResponse = await GetActiveCachedRotationAsync(cacheKey);
                if (concurrentResponse is not null)
                {
                    return new RotateRefreshTokenResult(RefreshTokenStatus.Success, Response: concurrentResponse);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            return new RotateRefreshTokenResult(RefreshTokenStatus.Invalid, User: oldToken.User);
        }
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

    private async Task<AuthResponse?> GetActiveCachedRotationAsync(string cacheKey)
    {
        if (!_cache.TryGetValue(cacheKey, out AuthResponse? response) || response?.RefreshToken is null) return null;
        var hash = HashToken(response.RefreshToken);
        var token = await _db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(x => x.Token == hash);
        return token is not null && token.IsActive ? response : null;
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
