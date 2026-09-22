namespace ClubReportHub.Shared.Messaging;

public sealed class RedisStreamOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; init; } = "localhost:6379";

    /// <summary>
    /// Name of the Redis stream used for all ClubReportHub events.
    /// </summary>
    public string StreamName { get; init; } = "clubreporthub-events";

    /// <summary>
    /// Consumer group name used by the NotificationService.
    /// </summary>
    public string ConsumerGroup { get; init; } = "notification-service";

    /// <summary>
    /// Prefix for consumer instance names (e.g. "notification-service-instance-{MachineName}-{ProcessId}").
    /// </summary>
    public string ConsumerNamePrefix { get; init; } = "consumer";

    /// <summary>
    /// Number of messages to read per XREADGROUP call.
    /// </summary>
    public int BatchSize { get; init; } = 5;

    /// <summary>
    /// Polling interval in milliseconds when the stream is empty.
    /// </summary>
    public int PollIntervalMs { get; init; } = 1000;

    /// <summary>
    /// Maximum retry attempts when a Redis operation fails.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Base delay in milliseconds for exponential back-off between retries.
    /// </summary>
    public int RetryBaseDelayMs { get; init; } = 500;

    /// <summary>
    /// Maximum times a message can be delivered/retried before being moved to the dead-letter queue.
    /// </summary>
    public int MaxDeliveryAttempts { get; init; } = 3;

    /// <summary>
    /// Name of the Redis stream used for dead-lettered events that failed processing.
    /// </summary>
    public string DeadLetterStreamName { get; init; } = "clubreporthub-events-dlq";

    /// <summary>
    /// Maximum length of the Redis stream. When new messages are published via XADD,
    /// the stream will be approximately trimmed to this length using MAXLEN ~ N.
    /// Default is 10,000 entries (approx. 20-30 MB under typical event payload sizes).
    /// </summary>
    public int MaxStreamLength { get; init; } = 10000;

    /// <summary>
    /// Maximum length of the Redis dead-letter queue stream. When dead-letter messages
    /// are published via XADD, the stream will be approximately trimmed to this length.
    /// Default is 5,000 entries.
    /// </summary>
    public int MaxDeadLetterStreamLength { get; init; } = 5000;

    /// <summary>
    /// Whether to use approximate trimming (MAXLEN ~ N) for zero-cost O(1) listpack node trimming.
    /// Default is true.
    /// </summary>
    public bool UseApproximateTrimming { get; init; } = true;

    /// <summary>
    /// Socket keep-alive interval in seconds.
    /// Default is 60 seconds.
    /// </summary>
    public int KeepAliveSeconds { get; init; } = 60;

    /// <summary>
    /// Connection timeout in milliseconds.
    /// Default is 5,000 milliseconds.
    /// </summary>
    public int ConnectTimeoutMs { get; init; } = 5000;

    /// <summary>
    /// Synchronous operation timeout in milliseconds.
    /// Default is 5,000 milliseconds.
    /// </summary>
    public int SyncTimeoutMs { get; init; } = 5000;

    /// <summary>
    /// Interval in milliseconds between checks for stale pending messages across the consumer group.
    /// Default is 5,000 milliseconds (5 seconds).
    /// </summary>
    public int PendingRecoveryIntervalMs { get; init; } = 5000;

    /// <summary>
    /// Minimum idle duration in milliseconds before a pending unacknowledged message is considered abandoned or eligible for retry.
    /// Default is 10,000 milliseconds (10 seconds).
    /// </summary>
    public int PendingMessageIdleThresholdMs { get; init; } = 10000;
}
