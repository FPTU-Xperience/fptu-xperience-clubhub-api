using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AuthService.Contracts;
using AuthService.Data;
using AuthService.Models;
using AuthService.Services;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AuthService.Endpoints;

public sealed record StaffSsoIssueRequest(string RefreshToken, string TargetOrigin, string CodeChallenge);
public sealed record StaffSsoRedeemRequest(string Code, string CodeVerifier);

public static class StaffSsoEndpoints
{
    public static IEndpointRouteBuilder MapStaffSsoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/sso").WithTags("Staff SSO");
        group.MapPost("/tickets", Issue).RequireAuthorization(AuthPolicies.StudentAffairsAdministration)
            .RequireRateLimiting("refreshLimit");
        group.MapPost("/redeem", Redeem).AllowAnonymous().RequireRateLimiting("refreshLimit");
        group.MapPost("/status", Status).AllowAnonymous();
        return app;
    }

    private static string[] Origins(IConfiguration configuration, IHostEnvironment environment)
    {
        string[] deployed =
        [
            (configuration["Sso:AdminOrigin"] ?? (environment.IsDevelopment() ? "http://localhost:5174" : "https://fptux-admin-ui.pages.dev")).TrimEnd('/'),
            (configuration["Sso:ClubHubOrigin"] ?? (environment.IsDevelopment() ? "http://localhost:5173" : "https://fptux-clubhub-ui.pages.dev")).TrimEnd('/')
        ];
        return configuration.GetValue("Sso:AllowLocalOrigins", true)
            ? [.. deployed, "http://localhost:5174", "http://localhost:5173"] : deployed;
    }

    private static bool IsStaff(User user) => user.IsActive && !user.IsLocked
        && ActorAccountPolicy.HasValidActorConfiguration(user)
        && user.UserRoles.Any(x => x.Role.Name is AuthRoles.StudentAffairsAdmin or AuthRoles.Admin);

    private static bool IsPkce(string? value, int min = 43, int max = 43) => value is not null
        && value.Length >= min && value.Length <= max
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~');

    private static void NoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }

    private static async Task<IResult> Issue(StaffSsoIssueRequest request, ClaimsPrincipal actor,
        RefreshTokenService refresh, AuthDbContext db, IConfiguration configuration, IHostEnvironment environment,
        HttpContext context, CancellationToken ct)
    {
        NoStore(context);
        var origins = Origins(configuration, environment);
        var source = context.Request.Headers.Origin.ToString();
        if (!origins.Contains(source, StringComparer.Ordinal) || !origins.Contains(request.TargetOrigin, StringComparer.Ordinal)
            || source == request.TargetOrigin
            || Array.IndexOf(origins, source) / 2 != Array.IndexOf(origins, request.TargetOrigin) / 2
            || !IsPkce(request.CodeChallenge)) return Results.BadRequest();
        var token = await refresh.GetRefreshTokenAsync(request.RefreshToken);
        if (token is null || !token.IsActive || token.UserId != actor.GetUserId() || !IsStaff(token.User)) return Results.Unauthorized();
        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        db.StaffSsoTickets.Add(new StaffSsoTicket
        {
            CodeHash = RefreshTokenService.HashToken(code),
            UserId = token.UserId,
            FamilyId = token.FamilyId,
            SecurityVersion = token.User.SecurityVersion,
            TargetOrigin = request.TargetOrigin,
            CodeChallenge = request.CodeChallenge,
            ExpiresAtUtc = now.AddMinutes(1)
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { code, expiresAtUtc = now.AddMinutes(1) });
    }

    private static async Task<IResult> Redeem(StaffSsoRedeemRequest request, AuthDbContext db,
        RefreshTokenService refresh, IOptions<JwtOptions> jwt, IConfiguration configuration,
        IHostEnvironment environment, HttpContext context, CancellationToken ct)
    {
        NoStore(context);
        var origin = context.Request.Headers.Origin.ToString();
        if (!Origins(configuration, environment).Contains(origin, StringComparer.Ordinal)
            || !IsPkce(request.Code) || !IsPkce(request.CodeVerifier, 43, 128)) return Results.BadRequest();
        var hash = RefreshTokenService.HashToken(request.Code);
        var ticket = await db.StaffSsoTickets.AsNoTracking().SingleOrDefaultAsync(x => x.CodeHash == hash, ct);
        var now = DateTimeOffset.UtcNow;
        if (ticket is null || ticket.ConsumedAtUtc is not null || ticket.ExpiresAtUtc <= now || ticket.TargetOrigin != origin)
            return Results.Unauthorized();
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(request.CodeVerifier)));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(challenge), Encoding.ASCII.GetBytes(ticket.CodeChallenge)))
            return Results.Unauthorized();

        // Serialize redemption with family revocation: concurrent logout must not leave a new live branch.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Id == ticket.UserId, ct);
        var activeExpiries = await RefreshTokenService.FamilyForUpdate(db, ticket.FamilyId).Where(x => x.UserId == ticket.UserId
            && x.RevokedAtUtc == null).Select(x => x.ExpiresAtUtc).ToListAsync(ct);
        now = DateTimeOffset.UtcNow;
        if (user is null || !IsStaff(user) || user.SecurityVersion != ticket.SecurityVersion
            || ticket.ExpiresAtUtc <= now || !activeExpiries.Any(expiry => expiry > now)) return Results.Unauthorized();
        var consumed = await db.StaffSsoTickets.Where(x => x.CodeHash == hash && x.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ConsumedAtUtc, now), ct);
        if (consumed != 1) return Results.Unauthorized();
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var branch = new RefreshToken
        {
            UserId = user.Id,
            FamilyId = ticket.FamilyId,
            Token = RefreshTokenService.HashToken(rawToken),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(jwt.Value.RefreshTokenExpirationDays)
        };
        db.RefreshTokens.Add(branch);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(refresh.CreateAuthResponse(user, branch, rawToken));
    }

    private static async Task<IResult> Status(RefreshTokenRequest request, RefreshTokenService refresh,
        IConfiguration configuration, IHostEnvironment environment, HttpContext context)
    {
        NoStore(context);
        if (!Origins(configuration, environment).Contains(context.Request.Headers.Origin.ToString(), StringComparer.Ordinal)) return Results.BadRequest();
        var token = await refresh.GetRefreshTokenAsync(request.RefreshToken);
        return token is not null && token.IsActive && IsStaff(token.User) ? Results.NoContent() : Results.Unauthorized();
    }
}
