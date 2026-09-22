using ActivityService.Data;
using ActivityService.Infrastructure;
using ActivityService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Health;
using ClubReportHub.Shared.Messaging;
using ClubReportHub.Shared.Tracing;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddActivityService(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddDbContext<ActivityDbContext>(options =>
        {
            var connectionString =
                configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "Connection string 'DefaultConnection' was not found.");

            options.UseSqlServer(connectionString);
        });

        services.AddClubReportJwt(configuration, environment);
        services.AddClubReportTracing();
        services.AddClubAccessClient(configuration);

        services.AddScoped<MemberActivityStatisticsService>();

        services.AddHttpClient<ClubMemberRosterClient>(client =>
        {
            var baseUrl =
                configuration["Services:ClubService:BaseUrl"]
                ?? "http://localhost:5102/";

            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
        }).AddCorrelationIdForwarding().AddStandardResilienceHandler();

        services.AddHttpClient<ReportVerificationClient>(client =>
        {
            var baseUrl =
                configuration["Services:ReportService:BaseUrl"]
                ?? "http://localhost:5103/";

            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
        }).AddCorrelationIdForwarding().AddStandardResilienceHandler();

        services.AddRedisStreamEventBus(configuration);
        services.AddTransactionalOutbox<ActivityDbContext>();

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services.AddCors(options =>
        {
            options.AddPolicy("frontend", policy =>
            {
                var allowedOrigins = ClubReportHub.Shared.Cors.CorsOriginConfiguration.ResolveAllowedOrigins(
                    configuration,
                    environment);

                policy
                    .WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        services.AddHealthChecks()
            .AddDbContextCheck<ActivityDbContext>("activity-db", tags: ["ready"])
            .AddRedisHealthCheck();

        return services;
    }
}
