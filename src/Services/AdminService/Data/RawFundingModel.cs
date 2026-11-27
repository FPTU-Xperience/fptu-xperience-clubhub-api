using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AdminService.Data;

public sealed class RawFundingAccount
{
    public int ClubId { get; set; }
    public string CampusCode { get; set; } = "";
    public string SemesterId { get; set; } = "";
    public decimal Granted { get; set; }
    public decimal Available { get; set; }
    public decimal Reserved { get; set; }
    public decimal Spent { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class RawFundingReservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ClubId { get; set; }
    public string CampusCode { get; set; } = "";
    public string SemesterId { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string SourceVersion { get; set; } = "";
    public int? ActivityId { get; set; }
    public Guid PolicyVersion { get; set; }
    public decimal Budget { get; set; }
    public decimal Spent { get; set; }
    public string Status { get; set; } = "RESERVED";
    public Guid Version { get; set; } = Guid.NewGuid();
    public int ReservedBy { get; set; }
    public DateTimeOffset ReservedAt { get; set; }
}

public sealed class RawFundingEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ClubId { get; set; }
    public string CampusCode { get; set; } = "";
    public string SemesterId { get; set; } = "";
    public Guid? ReservationId { get; set; }
    public Guid? ContributionId { get; set; }
    public long? ExperienceEffectId { get; set; }
    public ExperienceLedgerEffect? ExperienceEffect { get; set; }
    public string OperationKey { get; set; } = "";
    public string Kind { get; set; } = "";
    public decimal GrantedDelta { get; set; }
    public decimal AvailableDelta { get; set; }
    public decimal ReservedDelta { get; set; }
    public decimal SpentDelta { get; set; }
    public int? ActorId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string Reason { get; set; } = "";
}

internal static class RawFundingModel
{
    internal static void Apply(ModelBuilder model, string? provider)
    {
        model.Entity<RawFundingAccount>(e =>
        {
            e.HasKey(x => new { x.ClubId, x.CampusCode, x.SemesterId });
            e.Property(x => x.ClubId).ValueGeneratedNever();
            e.Property(x => x.CampusCode).HasMaxLength(40); e.Property(x => x.SemesterId).HasMaxLength(40);
            e.Property(x => x.Granted).HasPrecision(18, 4); e.Property(x => x.Available).HasPrecision(18, 4);
            e.Property(x => x.Reserved).HasPrecision(18, 4); e.Property(x => x.Spent).HasPrecision(18, 4);
            e.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<RawFundingReservation>(e =>
        {
            e.HasKey(x => x.Id); e.HasIndex(x => new { x.SourceKey, x.SourceVersion }).IsUnique();
            e.HasIndex(x => x.ActivityId).IsUnique().HasFilter("[ActivityId] IS NOT NULL");
            e.Property(x => x.SourceKey).HasMaxLength(160); e.Property(x => x.SourceVersion).HasMaxLength(100);
            e.Property(x => x.CampusCode).HasMaxLength(40); e.Property(x => x.SemesterId).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(20); e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Budget).HasPrecision(18, 4); e.Property(x => x.Spent).HasPrecision(18, 4);
            e.HasOne<RawFundingAccount>().WithMany().HasForeignKey(x => new { x.ClubId, x.CampusCode, x.SemesterId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RawFundingEntry>(e =>
        {
            e.HasKey(x => x.Id); e.HasIndex(x => x.OperationKey).IsUnique();
            e.HasIndex(x => new { x.ClubId, x.CampusCode, x.SemesterId, x.RecordedAt });
            e.Property(x => x.OperationKey).HasMaxLength(200); e.Property(x => x.Kind).HasMaxLength(30);
            e.Property(x => x.CampusCode).HasMaxLength(40); e.Property(x => x.SemesterId).HasMaxLength(40); e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.GrantedDelta).HasPrecision(18, 4); e.Property(x => x.AvailableDelta).HasPrecision(18, 4);
            e.Property(x => x.ReservedDelta).HasPrecision(18, 4); e.Property(x => x.SpentDelta).HasPrecision(18, 4);
            e.HasOne<RawFundingAccount>().WithMany().HasForeignKey(x => new { x.ClubId, x.CampusCode, x.SemesterId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RawFundingReservation>().WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ExperienceEffect).WithMany().HasForeignKey(x => x.ExperienceEffectId).OnDelete(DeleteBehavior.Restrict);
        });
        if (provider == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var converter = new ValueConverter<DateTimeOffset, long>(value => value.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
            model.Entity<RawFundingEntry>().Property(x => x.RecordedAt).HasConversion(converter);
            model.Entity<RawFundingReservation>().Property(x => x.ReservedAt).HasConversion(converter);
        }
    }
}
