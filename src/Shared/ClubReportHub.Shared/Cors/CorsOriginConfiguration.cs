using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ClubReportHub.Shared.Cors;

public static class CorsOriginConfiguration
{
    private static readonly string[] DevelopmentOrigins =
    [
        "http://localhost:3000",
        "http://localhost:3001",
        "http://localhost:5173",
        "http://127.0.0.1:3000",
        "http://127.0.0.1:5173"
    ];

    public static string[] ResolveAllowedOrigins(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var configuredOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [];
        var additionalOrigins = (configuration["Cors:AdditionalOrigins"] ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries);

        IEnumerable<string> origins = configuredOrigins.Concat(additionalOrigins);
        if (!environment.IsProduction())
        {
            origins = origins.Concat(DevelopmentOrigins);
        }

        return origins
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsOriginAllowed(string origin, IEnumerable<string> allowedOrigins)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        foreach (var allowed in allowedOrigins)
        {
            if (string.Equals(origin, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (allowed.StartsWith("https://*.", StringComparison.OrdinalIgnoreCase) ||
                allowed.StartsWith("http://*.", StringComparison.OrdinalIgnoreCase))
            {
                var schemeEnd = allowed.IndexOf("://*.", StringComparison.OrdinalIgnoreCase);
                var allowedScheme = allowed[..schemeEnd];
                var rootDomain = allowed[(schemeEnd + 5)..]; // e.g. "fptux-clubhub-ui.pages.dev"
                var suffix = "." + rootDomain;

                if (string.Equals(uri.Scheme, allowedScheme, StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(uri.Host, rootDomain, StringComparison.OrdinalIgnoreCase) ||
                     uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            if (allowed.Equals("http://localhost:*", StringComparison.OrdinalIgnoreCase) ||
                allowed.Equals("https://localhost:*", StringComparison.OrdinalIgnoreCase) ||
                allowed.Equals("http://127.0.0.1:*", StringComparison.OrdinalIgnoreCase) ||
                allowed.Equals("https://127.0.0.1:*", StringComparison.OrdinalIgnoreCase))
            {
                var schemeEnd = allowed.IndexOf("://", StringComparison.OrdinalIgnoreCase);
                var allowedScheme = allowed[..schemeEnd];
                var allowedHost = allowed[(schemeEnd + 3)..^2]; // "localhost" or "127.0.0.1"

                if (string.Equals(uri.Scheme, allowedScheme, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(uri.Host, allowedHost, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static void ApplyFrontendCorsPolicy(
        Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder policy,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var allowedOrigins = ResolveAllowedOrigins(configuration, environment);
        policy.WithOrigins(allowedOrigins)
              .SetIsOriginAllowed(origin => IsOriginAllowed(origin, allowedOrigins))
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    }
}
