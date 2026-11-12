using System.Text.Json.Serialization;

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
    string? UpdatedBy)
{
    public DiscoveryCaptureSettings DiscoveryCapture { get; init; } = DiscoveryCaptureSettings.Default;
}

public sealed record UpdatePlatformSettingsRequest(
    string? GoogleDomain,
    string? TimetableUrl,
    bool? InAppNotifications,
    bool? EmailNotifications,
    string? Digest,
    int? RateLimit,
    int? Retention)
{
    public DiscoveryCaptureUpdate? DiscoveryCapture { get; init; }
}

public sealed record DiscoveryCaptureSettings(bool ClickEnabled, bool ViewEnabled,
    int ViewSeconds, int VisiblePercent, Guid Version)
{
    public static DiscoveryCaptureSettings Default { get; } = new(true, true, 5, 50, Guid.Empty);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DiscoveryCaptureUpdate(
    [property: JsonRequired] bool ClickEnabled,
    [property: JsonRequired] bool ViewEnabled,
    [property: JsonRequired] int ViewSeconds,
    [property: JsonRequired] int VisiblePercent,
    [property: JsonRequired] Guid ExpectedVersion);

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
