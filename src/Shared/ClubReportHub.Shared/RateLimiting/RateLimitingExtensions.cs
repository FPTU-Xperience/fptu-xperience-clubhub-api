using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClubReportHub.Shared.RateLimiting;

public static class RateLimitingExtensions
{
    public static string ResolveClientKey(HttpContext context)
    {
        var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? context.User?.FindFirst(ClaimTypes.Email)?.Value;

        var ip = ResolveClientIp(context);

        if (!string.IsNullOrWhiteSpace(userId))
        {
            return $"user:{userId}:{ip}";
        }

        return $"ip:{ip}";
    }

    public static string ResolveClientIp(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(remoteIp))
        {
            return "unknown";
        }

        return remoteIp;
    }

    public static Func<OnRejectedContext, CancellationToken, ValueTask> CreateRateLimitRejectedHandler()
    {
        return async (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.ContentType = "application/json";

            var retryAfterSeconds = 60;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterSpan))
            {
                retryAfterSeconds = Math.Max(1, (int)retryAfterSpan.TotalSeconds);
            }
            context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();

            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                error = "TooManyRequests",
                message = "Too many requests. Please try again later.",
                retryAfter = retryAfterSeconds
            }, cancellationToken: cancellationToken);
        };
    }

    public static IServiceCollection ConfigureTrustedForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            var knownNetworksConfig = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>();
            if (knownNetworksConfig is { Length: > 0 })
            {
                options.KnownNetworks.Clear();
                foreach (var cidr in knownNetworksConfig)
                {
                    if (TryParseCidr(cidr, out var network))
                    {
                        options.KnownNetworks.Add(network);
                    }
                }
            }
            else
            {
                // Default trusted internal ranges for local / Docker Compose private networks
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Loopback, 8));
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("192.168.0.0"), 16));
            }

            var knownProxiesConfig = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
            if (knownProxiesConfig is { Length: > 0 })
            {
                foreach (var ipStr in knownProxiesConfig)
                {
                    if (IPAddress.TryParse(ipStr, out var ip))
                    {
                        options.KnownProxies.Add(ip);
                    }
                }
            }
        });

        return services;
    }

    private static bool TryParseCidr(string cidr, out Microsoft.AspNetCore.HttpOverrides.IPNetwork network)
    {
        network = default!;
        if (string.IsNullOrWhiteSpace(cidr))
        {
            return false;
        }

        var parts = cidr.Trim().Split('/');
        if (parts.Length != 2)
        {
            return false;
        }

        if (IPAddress.TryParse(parts[0], out var ip) && int.TryParse(parts[1], out var prefixLength))
        {
            network = new Microsoft.AspNetCore.HttpOverrides.IPNetwork(ip, prefixLength);
            return true;
        }

        return false;
    }
}
