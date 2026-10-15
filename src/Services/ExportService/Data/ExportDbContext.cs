using ClubReportHub.Shared.Data;
using ExportService.Models;
using Microsoft.EntityFrameworkCore;

namespace ExportService.Data;

public sealed class ExportDbContext(DbContextOptions<ExportDbContext> options) : DbContext(options)
{
    public DbSet<ExportRequest> ExportRequests => Set<ExportRequest>();
    public DbSet<ExportFile> ExportFiles => Set<ExportFile>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ClubExperienceReport> ClubExperienceReports => Set<ClubExperienceReport>();

    public override int SaveChanges() => SaveChanges(true);
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateReportSnapshots();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => SaveChangesAsync(true, cancellationToken);
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateReportSnapshots();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    private void ValidateReportSnapshots()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<ClubExperienceReport>())
        {
            if (entry.State == EntityState.Deleted || entry.State == EntityState.Modified &&
                new[] { nameof(ClubExperienceReport.SnapshotJson), nameof(ClubExperienceReport.SnapshotHash),
                    nameof(ClubExperienceReport.ClubId), nameof(ClubExperienceReport.SemesterId),
                    nameof(ClubExperienceReport.CreatedBy), nameof(ClubExperienceReport.CreatedAtUtc) }
                    .Any(name => entry.Property(name).IsModified))
                throw new InvalidOperationException("Report source snapshots and provenance are immutable.");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyApiOperationConfiguration();
        modelBuilder.Entity<ClubExperienceReport>(entity =>
        {
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.SemesterId).HasMaxLength(40);
            entity.Property(x => x.Status).HasMaxLength(30);
            entity.Property(x => x.SnapshotHash).HasMaxLength(64);
            entity.HasIndex(x => new { x.Status, x.LeaseUntil });
            entity.HasIndex(x => new { x.ClubId, x.SemesterId });
        });
        modelBuilder.Entity<ExportRequest>(entity =>
        {
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.Property(x => x.ExportType).HasMaxLength(20);
            entity.Property(x => x.Scope).HasMaxLength(40);
            entity.Property(x => x.Status).HasMaxLength(30);
            entity.Property(x => x.Period).HasMaxLength(40);
            entity.Property(x => x.RequestedByName).HasMaxLength(200);
            entity.Property(x => x.CriteriaJson).HasMaxLength(2000);
            entity.Property(x => x.ErrorMessage).HasMaxLength(2000);
            entity.HasOne(x => x.File)
                .WithOne(x => x.ExportRequest)
                .HasForeignKey<ExportFile>(x => x.ExportRequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExportFile>(entity =>
        {
            entity.Property(x => x.FileName).HasMaxLength(260);
            entity.Property(x => x.ContentType).HasMaxLength(120);
            entity.Property(x => x.FilePath).HasMaxLength(500);
            entity.Property(x => x.Checksum).HasMaxLength(128);
        });

        modelBuilder.ApplyOutboxConfiguration();
    }
}
