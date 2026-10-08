namespace AdminService.Contracts;

public sealed record PlatformSettingsResponse(
    Guid Id,
    string GoogleDomain,
    string? TimetableUrl,
    bool InAppNotifications,
    bool EmailNotifications,
    string Digest,
    int RateLimit,
    int Retention,
    DateTimeOffset UpdatedAtUtc,
    string? UpdatedBy);

public sealed record UpdatePlatformSettingsRequest(
    string? GoogleDomain,
    string? TimetableUrl,
    bool? InAppNotifications,
    bool? EmailNotifications,
    string? Digest,
    int? RateLimit,
    int? Retention);

public sealed record SystemServiceHealthInfo(
    string Name,
    string? Endpoint,
    string Status,
    string? Description);

public sealed record SystemHealthStatsResponse(
    int Accounts,
    int Clubs,
    int Ledger,
    int Audit,
    IReadOnlyCollection<SystemServiceHealthInfo> Services);
