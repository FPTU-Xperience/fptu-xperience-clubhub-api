using AuthService.Contracts;
using AuthService.Services;
using AuthService.Validators;

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

        auth.MapPost("/refresh", HandleRefresh)
            .AllowAnonymous()
            .RequireRateLimiting("refreshLimit");

        auth.MapPost("/logout", HandleLogout)
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> HandleGoogleSignIn(
        GoogleLoginRequest request,
        GoogleSignInService googleSignInService,
        CancellationToken cancellationToken)
    {
        var validation = AuthValidators.ValidateGoogleLogin(request);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new { message = string.Join(" ", validation.Errors) });
        }

        var result = await googleSignInService.SignInAsync(request.Credential, cancellationToken);
        return result.Status switch
        {
            GoogleSignInStatus.Authenticated => Results.Ok(result.Response),
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

    private static async Task<IResult> HandleRefresh(
        RefreshTokenRequest request,
        RefreshTokenService refreshTokenService)
    {
        var oldToken = await refreshTokenService.GetRefreshTokenAsync(request.RefreshToken);
        if (oldToken is null)
        {
            return Results.Unauthorized();
        }

        if (!oldToken.IsActive)
        {
            // Token is expired or revoked - revoke entire family for security.
            if (oldToken.IsRevoked && !oldToken.IsExpired)
            {
                await refreshTokenService.RevokeFamilyAsync(oldToken.FamilyId, null);
            }
            return Results.Unauthorized();
        }

        if (!ActorAccountPolicy.HasValidActorConfiguration(oldToken.User))
        {
            return Results.Json(
                new { message = "Bạn không có quyền truy cập." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var rotatedToken = await refreshTokenService.RotateRefreshTokenAsync(oldToken, null);
        if (rotatedToken is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(refreshTokenService.CreateAuthResponse(oldToken.User, rotatedToken));
    }

    private static async Task<IResult> HandleLogout(
        RefreshTokenRequest request,
        RefreshTokenService refreshTokenService)
    {
        var token = await refreshTokenService.GetRefreshTokenAsync(request.RefreshToken);
        if (token is not null)
        {
            await refreshTokenService.RevokeFamilyAsync(token.FamilyId, null);
        }
        return Results.NoContent();
    }
}
