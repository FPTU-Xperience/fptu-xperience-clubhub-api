using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using ClubReportHub.Shared.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Backend.StabilizationTests;

public sealed class GatewayRateLimitingAndSecurityTests
{
    [Fact]
    public async Task RateLimiter_ExceedingLimit_Returns429WithRetryAfterAndStructuredJson()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = RateLimitingExtensions.CreateRateLimitRejectedHandler();
            options.AddPolicy("test-policy", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitingExtensions.ResolveClientKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 2,
                        Window = TimeSpan.FromSeconds(30),
                        QueueLimit = 0
                    }));
        });

        await using var app = builder.Build();
        app.UseRateLimiter();
        app.MapGet("/limited", () => Results.Ok(new { status = "ok" }))
           .RequireRateLimiting("test-policy");

        await app.StartAsync();
        using var client = app.GetTestClient();

        // First 2 requests should succeed
        var resp1 = await client.GetAsync("/limited");
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

        var resp2 = await client.GetAsync("/limited");
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);

        // 3rd request should be throttled
        var resp3 = await client.GetAsync("/limited");
        Assert.Equal((HttpStatusCode)429, resp3.StatusCode);

        // Assert Retry-After header is present
        Assert.NotNull(resp3.Headers.RetryAfter);
        Assert.True(int.TryParse(resp3.Headers.RetryAfter.ToString(), out var seconds));
        Assert.True(seconds > 0);

        // Assert structured JSON error body
        var content = await resp3.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TooManyRequests", content.GetProperty("error").GetString());
        Assert.True(content.GetProperty("retryAfter").GetInt32() > 0);
    }

    [Fact]
    public async Task RateLimiter_DistinctClientIps_HaveIndependentBuckets()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
            // Trust loopback test client
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Loopback, 8));
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = RateLimitingExtensions.CreateRateLimitRejectedHandler();
            options.AddPolicy("test-ip-policy", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitingExtensions.ResolveClientKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 1,
                        Window = TimeSpan.FromSeconds(30),
                        QueueLimit = 0
                    }));
        });

        await using var app = builder.Build();
        app.UseForwardedHeaders();
        app.UseRateLimiter();
        app.MapGet("/api/test", () => Results.Ok(new { status = "ok" }))
           .RequireRateLimiting("test-ip-policy");

        await app.StartAsync();
        using var client = app.GetTestClient();

        // Client A with IP 198.51.100.1
        using var reqA1 = new HttpRequestMessage(HttpMethod.Get, "/api/test");
        reqA1.Headers.Add("X-Forwarded-For", "198.51.100.1");
        var respA1 = await client.SendAsync(reqA1);
        Assert.Equal(HttpStatusCode.OK, respA1.StatusCode);

        // Client A second request -> throttled
        using var reqA2 = new HttpRequestMessage(HttpMethod.Get, "/api/test");
        reqA2.Headers.Add("X-Forwarded-For", "198.51.100.1");
        var respA2 = await client.SendAsync(reqA2);
        Assert.Equal((HttpStatusCode)429, respA2.StatusCode);

        // Client B with IP 198.51.100.2 -> must succeed despite Client A being exhausted
        using var reqB = new HttpRequestMessage(HttpMethod.Get, "/api/test");
        reqB.Headers.Add("X-Forwarded-For", "198.51.100.2");
        var respB = await client.SendAsync(reqB);
        Assert.Equal(HttpStatusCode.OK, respB.StatusCode);
    }

    [Fact]
    public async Task RateLimiter_DistinctAuthenticatedUsersOnSameIp_HaveIndependentBuckets()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddAuthentication("TestAuth")
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestAuth", _ => { });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = RateLimitingExtensions.CreateRateLimitRejectedHandler();
            options.AddPolicy("test-user-policy", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitingExtensions.ResolveClientKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 1,
                        Window = TimeSpan.FromSeconds(30),
                        QueueLimit = 0
                    }));
        });

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.MapGet("/api/user-test", () => Results.Ok(new { status = "ok" }))
           .RequireRateLimiting("test-user-policy");

        await app.StartAsync();
        using var client = app.GetTestClient();

        // User 1 behind shared NAT
        using var reqUser1A = new HttpRequestMessage(HttpMethod.Get, "/api/user-test");
        reqUser1A.Headers.Add("X-Test-UserId", "user-101");
        var resp1A = await client.SendAsync(reqUser1A);
        Assert.Equal(HttpStatusCode.OK, resp1A.StatusCode);

        // User 1 second request -> throttled
        using var reqUser1B = new HttpRequestMessage(HttpMethod.Get, "/api/user-test");
        reqUser1B.Headers.Add("X-Test-UserId", "user-101");
        var resp1B = await client.SendAsync(reqUser1B);
        Assert.Equal((HttpStatusCode)429, resp1B.StatusCode);

        // User 2 on the same IP -> must succeed
        using var reqUser2 = new HttpRequestMessage(HttpMethod.Get, "/api/user-test");
        reqUser2.Headers.Add("X-Test-UserId", "user-102");
        var resp2 = await client.SendAsync(reqUser2);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
    }

    [Fact]
    public async Task RateLimiter_HealthCheck_IsExemptFromThrottling()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                if (httpContext.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
                {
                    return RateLimitPartition.GetNoLimiter("no-limiter");
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    "global",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 1,
                        Window = TimeSpan.FromSeconds(30),
                        QueueLimit = 0
                    });
            });
        });

        await using var app = builder.Build();
        app.UseRateLimiter();
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
        app.MapGet("/other", () => Results.Ok(new { status = "ok" }));

        await app.StartAsync();
        using var client = app.GetTestClient();

        // Health check should never be blocked even after multiple requests
        for (int i = 0; i < 10; i++)
        {
            var healthResp = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);
        }

        // Other endpoint should be throttled on 2nd request
        var otherResp1 = await client.GetAsync("/other");
        Assert.Equal(HttpStatusCode.OK, otherResp1.StatusCode);

        var otherResp2 = await client.GetAsync("/other");
        Assert.Equal((HttpStatusCode)429, otherResp2.StatusCode);
    }

    [Fact]
    public async Task ForwardedHeaders_UntrustedProxy_DoesNotUpdateRemoteIp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
            // Explicitly only trust 10.0.0.1, NOT the connecting IP 198.51.100.55
            options.KnownProxies.Add(IPAddress.Parse("10.0.0.1"));
        });

        await using var app = builder.Build();

        // In TestServer, simulate socket connection from external untrusted IP
        app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.55");
            return next();
        });

        app.UseForwardedHeaders();

        string? resolvedIp = null;
        app.MapGet("/check-ip", (HttpContext ctx) =>
        {
            resolvedIp = ctx.Connection.RemoteIpAddress?.ToString();
            return Results.Ok();
        });

        await app.StartAsync();
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/check-ip");
        request.Headers.Add("X-Forwarded-For", "203.0.113.199");
        await client.SendAsync(request);

        // Since 198.51.100.55 is not in KnownProxies, ForwardedHeaders must NOT update
        // RemoteIpAddress to the spoofed header 203.0.113.199.
        Assert.Equal("198.51.100.55", resolvedIp);
        Assert.NotEqual("203.0.113.199", resolvedIp);
    }

    [Fact]
    public async Task Transforms_StripsXCombinedReportWorkflowHeader()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        string? receivedHeader = null;

        builder.Services.AddReverseProxy()
            .LoadFromMemory(
                [
                    new Yarp.ReverseProxy.Configuration.RouteConfig
                    {
                        RouteId = "test-route",
                        ClusterId = "test-cluster",
                        Match = new Yarp.ReverseProxy.Configuration.RouteMatch
                        {
                            Path = "/api/{**catch-all}"
                        }
                    }
                ],
                [
                    new Yarp.ReverseProxy.Configuration.ClusterConfig
                    {
                        ClusterId = "test-cluster",
                        Destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>
                        {
                            ["dest1"] = new Yarp.ReverseProxy.Configuration.DestinationConfig
                            {
                                Address = "http://localhost:5999/"
                            }
                        }
                    }
                ])
            .AddTransforms(builderContext =>
            {
                builderContext.AddRequestHeaderRemove("X-Combined-Report-Workflow");
            });

        // Test middleware that runs before proxy to observe the transform effect
        await using var app = builder.Build();

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/simulate-downstream"))
            {
                receivedHeader = context.Request.Headers["X-Combined-Report-Workflow"].ToString();
                context.Response.StatusCode = 200;
                return;
            }
            await next();
        });

        // Apply transform directly to test transform behavior
        var transformBuilder = new TransformBuilderContext();
        transformBuilder.AddRequestHeaderRemove("X-Combined-Report-Workflow");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Combined-Report-Workflow"] = "true";
        httpContext.Request.Headers["Authorization"] = "Bearer test";

        foreach (var action in transformBuilder.RequestTransforms)
        {
            var transformContext = new RequestTransformContext
            {
                HttpContext = httpContext,
                ProxyRequest = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test")
            };
            // Copy request headers to proxy request to simulate YARP pipeline
            transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-Combined-Report-Workflow", "true");
            transformContext.ProxyRequest.Headers.TryAddWithoutValidation("Authorization", "Bearer test");

            await action.ApplyAsync(transformContext);

            Assert.False(transformContext.ProxyRequest.Headers.Contains("X-Combined-Report-Workflow"));
            Assert.True(transformContext.ProxyRequest.Headers.Contains("Authorization"));
        }
    }

    [Fact]
    public void ResolveClientKey_Unauthenticated_ReturnsIpPrefix()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");

        var key = RateLimitingExtensions.ResolveClientKey(context);

        Assert.Equal("ip:192.0.2.1", key);
    }

    [Fact]
    public void ResolveClientKey_Authenticated_ReturnsUserPrefix()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-42")], "TestAuth"));

        var key = RateLimitingExtensions.ResolveClientKey(context);

        Assert.Equal("user:user-42:192.0.2.1", key);
    }

    [Fact]
    public void KpiGrpcService_IsNotExposedToInternetOrGatewayRoutes()
    {
        // 1. Verify yarp.json has no route mapping to kpi-grpc-service
        var yarpJsonPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Gateway", "ApiGateway", "yarp.json");

        if (!File.Exists(yarpJsonPath))
        {
            yarpJsonPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "Gateway", "ApiGateway", "yarp.json");
        }

        Assert.True(File.Exists(yarpJsonPath), $"yarp.json not found at {yarpJsonPath}");
        var json = File.ReadAllText(yarpJsonPath);
        var doc = JsonDocument.Parse(json);
        var routes = doc.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");

        foreach (var route in routes.EnumerateObject())
        {
            if (route.Value.TryGetProperty("ClusterId", out var clusterId))
            {
                Assert.NotEqual("kpi-grpc-service", clusterId.GetString());
            }
        }

        // 2. Verify docker-compose.yml does not expose kpi-grpc-service ports to host
        var dockerComposePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docker-compose.yml");
        if (!File.Exists(dockerComposePath))
        {
            dockerComposePath = Path.Combine(Directory.GetCurrentDirectory(), "docker-compose.yml");
        }

        Assert.True(File.Exists(dockerComposePath), $"docker-compose.yml not found at {dockerComposePath}");
        var dockerComposeText = File.ReadAllText(dockerComposePath);

        // Find kpi-grpc-service section
        var kpiSectionIndex = dockerComposeText.IndexOf("kpi-grpc-service:", StringComparison.Ordinal);
        Assert.True(kpiSectionIndex >= 0);

        var nextServiceIndex = dockerComposeText.IndexOf("auth-service:", kpiSectionIndex, StringComparison.Ordinal);
        var kpiBlock = dockerComposeText.Substring(kpiSectionIndex, nextServiceIndex - kpiSectionIndex);

        Assert.DoesNotContain("ports:", kpiBlock);
        Assert.Contains("internal-net", kpiBlock);
    }
}

internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Request.Headers["X-Test-UserId"].ToString();
        if (string.IsNullOrEmpty(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Email, $"{userId}@example.edu")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestAuth");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
