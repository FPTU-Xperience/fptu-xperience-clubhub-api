using System.Text.Json.Serialization;
using ClubReportHub.Shared.Experience;

namespace AdminService.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateVerificationAssignmentRequest(int UserId, string SourceType, int? ClubId,
    IReadOnlyList<string> Capabilities, DateTimeOffset StartsAt, DateTimeOffset? ExpiresAt, string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateVerificationAssignmentRequest(Guid ExpectedVersion, bool IsRevoked, string Reason,
    DateTimeOffset? ExpiresAt = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveExperiencePolicyRequest(Guid ExpectedVersion, string Environment,
    string ApprovalBasis, bool ParametersP01ThroughP06Agreed, string CancellationRule = "REVERSE_SOURCE_INVALIDATION",
    string Scope = "PERIOD", DateTimeOffset? EffectiveAt = null);

public sealed record ApprovedExperiencePolicySnapshot(ExperiencePolicyReference Policy, Guid BenchmarkId,
    IReadOnlyDictionary<string, decimal> TauByPillar, decimal ThresholdStarter, decimal ThresholdPractitioner,
    decimal ThresholdLeader, decimal MinPillarScoreAllRounder, decimal MinJAllRounder,
    string IndexFormulaVersion = "ERI_MEAN_PIELOU_SATURATED_V1", string Scope = "PERIOD", DateTimeOffset? EffectiveAt = null);
