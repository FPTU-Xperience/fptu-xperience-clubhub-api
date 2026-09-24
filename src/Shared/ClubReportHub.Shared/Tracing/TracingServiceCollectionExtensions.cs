using Microsoft.Extensions.DependencyInjection;

namespace ClubReportHub.Shared.Tracing;

public static class TracingServiceCollectionExtensions
{
    public static IServiceCollection AddClubReportTracing(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationIdDelegatingHandler>();
        return services;
    }

    public static IHttpClientBuilder AddCorrelationIdForwarding(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<CorrelationIdDelegatingHandler>();
        return builder.AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
    }
}
