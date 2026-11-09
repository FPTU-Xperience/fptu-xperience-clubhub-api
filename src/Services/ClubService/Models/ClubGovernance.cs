using Microsoft.EntityFrameworkCore;

namespace ClubService.Models;

public sealed class ClubGovernanceTerm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ClubId { get; set; }
    public Guid SemesterId { get; set; }
    public Guid RevisionId { get; set; }
    public int Sequence { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class ClubGovernanceRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TermId { get; set; }
    public int Sequence { get; set; }
    public Guid? PreviousId { get; set; }
    public int ActorId { get; set; }
    public string ActorName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string ConfigurationJson { get; set; } = "{}";
    public string MemberChangesJson { get; set; } = "[]";
    public string AffectedGroupIdsJson { get; set; } = "[]";
}

public sealed class ClubGovernanceMemberChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RevisionId { get; set; }
    public int MembershipId { get; set; }
    public int UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}

public sealed class ClubGovernanceReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TermId { get; set; }
    public int Number { get; set; }
    public Guid SourceRevisionId { get; set; }
    public int ActorId { get; set; }
    public DateTimeOffset At { get; set; }
    public string Reason { get; set; } = "";
    public string SnapshotJson { get; set; } = "{}";
    public string LeadershipDigest { get; set; } = "";
}

public sealed class ClubViceAppointment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ClubId { get; set; }
    public int MembershipId { get; set; }
    public int UserId { get; set; }
    public string MemberName { get; set; } = "";
    public int ProposedBy { get; set; }
    public DateTimeOffset ProposedAt { get; set; }
    public string Status { get; set; } = "PENDING";
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? Until { get; set; }
}

public sealed class ClubGovernanceLeadershipEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AppointmentId { get; set; }
    public int ActorId { get; set; }
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string SnapshotJson { get; set; } = "{}";
}

public sealed class ClubGovernanceAuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ClubId { get; set; }
    public int ActorId { get; set; }
    public string ActorName { get; set; } = "";
    public int? MembershipId { get; set; }
    public int? UserId { get; set; }
    public string Source { get; set; } = "";
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public Guid? RevisionId { get; set; }
}

internal static class ClubGovernanceModel
{
    internal static void Apply(ModelBuilder model)
    {
        model.Entity<ClubGovernanceTerm>(e =>
        {
            e.HasKey(x => x.Id); e.HasIndex(x => new { x.ClubId, x.SemesterId }).IsUnique();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ClubGovernanceRevision>(e =>
        {
            e.HasKey(x => x.Id); e.HasIndex(x => new { x.TermId, x.Sequence }).IsUnique();
            e.Property(x => x.ActorName).HasMaxLength(200); e.Property(x => x.Action).HasMaxLength(50); e.Property(x => x.Reason).HasMaxLength(1000);
            e.HasOne<ClubGovernanceTerm>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ClubGovernanceReport>(e =>
        {
            e.HasKey(x => x.Id); e.HasIndex(x => new { x.TermId, x.Number }).IsUnique(); e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.LeadershipDigest).HasMaxLength(64);
            e.HasOne<ClubGovernanceTerm>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ClubGovernanceRevision>().WithMany().HasForeignKey(x => x.SourceRevisionId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ClubGovernanceMemberChange>(e =>
        {
            e.HasKey(x => x.Id); e.HasIndex(x => new { x.RevisionId, x.MembershipId }).IsUnique(); e.HasIndex(x => x.MembershipId);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.HasOne<ClubGovernanceRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ClubViceAppointment>(e =>
        {
            e.HasKey(x => x.Id); e.HasIndex(x => new { x.ClubId, x.UserId, x.Status }); e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Status).HasMaxLength(20); e.Property(x => x.MemberName).HasMaxLength(200);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ClubMembership>().WithMany().HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ClubGovernanceLeadershipEvent>(e =>
        {
            e.HasKey(x => x.Id); e.HasIndex(x => new { x.AppointmentId, x.At }); e.Property(x => x.Action).HasMaxLength(50); e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<ClubViceAppointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ClubGovernanceAuditEvent>(e =>
        {
            e.HasKey(x => x.Id); e.HasIndex(x => new { x.ClubId, x.At }); e.HasIndex(x => x.MembershipId);
            e.Property(x => x.Source).HasMaxLength(50); e.Property(x => x.Action).HasMaxLength(50);
            e.Property(x => x.ActorName).HasMaxLength(200); e.Property(x => x.Reason).HasMaxLength(1000);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ClubGovernanceRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
