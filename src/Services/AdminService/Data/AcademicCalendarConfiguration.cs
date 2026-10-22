using Microsoft.EntityFrameworkCore;

namespace AdminService.Data;

public sealed class AcademicCalendarConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SemesterId { get; set; }
    public string CampusCode { get; set; } = "";
    public string TermStartDate { get; set; } = "";
    public string Block10StartDate { get; set; } = "";
    public string Block3StartDate { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
    public int UpdatedByUserId { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
public sealed class AcademicCalendarRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CalendarId { get; set; }
    public Guid Version { get; set; }
    public int ActorId { get; set; }
    public DateTimeOffset AtUtc { get; set; }
    public string SnapshotJson { get; set; } = "";
}
internal static class AcademicCalendarModel
{
    internal static void Apply(ModelBuilder model)
    {
        model.Entity<AcademicCalendarConfiguration>(e =>
        {
            e.ToTable("AcademicCalendars"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SemesterId, x.CampusCode }).IsUnique();
            e.Property(x => x.CampusCode).HasMaxLength(20); e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.TermStartDate).HasMaxLength(10); e.Property(x => x.Block10StartDate).HasMaxLength(10); e.Property(x => x.Block3StartDate).HasMaxLength(10);
            e.HasOne<Semester>().WithMany().HasForeignKey(x => x.SemesterId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AcademicCalendarRevision>(e =>
        {
            e.ToTable("AcademicCalendarRevisions"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.CalendarId, x.Version }).IsUnique();
            e.HasOne<AcademicCalendarConfiguration>().WithMany().HasForeignKey(x => x.CalendarId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
