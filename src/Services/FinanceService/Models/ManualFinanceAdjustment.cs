namespace FinanceService.Models;

public sealed class ManualFinanceAdjustment
{
    public int Id { get; set; }
    public int FinanceTransactionId { get; set; }
    public FinanceTransaction FinanceTransaction { get; set; } = null!;
    public string IdempotencyKey { get; set; } = string.Empty;
    public int CreatedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
