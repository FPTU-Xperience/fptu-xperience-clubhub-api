using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AuthService.Data;
using AuthService.Models;
using AuthService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Endpoints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InternalRegisterRequest(string Username, string Email, string Password, string DisplayName);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InternalLoginRequest(string Email, string Password);

public static class InternalAccountEndpoints
{
    public static void MapInternalAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/internal/register", async (InternalRegisterRequest input, AuthDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.Username) || input.Username.Length is < 3 or > 100
                || input.Username.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.'))
                || string.IsNullOrWhiteSpace(input.Email) || input.Email.Length > 200 || !new EmailAddressAttribute().IsValid(input.Email)
                || string.IsNullOrWhiteSpace(input.DisplayName) || input.DisplayName.Trim().Length > 120
                || input.Password is null || input.Password.Length is < 12 or > 128)
                return ApiOperations.Error(400, "INVALID_ACCOUNT", "Check username, email, displayName and a 12–128 character password.");
            var email = input.Email.Trim().ToLowerInvariant(); var username = input.Username.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(x => x.Email == email || x.Username == username, ct)) return ApiOperations.Error(409, "ACCOUNT_EXISTS", "An account already exists.");
            var role = await db.Roles.SingleOrDefaultAsync(x => x.Name == AuthRoles.ClubMember, ct);
            if (role is null) return ApiOperations.Error(503, "ACCOUNT_ROLE_UNAVAILABLE", "The student actor role is unavailable.");
            var user = new User { Username = username, Email = email, FullName = input.DisplayName.Trim() };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, input.Password);
            user.UserRoles.Add(new UserRole { Role = role }); db.Users.Add(user);
            try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { return ApiOperations.Error(409, "ACCOUNT_EXISTS", "An account already exists."); }
            return Results.Created("/api/users/me", new { userId = user.Id, identityProvenance = "PROJECT_ACCOUNT" });
        }).AllowAnonymous().RequireRateLimiting("googleSignInLimit");
        app.MapPost("/api/auth/internal/login", async (InternalLoginRequest input, AuthDbContext db, RefreshTokenService tokens, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.Email) || input.Email.Length > 200 || input.Password is null || input.Password.Length > 128) return Results.Unauthorized();
            var email = input.Email.Trim().ToLowerInvariant();
            var user = await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Email == email, ct);
            var hasher = new PasswordHasher<User>();
            if (user?.PasswordHash is null)
            {
                // Still perform a password hash to avoid the trivial empty-user path.
                _ = hasher.HashPassword(new User(), input.Password); return Results.Unauthorized();
            }
            var verified = hasher.VerifyHashedPassword(user, user.PasswordHash, input.Password);
            if (verified == PasswordVerificationResult.Failed || !user.IsActive || user.IsLocked || !ActorAccountPolicy.HasValidActorConfiguration(user)) return Results.Unauthorized();
            if (verified == PasswordVerificationResult.SuccessRehashNeeded) { user.PasswordHash = hasher.HashPassword(user, input.Password); await db.SaveChangesAsync(ct); }
            var refresh = await tokens.CreateRefreshTokenAsync(user.Id); return Results.Ok(tokens.CreateAuthResponse(user, refresh));
        }).AllowAnonymous().RequireRateLimiting("googleSignInLimit");
    }
}
