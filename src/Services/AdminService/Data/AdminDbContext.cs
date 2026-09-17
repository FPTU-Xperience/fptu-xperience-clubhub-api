using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AdminService.Data;

public sealed class AdminDbContext(DbContextOptions<AdminDbContext> options) : DbContext(options)
{
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

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
    }
}
