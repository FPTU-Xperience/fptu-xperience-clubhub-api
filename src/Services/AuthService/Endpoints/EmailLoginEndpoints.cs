using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AuthService.Data;
using AuthService.Models;
using AuthService.Services;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AuthService.Endpoints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmailLoginRequest(string Email, string Password);
public sealed record SetPasswordRequest(string Password);

public static class EmailLoginEndpoints
{
    private static readonly PasswordHasher<User> Hasher = new();
    private static readonly User DummyUser = new();
    private static readonly string DummyHash = Hasher.HashPassword(DummyUser, Guid.NewGuid().ToString());

    public static void MapEmailLoginEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/email-login", Login).AllowAnonymous().RequireRateLimiting("emailSignInLimit");
        app.MapPut("/api/users/{id:int}/password", async (int id, SetPasswordRequest input,
            AuthDbContext db, RefreshTokenService tokens, IMemoryCache cache, CancellationToken ct) =>
        {
            if (!ValidNewPassword(input.Password)) return Results.BadRequest(new { message = "Mật khẩu phải có từ 12 đến 128 ký tự." });
            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (user is null) return Results.NotFound();
            await SetPassword(user, input.Password, db, tokens, cache);
            return Results.NoContent();
        }).RequireAuthorization(AuthPolicies.SystemAdministration).RequireRateLimiting("emailSignInLimit");
    }

    public static async Task<IResult> Login(EmailLoginRequest input, AuthDbContext db, RefreshTokenService tokens, HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(input.Email) || input.Email.Length > 200
            || !new EmailAddressAttribute().IsValid(input.Email.Trim()) || string.IsNullOrEmpty(input.Password) || input.Password.Length > 128)
            return Rejected();
        var email = input.Email.Trim().ToLowerInvariant();
        var user = await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Email.ToLower() == email, ct);
        PasswordVerificationResult verified;
        try { verified = Hasher.VerifyHashedPassword(user ?? DummyUser, user?.PasswordHash ?? DummyHash, input.Password); }
        catch (FormatException) { return Rejected(); }
        if (user is null || string.IsNullOrEmpty(user.PasswordHash) || verified == PasswordVerificationResult.Failed
            || !user.IsActive || user.IsLocked || !ActorAccountPolicy.HasValidActorConfiguration(user)) return Rejected();
        if (verified == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, input.Password);
            await db.SaveChangesAsync(ct);
        }
        var refresh = await tokens.CreateRefreshTokenAsync(user.Id);
        return Results.Ok(tokens.CreateAuthResponse(user, refresh));
    }

    private static IResult Rejected() => Results.Json(new { code = "invalid_credentials", message = "Email hoặc mật khẩu không đúng, hoặc tài khoản không được phép đăng nhập." }, statusCode: 401);
    public static bool ValidNewPassword(string? password) => password is { Length: >= 12 and <= 128 } && !string.IsNullOrWhiteSpace(password);

    public static async Task SetPassword(User user, string password, AuthDbContext db, RefreshTokenService tokens, IMemoryCache cache)
    {
        if (!ValidNewPassword(password)) throw new ArgumentException("Password must contain 12–128 characters.", nameof(password));
        user.PasswordHash = Hasher.HashPassword(user, password);
        user.SecurityVersion++;
        // This SaveChanges persists the hash, security version and token revocations together.
        await tokens.RevokeForUserAsync(user.Id);
        cache.Remove($"sec_stamp:user:{user.Id}");
    }
}
