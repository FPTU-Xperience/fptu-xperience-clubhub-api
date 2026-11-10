using ActivityService.Models;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Data;

internal static class ActivityBookingConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<CampusVenue>(entity =>
        {
            entity.ToTable(table => table.HasCheckConstraint("CK_CampusVenue_Capacity", "Capacity >= 0"));
            entity.Property(x => x.CampusCode).HasMaxLength(8);
            entity.Property(x => x.Code).HasMaxLength(40);
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.VenueType).HasMaxLength(40);
            entity.Property(x => x.Building).HasMaxLength(80);
            entity.Property(x => x.Floor).HasMaxLength(40);
            entity.HasIndex(x => new { x.CampusCode, x.Code }).IsUnique();
        });
        model.Entity<CampusBookingPolicy>(entity =>
        {
            entity.ToTable(table => table.HasCheckConstraint("CK_CampusBookingPolicy_Day", "CutoffDay BETWEEN 1 AND 7"));
            entity.HasKey(x => x.CampusCode);
            entity.Property(x => x.CampusCode).HasMaxLength(8);
        });
        model.Entity<CampusBookingLayout>(entity =>
        {
            entity.HasKey(x => new { x.CampusCode, x.Building, x.Floor });
            entity.Property(x => x.CampusCode).HasMaxLength(8);
            entity.Property(x => x.Building).HasMaxLength(80);
            entity.Property(x => x.Floor).HasMaxLength(40);
            entity.Property(x => x.ImageUrl).HasMaxLength(2048);
        });
        model.Entity<CampusEquipment>(entity =>
        {
            entity.ToTable(table => table.HasCheckConstraint("CK_CampusEquipment_Quantity", "Quantity >= 0"));
            entity.Property(x => x.CampusCode).HasMaxLength(8);
            entity.Property(x => x.Code).HasMaxLength(40);
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.HasIndex(x => new { x.CampusCode, x.Code }).IsUnique();
        });
        model.Entity<ActivityBooking>(entity =>
        {
            entity.ToTable(table => table.HasCheckConstraint("CK_ActivityBooking_Window", "EndsAtUtc > StartsAtUtc"));
            entity.Property(x => x.CampusCode).HasMaxLength(8);
            entity.Property(x => x.OriginalLocation).HasMaxLength(200);
            entity.Property(x => x.RequestedVenueType).HasMaxLength(40);
            entity.Property(x => x.Status).HasMaxLength(20);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.CampusCode, x.StartsAtUtc });
            entity.HasIndex(x => new { x.ConfirmedVenueId, x.StartsAtUtc, x.EndsAtUtc });
            entity.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CampusVenue>().WithMany().HasForeignKey(x => x.RequestedVenueId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CampusVenue>().WithMany().HasForeignKey(x => x.ConfirmedVenueId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Equipment).WithOne().HasForeignKey(x => x.BookingId);
        });
        model.Entity<ActivityBookingEquipment>(entity =>
        {
            entity.ToTable(table => table.HasCheckConstraint("CK_ActivityBookingEquipment_Quantity",
                "RequestedQuantity > 0 AND ConfirmedQuantity BETWEEN 0 AND RequestedQuantity"));
            entity.HasKey(x => new { x.BookingId, x.EquipmentId });
            entity.HasOne<CampusEquipment>().WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
