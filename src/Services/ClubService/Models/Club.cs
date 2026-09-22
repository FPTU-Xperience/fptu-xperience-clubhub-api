using System.ComponentModel.DataAnnotations;

namespace ClubService.Models;

public sealed class Club
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = ClubCategories.Other;
    public string Description { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string? ScheduleLabel { get; set; }
    public bool IsRecruiting { get; set; } = false;
    public bool IsActive { get; set; } = true;
    [ConcurrencyCheck]
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public int? DeletedByUserId { get; set; }
    public ICollection<ClubManagerAssignment> ManagerAssignments { get; set; } = [];
    public ICollection<ClubMembership> Memberships { get; set; } = [];
}
