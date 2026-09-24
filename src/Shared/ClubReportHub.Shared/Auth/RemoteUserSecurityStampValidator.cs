using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ClubReportHub.Shared.Tracing;

namespace ClubReportHub.Shared.Auth;

public sealed class RemoteUserSecurityStampValidator(
    HttpClient httpClient,
    IMemoryCache cache,
    ILogger<RemoteUserSecurityStampValidator> logger) : IUserSecurityStampValidator
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public async Task<bool> ValidateSecurityStampAsync(
        int userId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = $"remote_sec_stamp:user:{userId}";
            if (!cache.TryGetValue(cacheKey, out RemoteUserStatus? status))
            {
                var response = await httpClient.GetAsync($"/api/auth/user-status/{userId}", cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    status = new RemoteUserStatus(userId, false, true, 0, []);
                }
                else if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Remote user status check returned {StatusCode} for user {UserId}. Gracefully allowing token.",
                        response.StatusCode,
                        userId);
                    return true;
                }
                else
                {
                    status = await response.Content.ReadFromJsonAsync<RemoteUserStatus>(cancellationToken: cancellationToken);
                }

                if (status != null)
                {
                    cache.Set(cacheKey, status, CacheDuration);
                }
            }

            if (status == null || !status.IsActive || status.IsLocked)
            {
                logger.LogWarning(
                    "Remote security stamp check rejected user {UserId}: active={IsActive}, locked={IsLocked}",
                    userId,
                    status?.IsActive,
                    status?.IsLocked);
                return false;
            }

            var verClaim = principal.FindFirst("ver")?.Value;
            var tokenVer = int.TryParse(verClaim, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedVer)
                ? parsedVer
                : 1;

            if (tokenVer < status.SecurityVersion)
            {
                logger.LogWarning(
                    "Remote security stamp version mismatch for user {UserId}: token ver {TokenVer} < remote ver {RemoteVer}",
                    userId,
                    tokenVer,
                    status.SecurityVersion);
                return false;
            }

            var tokenRoles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value);
            var activeRoles = new HashSet<string>(status.Roles, StringComparer.OrdinalIgnoreCase);
            foreach (var role in tokenRoles)
            {
                if (!activeRoles.Contains(role))
                {
                    logger.LogWarning("Remote security stamp rejected user {UserId}: role {Role} revoked", userId, role);
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to check remote user status for user {UserId}. Gracefully allowing token.",
                userId);
            return true;
        }
    }
}

public sealed record RemoteUserStatus(
    int UserId,
    bool IsActive,
    bool IsLocked,
    int SecurityVersion,
    string[] Roles);

public static class RemoteSecurityStampExtensions
{
    public static IServiceCollection AddRemoteUserSecurityStampValidator(
        this IServiceCollection services,
        Uri authServiceBaseUrl)
    {
        services.AddMemoryCache();
        services.AddClubReportTracing();
        services.AddHttpClient<IUserSecurityStampValidator, RemoteUserSecurityStampValidator>(client =>
        {
            client.BaseAddress = authServiceBaseUrl;
            client.Timeout = TimeSpan.FromSeconds(5);
        }).AddCorrelationIdForwarding().AddStandardResilienceHandler();
        return services;
    }
}
