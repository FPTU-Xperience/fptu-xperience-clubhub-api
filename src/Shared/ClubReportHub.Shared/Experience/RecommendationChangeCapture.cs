using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using Microsoft.EntityFrameworkCore;

namespace ClubReportHub.Shared.Experience;

public sealed record RecommendationSourceChanged(Guid EventId, DateTimeOffset OccurredAtUtc, int[]? UserIds)
    : IntegrationEvent(EventId, OccurredAtUtc);

/// <summary>Minimal invalidation, committed in the same transaction as source writes.
/// Consumers reread source APIs; events never carry student profile or timetable data.</summary>
public static class RecommendationChangeCapture
{
    public const string Route = "recommendation.source.changed";

    public static void Capture(DbContext db)
    {
        string[] userEntities = ["CanonicalUserProfile", "StudyScheduleBlock", "User", "ActivityParticipant", "ClubMembership", "ExperienceLedgerEffect"];
        string[] catalogEntities = ["StudyScheduleCell", "Club", "ClubManagerAssignment", "ClubActivity", "ActivitySession", "AcademicCalendarConfiguration", "Semester", "ApprovedExperiencePolicy"];
        var entries = db.ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
            && (userEntities.Contains(x.Metadata.ClrType.Name) || catalogEntities.Contains(x.Metadata.ClrType.Name))).ToArray();
        if (entries.Length == 0 || db.ChangeTracker.Entries<OutboxMessage>().Any(x => x.State == EntityState.Added && x.Entity.EventType == Route)) return;
        var all = entries.Any(x => catalogEntities.Contains(x.Metadata.ClrType.Name));
        var users = entries.Where(x => userEntities.Contains(x.Metadata.ClrType.Name)).Select(x =>
            x.Property(x.Metadata.ClrType.Name == "User" ? "Id" : "UserId").CurrentValue).OfType<int>().Where(x => x > 0).Distinct().ToArray();
        if (all || users.Length > 0) db.AddOutboxMessage(new RecommendationSourceChanged(Guid.NewGuid(), DateTimeOffset.UtcNow, all ? null : users), Route);
    }
}
