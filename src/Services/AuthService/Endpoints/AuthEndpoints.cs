using System.Security.Claims;
using AuthService.Contracts;
using AuthService.Data;
using AuthService.Services;
using AuthService.Validators;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Tracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuthService.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Authentication");

        // Password login and public registration are intentionally absent.
        // Accounts are provisioned by an ADMIN and enter through Google only.
        auth.MapPost("/google", HandleGoogleSignIn)
            .AllowAnonymous()
            .RequireRateLimiting("googleSignInLimit");

        // The admin UI uses email-based dev login in every deployed environment.
        auth.MapPost("/dev-login", HandleDevLogin)
            .AllowAnonymous()
            .RequireRateLimiting("googleSignInLimit");

        var env = app.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (env.IsDevelopment() || env.IsEnvironment("Testing"))
        {
            // Keep the test alias limited to non-production environments.
            auth.MapPost("/test-login", HandleDevLogin)
                .AllowAnonymous();
        }

        auth.MapPost("/refresh", HandleRefresh)
            .AllowAnonymous()
            .RequireRateLimiting("refreshLimit");

        auth.MapPost("/logout", HandleLogout)
            .RequireAuthorization();

        auth.MapGet("/user-status/{id:int}", HandleGetUserStatus)
            .AllowAnonymous();

        return app;
    }

    private static async Task<IResult> HandleGoogleSignIn(
        GoogleLoginRequest request,
        GoogleSignInService googleSignInService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("AuthService.Authentication");
        var correlationId = httpContext.GetCorrelationId();
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();

        var validation = AuthValidators.ValidateGoogleLogin(request);
        if (!validation.IsValid)
        {
            logger.LogWarning("GoogleSignIn validation failed: {Errors}, ClientIp: {ClientIp}, CorrelationId: {CorrelationId}",
                string.Join(" ", validation.Errors), clientIp, correlationId);
            return Results.BadRequest(new { message = string.Join(" ", validation.Errors) });
        }

        var result = await googleSignInService.SignInAsync(request.Credential, cancellationToken);
        if (result.Status == GoogleSignInStatus.Authenticated && result.Response is not null)
        {
            logger.LogInformation("GoogleSignIn succeeded for User: {Email}, Id: {UserId}, ClientIp: {ClientIp}, CorrelationId: {CorrelationId}",
                result.Response.User.Email, result.Response.User.Id, clientIp, correlationId);
            return Results.Ok(result.Response);
        }

        logger.LogWarning("GoogleSignIn failed with status: {Status}, ClientIp: {ClientIp}, CorrelationId: {CorrelationId}",
            result.Status, clientIp, correlationId);

        return result.Status switch
        {
            GoogleSignInStatus.InvalidGoogleCredential => Results.Json(
                new { message = "Thông tin xác thực Google không hợp lệ hoặc đã hết hạn." },
                statusCode: StatusCodes.Status401Unauthorized),
            GoogleSignInStatus.NotConfigured => Results.Problem(
                title: "Google sign-in is not configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Json(
                new { message = "Bạn không có quyền truy cập." },
                statusCode: StatusCodes.Status403Forbidden)
        };
    }

    private static async Task<IResult> HandleDevLogin(
        DevLoginRequest request,
        AuthDbContext db,
        RefreshTokenService refreshTokenService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("AuthService.DevLogin");
        var correlationId = httpContext.GetCorrelationId();
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();

        if (string.IsNullOrWhiteSpace(request?.Email))
        {
            logger.LogWarning("DevLogin rejected: missing email. ClientIp: {ClientIp}, CorrelationId: {CorrelationId}",
                clientIp, correlationId);
            return Results.BadRequest(new { message = "Email là bắt buộc." });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users
            .Include(x => x.UserRoles)
            .ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail, cancellationToken);

        if (user is null || !user.IsActive || user.IsLocked ||
            !ActorAccountPolicy.HasValidActorConfiguration(user))
        {
            logger.LogWarning("DevLogin rejected for email: {Email}. Found: {Found}, Active: {IsActive}, Locked: {IsLocked}, ClientIp: {ClientIp}, CorrelationId: {CorrelationId}",
                normalizedEmail, user is not null, user?.IsActive, user?.IsLocked, clientIp, correlationId);
            return Results.Json(
                new { message = "Bạn không có quyền truy cập." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        logger.LogInformation("DevLogin succeeded for user: {Email}, Id: {UserId}, ClientIp: {ClientIp}, CorrelationId: {CorrelationId}",
            normalizedEmail, user.Id, clientIp, correlationId);
        var refreshToken = await refreshTokenService.CreateRefreshTokenAsync(user.Id);
        return Results.Ok(refreshTokenService.CreateAuthResponse(user, refreshToken));
    }

    private static async Task<IResult> HandleRefresh(
        RefreshTokenRequest request,
        RefreshTokenService refreshTokenService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AuthService.TokenRefresh");
        var correlationId = httpContext.GetCorrelationId();
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();

        if (string.IsNullOrWhiteSpace(request?.RefreshToken))
        {
            logger.LogWarning("TokenRefresh rejected: missing refresh token. ClientIp: {ClientIp}, CorrelationId: {CorrelationId}",
                clientIp, correlationId);
            return Results.Unauthorized();
        }

        var result = await refreshTokenService.RotateRefreshTokenAsync(request.RefreshToken, clientIp);

        if (result.Status == RefreshTokenStatus.Success)
        {
            var userId = result.User?.Id ?? result.Response?.User.Id;
            logger.LogInformation("TokenRefresh succeeded for User: {UserId}, ClientIp: {ClientIp}, CorrelationId: {CorrelationId}",
                userId, clientIp, correlationId);

            if (result.Response is not null)
                return Results.Ok(result.Response);

            if (result.NewToken is not null && result.User is not null)
                return Results.Ok(refreshTokenService.CreateAuthResponse(result.User, result.NewToken, result.NewToken.RawToken));
        }

        logger.LogWarning("TokenRefresh failed with status: {Status}, ClientIp: {ClientIp}, CorrelationId: {CorrelationId}",
            result.Status, clientIp, correlationId);

        return result.Status switch
        {
            RefreshTokenStatus.AccountNotAllowed =>
                Results.Json(
                    new { message = "Bạn không có quyền truy cập." },
                    statusCode: StatusCodes.Status403Forbidden),
            _ => Results.Unauthorized()
        };
    }

    private static async Task<IResult> HandleLogout(
        RefreshTokenRequest request,
        RefreshTokenService refreshTokenService,
        AuthDbContext db,
        ClaimsPrincipal actor,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        HttpContext httpContext,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AuthService.Logout");
        var correlationId = httpContext.GetCorrelationId();
        var userId = actor.GetUserId();

        if (!string.IsNullOrWhiteSpace(request?.RefreshToken))
        {
            var token = await refreshTokenService.GetRefreshTokenAsync(request.RefreshToken);
            if (token is not null)
            {
                await refreshTokenService.RevokeFamilyAsync(token.FamilyId, null);
                if (userId <= 0)
                {
                    userId = token.UserId;
                }
            }
        }

        if (userId > 0)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is not null)
            {
                user.SecurityVersion++;
                await db.SaveChangesAsync();
                cache.Remove($"sec_stamp:user:{userId}");
            }
        }

        logger.LogInformation("Logout processed for User: {UserId}, CorrelationId: {CorrelationId}",
            userId, correlationId);

        return Results.NoContent();
    }

    private static async Task<IResult> HandleGetUserStatus(
        int id,
        AuthDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await db.Users
            .AsNoTracking()
            .Include(x => x.UserRoles)
            .ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new
        {
            userId = user.Id,
            isActive = user.IsActive,
            isLocked = user.IsLocked,
            securityVersion = user.SecurityVersion,
            roles = user.UserRoles.Select(x => x.Role.Name).OrderBy(x => x).ToArray()
        });
    }
}
