namespace AdminService.Data;

public sealed class QuestParticipant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid QuestId { get; set; }

    public Quest? Quest { get; set; }

    public int StudentUserId { get; set; }

    public string StudentName { get; set; } = string.Empty;

    public string StudentEmail { get; set; } = string.Empty;

    public string CampusCode { get; set; } = "HAN";

    public string Status { get; set; } = "Joined";

    public DateTimeOffset JoinedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? VerifiedByName { get; set; }

    public string? Note { get; set; }
}
