using Microsoft.EntityFrameworkCore;

namespace AdminService.Data;

internal static class SemesterRosterModel
{
    internal static void Apply(ModelBuilder model)
    {
        model.Entity<SemesterRoster>(e =>
        {
            e.HasOne<Semester>().WithMany().HasForeignKey(x => x.SemesterId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Version).IsConcurrencyToken(); e.Property(x => x.CampusCode).HasMaxLength(40);
            e.HasIndex(x => new { x.SemesterId, x.CampusCode }).IsUnique();
        });
        model.Entity<SemesterRosterRevision>(e =>
        {
            e.HasOne<SemesterRoster>().WithMany().HasForeignKey(x => x.RosterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SemesterRosterImport>().WithMany().HasForeignKey(x => x.ImportId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SemesterRosterRevision>().WithMany().HasForeignKey(x => x.SourceRevisionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ImportId).IsUnique().HasFilter("[ImportId] IS NOT NULL"); e.Property(x => x.Source).HasMaxLength(30);
        });
        model.Entity<SemesterRosterRow>(e =>
        {
            e.HasOne<SemesterRosterRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.RevisionId, x.UserId }).IsUnique();
            e.HasIndex(x => new { x.RevisionId, x.StudentCode }).IsUnique();
            e.Property(x => x.Email).HasMaxLength(200); e.Property(x => x.StudentCode).HasMaxLength(40); e.Property(x => x.CampusCode).HasMaxLength(40);
        });
        model.Entity<SemesterRosterImport>(e =>
        {
            e.HasOne<Semester>().WithMany().HasForeignKey(x => x.SemesterId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.CampusCode).HasMaxLength(40); e.HasIndex(x => new { x.SemesterId, x.CampusCode });
        });
    }
}
