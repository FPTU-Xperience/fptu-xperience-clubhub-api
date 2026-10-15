namespace AdminService.Data;

public sealed class VerificationAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public int? ClubId { get; set; }
    public string SourceType { get; set; } = "CLUB";
    public string CapabilitiesJson { get; set; } = "[]";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public int AssignedBy { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class VerifiedContribution
{
    public int? AttendanceId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public string SourceType { get; set; } = "CLUB";
    public string SourceKey { get; set; } = "";
    public string? SourceId { get; set; }
    public int? ClubId { get; set; }
    public int? ActivityId { get; set; }
    public int? SessionId { get; set; }
    public string SemesterId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Status { get; set; } = "SUBMITTED";
    public Guid Version { get; set; } = Guid.NewGuid();
    public int RevisionOrdinal { get; set; }
    public Guid CurrentRevisionId { get; set; }
    public string ProposedAttributesJson { get; set; } = "";
    public string? EffectiveAttributesJson { get; set; }
    public string EvidenceRefsJson { get; set; } = "[]";
    public string SourceSnapshotJson { get; set; } = "";
    public string? PolicySnapshotJson { get; set; }
    public decimal RawPoints { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ContributionRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContributionId { get; set; }
    public int Ordinal { get; set; }
    public string Status { get; set; } = "";
    public string AttributesJson { get; set; } = "";
    public string EvidenceRefsJson { get; set; } = "[]";
    public int? ActorId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class ContributionDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContributionId { get; set; }
    public Guid TargetRevisionId { get; set; }
    public Guid RevisionId { get; set; }
    public int ReviewerUserId { get; set; }
    public Guid? AssignmentId { get; set; }
    public Guid? AssignmentVersion { get; set; }
    public string Decision { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class ContributionUpgradeRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContributionId { get; set; }
    public Guid ContributionVersion { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public string ProposedRole { get; set; } = "";
    public string? ProposedResult { get; set; }
    public string EvidenceRefsJson { get; set; } = "[]";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "SUBMITTED";
    public string? ReviewReason { get; set; }
    public int? ReviewedBy { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class ExperienceLedgerEffect
{
    public string? CalculationSnapshotJson { get; set; }
    public long Id { get; set; }
    public Guid ContributionId { get; set; }
    public Guid RevisionId { get; set; }
    public int UserId { get; set; }
    public string SemesterId { get; set; } = "";
    public string EffectType { get; set; } = "";
    public decimal Amount { get; set; }
    public Guid VerificationId { get; set; }
    public int? VerifiedByUserId { get; set; }
    public string VerificationMethod { get; set; } = "";
    public Guid PolicyVersion { get; set; }
    public string FormulaVersion { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string Reason { get; set; } = "";
    public Guid? EventId { get; set; }
}

public sealed class ExperienceLedgerAllocation
{
    public long Id { get; set; }
    public long EffectId { get; set; }
    public string Pillar { get; set; } = "";
    public decimal Amount { get; set; }
}

public sealed class PrivateContributionEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int OwnerUserId { get; set; }
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public byte[] Bytes { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class ManualAttendanceRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public int ClubId { get; set; }
    public int ActivityId { get; set; }
    public int SessionId { get; set; }
    public string Reason { get; set; } = "";
    public string EvidenceRefsJson { get; set; } = "[]";
    public string SourceSnapshotJson { get; set; } = "";
    public string Status { get; set; } = "PENDING";
    public Guid Version { get; set; } = Guid.NewGuid();
    public string RecognitionStatus { get; set; } = "NOT_REQUESTED";
    public Guid? ContributionId { get; set; }
    public string? FailureCode { get; set; }
    public int? ReviewedBy { get; set; }
    public string? ReviewReason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class ExperienceInbox
{
    public Guid EventId { get; set; }
    public string RoutingKey { get; set; } = "";
    public DateTimeOffset ProcessedAtUtc { get; set; }
}

public sealed class InvalidatedExperienceSource
{
    public long Id { get; set; }
    public int ActivityId { get; set; }
    public int? SessionId { get; set; }
    public int ActorId { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset InvalidatedAt { get; set; }
}

public sealed class ApprovedExperiencePolicy
{
    public string Scope { get; set; } = "PERIOD";
    public DateTimeOffset? EffectiveAt { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BenchmarkId { get; set; }
    public string SemesterId { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
    public string ApprovalBasis { get; set; } = "";
    public int ApprovedBy { get; set; }
    public DateTimeOffset ApprovedAt { get; set; }
    public string Environment { get; set; } = "PROJECT";
}


