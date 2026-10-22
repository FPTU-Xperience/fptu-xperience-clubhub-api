namespace ClubService.Models;

public static class ClubMembershipSources
{
    public const string Unknown = "UNKNOWN";
    public const string Application = "APPLICATION";
    public const string Invitation = "INVITATION";
}

public sealed class ClubMembershipAction
{
    public long Id { get; set; }
    public int MembershipId { get; set; }
    public int ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string PreviousStatus { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid PreviousVersion { get; set; }
    public Guid Version { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}
