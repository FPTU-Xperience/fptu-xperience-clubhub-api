namespace NotificationService.Models;

public sealed class NotificationReadReceipt
{
    public int NotificationId { get; set; }
    public int UserId { get; set; }
    public DateTimeOffset ReadAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
