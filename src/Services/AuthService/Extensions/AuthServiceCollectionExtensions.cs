using System.Threading.RateLimiting;
using AuthService.Data;
using AuthService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Cors;
using ClubReportHub.Shared.RateLimiting;
using ClubReportHub.Shared.Tracing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Extensions;

public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddAuthServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.ConfigureTrustedForwardedHeaders(configuration);

        // Database
        services.AddDbContext<AuthDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        // JWT Authentication
        services.AddClubReportJwt(configuration, environment);
        services.AddClubReportTracing();
        services.AddMemoryCache();
        services.AddScoped<IUserSecurityStampValidator, AuthDbContextSecurityStampValidator>();

        // Google proves identity; the local database allow-list decides whether
        // that identity is permitted to receive this application's JWT.
        services.Configure<GoogleAuthenticationOptions>(
            configuration.GetSection(GoogleAuthenticationOptions.SectionName));
        services.AddSingleton<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
        services.AddScoped<GoogleSignInService>();

        // Refresh Token Service
        services.AddScoped<Services.RefreshTokenService>();

        // Rate Limiting
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = RateLimitingExtensions.CreateRateLimitRejectedHandler();

            // Allow a campus-wide login burst.
            options.AddPolicy("googleSignInLimit", context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: RateLimitingExtensions.ResolveClientKey(context),
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 1200,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 10,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Refresh token limit
            options.AddPolicy("refreshLimit", context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: RateLimitingExtensions.ResolveClientKey(context),
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 5,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));
        });

        // API Documentation
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        // CORS
        services.AddCors(options =>
        {
            options.AddPolicy("frontend", policy =>
            {
                CorsOriginConfiguration.ApplyFrontendCorsPolicy(
                    policy,
                    configuration,
                    environment);
            });
        });

        // Health Checks
        services.AddHealthChecks()
            .AddDbContextCheck<AuthDbContext>("auth-db", tags: ["ready"]);

        return services;
    }
}
