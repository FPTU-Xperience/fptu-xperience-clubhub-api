namespace AuthService.Models;

// Only the hash is persisted. Browser codes expire after one minute and need a PKCE verifier.
public sealed class StaffSsoTicket
{
    public string CodeHash { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string FamilyId { get; set; } = string.Empty;
    public int SecurityVersion { get; set; }
    public string TargetOrigin { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
}
