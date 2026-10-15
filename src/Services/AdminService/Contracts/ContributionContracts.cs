using System.Text.Json.Serialization;
using ClubReportHub.Shared.Experience;

namespace AdminService.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SubmitClubContributionRequest(int SessionId, string ContributionKind,
    ContributionAttributes ProposedAttributes, string Message, IReadOnlyList<Guid> EvidenceRefs, Guid ExpectedActivityVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviseClubContributionRequest(Guid ExpectedVersion, ContributionAttributes ProposedAttributes,
    string Message, IReadOnlyList<Guid> EvidenceRefs);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewContributionRequest(string Decision, string Reason, Guid ExpectedVersion,
    ContributionAttributes? VerifiedAttributes = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestContributionUpgradeRequest(string ProposedRole, string? ProposedResult,
    IReadOnlyList<Guid> EvidenceRefs, string Reason, Guid ExpectedVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdjustContributionRequest(string Kind, string Reason, Guid ExpectedVersion,
    ContributionAttributes? CorrectedAttributes = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SubmitManualAttendanceRequest(string Reason, IReadOnlyList<Guid>? EvidenceRefs = null);

public sealed record ContributionResponse(Guid Id, int UserId, string SourceType, string? SourceId,
    int? ClubId, int? ActivityId, int? SessionId, string SemesterId, DateTimeOffset OccurredAt,
    string Title, string Message, string Status, Guid Version, Guid CurrentRevisionId,
    ContributionAttributes ProposedAttributes, ContributionAttributes? VerifiedAttributes,
    IReadOnlyList<Guid> EvidenceRefs, decimal RawPoints, string RecognitionStatus, DateTimeOffset UpdatedAt)
{
    public string SourceNamespace { get; init; } = "CONTRIBUTION";
    public string SourceKey { get; init; } = string.Empty;
    public bool IsAuthoritative { get; init; } = true;
    public CalculationBreakdown? CalculationBreakdown { get; init; }
}
