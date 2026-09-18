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
}
