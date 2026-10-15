namespace AdminService.Data;

public sealed class QuestCompletionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ParticipantId { get; set; }
    public Guid CriteriaVersion { get; set; }
    public long LedgerRevision { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    public DateTimeOffset RecordedAt { get; set; }
}

