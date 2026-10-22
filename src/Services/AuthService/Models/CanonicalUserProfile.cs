namespace AuthService.Models;

public sealed class CanonicalUserProfile
{
    public int UserId { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = "";
    public string About { get; set; } = "";
    public string SkillsJson { get; set; } = "[]";
    public string InterestsJson { get; set; } = "[]";
    public string? PhoneNumber { get; set; }
    public string? Headline { get; set; }
    public string? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public string? AvatarUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
