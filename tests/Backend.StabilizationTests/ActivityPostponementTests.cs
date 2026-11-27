using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Models;
using ActivityService.Services;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using Microsoft.EntityFrameworkCore;

namespace Backend.StabilizationTests;

public sealed partial class ActivityActionContractTests
{
    [Fact]
    public Task PostponementFreezesExecutionReleasesBookingsAndResumesOnlyThroughCampusReview() => PostponementWorkflow(false);

    [Fact]
    public Task PostponementSqlWorkerNeverCompletesAPostponedActivity() => PostponementWorkflow(true);

    private async Task PostponementWorkflow(bool sql)
    {
        await using var f = await Fixture.Create(sql: sql); var (_, id) = await f.Approved();
        using var manager = f.Client(200); using var reviewer = f.Client(202); using var foreign = f.Client(203); using var student = f.Client(100);
        Guid version = default, registrationVersion = default, sessionVersion = default;
        DateTimeOffset originalStart = default, originalEnd = default;
        await f.WithDb(async db =>
        {
            var activity = await db.Activities.SingleAsync(); activity.PublicationStatus = "PUBLISHED";
            version = activity.Version; originalStart = activity.StartTimeUtc; originalEnd = activity.EndTimeUtc;
            var participant = new ActivityParticipant { ActivityId = id, UserId = 100, FullName = "Student 100" };
            db.ActivityParticipants.Add(participant); registrationVersion = participant.Version;
            var session = new ActivitySession { ActivityId = id, SemesterId = "FA2026", Mode = "ONSITE", StartTimeUtc = originalStart, EndTimeUtc = originalEnd, OpensAt = originalStart, ClosesAt = originalEnd };
            db.Sessions.Add(session); await db.SaveChangesAsync(); sessionVersion = session.Version;
            db.Add(new ActivityBooking { SessionId = session.Id, CampusCode = "HN", StartsAtUtc = originalStart, EndsAtUtc = originalEnd, Status = "CONFIRMED", RequestedByUserId = 200 }); await db.SaveChangesAsync();
        });
        var path = $"/api/activities/{id}/postponement";
        var request = new { expectedVersion = version, reason = "Unsafe weather conditions" };
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(foreign, HttpMethod.Post, path, request, "foreign-postpone")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(student, HttpMethod.Post, path, request, "student-postpone")).StatusCode);
        var postponedResponse = await Send(manager, HttpMethod.Post, path, request, "postpone"); Assert.Equal(HttpStatusCode.OK, postponedResponse.StatusCode);
        var postponed = await Json(postponedResponse); Assert.Equal("Postponed", postponed.GetProperty("status").GetString());
        Assert.Equal(postponed.GetRawText(), (await Json(await Send(manager, HttpMethod.Post, path, request, "postpone"))).GetRawText());
        f.Clock.Utc = originalEnd.AddDays(1);
        await f.WithDb(async db =>
        {
            // The production worker's DateTimeOffset queries require SQL Server;
            // the SQL variant executes it rather than weakening its predicates for SQLite.
            if (sql) Assert.Equal(0, await new ActivityLifecycleSynchronizer(db).SynchronizeAsync(f.Clock.Utc));
            Assert.Equal("Postponed", (await db.Activities.SingleAsync()).Status);
            Assert.NotEqual(sessionVersion, (await db.Sessions.SingleAsync()).Version);
            Assert.Equal("SCHEDULED", (await db.Sessions.SingleAsync()).Status);
            Assert.Equal("CANCELLED", (await db.Set<ActivityBooking>().SingleAsync()).Status);
        });
        Assert.Equal(HttpStatusCode.OK, (await Send(student, HttpMethod.Post, $"/api/activities/{id}/registration/cancel", new { expectedVersion = registrationVersion }, "cancel-postponed")).StatusCode);
        var current = await reviewer.GetFromJsonAsync<JsonElement>(path);
        var resume = new { expectedVersion = current.GetProperty("version").GetGuid(), reason = "Campus confirms replacement dates", startTimeUtc = originalStart.AddDays(5), endTimeUtc = originalEnd.AddDays(5) };
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(manager, HttpMethod.Post, path + "/resume", resume, "manager-resume")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(reviewer, HttpMethod.Post, path + "/resume", resume, "reviewer-resume")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(reviewer, HttpMethod.Post, path + "/resume", resume, "stale-resume")).StatusCode);
        await f.WithDb(async db =>
        {
            Assert.Equal("Scheduled", (await db.Activities.SingleAsync()).Status);
            Assert.Equal(originalStart.AddDays(5), (await db.Sessions.SingleAsync()).StartTimeUtc);
            Assert.Equal("CANCELLED", (await db.Set<ActivityBooking>().SingleAsync()).Status);
            Assert.Equal(2, await db.OutboxMessages.CountAsync(x => x.EventType == ExperienceRoutingKeys.Notification));
            Assert.Empty(await db.OutboxMessages.Where(x => x.EventType == ExperienceRoutingKeys.AttendanceVerified).ToArrayAsync());
        });
    }

    [Fact]
    public async Task PostponementCannotRewriteRecordedAttendance()
    {
        await using var f = await Fixture.Create(); var (_, id) = await f.Approved(); using var manager = f.Client(200);
        Guid version = default;
        await f.WithDb(async db =>
        {
            version = (await db.Activities.SingleAsync()).Version;
            db.ActivityAttendances.Add(new ActivityAttendance { ActivityId = id, UserId = 100, FullName = "Recorded student" }); await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Conflict, (await Send(manager, HttpMethod.Post, $"/api/activities/{id}/postponement", new { expectedVersion = version, reason = "Cannot move recorded attendance" }, "postpone-history")).StatusCode);
        await f.WithDb(async db => Assert.Empty(await db.SessionAudits.Where(x => x.Action == "POSTPONE").ToListAsync()));
    }
}
