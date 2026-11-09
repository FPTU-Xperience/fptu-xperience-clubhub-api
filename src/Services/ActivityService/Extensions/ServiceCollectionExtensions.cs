using ActivityService.Data;
using ActivityService.Infrastructure;
using ActivityService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Health;
using ClubReportHub.Shared.Messaging;
using ClubReportHub.Shared.Tracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddCurrentAccountValidation(configuration, environment);
        services.AddClubReportTracing();
        services.AddClubAccessClient(configuration);
        services.AddHttpClient<CurrentUserProfileClient>(client =>
        { client.BaseAddress = new Uri(configuration["Services:AuthService:BaseUrl"] ?? "http://auth-service:8080/"); client.Timeout = TimeSpan.FromSeconds(10); });
        services.AddHttpClient<ClubExperienceAuthorityClient>(client =>
        { client.BaseAddress = new Uri(configuration["Services:ClubService:BaseUrl"] ?? "http://club-service:8080/"); client.Timeout = TimeSpan.FromSeconds(10); });
        services.AddHttpClient<MediaAttachmentClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["Services:AuthService:BaseUrl"] ?? "http://auth-service:8080/");
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddCorrelationIdForwarding();
        services.AddHttpClient<ExperienceAuthorityClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["Services:AdminService:BaseUrl"] ?? "http://admin-service:8080/");
            client.Timeout = TimeSpan.FromSeconds(15);
        }).AddCorrelationIdForwarding();

        services.AddScoped<MemberActivityStatisticsService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ActivityLifecycleSynchronizer>();
        services.AddHostedService<ActivityLifecycleWorker>();
        services.AddHttpClient<SemesterCatalogClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["Services:AdminService:BaseUrl"] ?? "http://admin-service:8080/");
            client.Timeout = TimeSpan.FromSeconds(15);
        }).AddCorrelationIdForwarding();

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
        services.AddScoped<ActivityExperienceEventHandler>();
        services.AddHostedService<ExperienceStreamConsumer<ActivityExperienceEventHandler>>();

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services.AddCors(options =>
        {
            options.AddPolicy("frontend", policy =>
            {
                ClubReportHub.Shared.Cors.CorsOriginConfiguration.ApplyFrontendCorsPolicy(
                    policy,
                    configuration,
                    environment);
            });
        });

        services.AddHealthChecks()
            .AddDbContextCheck<ActivityDbContext>("activity-db", tags: ["ready"])
            .AddRedisHealthCheck();

        return services;
    }
}
