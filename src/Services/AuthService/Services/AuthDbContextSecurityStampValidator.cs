using System.Globalization;
using System.Security.Claims;
using AuthService.Data;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AuthService.Services;

public sealed class AuthDbContextSecurityStampValidator(
    AuthDbContext db,
    IMemoryCache cache,
    ILogger<AuthDbContextSecurityStampValidator> logger) : IUserSecurityStampValidator
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    public async Task<bool> ValidateSecurityStampAsync(
        int userId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = $"sec_stamp:user:{userId}";
            if (!cache.TryGetValue(cacheKey, out CachedUserSecurityStamp? stamp))
            {
                var user = await db.Users
                    .AsNoTracking()
                    .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

                if (user is null)
                {
                    stamp = new CachedUserSecurityStamp(
                        Exists: false,
                        IsActive: false,
                        IsLocked: true,
                        SecurityVersion: 0,
                        Roles: []);
                }
                else
                {
                    var roles = user.UserRoles
                        .Select(r => r.Role.Name)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    stamp = new CachedUserSecurityStamp(
                        Exists: true,
                        IsActive: user.IsActive,
                        IsLocked: user.IsLocked,
                        SecurityVersion: user.SecurityVersion,
                        Roles: roles);
                }

                cache.Set(cacheKey, stamp, CacheDuration);
            }

            if (stamp is null || !stamp.Exists || !stamp.IsActive || stamp.IsLocked)
            {
                logger.LogWarning(
                    "Security stamp validation rejected user {UserId}: exists={Exists}, active={IsActive}, locked={IsLocked}",
                    userId,
                    stamp?.Exists,
                    stamp?.IsActive,
                    stamp?.IsLocked);
                return false;
            }

            // Check security version
            var verClaim = principal.FindFirst("ver")?.Value;
            var tokenVer = int.TryParse(verClaim, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedVer)
                ? parsedVer
                : 1;

            if (tokenVer < stamp.SecurityVersion)
            {
                logger.LogWarning(
                    "Security stamp version mismatch for user {UserId}: token ver {TokenVer} < db ver {DbVer}",
                    userId,
                    tokenVer,
                    stamp.SecurityVersion);
                return false;
            }

            // Check roles: token roles must still be granted to the user
            var tokenRoles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value);
            foreach (var role in tokenRoles)
            {
                if (!stamp.Roles.Contains(role))
                {
                    logger.LogWarning("Security stamp rejected user {UserId}: role {Role} revoked", userId, role);
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error validating security stamp for user {UserId}", userId);
            return false;
        }
    }
}

public sealed record CachedUserSecurityStamp(
    bool Exists,
    bool IsActive,
    bool IsLocked,
    int SecurityVersion,
    HashSet<string> Roles);
