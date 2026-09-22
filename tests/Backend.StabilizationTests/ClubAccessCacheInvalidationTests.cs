using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using ClubReportHub.Shared.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.StabilizationTests;

public sealed class ClubAccessCacheInvalidationTests
{
    private const string SigningKey = "test-only-signing-key-at-least-32-characters";

    private static string CreateToken(int userId)
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = SigningKey,
            ExpirationMinutes = 60
        });
        return new JwtTokenFactory(options).CreateToken(userId, $"user{userId}", $"User {userId}", ["User"]).AccessToken;
    }

    private sealed class DelegatingMockHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handlerFunc(request));
        }
    }

    [Fact]
    public async Task GetMyAccessAsync_CachesResult_SecondCallDoesNotHitHttp()
    {
        var fetchCount = 0;
        var handler = new DelegatingMockHandler(_ =>
        {
            Interlocked.Increment(ref fetchCount);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<ClubAccessSnapshot>
                {
                    new(101, "Coding Club", true, false, true, [42])
                })
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var client = new ClubAccessClient(httpClient, cache, NullLogger<ClubAccessClient>.Instance);

        var token = CreateToken(42);

        // First call: fetches from HTTP
        var first = await client.GetMyAccessAsync(token);
        Assert.Single(first);
        Assert.Equal(1, fetchCount);

        // Second call: served from cache
        var second = await client.GetMyAccessAsync(token);
        Assert.Single(second);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task InvalidateCache_RemovesCachedAccess_SubsequentCallRefetches()
    {
        var fetchCount = 0;
        var handler = new DelegatingMockHandler(_ =>
        {
            Interlocked.Increment(ref fetchCount);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<ClubAccessSnapshot>
                {
                    new(101, "Coding Club", fetchCount == 1, false, true, [42])
                })
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var client = new ClubAccessClient(httpClient, cache, NullLogger<ClubAccessClient>.Instance);

        var token = CreateToken(42);

        var first = await client.GetMyAccessAsync(token);
        Assert.True(first[0].IsManager);
        Assert.Equal(1, fetchCount);

        // Invalidate user cache (simulating membership revocation / manager change)
        client.InvalidateCache(42);

        // Next call should fetch fresh data from server
        var second = await client.GetMyAccessAsync(token);
        Assert.False(second[0].IsManager);
        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public async Task InvalidateUsers_PurgesMultipleUsersFromCache()
    {
        var fetchCount = 0;
        var handler = new DelegatingMockHandler(_ =>
        {
            Interlocked.Increment(ref fetchCount);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<ClubAccessSnapshot>
                {
                    new(101, "Coding Club", true, false, true, [1, 2])
                })
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var client = new ClubAccessClient(httpClient, cache, NullLogger<ClubAccessClient>.Instance);

        var token1 = CreateToken(1);
        var token2 = CreateToken(2);

        await client.GetMyAccessAsync(token1);
        await client.GetMyAccessAsync(token2);
        Assert.Equal(2, fetchCount);

        // Cached
        await client.GetMyAccessAsync(token1);
        await client.GetMyAccessAsync(token2);
        Assert.Equal(2, fetchCount);

        // Bulk invalidation
        client.InvalidateUsers([1, 2]);

        // Both refetched
        await client.GetMyAccessAsync(token1);
        await client.GetMyAccessAsync(token2);
        Assert.Equal(4, fetchCount);
    }

    [Fact]
    public async Task BypassCache_ForcesFreshFetchEvenWhenCached()
    {
        var fetchCount = 0;
        var handler = new DelegatingMockHandler(_ =>
        {
            var current = Interlocked.Increment(ref fetchCount);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<ClubAccessSnapshot>
                {
                    new(101, "Coding Club", current == 1, false, true, [42])
                })
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var client = new ClubAccessClient(httpClient, cache, NullLogger<ClubAccessClient>.Instance);

        var token = CreateToken(42);

        // Populate cache with IsManager = true
        var first = await client.GetMyAccessAsync(token);
        Assert.True(first[0].IsManager);
        Assert.Equal(1, fetchCount);

        // Privileged mutation revalidates with bypassCache: true
        var second = await client.GetMyAccessAsync(token, bypassCache: true);
        Assert.False(second[0].IsManager);
        Assert.Equal(2, fetchCount);

        // Subsequent normal call uses updated cache
        var third = await client.GetMyAccessAsync(token);
        Assert.False(third[0].IsManager);
        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public async Task NegativeCaching_EmptyResultsAreCached_PreventsExcessiveHttpHits()
    {
        var fetchCount = 0;
        var handler = new DelegatingMockHandler(_ =>
        {
            Interlocked.Increment(ref fetchCount);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<ClubAccessSnapshot>()) // Empty: user belongs to no clubs
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var client = new ClubAccessClient(httpClient, cache, NullLogger<ClubAccessClient>.Instance);

        var token = CreateToken(999);

        // First call: hits HTTP
        var first = await client.GetMyAccessAsync(token);
        Assert.Empty(first);
        Assert.Equal(1, fetchCount);

        // Second call: served from negative cache, no HTTP hit!
        var second = await client.GetMyAccessAsync(token);
        Assert.Empty(second);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task StampedeProtection_ConcurrentRequestsOnCacheMiss_TriggerOnlyOneHttpFetch()
    {
        var fetchCount = 0;
        var handler = new DelegatingMockHandler(_ =>
        {
            Interlocked.Increment(ref fetchCount);
            // Simulate 50ms network latency
            Thread.Sleep(50);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<ClubAccessSnapshot>
                {
                    new(101, "Coding Club", true, false, true, [42])
                })
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var client = new ClubAccessClient(httpClient, cache, NullLogger<ClubAccessClient>.Instance);

        var token = CreateToken(42);

        // Launch 20 concurrent requests on a cold cache
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => client.GetMyAccessAsync(token))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // All 20 callers must receive the exact same valid result
        foreach (var result in results)
        {
            Assert.Single(result);
            Assert.Equal(101, result[0].ClubId);
        }

        // Cache stampede protection must guarantee only 1 HTTP fetch occurred
        Assert.Equal(1, fetchCount);
    }
}
