using System.Text.Json.Serialization;
using ClubReportHub.Shared.Experience;

namespace ActivityService.Contracts;

public sealed record ActivityResponse(
    int Id,
    int ClubId,
    string ClubName,
    string Title,
    string Description,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset EndTimeUtc,
    IReadOnlyCollection<int> MeetingDays,
    string Location,
    string Status,
    int CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyCollection<ActivityParticipantResponse> Participants,
    IReadOnlyCollection<ActivityAttendanceResponse> Attendances)
{
    public Guid Version { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PublicationStatus { get; init; } = "LEGACY";
    public bool? IsPublished { get; init; }
    public int? PublicationVersion { get; init; }
    public int? SourceApplicationId { get; init; }
    public ActivityPublicationCapabilities PublicationCapabilities { get; init; } = new(false);
    public string DiscoveryVisibility { get; init; } = "CLUB_ONLY";
    public string? SemesterId { get; init; }
    public int? Capacity { get; init; }
    public DateTimeOffset? RegistrationOpensAt { get; init; }
    public DateTimeOffset? RegistrationClosesAt { get; init; }
    public string Eligibility { get; init; } = "CLUB_MEMBER";
    public IReadOnlyList<PillarWeight> PillarWeights { get; init; } = [];
    public string? CoverImageUrl { get; init; }
    public IReadOnlyList<ActivityResourceResponse>? Resources { get; init; }
    public ActivityResourceCapabilities ResourceCapabilities { get; init; } = new(false, false, []);
    public string ResourceAvailability { get; init; } = "NOT_REQUESTED";
    public int RegisteredCount { get; init; }
    public int VerifiedAttendanceCount { get; init; }
    public string EffortTier { get; init; } = "T1";
    public string Scale { get; init; } = "Club";
    public Guid? PolicyVersion { get; init; }
    public string? EvidenceRequirements { get; init; }
}

public sealed record ActivityPublicationCapabilities(bool CanManage);

public sealed record ActivityParticipantResponse(
    int Id,
    int UserId,
    string FullName,
    string AttendanceStatus,
    DateTimeOffset RegisteredAtUtc);

public sealed record ActivityAttendanceResponse(
    int Id,
    int UserId,
    string FullName,
    DateOnly AttendanceDate,
    string Status,
    string? Note,
    DateTimeOffset? CheckedInAtUtc,
    int? CheckedInByUserId)
{
    public int? SessionId { get; init; }
    public Guid Version { get; init; }
    public string VerificationStatus { get; init; } = "UNVERIFIED";
    public string? VerificationMethod { get; init; }
    public string RecognitionStatus { get; init; } = "NOT_REQUESTED";
    public Guid? ContributionId { get; init; }
}

public sealed record CreateActivityRequest(
    int ClubId,
    string ClubName,
    string Title,
    string Description,
    DateTimeOffset? StartTimeUtc,
    DateTimeOffset? EndTimeUtc,
    string Location,
    IReadOnlyCollection<int>? MeetingDays,
    string? Name = null,
    DateTimeOffset? StartAt = null,
    DateTimeOffset? EndAt = null, string? SemesterId = null, string? EffortTier = null, string? Scale = null,
    IReadOnlyList<PillarWeight>? PillarWeights = null, Guid? PolicyVersion = null, int? Capacity = null,
    DateTimeOffset? RegistrationOpensAt = null, DateTimeOffset? RegistrationClosesAt = null,
    string? Eligibility = null, string? EvidenceRequirements = null, string DiscoveryVisibility = "CLUB_ONLY");

public sealed record CreateActivityFromApprovedReportRequest(
    int ReportId,
    int ReportDetailId,
    int ClubId,
    string ClubName,
    string Title,
    string Description,
    DateOnly ActivityDate,
    string Location);

public sealed record UpdateActivityRequest(
    string Title,
    string Description,
    DateTimeOffset? StartTimeUtc,
    DateTimeOffset? EndTimeUtc,
    string Location,
    string Status,
    IReadOnlyCollection<int>? MeetingDays);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateActivityDetailsRequest(
    string? Title = null,
    string? Name = null,
    string? Description = null,
    DateTimeOffset? StartTimeUtc = null,
    DateTimeOffset? StartAt = null,
    DateTimeOffset? EndTimeUtc = null,
    DateTimeOffset? EndAt = null,
    string? Location = null,
    IReadOnlyCollection<int>? MeetingDays = null,
    string? Operation = null, Guid? ExpectedVersion = null, string? SemesterId = null,
    string? EffortTier = null, string? Scale = null, IReadOnlyList<PillarWeight>? PillarWeights = null,
    Guid? PolicyVersion = null, int? Capacity = null, DateTimeOffset? RegistrationOpensAt = null,
    DateTimeOffset? RegistrationClosesAt = null, string? Eligibility = null, string? EvidenceRequirements = null,
    string? DiscoveryVisibility = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterActivityParticipantRequest(int? UserId, string? FullName);

public sealed record MemberStatisticsInput(int UserId, DateTimeOffset JoinedAtUtc);
public sealed record MemberStatisticsQuery(IReadOnlyCollection<MemberStatisticsInput> Members);
public sealed record MemberStatisticsResponse(int UserId, int EligibleActivities, int AttendedActivities, decimal ParticipationRate);
public sealed record MemberStatisticsDetailQuery(int UserId, DateTimeOffset JoinedAtUtc, int Page = 1, int PageSize = 20);
public sealed record MemberActivityHistoryResponse(int ActivityId, string Title, DateTimeOffset StartTimeUtc, string ActivityStatus, string AttendanceStatus);
public sealed record MemberStatisticsDetailResponse(MemberStatisticsResponse Statistics, IReadOnlyCollection<MemberActivityHistoryResponse> Items, int Page, int PageSize, int TotalItems, int TotalPages);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateMemberAttendanceRequest(string Status, string? Note, int? SessionId = null,
    Guid? ExpectedVersion = null, Guid? ExpectedSessionVersion = null, string? Reason = null);
public sealed record BulkAttendanceItemRequest(int MemberId, string Status, string? Note, Guid? ExpectedVersion = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BulkUpdateAttendanceRequest(IReadOnlyCollection<BulkAttendanceItemRequest> Items,
    int? SessionId = null, Guid? ExpectedSessionVersion = null, string? Reason = null);
public sealed record AttendanceMemberResponse(int MemberId, int UserId, string FullName, string Email, string PhoneNumber, string Role, DateTimeOffset JoinedAtUtc, string Status, string? Note, DateTimeOffset? CheckedInAtUtc, int? CheckedInByUserId);
public sealed record ActivityAttendanceManagementResponse(int ActivityId, int ClubId, string Title, DateTimeOffset StartTimeUtc, string ActivityStatus, IReadOnlyCollection<AttendanceMemberResponse> Items, int Page, int PageSize, int TotalItems, int TotalPages, int PresentCount, int AbsentCount, int ExcusedCount, int LateCount, int NotMarkedCount);

public sealed record MyWeeklyAttendanceResponse(
    int ActivityId,
    string Title,
    IReadOnlyCollection<int> MeetingDays,
    DateOnly VietnamDate,
    bool IsScheduledToday,
    bool AlreadyCheckedInToday,
    bool CanCheckInToday,
    int ScheduledDays,
    int AttendedDays,
    decimal AttendanceRate,
    IReadOnlyCollection<ActivityAttendanceResponse> History,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);
