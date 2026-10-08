using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AdminService.Data;

public sealed class AdminDbContext(DbContextOptions<AdminDbContext> options) : DbContext(options)
{
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();
    public DbSet<SelfDeclaration> SelfDeclarations => Set<SelfDeclaration>();
    public DbSet<SemesterBenchmarkConfig> SemesterBenchmarkConfigs => Set<SemesterBenchmarkConfig>();
    public DbSet<BonusMatrixNode> BonusMatrixNodes => Set<BonusMatrixNode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var audit = modelBuilder.Entity<AuditRecord>();
        audit.ToTable("AuditRecords");
        audit.HasKey(record => record.Id);
        audit.Property(record => record.ActorSubjectId).HasMaxLength(200).IsRequired();
        audit.Property(record => record.ActorEmail).HasMaxLength(320);
        audit.Property(record => record.ActorRolesJson).IsRequired();
        audit.Property(record => record.Action).HasMaxLength(100).IsRequired();
        audit.Property(record => record.ResourceType).HasMaxLength(100).IsRequired();
        audit.Property(record => record.ResourceId).HasMaxLength(200);
        audit.Property(record => record.CorrelationId).HasMaxLength(128).IsRequired();
        var timestamp = audit.Property(record => record.TimestampUtc).IsRequired();
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            timestamp.HasConversion(new DateTimeOffsetToBinaryConverter());
        }
        else
        {
            timestamp.HasPrecision(7);
        }
        audit.Property(record => record.Outcome).HasMaxLength(30).IsRequired();
        audit.Property(record => record.MetadataJson).IsRequired();
        audit.HasIndex(record => record.TimestampUtc);
        audit.HasIndex(record => new { record.ActorSubjectId, record.TimestampUtc });
        audit.HasIndex(record => new { record.Action, record.TimestampUtc });

        var declaration = modelBuilder.Entity<SelfDeclaration>();
        declaration.ToTable("SelfDeclarations");
        declaration.HasKey(x => x.Id);
        declaration.Property(x => x.Title).HasMaxLength(200).IsRequired();
        declaration.Property(x => x.StudentName).HasMaxLength(150).IsRequired();
        declaration.Property(x => x.StudentEmail).HasMaxLength(320).IsRequired();
        declaration.Property(x => x.Category).HasMaxLength(50).IsRequired();
        declaration.Property(x => x.FinalCategory).HasMaxLength(50);
        declaration.Property(x => x.OrganizationSource).HasMaxLength(150).IsRequired();
        declaration.Property(x => x.EvidenceUrl).HasMaxLength(1000).IsRequired();
        declaration.Property(x => x.EvidenceDescription).HasMaxLength(2000).IsRequired();
        declaration.Property(x => x.RoleProposed).HasMaxLength(50);
        declaration.Property(x => x.CampusCode).HasMaxLength(20).HasDefaultValue(ClubReportHub.Shared.Auth.CampusCodes.Hanoi).IsRequired();
        declaration.Property(x => x.Status).HasMaxLength(30).IsRequired();
        declaration.Property(x => x.ReviewedByName).HasMaxLength(150);
        declaration.Property(x => x.ReviewNote).HasMaxLength(1000);
        declaration.Property(x => x.Tier).HasMaxLength(10);
        declaration.Property(x => x.Role).HasMaxLength(50);
        declaration.Property(x => x.Scale).HasMaxLength(50);
        declaration.Property(x => x.BonusResult).HasMaxLength(50);
        declaration.Property(x => x.RawPoints).HasPrecision(18, 4);

        var createdAt = declaration.Property(x => x.CreatedAtUtc).IsRequired();
        var reviewedAt = declaration.Property(x => x.ReviewedAtUtc);
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            createdAt.HasConversion(new DateTimeOffsetToBinaryConverter());
            reviewedAt.HasConversion(new DateTimeOffsetToBinaryConverter());
        }
        else
        {
            createdAt.HasPrecision(7);
            reviewedAt.HasPrecision(7);
        }

        declaration.HasIndex(x => x.StudentId);
        declaration.HasIndex(x => x.Status);
        declaration.HasIndex(x => x.Category);
        declaration.HasIndex(x => x.CampusCode);
        declaration.HasIndex(x => x.CreatedAtUtc);

        var benchmark = modelBuilder.Entity<SemesterBenchmarkConfig>();
        benchmark.ToTable("SemesterBenchmarkConfigs");
        benchmark.HasKey(x => x.Id);
        benchmark.Property(x => x.SemesterCode).HasMaxLength(20).IsRequired();
        benchmark.Property(x => x.AcademicYear).HasMaxLength(20).IsRequired();
        benchmark.Property(x => x.CreatedByName).HasMaxLength(150);
        benchmark.Property(x => x.LockedByName).HasMaxLength(150);

        benchmark.Property(x => x.TauAcademic).HasPrecision(18, 4);
        benchmark.Property(x => x.TauResearch).HasPrecision(18, 4);
        benchmark.Property(x => x.TauGlobal).HasPrecision(18, 4);
        benchmark.Property(x => x.TauCultureSports).HasPrecision(18, 4);
        benchmark.Property(x => x.TauCommunity).HasPrecision(18, 4);
        benchmark.Property(x => x.TauEntrepreneurship).HasPrecision(18, 4);
        benchmark.Property(x => x.TauRealWorldWork).HasPrecision(18, 4);

        benchmark.Property(x => x.ThresholdStarter).HasPrecision(18, 4);
        benchmark.Property(x => x.ThresholdPractitioner).HasPrecision(18, 4);
        benchmark.Property(x => x.ThresholdLeader).HasPrecision(18, 4);
        benchmark.Property(x => x.MinPillarScoreAllRounder).HasPrecision(18, 4);
        benchmark.Property(x => x.MinJAllRounder).HasPrecision(18, 4);

        var benchmarkCreated = benchmark.Property(x => x.CreatedAtUtc).IsRequired();
        var benchmarkLocked = benchmark.Property(x => x.LockedAtUtc);
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            benchmarkCreated.HasConversion(new DateTimeOffsetToBinaryConverter());
            benchmarkLocked.HasConversion(new DateTimeOffsetToBinaryConverter());
        }
        else
        {
            benchmarkCreated.HasPrecision(7);
            benchmarkLocked.HasPrecision(7);
        }

        benchmark.HasIndex(x => x.SemesterCode).IsUnique();
        benchmark.HasIndex(x => x.IsActive);

        var node = modelBuilder.Entity<BonusMatrixNode>();
        node.ToTable("BonusMatrixNodes");
        node.HasKey(x => x.Id);
        node.Property(x => x.Position).IsRequired();
        node.Property(x => x.Label).HasMaxLength(60).IsRequired();
        node.Property(x => x.Multiplier).IsRequired();

        var nodeCreated = node.Property(x => x.CreatedAtUtc).IsRequired();
        var nodeUpdated = node.Property(x => x.UpdatedAtUtc).IsRequired();
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            nodeCreated.HasConversion(new DateTimeOffsetToBinaryConverter());
            nodeUpdated.HasConversion(new DateTimeOffsetToBinaryConverter());
        }
        else
        {
            nodeCreated.HasPrecision(7);
            nodeUpdated.HasPrecision(7);
        }

        node.HasIndex(x => x.Position).IsUnique();
    }
}
