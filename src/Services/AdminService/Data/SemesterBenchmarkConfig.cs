using ClubReportHub.Shared.Experience;

namespace AdminService.Data;

public sealed class SemesterBenchmarkConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Semester code, e.g. "FA26", "SP27", "SU27".
    /// </summary>
    public string SemesterCode { get; set; } = string.Empty;

    /// <summary>
    /// Academic year, e.g. "2026-2027".
    /// </summary>
    public string AcademicYear { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is the active semester benchmark configuration.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Immutable lock (Appendix A4): locked once scores are awarded in this semester;
    /// configuration cannot be silently altered after locking.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// Tau parameter for Academic pillar (default 1000).
    /// </summary>
    public decimal TauAcademic { get; set; } = 1000m;

    /// <summary>
    /// Tau parameter for Research pillar (default 1000).
    /// </summary>
    public decimal TauResearch { get; set; } = 1000m;

    /// <summary>
    /// Tau parameter for Global pillar (default 1000).
    /// </summary>
    public decimal TauGlobal { get; set; } = 1000m;

    /// <summary>
    /// Tau parameter for CultureSports pillar (default 1000).
    /// </summary>
    public decimal TauCultureSports { get; set; } = 1000m;

    /// <summary>
    /// Tau parameter for Community pillar (default 1000).
    /// </summary>
    public decimal TauCommunity { get; set; } = 1000m;

    /// <summary>
    /// Tau parameter for Entrepreneurship pillar (default 1000).
    /// </summary>
    public decimal TauEntrepreneurship { get; set; } = 1000m;

    /// <summary>
    /// Tau parameter for RealWorldWork (+1) pillar (default 1000).
    /// </summary>
    public decimal TauRealWorldWork { get; set; } = 1000m;

    /// <summary>
    /// Saturated score threshold to enter Starter tier (default 20).
    /// </summary>
    public decimal ThresholdStarter { get; set; } = 20m;

    /// <summary>
    /// Saturated score threshold to enter Practitioner tier (default 50).
    /// </summary>
    public decimal ThresholdPractitioner { get; set; } = 50m;

    /// <summary>
    /// Saturated score threshold to enter Leader tier (default 80, requires leader proof).
    /// </summary>
    public decimal ThresholdLeader { get; set; } = 80m;

    /// <summary>
    /// Minimum pillar score across all 6 axes to qualify for "Người Toàn Diện" (default 20).
    /// </summary>
    public decimal MinPillarScoreAllRounder { get; set; } = 20m;

    /// <summary>
    /// Minimum normalized evenness (J) to qualify for "Người Toàn Diện" (default 0.90).
    /// </summary>
    public decimal MinJAllRounder { get; set; } = 0.90m;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int? CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
    public DateTimeOffset? LockedAtUtc { get; set; }
    public int? LockedByUserId { get; set; }
    public string? LockedByName { get; set; }

    public Dictionary<string, decimal> ToTauDictionary() => new(StringComparer.OrdinalIgnoreCase)
    {
        [ExperienceCategories.Academic] = TauAcademic,
        [ExperienceCategories.Research] = TauResearch,
        [ExperienceCategories.Global] = TauGlobal,
        [ExperienceCategories.CultureSports] = TauCultureSports,
        [ExperienceCategories.Community] = TauCommunity,
        [ExperienceCategories.Entrepreneurship] = TauEntrepreneurship,
        [ExperienceCategories.RealWorldWork] = TauRealWorldWork
    };
}
