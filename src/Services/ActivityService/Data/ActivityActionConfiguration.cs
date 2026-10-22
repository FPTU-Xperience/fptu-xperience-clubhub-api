using ActivityService.Models;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Data;

internal static class ActivityActionConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<ActivityApplication>(e =>
        {
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.CampusCode).HasMaxLength(40);
            e.Property(x => x.SemesterId).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(30);
            e.HasIndex(x => new { x.ClubId, x.SemesterId, x.Status });
        });
        model.Entity<ActivityApplicationHistory>(e =>
        {
            e.HasIndex(x => new { x.ApplicationId, x.Version }).IsUnique();
            e.HasOne<ActivityApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).HasMaxLength(30);
            e.Property(x => x.Reason).HasMaxLength(2000);
        });
        model.Entity<ActivityExecution>(e =>
        {
            e.HasKey(x => x.ActivityId);
            e.HasOne<ClubActivity>().WithOne().HasForeignKey<ActivityExecution>(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ActivityApplication>().WithOne().HasForeignKey<ActivityExecution>(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.PublicationVersion).IsConcurrencyToken();
        });
        model.Entity<ActivityActionRule>(e =>
        {
            e.HasOne<ActivityExecution>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ClientId).HasMaxLength(80);
            e.HasIndex(x => new { x.ActivityId, x.ClientId }).IsUnique();
        });
        model.Entity<ActivityRecruitment>(e =>
        {
            e.HasOne<ActivityExecution>().WithOne().HasForeignKey<ActivityRecruitment>(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Title).HasMaxLength(120);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Visibility).HasMaxLength(20);
            e.Property(x => x.Eligibility).HasMaxLength(30);
            e.Property(x => x.Status).HasMaxLength(20);
        });
        model.Entity<ActivityRecruitmentPosition>(e =>
        {
            e.HasOne<ActivityRecruitment>().WithMany().HasForeignKey(x => x.RecruitmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ActivityActionRule>().WithMany().HasForeignKey(x => x.ActionRuleId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.AssignmentKind).HasMaxLength(20);
            e.HasIndex(x => new { x.RecruitmentId, x.ActionRuleId }).IsUnique().HasFilter("[IsArchived] = 0");
        });
        model.Entity<ActivityStaffAssignment>(e =>
        {
            e.HasOne<ActivityExecution>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ActivityActionRule>().WithMany().HasForeignKey(x => x.ActionRuleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ActivityRecruitmentPosition>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.AssignmentKind).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => new { x.ActivityId, x.ActionRuleId, x.UserId }).IsUnique().HasFilter("[Status] <> 'REMOVED'");
        });
        model.Entity<ActivityActionCompletion>(e =>
        {
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<ActivityStaffAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ActivityActionRule>().WithMany().HasForeignKey(x => x.ActionRuleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ActivityExecution>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.OccurrenceKey).HasMaxLength(80);
            e.Property(x => x.OccurrenceLabel).HasMaxLength(200);
            e.Property(x => x.Evidence).HasMaxLength(4000);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => new { x.ActionRuleId, x.UserId, x.OccurrenceKey }).IsUnique();
        });
        model.Entity<ActivityExecutionAudit>(e =>
        {
            e.HasOne<ActivityExecution>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ActivityId, x.Version }).IsUnique();
            e.Property(x => x.Action).HasMaxLength(40);
        });
    }
}
