namespace ActivityService.Models;

// Action workflows deliberately have no raw-XP/credit ledger or award effects.
public sealed class ActivityApplication
{
    public int Id { get; set; }
    public int ClubId { get; set; }
    public string CampusCode { get; set; } = "";
    public string SemesterId { get; set; } = "";
    public int Version { get; set; } = 1;
    public int Revision { get; set; } = 1;
    public string Status { get; set; } = "DRAFT";
    public string ContentJson { get; set; } = "{}";
    public int CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ActivityApplicationHistory
{
    public long Id { get; set; }
    public int ApplicationId { get; set; }
    public int Version { get; set; }
    public int Revision { get; set; }
    public string Status { get; set; } = "";
    public string ContentJson { get; set; } = "{}";
    public int ActorId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ActivityExecution
{
    public int ActivityId { get; set; }
    public int ApplicationId { get; set; }
    public int Version { get; set; } = 1;
    public int PublicationVersion { get; set; } = 1;
    public bool IsPublished { get; set; }
}

public sealed class ActivityActionRule
{
    public int Id { get; set; }
    public int ActivityId { get; set; }
    public string ClientId { get; set; } = "";
    public string DefinitionJson { get; set; } = "{}";
}

public sealed class ActivityRecruitment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ActivityId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Visibility { get; set; } = "PUBLIC";
    public string Eligibility { get; set; } = "SCHOOL_STUDENTS";
    public string Status { get; set; } = "DRAFT";
}

public sealed class ActivityRecruitmentPosition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecruitmentId { get; set; }
    public int ActionRuleId { get; set; }
    public int Capacity { get; set; }
    public string AssignmentKind { get; set; } = "STAFF";
    public bool IsArchived { get; set; }
}

public sealed class ActivityStaffAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ActivityId { get; set; }
    public int ActionRuleId { get; set; }
    public Guid? PositionId { get; set; }
    public int UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string AssignmentKind { get; set; } = "STAFF";
    public string Status { get; set; } = "ASSIGNED";
    public int CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? CancelledBy { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}

public sealed class ActivityActionCompletion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ActivityId { get; set; }
    public Guid AssignmentId { get; set; }
    public int ActionRuleId { get; set; }
    public int UserId { get; set; }
    public int Version { get; set; } = 1;
    public string OccurrenceKey { get; set; } = "";
    public string OccurrenceLabel { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Status { get; set; } = "PENDING";
    public DateTimeOffset SubmittedAt { get; set; }
    public int? VerifiedBy { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
}

public sealed class ActivityExecutionAudit
{
    public long Id { get; set; }
    public int ActivityId { get; set; }
    public int Version { get; set; }
    public string Action { get; set; } = "";
    public int ActorId { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}
