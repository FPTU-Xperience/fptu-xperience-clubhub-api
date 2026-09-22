using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using ClubReportHub.Shared.Tracing;

namespace ClubReportHub.Shared.Auth;

public sealed record ClubAccessSnapshot(
    int ClubId,
    string ClubName,
    bool IsManager,
    bool IsTreasurer,
    bool IsApprovedMember,
    IReadOnlyCollection<int> ManagerUserIds,
    IReadOnlyCollection<int>? MemberUserIds = null,
    IReadOnlyCollection<int>? TreasurerUserIds = null)
{
    public bool CanManage => IsManager;
    public bool CanManageFinance => IsTreasurer;
    public bool CanView => IsManager || IsApprovedMember;
}

public sealed class ClubAccessClient(
    HttpClient httpClient,
    IMemoryCache cache,
    ILogger<ClubAccessClient> logger)
{
    // Reduced TTLs for authorization freshness (SEC-F07)
    private static readonly TimeSpan ActiveSlidingExpiration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ActiveAbsoluteExpiration = TimeSpan.FromMinutes(3);
    // Negative caching duration for users without club access (PERF-F04)
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromMinutes(1);

    // Cache stampede protection (PERF-F04)
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _userLocks = new();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<ClubAccessSnapshot>> GetMyAccessAsync(
        string bearerToken,
        CancellationToken cancellationToken = default,
        bool bypassCache = false)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            return [];
        }

        // Extract user ID from token for cache key
        var userId = ExtractUserIdFromToken(bearerToken);
        if (userId <= 0)
        {
            return [];
        }

        var cacheKey = GetCacheKey(userId);

        // Try to get from cache first (if not bypassing cache)
        if (!bypassCache && cache.TryGetValue(cacheKey, out IReadOnlyList<ClubAccessSnapshot>? cachedAccess) && cachedAccess is not null)
        {
            return cachedAccess;
        }

        // Stampede protection: lock per userId
        var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            // Double-checked locking
            if (!bypassCache && cache.TryGetValue(cacheKey, out cachedAccess) && cachedAccess is not null)
            {
                return cachedAccess;
            }

            // Fetch from API
            var access = await FetchAccessFromApiAsync(bearerToken, cancellationToken);

            if (access.Count > 0)
            {
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(ActiveSlidingExpiration)
                    .SetAbsoluteExpiration(ActiveAbsoluteExpiration)
                    .SetSize(1)
                    .SetPriority(CacheItemPriority.Normal);
                cache.Set(cacheKey, access, cacheOptions);
            }
            else
            {
                // PERF-F04: Negative caching for users with no club access
                var negativeOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(NegativeCacheDuration)
                    .SetSize(1)
                    .SetPriority(CacheItemPriority.Low);
                cache.Set(cacheKey, access, negativeOptions);
            }

            return access;
        }
        finally
        {
            userLock.Release();
        }
    }

    public void InvalidateCache(int userId)
    {
        cache.Remove(GetCacheKey(userId));
        // Also remove legacy key format for backward compatibility
        cache.Remove($"ClubAccess_{userId}");
    }

    public void InvalidateUsers(IEnumerable<int> userIds)
    {
        foreach (var userId in userIds)
        {
            InvalidateCache(userId);
        }
    }

    public void InvalidateCacheForAllUsers()
    {
        // For distributed deployment, events trigger per-user eviction
    }

    private static string GetCacheKey(int userId) => $"club_access:user:{userId}";

    private async Task<IReadOnlyList<ClubAccessSnapshot>> FetchAccessFromApiAsync(
        string bearerToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/clubs/me/access");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Club access lookup failed with status code {StatusCode}.", response.StatusCode);
                return [];
            }

            return await response.Content.ReadFromJsonAsync<List<ClubAccessSnapshot>>(JsonOptions, cancellationToken) ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException
            or TaskCanceledException
            or JsonException
            or BrokenCircuitException
            or TimeoutRejectedException
            or RateLimiterRejectedException)
        {
            logger.LogWarning(ex, "Club access lookup failed.");
            return [];
        }
    }

    private static int ExtractUserIdFromToken(string bearerToken)
    {
        try
        {
            // Parse JWT without validation to get user ID for cache key
            var parts = bearerToken.Split('.');
            if (parts.Length != 3)
            {
                return 0;
            }

            var payload = parts[1];
            // Add padding if needed for Base64 decoding
            var padded = payload.Length % 4 == 0 ? payload : payload + new string('=', 4 - payload.Length % 4);
            // Replace URL-safe characters
            padded = padded.Replace('-', '+').Replace('_', '/');
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("sub", out var subElement) && int.TryParse(subElement.GetString(), out var subId))
            {
                return subId;
            }
            if (doc.RootElement.TryGetProperty("nameid", out var nameidElement) && int.TryParse(nameidElement.GetString(), out var nameId))
            {
                return nameId;
            }
            if (doc.RootElement.TryGetProperty("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier", out var uriElement) && int.TryParse(uriElement.GetString(), out var uriId))
            {
                return uriId;
            }
            return 0;
        }
        catch
        {
            return 0;
        }
    }
}

public static class ClubAccessServiceCollectionExtensions
{
    public static IServiceCollection AddClubAccessClient(this IServiceCollection services, IConfiguration configuration)
    {
        // Add memory cache if not already registered
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 10000; // Max 10000 entries
            options.ExpirationScanFrequency = TimeSpan.FromMinutes(5);
        });

        services.AddHttpClient<ClubAccessClient>(client =>
        {
            var baseUrl = configuration["Services:ClubService:BaseUrl"] ?? "http://localhost:5102";
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddCorrelationIdForwarding().AddStandardResilienceHandler();

        return services;
    }
}

public static class ClubAccessHttpContextExtensions
{
    public static string GetBearerToken(this HttpContext httpContext)
    {
        var authorization = httpContext.Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        return authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[bearerPrefix.Length..].Trim()
            : string.Empty;
    }
}
