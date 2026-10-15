using System.Text.Json.Serialization;
using ClubReportHub.Shared.Experience;

namespace ActivityService.Contracts;

public sealed record AttendanceGeofence(double Latitude, double Longitude, double RadiusMeters);
public sealed record AttendancePolicyRequest(string Mode, DateTimeOffset OpensAt, DateTimeOffset ClosesAt,
    AttendanceGeofence? Geofence, bool ManualConfirmationEnabled, int QrRotationSeconds = 30);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateActivitySessionsRequest(Guid ExpectedVersion, AttendancePolicyRequest AttendancePolicy,
    DateTimeOffset? StartAt = null, DateTimeOffset? EndAt = null, string Timezone = "Asia/Ho_Chi_Minh",
    IReadOnlyList<int>? Weekdays = null, TimeOnly? StartTime = null, TimeOnly? EndTime = null,
    DateOnly? RangeStartDate = null, DateOnly? RangeEndDate = null, IReadOnlyList<DateOnly>? ExcludedDates = null,
    string? Location = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateActivitySessionRequest(Guid ExpectedVersion, DateTimeOffset? StartAt = null,
    DateTimeOffset? EndAt = null, string? Location = null, AttendancePolicyRequest? AttendancePolicy = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SessionDecisionRequest(Guid ExpectedVersion, string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AttendanceLocation(double Latitude, double Longitude, double AccuracyMeters);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SessionCheckInRequest(int SessionId, string Challenge, AttendanceLocation? Location = null);

public sealed record ActivitySessionResponse(int Id, int ActivityId, Guid SeriesId, string SemesterId,
    DateTimeOffset StartTime, DateTimeOffset EndTime, string Timezone, string Location, string Mode,
    string Status, Guid Version, object AttendancePolicy, ExperienceSourceSnapshot ScoringSnapshot);
