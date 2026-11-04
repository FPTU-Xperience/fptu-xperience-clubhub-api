using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AdminService.Data;

public sealed class ClubCreditWallet
{
    public int UserId { get; set; }
    public int ClubId { get; set; }
    public decimal Spent { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class ClubReward
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ClubId { get; set; }
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public string Kind { get; set; } = "";
    public decimal Price { get; set; }
    public string GrantIdsJson { get; set; } = "[]";
    public string Status { get; set; } = "DRAFT";
    public Guid Version { get; set; } = Guid.NewGuid();
    public int CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? OpensAt { get; set; }
    public DateTimeOffset? ClosesAt { get; set; }
}

public sealed class ClubCosmeticDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ClubId { get; set; }
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public string DefinitionJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ClubRewardRedemption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RewardId { get; set; }
    public int UserId { get; set; }
    public int ClubId { get; set; }
    public decimal Amount { get; set; }
    public string GrantIdsJson { get; set; } = "[]";
    public string RewardLabel { get; set; } = "";
    public Guid RewardVersion { get; set; }
    public long SourceRevision { get; set; }
    public Guid WalletVersion { get; set; }
    public decimal BalanceAfter { get; set; }
    public string PolicyVersion { get; set; } = "CLUB_VERIFIED_XP_1_TO_1_V1";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ClubCosmeticOwnership
{
    public int UserId { get; set; }
    public Guid CosmeticId { get; set; }
    public Guid ReceiptId { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
}

public sealed class ClubRewardAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RewardId { get; set; }
    public int ActorId { get; set; }
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public string SnapshotJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

internal static class ClubCreditModel
{
    internal static void Apply(ModelBuilder model, string? provider)
    {
        model.Entity<ClubCreditWallet>(entity =>
        {
            entity.ToTable("ClubCreditWallets", table => table.HasCheckConstraint("CK_ClubCreditWallet_Spent",
                provider == "Microsoft.EntityFrameworkCore.Sqlite" ? "CAST([Spent] AS REAL) >= 0" : "[Spent] >= 0"));
            entity.HasKey(x => new { x.UserId, x.ClubId });
            entity.Property(x => x.Spent).HasPrecision(18, 4);
            entity.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<ClubReward>(entity =>
        {
            entity.ToTable("ClubRewards", table => table.HasCheckConstraint("CK_ClubReward_Price",
                provider == "Microsoft.EntityFrameworkCore.Sqlite" ? "CAST([Price] AS REAL) >= 0 AND CAST([Price] AS REAL) <= 100000" : "[Price] >= 0 AND [Price] <= 100000"));
            entity.Property(x => x.Label).HasMaxLength(80); entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Kind).HasMaxLength(20); entity.Property(x => x.Status).HasMaxLength(20);
            entity.Property(x => x.Price).HasPrecision(18, 4); entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.ClubId, x.Status, x.CreatedAt });
        });
        model.Entity<ClubCosmeticDefinition>(entity =>
        {
            entity.ToTable("ClubCosmeticDefinitions"); entity.Property(x => x.Kind).HasMaxLength(20);
            entity.Property(x => x.Label).HasMaxLength(80); entity.HasIndex(x => x.ClubId);
        });
        model.Entity<ClubRewardRedemption>(entity =>
        {
            entity.ToTable("ClubRewardRedemptions"); entity.Property(x => x.Amount).HasPrecision(18, 4);
            entity.Property(x => x.BalanceAfter).HasPrecision(18, 4); entity.Property(x => x.RewardLabel).HasMaxLength(80);
            entity.Property(x => x.PolicyVersion).HasMaxLength(60);
            entity.HasIndex(x => new { x.UserId, x.RewardId }).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.ClubId, x.CreatedAt });
            entity.HasOne<ClubReward>().WithMany().HasForeignKey(x => x.RewardId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ClubCosmeticOwnership>(entity =>
        {
            entity.ToTable("ClubCosmeticOwnerships"); entity.HasKey(x => new { x.UserId, x.CosmeticId });
            entity.HasOne<ClubCosmeticDefinition>().WithMany().HasForeignKey(x => x.CosmeticId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ClubRewardRedemption>().WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ClubRewardAudit>(entity =>
        {
            entity.ToTable("ClubRewardAudits"); entity.Property(x => x.Action).HasMaxLength(30);
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.HasOne<ClubReward>().WithMany().HasForeignKey(x => x.RewardId).OnDelete(DeleteBehavior.Restrict);
        });
        foreach (var type in new[] { typeof(ClubReward), typeof(ClubCosmeticDefinition), typeof(ClubRewardRedemption), typeof(ClubCosmeticOwnership), typeof(ClubRewardAudit) })
            foreach (var property in type.GetProperties().Where(x => x.PropertyType == typeof(DateTimeOffset) || x.PropertyType == typeof(DateTimeOffset?)))
                if (provider == "Microsoft.EntityFrameworkCore.Sqlite") model.Entity(type).Property(property.Name).HasConversion(new DateTimeOffsetToBinaryConverter());
                else model.Entity(type).Property(property.Name).HasPrecision(7);
    }
}
