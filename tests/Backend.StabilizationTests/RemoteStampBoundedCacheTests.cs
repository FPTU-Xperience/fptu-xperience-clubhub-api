using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using ClubReportHub.Shared.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backend.StabilizationTests;

public sealed class RemoteStampBoundedCacheTests
{
    [Fact]
    public async Task BoundedCache_PreservesRevocationChecks_AndCachesRemoteStatus()
    {
        var requests = 0;
        using var handler = new StatusHandler(() => requests++);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://auth.example.invalid") };
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });
        var validator = new RemoteUserSecurityStampValidator(http, cache, NullLogger<RemoteUserSecurityStampValidator>.Instance);
        var revoked = new ClaimsPrincipal(new ClaimsIdentity([new Claim("ver", "1"), new Claim(ClaimTypes.Role, AuthRoles.ClubMember)]));
        Assert.False(await validator.ValidateSecurityStampAsync(10, revoked));
        Assert.False(await validator.ValidateSecurityStampAsync(10, revoked));
        Assert.Equal(1, requests);
    }

    private sealed class StatusHandler(Action count) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            count();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new RemoteUserStatus(10, true, false, 2, [AuthRoles.ClubMember]))
            });
        }
    }
}
