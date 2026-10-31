using System.Text.Json.Serialization;

namespace ActivityService.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActivityApplicationInput(int ClubId, string? Period, string? Title, string? Description,
    string? Objective, string? Location, DateTimeOffset? StartTimeUtc, DateTimeOffset? EndTimeUtc,
    string? Scale, bool IndependentActionsConfirmed, IReadOnlyList<ActivityActionInput>? Actions,
    ApplicationRecruitmentInput? Recruitment, int? ExpectedVersion = null, bool IsInternal = true);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActivityActionInput(string ClientId, string? Name, decimal BasePoints,
    string ContributionRole, string ScaleSource, string? Scale, int ExpectedPeople,
    int ExpectedOccurrencesPerPerson, int MaxAwardsPerPerson, int MaxAwardCount,
    string VerificationMethod, string CompletionMode, string StackingRule);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApplicationRecruitmentInput(string Visibility, string Eligibility,
    IReadOnlyList<ApplicationPositionInput>? Positions);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApplicationPositionInput(string ActionClientId, int Capacity);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActionVersionInput(int ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApplicationReviewInput(int ExpectedVersion, string Decision, string? Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActivityPublicationInput(bool IsPublished, int ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StaffAssignmentInput(int ExpectedVersion, int UserId, int ActionRuleId, string AssignmentKind);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecruitmentInput(int ExpectedVersion, string Title, string? Description,
    string Visibility, string Eligibility, IReadOnlyList<RecruitmentPositionInput>? Positions);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecruitmentPositionInput(int ActionRuleId, int Capacity, string AssignmentKind);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecruitmentStatusInput(int ExpectedVersion, string Status);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActionCompletionInput(int ExpectedVersion, string OccurrenceKey, string OccurrenceLabel, string Evidence);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActionVerificationInput(int ExpectedVersion, int CompletionVersion);
