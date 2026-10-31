using System.Net;
using System.Net.Http.Json;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NotificationService.Contracts;
using NotificationService.Data;
using NotificationService.Endpoints;
using NotificationService.Models;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [ClubManagerSqlFact]
    public async Task Notifications_SqlServerReadAndMarkOperationsPreserveRecipientScopeAndReadState()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });
        builder.Services.AddDbContext<NotificationDbContext>(options => options.UseSqlServer(database.ConnectionString));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(CreateClubAccessClient());
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapNotificationEndpoints();

        var now = DateTimeOffset.UtcNow;
        var ownFirst = new Notification { RecipientUserId = 104, Title = "First", CreatedAtUtc = now };
        var ownSecond = new Notification { RecipientUserId = 104, Title = "Second", CreatedAtUtc = now.AddSeconds(1) };
        var ownRead = new Notification { RecipientUserId = 104, Title = "Read", IsRead = true, CreatedAtUtc = now.AddSeconds(2) };
        var other = new Notification { RecipientUserId = 105, Title = "Other", CreatedAtUtc = now.AddSeconds(3) };
        var managerOwn = new Notification { RecipientUserId = 100, Title = "Manager", CreatedAtUtc = now.AddSeconds(4) };
        var studentRole = new Notification { RecipientRole = AuthRoles.ClubMember, Title = "Students", CreatedAtUtc = now.AddSeconds(5) };
        var treasurerRole = new Notification { RecipientRole = AuthRoles.Treasurer, Title = "Treasurers", CreatedAtUtc = now.AddSeconds(6) };
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            await db.Database.MigrateAsync();
            db.Notifications.AddRange(ownFirst, ownSecond, ownRead, other, managerOwn, studentRole, treasurerRole);
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        using var student = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);

        var listed = await student.GetFromJsonAsync<NotificationResponse[]>("/api/notifications");
        Assert.Equal(new[] { studentRole.Id, ownRead.Id, ownSecond.Id, ownFirst.Id }, listed!.Select(x => x.Id));
        var unread = await student.GetFromJsonAsync<NotificationResponse[]>("/api/notifications?unreadOnly=true");
        Assert.Equal(new[] { studentRole.Id, ownSecond.Id, ownFirst.Id }, unread!.Select(x => x.Id));
        var detail = await student.GetFromJsonAsync<NotificationResponse>($"/api/notifications/{ownFirst.Id}");
        Assert.False(detail!.IsRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/notifications/{managerOwn.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/notifications?recipientUserId=105")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/notifications?recipientRole=TREASURER")).StatusCode);
        Assert.Equal(managerOwn.Id, (await manager.GetFromJsonAsync<NotificationResponse>($"/api/notifications/{managerOwn.Id}"))!.Id);
        await AssertReadStates((ownFirst.Id, false), (ownSecond.Id, false), (studentRole.Id, false));

        Assert.Equal(HttpStatusCode.Forbidden, (await student.PutAsync($"/api/notifications/{other.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PutAsync($"/api/notifications/{treasurerRole.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await student.PutAsync($"/api/notifications/{ownFirst.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await student.PutAsync($"/api/notifications/{ownFirst.Id}/read", null)).StatusCode);
        await AssertReadStates((ownFirst.Id, true), (ownSecond.Id, false), (other.Id, false));

        Assert.Equal(HttpStatusCode.Forbidden, (await student.PutAsync("/api/notifications/read-all?recipientUserId=105", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await student.PutAsync("/api/notifications/read-all?recipientUserId=104", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await student.PutAsync("/api/notifications/read-all?recipientUserId=104", null)).StatusCode);
        await AssertReadStates((ownFirst.Id, true), (ownSecond.Id, true), (ownRead.Id, true),
            (other.Id, false), (managerOwn.Id, false), (studentRole.Id, false), (treasurerRole.Id, false));
        unread = await student.GetFromJsonAsync<NotificationResponse[]>("/api/notifications?unreadOnly=true");
        Assert.Equal(new[] { studentRole.Id }, unread!.Select(x => x.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsync("/api/notifications/read-all?recipientUserId=100", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await student.PutAsync("/api/notifications/read-all?recipientRole=CLUB_MEMBER", null)).StatusCode);
        await AssertReadStates((managerOwn.Id, true), (studentRole.Id, true), (other.Id, false), (treasurerRole.Id, false));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            Assert.Equal(4, await db.ReadReceipts.CountAsync());
            Assert.False((await db.Notifications.AsNoTracking().SingleAsync(x => x.Id == studentRole.Id)).IsRead);
        }

        async Task AssertReadStates(params (int Id, bool Expected)[] expected)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            var ids = expected.Select(x => x.Id).ToArray();
            var actual = await db.Notifications.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            var receipts = await db.ReadReceipts.AsNoTracking().Where(x => ids.Contains(x.NotificationId)).ToArrayAsync();
            foreach (var item in expected)
            {
                var notification = actual[item.Id];
                var actorId = notification.RecipientUserId ?? 104;
                var isRead = notification.RecipientUserId == actorId && notification.IsRead
                    || receipts.Any(x => x.NotificationId == item.Id && x.UserId == actorId);
                Assert.Equal(item.Expected, isRead);
            }
        }
    }
}
