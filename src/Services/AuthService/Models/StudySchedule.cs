using Microsoft.EntityFrameworkCore;

namespace AuthService.Models;

public sealed class StudyScheduleBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public int Year { get; set; }
    public string Term { get; set; } = "";
    public string Block { get; set; } = "";
    public Guid SemesterKey { get; set; }
    public Guid CalendarVersion { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
public sealed class StudyScheduleCell
{
    public Guid BlockId { get; set; }
    public int Week { get; set; }
    public string Day { get; set; } = "";
    public int SlotNumber { get; set; }
    public string Subject { get; set; } = "";
    public string Room { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
internal static class StudyScheduleModel
{
    internal static void Apply(ModelBuilder model)
    {
        model.Entity<StudyScheduleBlock>(e =>
        {
            e.ToTable("StudyScheduleBlocks"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.Year, x.Term, x.Block }).IsUnique();
            e.Property(x => x.Term).HasMaxLength(2); e.Property(x => x.Block).HasMaxLength(3); e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<StudyScheduleCell>(e =>
        {
            e.ToTable("StudyScheduleCells"); e.HasKey(x => new { x.BlockId, x.Week, x.Day, x.SlotNumber });
            e.Property(x => x.Day).HasMaxLength(3); e.Property(x => x.Subject).HasMaxLength(120); e.Property(x => x.Room).HasMaxLength(80);
            e.HasOne<StudyScheduleBlock>().WithMany().HasForeignKey(x => x.BlockId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
