using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AuthService.Contracts;
using AuthService.Data;
using AuthService.Models;
using AuthService.Services;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Endpoints;

// The browser sends a host-only HttpOnly cookie directly to Auth. Neither UI
// reads another site's storage or navigates to the other UI to discover a login.
public static class BrowserSessionEndpoints
{
    private const string Audience = "FPTUX.BrowserSession.v1";
    private const string Header = "X-FPTUX-Session";
    private sealed record BrowserIdentity(int UserId, int SecurityVersion, string FamilyId, string SessionId, DateTime Expires);

    public static void MapBrowserSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/session", Check).AllowAnonymous();
        app.MapPost("/api/auth/session", Restore).AllowAnonymous().RequireRateLimiting("refreshLimit");
    }

    private static bool Allowed(HttpContext context)
    {
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        var origin = context.Request.Headers.Origin.ToString();
        var allowed = new[] {
            configuration["Sso:ClubHubOrigin"] ?? "https://fptux-clubhub-ui.pages.dev",
            configuration["Sso:AdminOrigin"] ?? "https://fptux-admin-ui.pages.dev"
        };
        if (allowed.Any(value => string.Equals(value.TrimEnd('/'), origin, StringComparison.Ordinal))) return true;
        var environment = context.RequestServices.GetRequiredService<IHostEnvironment>();
        return environment.IsDevelopment() && origin is "http://localhost:5173" or "http://localhost:5174";
    }

    private static JwtOptions Options(HttpContext context) => context.RequestServices.GetRequiredService<IOptions<JwtOptions>>().Value;
    private static SymmetricSecurityKey Key(JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKey) || Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
            throw new InvalidOperationException("Browser sessions require a configured JWT signing key of at least 32 bytes.");
        return new(HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(options.SigningKey), 32,
            info: Encoding.UTF8.GetBytes(Audience)));
    }
    private static bool Secure(HttpContext context) => context.Request.IsHttps
        || !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();
    private static string CookieName(HttpContext context) => Secure(context) ? "__Host-fptux-session" : "fptux-session";
    private static CookieOptions CookieSettings(HttpContext context) => new()
    {
        HttpOnly = true,
        Secure = Secure(context),
        Path = "/",
        SameSite = Secure(context) && !string.Equals(context.RequestServices.GetRequiredService<IConfiguration>()["BrowserSession:SameSite"],
            "Lax", StringComparison.OrdinalIgnoreCase) ? SameSiteMode.None : SameSiteMode.Lax,
        IsEssential = true
    };

    public static async Task WriteLoginCookie(HttpContext context, AuthResponse response, RefreshTokenService refresh)
    {
        // Non-browser clients remain supported but cannot mint a shared browser session.
        if (!Allowed(context) || string.IsNullOrEmpty(response.RefreshToken)) return;
        var token = await refresh.GetRefreshTokenAsync(response.RefreshToken);
        if (token is null || !token.IsActive) return;
        var options = Options(context);
        var jwt = new JwtSecurityToken(options.Issuer, Audience, [
            new Claim("uid", token.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim("sv", token.User.SecurityVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim("family", token.FamilyId),
            new Claim("sid", Guid.NewGuid().ToString("N"))
        ], expires: token.ExpiresAtUtc.UtcDateTime, signingCredentials: new SigningCredentials(Key(options), SecurityAlgorithms.HmacSha256));
        var settings = CookieSettings(context);
        settings.Expires = token.ExpiresAtUtc;
        context.Response.Cookies.Append(CookieName(context), new JwtSecurityTokenHandler().WriteToken(jwt), settings);
        context.Response.Headers.CacheControl = "no-store";
    }

    private static BrowserIdentity? Read(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName(context), out var raw)) return null;
        try
        {
            var options = Options(context);
            var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(raw, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = options.Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = Key(options),
                ValidateLifetime = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.Zero,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
            }, out var validated);
            if (!int.TryParse(principal.FindFirstValue("uid"), out var userId)
                || !int.TryParse(principal.FindFirstValue("sv"), out var version)
                || principal.FindFirstValue("family") is not { Length: > 0 } family
                || principal.FindFirstValue("sid") is not { Length: > 0 } sessionId) return null;
            return new(userId, version, family, sessionId, validated.ValidTo);
        }
        catch (Exception error) when (error is SecurityTokenException or ArgumentException) { return null; }
    }

    public static void ClearForUser(HttpContext context, int userId)
    {
        if (Read(context)?.UserId == userId) context.Response.Cookies.Delete(CookieName(context), CookieSettings(context));
    }

    private static bool Guard(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        // Exact origins plus a non-simple header protect cookie-authenticated operations from CSRF.
        return Allowed(context) && context.Request.Headers[Header] == "1";
    }

    private static async Task<User?> ValidUser(AuthDbContext db, BrowserIdentity identity, CancellationToken ct)
    {
        var user = await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == identity.UserId, ct);
        return user is not null && user.SecurityVersion == identity.SecurityVersion && user.IsActive && !user.IsLocked
            && ActorAccountPolicy.HasValidActorConfiguration(user) ? user : null;
    }

    private static UserSummary Summary(User user) => new(user.Id, user.Username, user.FullName, user.Email,
        user.UserRoles.Select(x => x.Role.Name).OrderBy(x => x).ToArray(), user.IsActive, user.IsLocked, user.CampusCode);

    private static async Task<IResult> Check(HttpContext context, AuthDbContext db, CancellationToken ct)
    {
        if (!Guard(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var identity = Read(context);
        if (identity is null) return Results.NoContent();
        var user = await ValidUser(db, identity, ct);
        var expiries = await db.RefreshTokens.AsNoTracking().Where(x => x.UserId == identity.UserId
            && x.FamilyId == identity.FamilyId && x.RevokedAtUtc == null).Select(x => x.ExpiresAtUtc).ToListAsync(ct);
        if (user is null || !expiries.Any(expiry => expiry > DateTimeOffset.UtcNow)) return Results.NoContent();
        return Results.Ok(new { user = Summary(user), browserSessionId = identity.SessionId });
    }

    private static async Task<IResult> Restore(HttpContext context, AuthDbContext db, RefreshTokenService refresh, CancellationToken ct)
    {
        if (!Guard(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var identity = Read(context);
        if (identity is null) return Results.NoContent();
        // Match the family lock used by logout. A concurrent logout cannot leave a live new branch.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var expiries = await RefreshTokenService.FamilyForUpdate(db, identity.FamilyId)
            .Where(x => x.UserId == identity.UserId && x.RevokedAtUtc == null).Select(x => x.ExpiresAtUtc).ToListAsync(ct);
        var user = await ValidUser(db, identity, ct);
        if (user is null || identity.Expires <= DateTime.UtcNow || !expiries.Any(expiry => expiry > DateTimeOffset.UtcNow)) return Results.NoContent();
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var branch = new RefreshToken
        {
            UserId = user.Id,
            FamilyId = identity.FamilyId,
            Token = RefreshTokenService.HashToken(raw),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = new DateTimeOffset(identity.Expires, TimeSpan.Zero)
        };
        db.RefreshTokens.Add(branch);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(new { session = refresh.CreateAuthResponse(user, branch, raw), browserSessionId = identity.SessionId });
    }
}
