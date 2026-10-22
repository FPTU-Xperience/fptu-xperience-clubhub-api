namespace AdminService.Data;

public sealed class SemesterRoster
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SemesterId { get; set; }
    public string CampusCode { get; set; } = "";
    public Guid Version { get; set; }
    public Guid RevisionId { get; set; }
}
public sealed class SemesterRosterRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RosterId { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public Guid? ImportId { get; set; }
    public Guid? SourceRevisionId { get; set; }
    public string Source { get; set; } = "IMPORT";
    public int ActorId { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
}
public sealed class SemesterRosterRow
{
    public long Id { get; set; }
    public Guid RevisionId { get; set; }
    public int UserId { get; set; }
    public string Email { get; set; } = "";
    public string StudentCode { get; set; } = "";
    public string CampusCode { get; set; } = "";
    public bool Active { get; set; }
}
public sealed class SemesterRosterImport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SemesterId { get; set; }
    public string CampusCode { get; set; } = "";
    public Guid BaseVersion { get; set; }
    public int ActorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string CandidatesJson { get; set; } = "[]";
    public string ErrorsJson { get; set; } = "[]";
}
