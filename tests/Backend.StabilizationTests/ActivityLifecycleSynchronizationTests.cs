using ActivityService.Data;
using ActivityService.Models;
using ActivityService.Services;
using ActivityService.Endpoints;
using ClubReportHub.Shared.Events;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.StabilizationTests;

public sealed class ActivityLifecycleSynchronizationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-29T03:00:00Z");

    [Fact]
    public void TimelineUsesNextSessionEvenWhenParentDatesAreStaleAndPreservesCancelledState()
    {
        var future = Session(1, Now.AddDays(1));
        var timeline = ActivityTimelineReadFilter.Resolve("Scheduled", Now.AddDays(-3), Now.AddDays(-2), [future], Now);
        Assert.Equal("UPCOMING", timeline.Status);
        Assert.Equal(future.StartTimeUtc, timeline.StartTimeUtc);
        future.StartTimeUtc = Now.AddMinutes(-1);
        Assert.Equal("LIVE", ActivityTimelineReadFilter.Resolve("Scheduled", Now.AddDays(-3), Now.AddDays(-2), [future], Now).Status);
        Assert.Equal("CANCELLED", ActivityTimelineReadFilter.Resolve("Cancelled", Now.AddDays(-3), Now.AddDays(-2), [future], Now).Status);
        Assert.Equal("COMPLETED", ActivityTimelineReadFilter.Resolve("Scheduled", Now.AddDays(-3), Now, [], Now).Status);
    }

    [SkippableFact]
    public async Task SqlServerTranslatesCatchUpQueryAndCommitsAuditAndOutboxTogether()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ADMIN_SERVICE_TEST_CONNECTION_STRING")),
            "SQL Server lifecycle test requires an explicit test connection.");
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var db = new ActivityDbContext(new DbContextOptionsBuilder<ActivityDbContext>()
            .UseSqlServer(database.ConnectionString).Options);
        await db.Database.MigrateAsync();
        var activity = Activity("PUBLISHED");
        db.Activities.Add(activity);
        await db.SaveChangesAsync();
        db.Sessions.Add(Session(activity.Id, Now.AddHours(-1)));
        await db.SaveChangesAsync();
        Assert.Equal(2, await new ActivityLifecycleSynchronizer(db).SynchronizeAsync(Now));
        Assert.Equal(2, await db.OutboxMessages.CountAsync(e => e.EventType == ActivityLifecycleChangedEvent.RoutingKey));
        Assert.Equal(2, await db.SessionAudits.CountAsync());
        Assert.Equal(ActivityStatuses.Completed, (await db.Activities.SingleAsync()).Status);
    }

    [Fact]
    public async Task CatchUpClosesExpiredSessionsAndCompletesActivitiesWithAtomicAuditAndEventsOnce()
    {
        await using var db = Database();
        var legacy = Activity("LEGACY");
        var published = Activity("PUBLISHED");
        db.AddRange(legacy, published);
        await db.SaveChangesAsync();
        db.Sessions.Add(Session(published.Id, Now.AddDays(-1)));
        await db.SaveChangesAsync();
        var service = new ActivityLifecycleSynchronizer(db);
        Assert.Equal(3, await service.SynchronizeAsync(Now));
        var completed = await db.Activities.ToListAsync();
        Assert.All(completed, a => Assert.Equal(ActivityStatuses.Completed, a.Status));
        Assert.Equal("LEGACY", completed.Single(a => a.Id == legacy.Id).PublicationStatus);
        Assert.Equal("COMPLETED", completed.Single(a => a.Id == published.Id).PublicationStatus);
        Assert.Equal("CLOSED", (await db.Sessions.SingleAsync()).Status);
        Assert.All(await db.SessionAudits.ToListAsync(), a => Assert.Equal(0, a.ActorId));
        Assert.Equal(3, await db.SessionAudits.CountAsync());
        Assert.Equal(3, await db.OutboxMessages.CountAsync(e => e.EventType == ActivityLifecycleChangedEvent.RoutingKey));
        Assert.Equal(0, await service.SynchronizeAsync(Now));
        Assert.Equal(3, await db.OutboxMessages.CountAsync(e => e.EventType == ActivityLifecycleChangedEvent.RoutingKey));
        Assert.Empty(await db.ActivityAttendances.ToListAsync());
    }

    [Fact]
    public async Task FutureSessionAndOpenAttendanceWindowPreventPrematureCompletion()
    {
        await using var db = Database();
        var future = Activity("PUBLISHED");
        var attendanceOpen = Activity("CLOSED");
        db.AddRange(future, attendanceOpen);
        await db.SaveChangesAsync();
        db.AddRange(Session(future.Id, Now.AddDays(1)), Session(future.Id, Now.AddDays(-1)),
            Session(attendanceOpen.Id, Now.AddMinutes(-1), Now.AddMinutes(15)));
        await db.SaveChangesAsync();
        var service = new ActivityLifecycleSynchronizer(db);
        Assert.Equal(1, await service.SynchronizeAsync(Now));
        Assert.All(await db.Activities.ToListAsync(), a => Assert.Equal(ActivityStatuses.Scheduled, a.Status));
        Assert.Equal(2, await db.Sessions.CountAsync(s => s.Status == "SCHEDULED"));
        Assert.Equal(2, await service.SynchronizeAsync(Now.AddMinutes(15)));
        Assert.Equal(ActivityStatuses.Completed, (await db.Activities.SingleAsync(a => a.Id == attendanceOpen.Id)).Status);
        Assert.Equal(ActivityStatuses.Scheduled, (await db.Activities.SingleAsync(a => a.Id == future.Id)).Status);
    }

    [Fact]
    public async Task DraftCancelledAndAlreadyCompletedRecordsAreNotRewritten()
    {
        await using var db = Database();
        var draft = Activity("DRAFT");
        var cancelled = Activity("CANCELLED"); cancelled.Status = ActivityStatuses.Cancelled;
        var completed = Activity("COMPLETED"); completed.Status = ActivityStatuses.Completed;
        db.AddRange(draft, cancelled, completed);
        await db.SaveChangesAsync();
        Assert.Equal(0, await new ActivityLifecycleSynchronizer(db).SynchronizeAsync(Now));
        Assert.Empty(await db.OutboxMessages.Where(e => e.EventType == ActivityLifecycleChangedEvent.RoutingKey).ToListAsync());
        Assert.Empty(await db.SessionAudits.ToListAsync());
    }

    private static ActivityDbContext Database() => new(new DbContextOptionsBuilder<ActivityDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ClubActivity Activity(string publication) => new()
    {
        ClubId = 1,
        Title = "Past activity",
        PublicationStatus = publication,
        StartTimeUtc = Now.AddDays(-2),
        EndTimeUtc = Now.AddDays(-1)
    };

    private static ActivitySession Session(int activityId, DateTimeOffset end, DateTimeOffset? closes = null) => new()
    {
        ActivityId = activityId,
        StartTimeUtc = end.AddHours(-1),
        EndTimeUtc = end,
        OpensAt = end.AddHours(-1),
        ClosesAt = closes ?? end
    };
}
