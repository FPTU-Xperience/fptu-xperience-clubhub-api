namespace ClubReportHub.Shared.Events;

// Temporal lifecycle only. This event does not attest attendance or award XP.
public sealed record ActivityLifecycleChangedEvent(Guid EventId, DateTimeOffset OccurredAtUtc,
    int ActivityId, int ClubId, int? SessionId, string PreviousStatus, string Status, Guid Version,
    string Reason) : IntegrationEvent(EventId, OccurredAtUtc)
{
    public const string RoutingKey = "activity.lifecycle.changed";
}
