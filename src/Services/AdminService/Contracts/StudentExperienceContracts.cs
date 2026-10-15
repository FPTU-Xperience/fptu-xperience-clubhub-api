using ClubReportHub.Shared.Experience;

namespace AdminService.Contracts;

public sealed record CalculationBreakdown(decimal TierBase, decimal RoleMultiplier, decimal ScaleMultiplier,
    decimal CappedRoleScale, decimal ResultBonus, decimal RawPoints,
    IReadOnlyList<PillarWeight> PillarWeights, IReadOnlyDictionary<string, decimal> AllocatedPoints,
    Guid PolicyVersion, string FormulaVersion, Guid? VerificationId, string Provenance);

public sealed record LedgerCalculationSnapshot(CalculationBreakdown After, decimal BeforeRawPoints,
    decimal DeltaAmount, IReadOnlyDictionary<string, decimal> BeforeAllocations,
    ContributionAttributes VerifiedAttributes);

public sealed record LegacyContributionItem(Guid Id, int UserId, string SourceType, string SourceNamespace,
    string SourceKey, string? SourceId, string? SemesterId, DateTimeOffset OccurredAt,
    string Title, string Status, bool IsAuthoritative, decimal? LegacyRawPointsPreview,
    object? CalculationBreakdown, object? VerifiedAttributes, IReadOnlyList<Guid> EvidenceRefs);

public sealed record RadarReadResult(StudentRadarResponse? Radar, int StatusCode = 200,
    string? Code = null, string? Message = null);

public sealed record StudentContributionPage(IReadOnlyList<object> Items, int Page, int PageSize,
    int Total, int TotalItems, int TotalPages, string LegacySemesterAssignment = "UNASSIGNED");

