namespace AdminService.Data;

public sealed class SelfDeclaration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int StudentId { get; set; }

    public string StudentName { get; set; } = string.Empty;

    public string StudentEmail { get; set; } = string.Empty;

    public string CampusCode { get; set; } = ClubReportHub.Shared.Auth.CampusCodes.Hanoi;

    public string Title { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string OrganizationSource { get; set; } = string.Empty;

    public bool IsOutsideClub { get; set; } = true;

    public int? ClubId { get; set; }

    public string EvidenceUrl { get; set; } = string.Empty;

    public string EvidenceDescription { get; set; } = string.Empty;

    public string? RoleProposed { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public string Status { get; set; } = "Submitted";

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ReviewedAtUtc { get; set; }

    public int? ReviewedByUserId { get; set; }

    public string? ReviewedByName { get; set; }

    public string? ReviewNote { get; set; }

    // Scoring parameters approved by Student Affairs (CTSV)
    public string? FinalCategory { get; set; }

    public string? Tier { get; set; }

    public string? Role { get; set; }

    public string? Scale { get; set; }

    public string? BonusResult { get; set; }

    public decimal? RawPoints { get; set; }
}
