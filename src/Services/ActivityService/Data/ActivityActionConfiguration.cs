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
            e.HasIndex(x => x.PublishAtUtc).HasFilter("[PublishAtUtc] IS NOT NULL");
        });
        model.Entity<ActivityActionRule>(e =>
        {
            e.HasOne<ActivityExecution>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ClientId).HasMaxLength(80);
            e.HasIndex(x => new { x.ActivityId, x.ClientId }).IsUnique();
        });
        model.Entity<ActivityRescheduleRequest>(e =>
        {
            e.HasOne<ClubActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Reason).HasMaxLength(2000);
            e.Property(x => x.ReviewNote).HasMaxLength(2000);
            e.HasIndex(x => x.ActivityId).IsUnique().HasFilter("[Status] = 'PENDING'");
        });
        model.Entity<ActivityRecapFeedback>(e =>
        {
            e.HasKey(x => new { x.ActivityId, x.UserId });
            e.HasOne<ClubActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Strengths).HasMaxLength(2000);
            e.Property(x => x.Weaknesses).HasMaxLength(2000);
            e.Property(x => x.Suggestions).HasMaxLength(2000);
        });
        model.Entity<ClubActivity>().Property(x => x.OrganizerCampusCode).HasMaxLength(30);
        model.Entity<ClubActivity>().HasIndex(x => x.CampusPublishAtUtc).HasFilter("[CampusPublishAtUtc] IS NOT NULL");
        model.Entity<ActivityHeldXp>(e =>
        {
            e.HasKey(x => x.EventId);
            e.HasOne<ClubReportHub.Shared.Data.OutboxMessage>().WithOne().HasForeignKey<ActivityHeldXp>(x => x.EventId);
            e.HasIndex(x => x.ActivityId);
        });
        model.Entity<CampusActivityDuty>(e =>
        {
            e.HasOne<ClubActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Role).HasMaxLength(16);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.Responsibility).HasMaxLength(500);
            e.HasIndex(x => new { x.ActivityId, x.UserId, x.Role }).IsUnique();
        });
        model.Entity<ActivityHandover>(e =>
        {
            e.HasKey(x => x.ActivityId);
            e.HasOne<ClubActivity>().WithOne().HasForeignKey<ActivityHandover>(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.ClubName).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(16);
            e.HasIndex(x => new { x.ClubId, x.Status });
        });
        model.Entity<ActivityCertificateDefinition>(e =>
        {
            e.HasOne<ClubActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Role).HasMaxLength(16);
            e.Property(x => x.Title).HasMaxLength(200);
            e.HasIndex(x => new { x.ActivityId, x.Role });
        });
        model.Entity<ActivityCertificate>(e =>
        {
            e.HasOne(x => x.Definition).WithMany().HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.RecipientKind).HasMaxLength(8);
            e.Property(x => x.RecipientName).HasMaxLength(200);
            e.Property(x => x.ActivityTitle).HasMaxLength(200);
            e.Property(x => x.IssuerName).HasMaxLength(200);
            e.HasIndex(x => new { x.ActivityId, x.RecipientKind, x.RecipientId, x.DefinitionId }).IsUnique();
            e.HasIndex(x => new { x.RecipientKind, x.RecipientId, x.IssuedAtUtc });
        });
        model.Entity<ActivityCertificateDisplay>(e =>
        {
            e.HasKey(x => x.UserId);
            e.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<ActivityClosingReport>(e =>
        {
            e.HasKey(x => x.ActivityId);
            e.HasOne<ClubActivity>().WithOne().HasForeignKey<ActivityClosingReport>(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Summary).HasMaxLength(4000);
            e.Property(x => x.Improvements).HasMaxLength(4000);
            e.Property(x => x.ReviewNote).HasMaxLength(2000);
        });
        model.Entity<ActivityClosingPolicy>(e =>
        {
            e.Property(x => x.CampusCode).HasMaxLength(30);
            e.HasIndex(x => new { x.CampusCode, x.Id });
        });
        model.Entity<ActivityFinalRole>(e =>
        {
            e.HasKey(x => new { x.ActivityId, x.UserId });
            e.HasOne<ClubActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Role).HasMaxLength(16);
            e.Property(x => x.DisplayName).HasMaxLength(200);
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
        model.Entity<ActivityRecruitmentApplication>(e =>
        {
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(2000);
            e.HasOne<ActivityExecution>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ActivityRecruitmentPosition>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ActivityStaffAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PositionId, x.UserId }).IsUnique();
            e.HasIndex(x => new { x.ActivityId, x.Status });
        });
        model.Entity<ActivityCompletionResource>(e =>
        {
            e.HasKey(x => new { x.CompletionId, x.ResourceId });
            e.HasOne<ActivityActionCompletion>().WithMany().HasForeignKey(x => x.CompletionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ActivityResource>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
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
