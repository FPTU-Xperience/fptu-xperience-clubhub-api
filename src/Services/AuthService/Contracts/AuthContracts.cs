using System.ComponentModel.DataAnnotations;

namespace AuthService.Contracts;

/// <summary>
/// Google Identity Services returns the signed ID token in its `credential`
/// field. The client must forward that value unchanged; e-mail and roles are
/// never accepted as login input.
/// </summary>
public sealed record GoogleLoginRequest(
    [Required, StringLength(8192)] string Credential);

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
    bool IsLocked);

public sealed record CreateUserRequest(
    [StringLength(100)] string Username,
    [StringLength(200)] string FullName,
    [StringLength(200), EmailAddress] string Email,
    IReadOnlyCollection<string> Roles);

public sealed record UpdateUserRequest(
    [StringLength(200)] string FullName,
    [StringLength(200), EmailAddress] string Email,
    bool IsActive,
    IReadOnlyCollection<string> Roles);

public sealed record CreateRoleRequest([StringLength(50)] string Name);
