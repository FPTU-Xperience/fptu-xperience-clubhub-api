using System.Net;
using System.Net.Http.Json;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Contracts;
using NotificationService.Data;
using NotificationService.Endpoints;
using NotificationService.Models;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [ClubManagerSqlFact]
    public async Task NotificationReceipts_RoleReadersAreIndependentAndUnreadFilteringPrecedesPaging()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var app = await CreateReceiptTestAppAsync(database);
        var rows = Enumerable.Range(0, 101).Select(index => new Notification
        {
            RecipientRole = AuthRoles.ClubMember,
            Title = "Broadcast " + index,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddSeconds(index)
        }).ToArray();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            db.Notifications.AddRange(rows); await db.SaveChangesAsync();
            db.ReadReceipts.AddRange(rows.Skip(1).Select(n => new NotificationReadReceipt { NotificationId = n.Id, UserId = 104 }));
            await db.SaveChangesAsync();
        }
        using var first = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        using var second = CreateAdminContractClient(app, 105, AuthRoles.ClubMember);
        var firstUnread = await first.GetFromJsonAsync<NotificationResponse[]>("/api/notifications?unreadOnly=true");
        Assert.Equal(rows[0].Id, Assert.Single(firstUnread!).Id);
        var secondUnread = await second.GetFromJsonAsync<NotificationResponse[]>("/api/notifications?unreadOnly=true");
        Assert.Equal(100, secondUnread!.Length);
        Assert.All(secondUnread, x => Assert.False(x.IsRead));
        Assert.True((await first.GetFromJsonAsync<NotificationResponse>($"/api/notifications/{rows[1].Id}"))!.IsRead);
        Assert.False((await second.GetFromJsonAsync<NotificationResponse>($"/api/notifications/{rows[1].Id}"))!.IsRead);

        Assert.Equal(HttpStatusCode.NoContent, (await first.PutAsync($"/api/notifications/{rows[0].Id}/read", null)).StatusCode);
        Assert.Empty((await first.GetFromJsonAsync<NotificationResponse[]>("/api/notifications?unreadOnly=true"))!);
        Assert.False((await second.GetFromJsonAsync<NotificationResponse>($"/api/notifications/{rows[0].Id}"))!.IsRead);
        await using var verify = app.Services.CreateAsyncScope();
        var actual = verify.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.Equal(101, await actual.ReadReceipts.CountAsync(x => x.UserId == 104));
        Assert.False(await actual.ReadReceipts.AnyAsync(x => x.UserId == 105));
        Assert.False(await actual.Notifications.AnyAsync(x => x.IsRead));
    }

    [ClubManagerSqlFact]
    public async Task NotificationReceipts_ReadAllKeepsRecipientScopeAndAdminCannotChangeAnotherUsersReadState()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var app = await CreateReceiptTestAppAsync(database);
        var own = new Notification { RecipientUserId = 104, Title = "Own" };
        var foreign = new Notification { RecipientUserId = 105, Title = "Other" };
        var broadcast = new Notification { RecipientRole = AuthRoles.ClubMember, Title = "Role" };
        var treasurer = new Notification { RecipientRole = AuthRoles.Treasurer, Title = "Treasurer" };
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            db.Notifications.AddRange(own, foreign, broadcast, treasurer); await db.SaveChangesAsync();
        }
        using var first = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        using var second = CreateAdminContractClient(app, 105, AuthRoles.ClubMember);
        using var admin = CreateAdminContractClient(app, 1, AuthRoles.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await first.PutAsync("/api/notifications/read-all?recipientUserId=105", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await first.PutAsync($"/api/notifications/{treasurer.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await first.PutAsync("/api/notifications/read-all?recipientRole=CLUB_MEMBER", null)).StatusCode);
        Assert.True((await first.GetFromJsonAsync<NotificationResponse>($"/api/notifications/{broadcast.Id}"))!.IsRead);
        Assert.False((await first.GetFromJsonAsync<NotificationResponse>($"/api/notifications/{own.Id}"))!.IsRead);
        Assert.False((await second.GetFromJsonAsync<NotificationResponse>($"/api/notifications/{broadcast.Id}"))!.IsRead);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync("/api/notifications/read-all?recipientUserId=105", null)).StatusCode);
        Assert.True((await admin.GetFromJsonAsync<NotificationResponse>($"/api/notifications/{foreign.Id}"))!.IsRead);
        Assert.False((await second.GetFromJsonAsync<NotificationResponse>($"/api/notifications/{foreign.Id}"))!.IsRead);
        await using var verify = app.Services.CreateAsyncScope();
        var actual = verify.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.Equal(2, await actual.ReadReceipts.CountAsync());
        Assert.False(await actual.ReadReceipts.AnyAsync(x => x.UserId == 105));
        Assert.False(await actual.Notifications.AnyAsync(x => x.IsRead));
    }

    [ClubManagerSqlFact]
    public async Task NotificationReceipts_ConcurrentSingleAndBulkMarksAreIdempotent()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var app = await CreateReceiptTestAppAsync(database);
        var rows = Enumerable.Range(0, 20).Select(index => new Notification { RecipientRole = AuthRoles.ClubMember, Title = "Concurrent " + index }).ToArray();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            db.Notifications.AddRange(rows); await db.SaveChangesAsync();
        }
        var clients = Enumerable.Range(0, 8).Select(_ => CreateAdminContractClient(app, 104, AuthRoles.ClubMember)).ToArray();
        try
        {
            var responses = await Task.WhenAll(clients.Select((client, index) => client.PutAsync(index % 2 == 0
                ? "/api/notifications/read-all?recipientRole=CLUB_MEMBER" : $"/api/notifications/{rows[0].Id}/read", null)));
            Assert.All(responses, x => Assert.Equal(HttpStatusCode.NoContent, x.StatusCode));
            foreach (var response in responses) response.Dispose();
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
        }
        await using var verify = app.Services.CreateAsyncScope();
        var actual = verify.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.Equal(20, await actual.ReadReceipts.CountAsync(x => x.UserId == 104));
        Assert.False(await actual.Notifications.AnyAsync(x => x.IsRead));
    }

    [ClubManagerSqlFact]
    public async Task NotificationReceipts_OnlyTheLegacyDirectOwnerKeepsTheSharedReadMarker()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var app = await CreateReceiptTestAppAsync(database);
        var mixed = new Notification { RecipientUserId = 104, RecipientRole = AuthRoles.ClubMember, IsRead = true, Title = "Legacy owner and role" };
        var broadcast = new Notification { RecipientRole = AuthRoles.ClubMember, IsRead = true, Title = "Legacy shared read" };
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            db.Notifications.AddRange(mixed, broadcast); await db.SaveChangesAsync();
        }
        using var owner = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        using var reader = CreateAdminContractClient(app, 105, AuthRoles.ClubMember);
        Assert.True((await owner.GetFromJsonAsync<NotificationResponse>($"/api/notifications/{mixed.Id}"))!.IsRead);
        Assert.False((await reader.GetFromJsonAsync<NotificationResponse>($"/api/notifications/{mixed.Id}"))!.IsRead);
        Assert.False((await owner.GetFromJsonAsync<NotificationResponse>($"/api/notifications/{broadcast.Id}"))!.IsRead);
        Assert.Equal(broadcast.Id, Assert.Single((await owner.GetFromJsonAsync<NotificationResponse[]>("/api/notifications?unreadOnly=true"))!).Id);
        Assert.Equal(2, (await reader.GetFromJsonAsync<NotificationResponse[]>("/api/notifications?unreadOnly=true"))!.Length);
        Assert.Equal(HttpStatusCode.NoContent, (await reader.PutAsync($"/api/notifications/{mixed.Id}/read", null)).StatusCode);
        Assert.True((await reader.GetFromJsonAsync<NotificationResponse>($"/api/notifications/{mixed.Id}"))!.IsRead);
        Assert.True((await owner.GetFromJsonAsync<NotificationResponse>($"/api/notifications/{mixed.Id}"))!.IsRead);
    }

    [ClubManagerSqlFact]
    public async Task NotificationReceipts_MigrationBackfillsOnlyIdentifiableOwnersAndCascadeKeepsNoOrphans()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        var options = new DbContextOptionsBuilder<NotificationDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new NotificationDbContext(options);
        var migrator = db.GetService<IMigrator>();
        var baseline = db.Database.GetMigrations().First();
        await migrator.MigrateAsync(baseline);
        var privateRead = new Notification { RecipientUserId = 104, IsRead = true, Title = "Private legacy read" };
        var privateUnread = new Notification { RecipientUserId = 105, Title = "Private legacy unread" };
        var sharedRead = new Notification { RecipientRole = AuthRoles.ClubMember, IsRead = true, Title = "Shared legacy read" };
        db.Notifications.AddRange(privateRead, privateUnread, sharedRead); await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        var receipt = Assert.Single(await db.ReadReceipts.AsNoTracking().ToArrayAsync());
        Assert.Equal(privateRead.Id, receipt.NotificationId);
        Assert.Equal(104, receipt.UserId);
        Assert.False((await db.Notifications.AsNoTracking().SingleAsync(x => x.Id == sharedRead.Id)).IsRead);
        Assert.True((await db.Notifications.AsNoTracking().SingleAsync(x => x.Id == privateRead.Id)).IsRead);
        db.Notifications.Remove(await db.Notifications.SingleAsync(x => x.Id == privateRead.Id));
        await db.SaveChangesAsync();
        Assert.False(await db.ReadReceipts.AnyAsync());
        db.ReadReceipts.Add(new NotificationReadReceipt { NotificationId = int.MaxValue, UserId = 104 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [ClubManagerSqlFact]
    public async Task NotificationReceipts_ExistingReceiptDoesNotGrantAccessAfterClubRoleIsLost()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        ClubAccessSnapshot[] snapshots = [CreateAccessSnapshot(1, 104)];
        using var http = CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(snapshots) });
        var access = new ClubAccessClient(http, cache, NullLogger<ClubAccessClient>.Instance);
        await using var app = await CreateReceiptTestAppAsync(database, access);
        var message = new Notification { RecipientRole = AuthRoles.ClubManager, Title = "Managers" };
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            db.Notifications.Add(message); await db.SaveChangesAsync();
        }
        using var user = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        Assert.Equal(HttpStatusCode.NoContent, (await user.PutAsync($"/api/notifications/{message.Id}/read", null)).StatusCode);
        snapshots = []; cache.Compact(1.0);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync($"/api/notifications/{message.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsync($"/api/notifications/{message.Id}/read", null)).StatusCode);
        Assert.Empty((await user.GetFromJsonAsync<NotificationResponse[]>("/api/notifications"))!);
        await using var verify = app.Services.CreateAsyncScope();
        Assert.True(await verify.ServiceProvider.GetRequiredService<NotificationDbContext>().ReadReceipts.AnyAsync(x => x.NotificationId == message.Id && x.UserId == 104));
    }

    private static async Task<WebApplication> CreateReceiptTestAppAsync(ManagerSqlDatabase database, ClubAccessClient? access = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });
        builder.Services.AddDbContext<NotificationDbContext>(options => options.UseSqlServer(database.ConnectionString));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(access ?? CreateClubAccessClient());
        var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.MapNotificationEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
        await app.StartAsync();
        return app;
    }
}
