using AuthService.Models;
using ClubReportHub.Shared.Auth;

namespace AuthService.Services;

public static class ActorAccountPolicy
{
    private static readonly HashSet<string> GoogleActorRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        AuthRoles.Admin,
        AuthRoles.StudentAffairsAdmin,
        AuthRoles.ClubManager,
        AuthRoles.ClubMember
    };

    public static bool IsAllowedGoogleActorRole(string? roleName) =>
        !string.IsNullOrWhiteSpace(roleName) && GoogleActorRoles.Contains(roleName);

    public static bool HasValidActorConfiguration(User user)
    {
        var roles = user.UserRoles
            .Select(x => x.Role.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // The deployed demo explicitly exposes only its three business actors.
        // Other legacy role definitions are retained for policy compatibility,
        // but cannot enter through Google sign-in.
        return roles.Length == 1 && IsAllowedGoogleActorRole(roles[0]);
    }
}
