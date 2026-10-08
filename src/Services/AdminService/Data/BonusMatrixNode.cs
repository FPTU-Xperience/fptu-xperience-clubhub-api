namespace AdminService.Data;

public sealed class BonusMatrixNode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int Position { get; set; }

    public string Label { get; set; } = string.Empty;

    public int Multiplier { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
