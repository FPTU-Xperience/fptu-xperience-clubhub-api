using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace ClubReportHub.Shared.Auth;

public static class JwtServiceCollectionExtensions
{
    public static IServiceCollection AddClubReportJwt(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddClubReportJwtValidation(configuration, environment);
        services.AddSingleton<JwtTokenFactory>();
        return services;
    }

    public static IServiceCollection AddClubReportJwtValidation(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
        {
            throw new InvalidOperationException("JWT SigningKey must be configured in Jwt:SigningKey section.");
        }

        if (jwtOptions.SigningKey.Length < 32)
        {
            throw new InvalidOperationException("JWT SigningKey must contain at least 32 characters.");
        }

        if (environment.IsProduction()
            && (jwtOptions.SigningKey.StartsWith("dev-only-", StringComparison.OrdinalIgnoreCase)
                || jwtOptions.SigningKey.StartsWith("replace-with-", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "A development or placeholder JWT signing key cannot be used in Production.");
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = environment.IsProduction();
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = signingKey,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var subject = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? context.Principal?.FindFirstValue("sub");
                        if (!int.TryParse(
                                subject,
                                NumberStyles.None,
                                CultureInfo.InvariantCulture,
                                out var userId)
                            || userId <= 0)
                        {
                            context.Fail("The token does not contain a valid AuthService user identifier.");
                            return;
                        }

                        var validator = context.HttpContext.RequestServices.GetService<IUserSecurityStampValidator>();
                        if (validator is not null)
                        {
                            var isValid = await validator.ValidateSecurityStampAsync(
                                userId,
                                context.Principal!,
                                context.HttpContext.RequestAborted);

                            if (!isValid)
                            {
                                context.Fail("The token is no longer valid due to security stamp or account status changes.");
                            }
                        }
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthPolicies.AllActors, policy => policy.RequireRole(
                AuthRoles.Admin,
                AuthRoles.SystemAdmin,
                AuthRoles.StudentAffairsAdmin,
                AuthRoles.ClubManager,
                AuthRoles.Treasurer,
                AuthRoles.ClubMember));
            options.AddPolicy(AuthPolicies.BusinessAccess, policy => policy.RequireRole(
                AuthRoles.Admin,
                AuthRoles.StudentAffairsAdmin,
                AuthRoles.ClubManager,
                AuthRoles.Treasurer,
                AuthRoles.ClubMember));
            options.AddPolicy(AuthPolicies.SystemAdministration, policy => policy.RequireRole(
                AuthRoles.Admin,
                AuthRoles.SystemAdmin));
            options.AddPolicy(AuthPolicies.StudentAffairsAdministration, policy => policy.RequireRole(
                AuthRoles.Admin,
                AuthRoles.StudentAffairsAdmin));
            options.AddPolicy(AuthPolicies.UserDirectoryRead, policy => policy.RequireRole(
                AuthRoles.Admin,
                AuthRoles.SystemAdmin,
                AuthRoles.StudentAffairsAdmin));
            options.AddPolicy(AuthPolicies.SuperAdminOnly, policy => policy.RequireRole(AuthRoles.Admin));
            options.AddPolicy(AuthPolicies.AdminOnly, policy => policy.RequireRole(AuthRoles.Admin));
            options.AddPolicy(AuthPolicies.ClubManagerOnly, policy => policy.RequireRole(AuthRoles.ClubManager));
            options.AddPolicy(AuthPolicies.TreasurerOnly, policy => policy.RequireRole(AuthRoles.Treasurer));
            options.AddPolicy(AuthPolicies.ClubMemberOnly, policy => policy.RequireRole(AuthRoles.ClubMember));
            options.AddPolicy(AuthPolicies.AdminOrClubManager, policy => policy.RequireRole(
                AuthRoles.Admin,
                AuthRoles.ClubManager));
            options.AddPolicy(AuthPolicies.AdminOrClubManagerOrMember, policy => policy.RequireRole(
                AuthRoles.Admin,
                AuthRoles.ClubManager,
                AuthRoles.Treasurer,
                AuthRoles.ClubMember));
            options.AddPolicy(AuthPolicies.ClubManagerOrTreasurer, policy => policy.RequireRole(
                AuthRoles.ClubManager,
                AuthRoles.Treasurer));
        });

        return services;
    }
}
