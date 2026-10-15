using ClubReportHub.Shared.Experience;

namespace ClubReportHub.Shared.Events;

public sealed record AttendanceVerifiedEvent(Guid EventId, DateTimeOffset OccurredAtUtc, int UserId,
    int AttendanceId, string VerificationMethod, int? VerifiedByUserId, ExperienceSourceSnapshot Source)
    : IntegrationEvent(EventId, OccurredAtUtc);

public sealed record ManualAttendanceDecisionEvent(Guid EventId, DateTimeOffset OccurredAtUtc, Guid RequestId,
    int UserId, int VerifiedByUserId, string Reason, ExperienceSourceSnapshot Source)
    : IntegrationEvent(EventId, OccurredAtUtc);

public sealed record AttendanceRecognitionEvent(Guid EventId, DateTimeOffset OccurredAtUtc, int AttendanceId,
    Guid? ContributionId, Guid? ManualRequestId, string Status, string? FailureCode)
    : IntegrationEvent(EventId, OccurredAtUtc);

public sealed record ExperienceSourceCancelledEvent(Guid EventId, DateTimeOffset OccurredAtUtc, int ActivityId,
    int? SessionId, int ClubId, int ActorId, string Reason)
    : IntegrationEvent(EventId, OccurredAtUtc);

public sealed record ExperienceNotificationEvent(Guid EventId, DateTimeOffset OccurredAtUtc,
    int ClubId, IReadOnlyList<int> RecipientUserIds, string Kind, string Title, string TargetPath)
    : IntegrationEvent(EventId, OccurredAtUtc);

public static class ExperienceRoutingKeys
{
    public const string AttendanceVerified = "experience.attendance.verified";
    public const string ManualAttendanceDecision = "experience.attendance.manual-decision";
    public const string AttendanceRecognition = "experience.attendance.recognition";
    public const string SourceCancelled = "experience.source.cancelled";
    public const string Notification = "experience.notification";
}
