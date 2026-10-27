using ActivityService.Models;
using ClubReportHub.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Data;

public sealed class ActivityDbContext(DbContextOptions<ActivityDbContext> options) : DbContext(options)
{
    public DbSet<ClubActivity> Activities => Set<ClubActivity>();
    public DbSet<ActivityParticipant> ActivityParticipants => Set<ActivityParticipant>();
    public DbSet<ActivityAttendance> ActivityAttendances => Set<ActivityAttendance>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ActivitySession> Sessions => Set<ActivitySession>();
    public DbSet<ActivitySessionAudit> SessionAudits => Set<ActivitySessionAudit>();
    public DbSet<ActivityExperienceInbox> ExperienceInbox => Set<ActivityExperienceInbox>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateActionHistory();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ValidateActionHistory();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateActionHistory();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateActionHistory()
    {
        ClubReportHub.Shared.Experience.RecommendationChangeCapture.Capture(this);
        if (ChangeTracker.Entries().Any(x => x.Entity is ActivityApplicationHistory or ActivityExecutionAudit or ActivityActionRule or ActivityResourceAudit
            && x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Application history, approved rules and execution audit are append-only.");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ActivityActionConfiguration.Configure(modelBuilder);
        ActivityResourceConfiguration.Configure(modelBuilder);
        modelBuilder.Entity<ClubActivity>(entity =>
        {
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.PublicationStatus).HasMaxLength(20).HasDefaultValue("LEGACY");
            entity.Property(x => x.DiscoveryVisibility).HasMaxLength(20).HasDefaultValue("CLUB_ONLY");
            entity.HasIndex(x => new { x.DiscoveryVisibility, x.PublicationStatus, x.ClubId });
            entity.Property(x => x.SemesterId).HasMaxLength(40);
            entity.Property(x => x.EffortTier).HasMaxLength(10).HasDefaultValue("T1");
            entity.Property(x => x.Scale).HasMaxLength(40).HasDefaultValue("Club");
            entity.Property(x => x.Eligibility).HasMaxLength(40).HasDefaultValue("CLUB_MEMBER");
            entity.Property(x => x.PillarWeightsJson).HasDefaultValue("[]");
            entity.Property(x => x.EvidenceRequirements).HasMaxLength(2000);
            entity.Property(x => x.CoverImageUrl).HasMaxLength(1000);
            entity.HasIndex(x => x.SourceReportId).IsUnique().HasFilter("[SourceReportId] IS NOT NULL");
            entity.HasIndex(x => new { x.ClubId, x.StartTimeUtc });
            entity.HasIndex(x => x.StartTimeUtc).IncludeProperties(x => new { x.ClubId, x.Title, x.Status });
            entity.HasIndex(x => x.Status);
            entity.Property(x => x.ClubName).HasMaxLength(200);
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.MeetingDaysCsv).HasMaxLength(32);
            entity.Property(x => x.Location).HasMaxLength(200);
            entity.Property(x => x.Status).HasMaxLength(40);
            entity.HasMany(x => x.Participants)
                .WithOne(x => x.Activity)
                .HasForeignKey(x => x.ActivityId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Attendances)
                .WithOne(x => x.Activity)
                .HasForeignKey(x => x.ActivityId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ActivityParticipant>(entity =>
        {
            entity.HasIndex(x => new { x.ActivityId, x.UserId }).IsUnique();
            entity.HasIndex(x => x.UserId).IncludeProperties(x => new { x.ActivityId, x.AttendanceStatus });
            entity.Property(x => x.FullName).HasMaxLength(200);
            entity.Property(x => x.AttendanceStatus).HasMaxLength(40);
        });

        modelBuilder.Entity<ActivityAttendance>(entity =>
        {
            entity.HasIndex(x => new { x.ActivityId, x.UserId, x.AttendanceDate }).IsUnique().HasFilter("[SessionId] IS NULL");
            entity.HasIndex(x => new { x.SessionId, x.UserId }).IsUnique().HasFilter("[SessionId] IS NOT NULL");
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.VerificationStatus).HasMaxLength(20).HasDefaultValue("UNVERIFIED");
            entity.Property(x => x.VerificationMethod).HasMaxLength(40);
            entity.Property(x => x.RecognitionStatus).HasMaxLength(30).HasDefaultValue("NOT_REQUESTED");
            entity.HasOne<ActivitySession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.UserId, x.AttendanceDate }).IncludeProperties(x => new { x.ActivityId, x.Status });
            entity.HasIndex(x => new { x.ActivityId, x.Status });
            entity.Property(x => x.FullName).HasMaxLength(200);
            entity.Property(x => x.AttendanceDate).HasColumnType("date");
            entity.Property(x => x.Status).HasMaxLength(40);
            entity.Property(x => x.Note).HasMaxLength(1000);
        });

        modelBuilder.ApplyOutboxConfiguration();
        modelBuilder.ApplyApiOperationConfiguration();
        modelBuilder.Entity<ActivitySession>(entity =>
        {
            entity.HasIndex(x => new { x.ActivityId, x.StartTimeUtc, x.EndTimeUtc }).IsUnique();
            entity.HasIndex(x => new { x.SemesterId, x.Status });
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.SemesterId).HasMaxLength(40);
            entity.Property(x => x.Timezone).HasMaxLength(40);
            entity.Property(x => x.Mode).HasMaxLength(20);
            entity.Property(x => x.Status).HasMaxLength(20);
            entity.Property(x => x.Location).HasMaxLength(200);
            entity.HasOne(x => x.Activity).WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ActivitySessionAudit>(entity =>
        {
            entity.HasIndex(x => new { x.ActivityId, x.CreatedAtUtc });
            entity.Property(x => x.Action).HasMaxLength(40);
            entity.Property(x => x.Reason).HasMaxLength(1000);
        });
        modelBuilder.Entity<ActivityExperienceInbox>().HasKey(x => x.EventId);
    }
}
