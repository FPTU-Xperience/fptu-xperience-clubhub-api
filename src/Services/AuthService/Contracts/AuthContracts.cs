using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AuthService.Contracts;

/// <summary>
/// Google Identity Services returns the signed ID token in its `credential`
/// field. The client must forward that value unchanged; e-mail and roles are
/// never accepted as login input.
/// </summary>
public sealed record GoogleLoginRequest(
    [Required, StringLength(8192)] string Credential);

public sealed record DevLoginRequest(
    [Required, EmailAddress, StringLength(200)] string Email);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    UserSummary User,
    string? RefreshToken = null,
    DateTimeOffset? RefreshTokenExpiresAtUtc = null);

public sealed record UserSummary(
    int Id,
    string Username,
    string FullName,
    string Email,
    IReadOnlyCollection<string> Roles,
    bool IsActive,
    bool IsLocked,
    string? CampusCode = null);

public sealed record CreateUserRequest(
    [StringLength(100)] string Username,
    [StringLength(200)] string FullName,
    [StringLength(200), EmailAddress] string Email,
    IReadOnlyCollection<string> Roles,
    string? CampusCode = null);

public sealed record UpdateUserRequest(
    [StringLength(200)] string FullName,
    [StringLength(200), EmailAddress] string Email,
    bool IsActive,
    IReadOnlyCollection<string> Roles,
    string? CampusCode = null);

public sealed record CreateRoleRequest([StringLength(50)] string Name);

public sealed record RoleStatItem(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("count")] int Count);

public sealed record RoleStatsResponse(
    [property: JsonPropertyName("totalUsers")] int TotalUsers,
    [property: JsonPropertyName("activeUsers")] int ActiveUsers,
    [property: JsonPropertyName("lockedUsers")] int LockedUsers,
    [property: JsonPropertyName("ADMIN")] int Admin,
    [property: JsonPropertyName("STUDENT_AFFAIRS_ADMIN")] int StudentAffairsAdmin,
    [property: JsonPropertyName("CLUB_MANAGER")] int ClubManager,
    [property: JsonPropertyName("CLUB_MEMBER")] int ClubMember,
    [property: JsonPropertyName("byRole")] IReadOnlyDictionary<string, int> ByRole,
    [property: JsonPropertyName("items")] IReadOnlyList<RoleStatItem> Items)
{
    public static RoleStatsResponse Create(
        int totalUsers,
        int activeUsers,
        int lockedUsers,
        IReadOnlyDictionary<string, int> byRole,
        IReadOnlyList<RoleStatItem> items)
    {
        var admin = byRole.GetValueOrDefault("ADMIN", 0) + byRole.GetValueOrDefault("SYSTEM_ADMIN", 0);
        var ctsv = byRole.GetValueOrDefault("STUDENT_AFFAIRS_ADMIN", 0);
        var manager = byRole.GetValueOrDefault("CLUB_MANAGER", 0);
        var member = byRole.GetValueOrDefault("CLUB_MEMBER", 0);

        return new RoleStatsResponse(
            TotalUsers: totalUsers,
            ActiveUsers: activeUsers,
            LockedUsers: lockedUsers,
            Admin: admin,
            StudentAffairsAdmin: ctsv,
            ClubManager: manager,
            ClubMember: member,
            ByRole: byRole,
            Items: items);
    }
}
