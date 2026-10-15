namespace AuthService.Models;

public sealed class PublicMediaIntent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ActorId { get; set; }
    public string Kind { get; set; } = "";
    public int OwnerId { get; set; }
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Key { get; set; } = "";
    public string PublicUrl { get; set; } = "";
    public string? VerifiedUrl { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
