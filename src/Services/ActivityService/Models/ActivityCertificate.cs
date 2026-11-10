namespace ActivityService.Models;

public sealed class ActivityCertificateDefinition
{
    public Guid Id { get; set; }
    public int ActivityId { get; set; }
    public string Role { get; set; } = "";
    public string Title { get; set; } = "";
    public string BadgePng { get; set; } = "";
    public string? FramePng { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class ActivityCertificate
{
    public Guid Id { get; set; }
    public int ActivityId { get; set; }
    public Guid DefinitionId { get; set; }
    public ActivityCertificateDefinition Definition { get; set; } = null!;
    public string RecipientKind { get; set; } = "";
    public int RecipientId { get; set; }
    public string RecipientName { get; set; } = "";
    public string ActivityTitle { get; set; } = "";
    public string IssuerName { get; set; } = "";
    public DateTimeOffset IssuedAtUtc { get; set; }
}

public sealed class ActivityCertificateDisplay
{
    public int UserId { get; set; }
    public Guid Version { get; set; }
    public Guid? BadgeId { get; set; }
    public Guid? FrameId { get; set; }
    public int BadgeX { get; set; } = 80;
    public int BadgeY { get; set; } = 40;
    public int BadgeSize { get; set; } = 15;
}
