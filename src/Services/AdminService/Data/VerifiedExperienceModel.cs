using ClubReportHub.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AdminService.Data;

internal static class VerifiedExperienceModel
{
    public static void Apply(ModelBuilder model, string? provider)
    {
        model.ApplyApiOperationConfiguration();
        model.ApplyOutboxConfiguration();
        model.Entity<VerificationAssignment>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.SourceType, x.ClubId });
            entity.Property(x => x.SourceType).HasMaxLength(30);
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<VerifiedContribution>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.SourceKey }).IsUnique();
            entity.HasIndex(x => new { x.ClubId, x.SemesterId, x.Status });
            entity.HasIndex(x => new { x.ActivityId, x.SessionId });
            entity.Property(x => x.SourceKey).HasMaxLength(200);
            entity.Property(x => x.SourceId).HasMaxLength(100);
            entity.Property(x => x.SourceType).HasMaxLength(30);
            entity.Property(x => x.SemesterId).HasMaxLength(40);
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Message).HasMaxLength(2000);
            entity.Property(x => x.Status).HasMaxLength(30);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.RawPoints).HasPrecision(18, 4);
        });
        model.Entity<ContributionRevision>(entity =>
        {
            entity.HasIndex(x => new { x.ContributionId, x.Ordinal }).IsUnique();
            entity.HasOne<VerifiedContribution>().WithMany().HasForeignKey(x => x.ContributionId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.Property(x => x.Status).HasMaxLength(30);
        });
        model.Entity<ContributionDecision>(entity =>
        {
            entity.HasIndex(x => new { x.ContributionId, x.TargetRevisionId }).IsUnique();
            entity.HasOne<VerifiedContribution>().WithMany().HasForeignKey(x => x.ContributionId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.Property(x => x.Decision).HasMaxLength(30);
        });
        model.Entity<ContributionUpgradeRequest>(entity =>
        {
            entity.HasIndex(x => x.ContributionId).IsUnique().HasFilter("[Status] = 'SUBMITTED'");
            entity.HasOne<VerifiedContribution>().WithMany().HasForeignKey(x => x.ContributionId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.ProposedRole).HasMaxLength(40);
            entity.Property(x => x.ProposedResult).HasMaxLength(40);
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.Property(x => x.ReviewReason).HasMaxLength(1000);
            entity.Property(x => x.Status).HasMaxLength(30);
        });
        model.Entity<ExperienceLedgerEffect>(entity =>
        {
            entity.HasIndex(x => new { x.ContributionId, x.RevisionId, x.EffectType }).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.SemesterId });
            entity.HasOne<VerifiedContribution>().WithMany().HasForeignKey(x => x.ContributionId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Amount).HasPrecision(18, 4);
            entity.Property(x => x.SemesterId).HasMaxLength(40);
            entity.Property(x => x.EffectType).HasMaxLength(30);
            entity.Property(x => x.VerificationMethod).HasMaxLength(40);
            entity.Property(x => x.FormulaVersion).HasMaxLength(40);
            entity.Property(x => x.Reason).HasMaxLength(1000);
        });
        model.Entity<ExperienceLedgerAllocation>(entity =>
        {
            entity.HasIndex(x => new { x.EffectId, x.Pillar }).IsUnique();
            entity.Property(x => x.Amount).HasPrecision(18, 4);
            entity.Property(x => x.Pillar).HasMaxLength(40);
            entity.HasOne<ExperienceLedgerEffect>().WithMany().HasForeignKey(x => x.EffectId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PrivateContributionEvidence>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.ContentType).HasMaxLength(100);
            entity.HasIndex(x => x.OwnerUserId);
        });
        model.Entity<ManualAttendanceRequest>(entity =>
        {
            entity.HasIndex(x => new { x.SessionId, x.UserId }).IsUnique().HasFilter("[Status] = 'PENDING'");
            entity.HasIndex(x => new { x.ClubId, x.Status });
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.Property(x => x.ReviewReason).HasMaxLength(1000);
            entity.Property(x => x.Status).HasMaxLength(30);
            entity.Property(x => x.RecognitionStatus).HasMaxLength(30);
            entity.Property(x => x.FailureCode).HasMaxLength(100);
        });
        model.Entity<ExperienceInbox>(entity =>
        {
            entity.HasKey(x => x.EventId);
            entity.Property(x => x.RoutingKey).HasMaxLength(100);
        });
        model.Entity<InvalidatedExperienceSource>(entity =>
        {
            entity.HasIndex(x => new { x.ActivityId, x.SessionId });
            entity.Property(x => x.Reason).HasMaxLength(1000);
        });
        model.Entity<ApprovedExperiencePolicy>(entity =>
        {
            entity.Property(x => x.Scope).HasMaxLength(20).HasDefaultValue("PERIOD");
            entity.HasIndex(x => new { x.Scope, x.Environment, x.SemesterId, x.ApprovedAt });
            entity.HasIndex(x => new { x.BenchmarkId, x.ApprovedAt });
            entity.Property(x => x.SemesterId).HasMaxLength(40);
            entity.Property(x => x.ApprovalBasis).HasMaxLength(1000);
            entity.Property(x => x.Environment).HasMaxLength(20);
            entity.HasOne<SemesterBenchmarkConfig>().WithMany().HasForeignKey(x => x.BenchmarkId).OnDelete(DeleteBehavior.Restrict);
        });
        if (provider == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var types = new[] { typeof(VerificationAssignment), typeof(VerifiedContribution), typeof(ContributionRevision),
                typeof(ContributionDecision), typeof(ContributionUpgradeRequest), typeof(ExperienceLedgerEffect),
                typeof(PrivateContributionEvidence), typeof(ManualAttendanceRequest), typeof(ExperienceInbox), typeof(InvalidatedExperienceSource),
                typeof(ApprovedExperiencePolicy) };
            foreach (var type in types)
                foreach (var property in type.GetProperties().Where(x => x.PropertyType == typeof(DateTimeOffset) || x.PropertyType == typeof(DateTimeOffset?)))
                    model.Entity(type).Property(property.Name).HasConversion(new DateTimeOffsetToBinaryConverter());
        }
    }
}

