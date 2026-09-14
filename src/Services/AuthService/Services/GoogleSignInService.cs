using AuthService.Contracts;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ClubReportHub.Shared.Auth;

namespace AuthService.Services;

public enum GoogleSignInStatus
{
    Authenticated,
    InvalidGoogleCredential,
    NotAllowlisted,
    NotConfigured
}

public sealed record GoogleSignInResult(
    GoogleSignInStatus Status,
    AuthResponse? Response = null);

/// <summary>
/// Connects a verified Google identity to a pre-existing user in the local
/// roster. This service never creates a user during login.
/// </summary>
public sealed class GoogleSignInService(
    AuthDbContext db,
    IGoogleIdTokenValidator googleIdTokenValidator,
    RefreshTokenService refreshTokenService,
    IOptions<GoogleAuthenticationOptions> googleAuthenticationOptions)
{
    public async Task<GoogleSignInResult> SignInAsync(
        string credential,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(googleAuthenticationOptions.Value.ClientId))
        {
            return new GoogleSignInResult(GoogleSignInStatus.NotConfigured);
        }

        var identity = await googleIdTokenValidator.ValidateAsync(credential, cancellationToken);
        if (identity is null || !identity.EmailVerified)
        {
            return new GoogleSignInResult(GoogleSignInStatus.InvalidGoogleCredential);
        }

        var normalizedEmail = NormalizeEmail(identity.Email);

        // Once linked, subject is the stable Google identity. We still require
        // its currently verified e-mail to remain on the local roster.
        var user = await db.Users
            .Include(x => x.UserRoles)
            .ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.GoogleSubject == identity.Subject, cancellationToken);

        if (user is not null &&
            !string.Equals(NormalizeEmail(user.Email), normalizedEmail, StringComparison.OrdinalIgnoreCase))
        {
            return new GoogleSignInResult(GoogleSignInStatus.NotAllowlisted);
        }

        if (user is null)
        {
            user = await db.Users
                .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
                .SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);
        }

        if (user is null || !user.IsActive || user.IsLocked ||
            !ActorAccountPolicy.HasValidActorConfiguration(user))
        {
            return new GoogleSignInResult(GoogleSignInStatus.NotAllowlisted);
        }

        // // The semester roster represents active students. Staff and admins
        // // must be able to sign in even when preparing the next roster.
        // if (user.UserRoles.Any(x => x.Role.Name == AuthRoles.ClubMember))
        // {
        //     var isCurrentSemesterStudent = await db.SemesterStudents
        //         .AsNoTracking()
        //         .AnyAsync(x => x.EmailNormalized == normalizedEmail &&
        //                        x.IsActive &&
        //                        x.Semester.IsCurrent,
        //             cancellationToken);

        //     if (!isCurrentSemesterStudent)
        //     {
        //         return new GoogleSignInResult(GoogleSignInStatus.NotAllowlisted);
        //     }
        // }

        if (!string.IsNullOrWhiteSpace(user.GoogleSubject) &&
            !string.Equals(user.GoogleSubject, identity.Subject, StringComparison.Ordinal))
        {
            return new GoogleSignInResult(GoogleSignInStatus.NotAllowlisted);
        }

        if (string.IsNullOrWhiteSpace(user.GoogleSubject))
        {
            user.GoogleSubject = identity.Subject;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The unique GoogleSubject index protects against concurrent
                // linking of one Google account to more than one local user.
                return new GoogleSignInResult(GoogleSignInStatus.NotAllowlisted);
            }
        }

        var refreshToken = await refreshTokenService.CreateRefreshTokenAsync(user.Id);
        return new GoogleSignInResult(
            GoogleSignInStatus.Authenticated,
            refreshTokenService.CreateAuthResponse(user, refreshToken));
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
