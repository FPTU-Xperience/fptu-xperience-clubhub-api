using ActivityService.Models;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Data;

internal static class ActivityResourceConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<ActivityResource>(entity =>
        {
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.Kind).HasMaxLength(20);
            entity.Property(x => x.Title).HasMaxLength(160);
            entity.Property(x => x.Visibility).HasMaxLength(10);
            entity.Property(x => x.Url).HasMaxLength(2000);
            entity.Property(x => x.Provider).HasMaxLength(20);
            entity.HasIndex(x => new { x.ActivityId, x.DetachedAtUtc, x.Position });
            entity.HasOne<ClubActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ActivityResourceAudit>(entity =>
        {
            entity.Property(x => x.Action).HasMaxLength(20);
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.HasIndex(x => new { x.ActivityId, x.Id });
            entity.HasOne<ActivityResource>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
