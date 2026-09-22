using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace ClubReportHub.Shared.Health;

public static class StandardHealthCheckExtensions
{
    public static IEndpointRouteBuilder MapStandardHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        // Liveness probe: returns 200 OK as long as the process is alive
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        }).AllowAnonymous();

        // Readiness probe: verifies essential dependencies (DB, Redis) tagged with 'ready'
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready")
        }).AllowAnonymous();

        // Standard /health route kept for backwards compatibility
        endpoints.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false
        }).AllowAnonymous();

        return endpoints;
    }

    public static IHealthChecksBuilder AddRedisHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "redis",
        string[]? tags = null)
    {
        tags ??= ["ready"];
        return builder.Add(new HealthCheckRegistration(
            name,
            sp =>
            {
                var multiplexer = sp.GetService<IConnectionMultiplexer>();
                return new RedisHealthCheck(multiplexer);
            },
            default,
            tags));
    }

    private sealed class RedisHealthCheck(IConnectionMultiplexer? multiplexer) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            if (multiplexer is null)
            {
                return Task.FromResult(HealthCheckResult.Healthy("Redis multiplexer not registered in DI."));
            }

            return Task.FromResult(multiplexer.IsConnected
                ? HealthCheckResult.Healthy("Redis connection is active.")
                : HealthCheckResult.Unhealthy("Redis connection is not established."));
        }
    }
}
