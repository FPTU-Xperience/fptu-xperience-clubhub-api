namespace AuthService.Models;

public sealed class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    // Google `sub` is the stable identifier for the Google account that has
    // been linked to this pre-approved roster entry. It is intentionally not
    // inferred from an unverified e-mail address sent by a client.
    public string? GoogleSubject { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsLocked { get; set; }
    public int SecurityVersion { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<UserRole> UserRoles { get; set; } = [];
}
