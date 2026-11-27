using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Backend.StabilizationTests;

public sealed partial class ActivityActionContractTests
{
    [Fact]
    public async Task ClosingReportReturnResubmitPreservesDeadlineAndRequiresCurrentIndependentReview()
    {
        await using var f = await Fixture.Create();
        var (_, id) = await f.Approved();
        using var manager = f.Client(200); using var reviewer = f.Client(202); using var outsider = f.Client(203);
        await f.WithDb(async db =>
        {
            var activity = await db.Activities.SingleAsync();
            activity.Status = ActivityStatuses.Completed; activity.UpdatedAtUtc = Instant;
            await db.SaveChangesAsync();
        });
        var path = $"/api/activities/{id}/closing-report";
        var submitted = await Json(await Send(manager, HttpMethod.Post, path, new { summary = "Initial report" }, "initial-report"));
        var version = submitted.GetProperty("version").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(manager, HttpMethod.Post, path + "/return", new { expectedVersion = version, note = "Correct totals" }, "manager-return")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(outsider, HttpMethod.Post, path + "/return", new { expectedVersion = version, note = "Correct totals" }, "wrong-campus-return")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(reviewer, HttpMethod.Post, path + "/return", new { expectedVersion = version, note = "" }, "blank-return")).StatusCode);
        var returned = await Json(await Send(reviewer, HttpMethod.Post, path + "/return", new { expectedVersion = version, note = "Correct totals" }, "return-report"));
        Assert.Equal("RETURNED", returned.GetProperty("status").GetString());
        Assert.False((await reviewer.GetFromJsonAsync<JsonElement>(path)).GetProperty("canApprove").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(reviewer, HttpMethod.Post, path + "/approve", new { expectedVersion = returned.GetProperty("version").GetGuid() }, "approve-returned")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(manager, HttpMethod.Post, path, new { expectedVersion = version, summary = "Stale revision" }, "stale-revision")).StatusCode);
        var state = await manager.GetFromJsonAsync<JsonElement>(path);
        f.Clock.Utc = state.GetProperty("deadlineUtc").GetDateTimeOffset().AddDays(1);
        var revisedResponse = await Send(manager, HttpMethod.Post, path, new { expectedVersion = returned.GetProperty("version").GetGuid(), summary = "Corrected totals" }, "resubmit-report");
        Assert.Equal(HttpStatusCode.OK, revisedResponse.StatusCode);
        var revised = await Json(revisedResponse);
        Assert.Equal(submitted.GetProperty("submittedAtUtc").GetDateTimeOffset(), revised.GetProperty("submittedAtUtc").GetDateTimeOffset());
        Assert.Null(revised.GetProperty("reviewNote").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Send(reviewer, HttpMethod.Post, path + "/approve", new { expectedVersion = revised.GetProperty("version").GetGuid() }, "approve-revision")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(reviewer, HttpMethod.Post, path + "/return", new { expectedVersion = revised.GetProperty("version").GetGuid(), note = "Return after approval" }, "return-approved")).StatusCode);
        await f.WithDb(async db =>
        {
            Assert.Equal(4, await db.SessionAudits.CountAsync(x => x.ActivityId == id && x.Action.StartsWith("CLOSING_REPORT_")));
            Assert.Empty(await db.Set<ActivityCertificate>().ToListAsync());
        });
    }

    [Fact]
    public async Task CertificateDisplaySqlPersistsAccountIdAndRejectsStaleVersion()
    {
        await using var f = await Fixture.Create(sql: true);
        using var student = f.Client(100);
        const string path = "/api/activities/certificates/me/display";
        var first = await Send(student, HttpMethod.Put, path, new { expectedVersion = Guid.Empty, badgeX = 45, badgeY = 30, badgeSize = 20 }, "display-first");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var saved = await Json(first);
        Assert.Equal(saved.GetProperty("version").GetGuid(), (await student.GetFromJsonAsync<JsonElement>(path)).GetProperty("version").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(student, HttpMethod.Put, path, new { expectedVersion = Guid.Empty, badgeX = 50, badgeY = 40, badgeSize = 20 }, "display-stale")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(student, HttpMethod.Put, path, new { expectedVersion = saved.GetProperty("version").GetGuid(), badgeX = 70, badgeY = 40, badgeSize = 20 }, "display-update")).StatusCode);
        await f.WithDb(async db => Assert.Equal(100, (await db.Set<ActivityCertificateDisplay>().SingleAsync()).UserId));
    }

    [Fact]
    public async Task CertificateDisplaySqlUpgradePreservesExistingSelectionsAndVersions()
    {
        await using var f = await Fixture.Create(sql: true);
        var version = Guid.NewGuid(); var badge = Guid.NewGuid();
        await f.WithDb(async db =>
        {
            await db.GetService<IMigrator>().MigrateAsync("20261117064019_BookingMultipleRoomsAndSharedFloors");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SET IDENTITY_INSERT [ActivityCertificateDisplay] ON;
                INSERT INTO [ActivityCertificateDisplay] ([UserId],[Version],[BadgeId],[FrameId],[BadgeX],[BadgeY],[BadgeSize])
                VALUES (100,{version},{badge},NULL,70,30,15);
                SET IDENTITY_INSERT [ActivityCertificateDisplay] OFF;
                """);
            await db.Database.MigrateAsync();
            var row = await db.Set<ActivityCertificateDisplay>().AsNoTracking().SingleAsync();
            Assert.Equal(100, row.UserId); Assert.Equal(version, row.Version); Assert.Equal(badge, row.BadgeId); Assert.Equal(70, row.BadgeX);
            db.Add(new ActivityCertificateDisplay { UserId = 200, Version = Guid.NewGuid() }); await db.SaveChangesAsync();
            Assert.Equal(2, await db.Set<ActivityCertificateDisplay>().CountAsync());
        });
    }
}
