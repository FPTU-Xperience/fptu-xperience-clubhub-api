using System.Threading.RateLimiting;
using AuthService.Data;
using AuthService.Services;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Extensions;

public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddAuthServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Database
        services.AddDbContext<AuthDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        // JWT Authentication
        services.AddClubReportJwt(configuration);

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

            // Strict rate limit for Google credential submissions.
            options.AddPolicy("googleSignInLimit", context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 5,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Refresh token limit
            options.AddPolicy("refreshLimit", context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
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
                var configuredOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
                var defaultOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "http://localhost:3000",
                    "http://localhost:3001",
                    "http://localhost:5173",
                    "https://fptux-legacy-ui.pages.dev"
                };

                foreach (var origin in configuredOrigins)
                {
                    if (!string.IsNullOrWhiteSpace(origin))
                    {
                        defaultOrigins.Add(origin.Trim().TrimEnd('/'));
                    }
                }

                policy.SetIsOriginAllowed(origin =>
                      {
                          if (string.IsNullOrWhiteSpace(origin)) return false;
                          if (defaultOrigins.Contains(origin.TrimEnd('/'))) return true;

                          if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                          {
                              return uri.Host == "localhost"
                                  || uri.Host == "127.0.0.1"
                                  || uri.Host.EndsWith(".pages.dev", StringComparison.OrdinalIgnoreCase);
                          }

                          return false;
                      })
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        // Health Checks
        services.AddHealthChecks();

        return services;
    }
}
