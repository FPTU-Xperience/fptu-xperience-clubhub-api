namespace AdminService.Data;

public sealed class Semester
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Semester code, e.g. "FALL2026", "SUMMER2026", "SP27".
    /// </summary>
    public string SemesterCode { get; set; } = string.Empty;

    /// <summary>
    /// Display label, e.g. "Fall 2026", "Summer 2026".
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Academic year, e.g. "2026-2027".
    /// </summary>
    public string AcademicYear { get; set; } = string.Empty;

    /// <summary>
    /// Start date for the semester period.
    /// </summary>
    public DateTimeOffset? StartDate { get; set; }

    /// <summary>
    /// End date for the semester period.
    /// </summary>
    public DateTimeOffset? EndDate { get; set; }

    /// <summary>
    /// Connected threshold XP required (default 200).
    /// </summary>
    public int Threshold { get; set; } = 200;

    /// <summary>
    /// XP per level progression (default 100).
    /// </summary>
    public int XpPerLevel { get; set; } = 100;

    /// <summary>
    /// Whether public rankings are enabled for this semester.
    /// </summary>
    public bool RankingsEnabled { get; set; } = true;

    /// <summary>
    /// Whether this is the currently active semester.
    /// </summary>
    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
