using AuthService.Models;
using Microsoft.EntityFrameworkCore;
using ClubReportHub.Shared.Data;

namespace AuthService.Data;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<CanonicalUserProfile> UserProfiles => Set<CanonicalUserProfile>();
    public DbSet<PublicMediaIntent> PublicMediaIntents => Set<PublicMediaIntent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        StudyScheduleModel.Apply(modelBuilder);
        modelBuilder.ApplyApiOperationConfiguration();
        modelBuilder.Entity<PublicMediaIntent>(entity =>
        {
            entity.HasIndex(x => x.PublicUrl).IsUnique(); entity.Property(x => x.PublicUrl).HasMaxLength(450);
            entity.Property(x => x.VerifiedUrl).HasMaxLength(1000); entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.ActorId, x.CreatedAt });
        });
        modelBuilder.Entity<CanonicalUserProfile>(entity =>
        {
            entity.HasKey(x => x.UserId); entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.DisplayName).HasMaxLength(120); entity.Property(x => x.About).HasMaxLength(2000);
            entity.Property(x => x.PhoneNumber).HasMaxLength(20);
            entity.HasOne<User>().WithOne().HasForeignKey<CanonicalUserProfile>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(x => x.Username).IsUnique();
            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasIndex(x => x.GoogleSubject)
                .IsUnique()
                .HasFilter("[GoogleSubject] IS NOT NULL");
            entity.Property(x => x.Username).HasMaxLength(100);
            entity.Property(x => x.FullName).HasMaxLength(200);
            entity.Property(x => x.Email).HasMaxLength(200);
            entity.Property(x => x.GoogleSubject).HasMaxLength(255);
            entity.Property(x => x.CampusCode).HasMaxLength(20).HasDefaultValue(ClubReportHub.Shared.Auth.CampusCodes.Hanoi);
            entity.Property(x => x.SecurityVersion).HasDefaultValue(1);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasIndex(x => x.Name).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(60);
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(x => new { x.UserId, x.RoleId });
            entity.HasOne(x => x.User).WithMany(x => x.UserRoles).HasForeignKey(x => x.UserId);
            entity.HasOne(x => x.Role).WithMany(x => x.UserRoles).HasForeignKey(x => x.RoleId);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.Ignore(x => x.IsExpired);
            entity.Ignore(x => x.IsRevoked);
            entity.Ignore(x => x.IsActive);
            entity.Ignore(x => x.RawToken);
            entity.HasIndex(x => x.Token).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.ExpiresAtUtc, x.RevokedAtUtc });
            entity.Property(x => x.Token).HasMaxLength(500);
            entity.Property(x => x.FamilyId).HasMaxLength(100);
            entity.Property(x => x.RevokedByIp).HasMaxLength(50);
            entity.Property(x => x.ReplacedByToken).HasMaxLength(500);
            entity.Property(x => x.RevokedAtUtc).IsConcurrencyToken();
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
