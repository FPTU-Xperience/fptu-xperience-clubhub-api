namespace AdminService.Data;

public sealed class Quest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Community";

    public string Kind { get; set; } = "Cá nhân";

    public string Scope { get; set; } = "Toàn bộ sinh viên";

    public int RewardXp { get; set; } = 30;

    public int TargetParticipants { get; set; } = 100;

    public string SemesterCode { get; set; } = "FALL2026";

    public string CampusCode { get; set; } = "GLOBAL";

    public string Icon { get; set; } = "sparkles";

    public DateTimeOffset? DeadlineUtc { get; set; }

    public string Status { get; set; } = "published";

    public int? CreatedByUserId { get; set; }

    public string? CreatedByName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<QuestParticipant> Participants { get; set; } = [];
}
