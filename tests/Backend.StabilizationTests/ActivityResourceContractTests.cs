using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ActivityService.Data;
using ActivityService.Endpoints;
using ActivityService.Infrastructure;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Backend.StabilizationTests;

public sealed class ActivityResourceContractTests
{
    private const string Key = "Activity-Resource-Contract-Tests-Only-Signing-Key-2026";
    private static object Link(string title = "Workshop", string visibility = "CLUB", string url = "https://youtu.be/dQw4w9WgXcQ") =>
        new { kind = "EMBED", title, visibility, url };
    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();
    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object body, string? key = "test")
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Gallery_PersistsLinksAcrossActorsAndDetailWithoutPrivateAuditLeak()
    {
        await using var f = await Fixture.Create(); using var manager = f.Client(200); using var member = f.Client(100);
        var created = await Send(manager, HttpMethod.Post, "/api/activities/1/resources", Link());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var resource = await Json(created); var id = resource.GetProperty("id").GetGuid();
        Assert.Equal("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", resource.GetProperty("url").GetString());
        Assert.Equal(200, resource.GetProperty("uploadedByUserId").GetInt32());
        var gallery = await Json(await member.GetAsync("/api/activities/1/resources"));
        var item = Assert.Single(gallery.GetProperty("items").EnumerateArray());
        Assert.Equal(id, item.GetProperty("id").GetGuid()); Assert.False(item.TryGetProperty("uploadedByUserId", out _));
        Assert.False(gallery.GetProperty("resourceCapabilities").GetProperty("canManage").GetBoolean());
        var detail = await Json(await member.GetAsync("/api/activities/1"));
        Assert.Single(detail.GetProperty("resources").EnumerateArray());
        Assert.Equal("AVAILABLE", detail.GetProperty("resourceAvailability").GetString());
        var capabilities = (await Json(await manager.GetAsync("/api/activities/1/resources"))).GetProperty("resourceCapabilities");
        Assert.True(capabilities.GetProperty("canManage").GetBoolean()); Assert.False(capabilities.GetProperty("canUpload").GetBoolean());
        Assert.Equal("EMBED", Assert.Single(capabilities.GetProperty("supportedKinds").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task Gallery_PatchOmissionsCasIdempotencyAndAuditedDetachAreAtomic()
    {
        await using var f = await Fixture.Create(); using var manager = f.Client(200);
        var original = await Json(await Send(manager, HttpMethod.Post, "/api/activities/1/resources", Link(), "create"));
        var id = original.GetProperty("id").GetGuid(); var version = original.GetProperty("version").GetGuid(); var path = $"/api/activities/1/resources/{id}";
        var patch = new { expectedVersion = version, title = "Renamed", position = 7 };
        var updated = await Send(manager, HttpMethod.Patch, path, patch, "edit"); Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var edited = await Json(updated); Assert.Equal("CLUB", edited.GetProperty("visibility").GetString()); Assert.Equal(7, edited.GetProperty("position").GetInt32());
        Assert.Equal(edited.GetRawText(), (await Json(await Send(manager, HttpMethod.Patch, path, patch, "edit"))).GetRawText());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(manager, HttpMethod.Patch, path, patch, "stale")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(manager, HttpMethod.Patch, path, new { expectedVersion = edited.GetProperty("version").GetGuid(), title = (string?)null }, "null")).StatusCode);
        var detach = new { expectedVersion = edited.GetProperty("version").GetGuid(), reason = "Replaced material" };
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Delete, path, detach, "detach")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Delete, path, detach, "detach")).StatusCode);
        Assert.Empty((await Json(await manager.GetAsync("/api/activities/1/resources"))).GetProperty("items").EnumerateArray());
        var history = await Json(await manager.GetAsync("/api/activities/1/resources/history?page=1&pageSize=10"));
        Assert.Equal(3, history.GetProperty("total").GetInt32());
        Assert.Equal("DETACH", history.GetProperty("items")[0].GetProperty("action").GetString());
        Assert.Contains("Renamed", history.GetProperty("items")[0].GetProperty("beforeJson").GetString());
    }

    [Theory]
    [InlineData(100)]
    [InlineData(201)]
    [InlineData(202)]
    [InlineData(203)]
    [InlineData(204)]
    public async Task Gallery_RejectsMemberForeignManagerReviewerLockedAndForeignCampusWrites(int actor)
    {
        await using var f = await Fixture.Create(); using var client = f.Client(actor);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, HttpMethod.Post, "/api/activities/1/resources", Link())).StatusCode);
    }

    [Fact]
    public async Task Gallery_RechecksAuthorityBeforeReplayingAndNeverReturnsPrivateLinksToCampusVisitor()
    {
        await using var f = await Fixture.Create(); using var manager = f.Client(200); using var visitor = f.Client(101);
        Assert.Equal(HttpStatusCode.Created, (await Send(manager, HttpMethod.Post, "/api/activities/1/resources", Link(), "private")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(manager, HttpMethod.Post, "/api/activities/1/resources", Link("Public", "PUBLIC"), "public")).StatusCode);
        var visit = await Json(await visitor.GetAsync("/api/activities/1/resources"));
        Assert.Equal("PUBLIC", Assert.Single(visit.GetProperty("items").EnumerateArray()).GetProperty("visibility").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await visitor.GetAsync("/api/activities/1/resources/history")).StatusCode);
        f.ManagerEnabled = false;
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(manager, HttpMethod.Post, "/api/activities/1/resources", Link(), "private")).StatusCode);
        f.AuthorityAvailable = false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await manager.GetAsync("/api/activities/1/resources")).StatusCode);
    }

    [Theory]
    [InlineData("http://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com.evil.example/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/redirect?q=https://example.com")]
    [InlineData("https://www.youtube.com/watch?v=invalid")]
    [InlineData("https://127.0.0.1/admin")]
    [InlineData("https://[::1]/admin")]
    [InlineData("https://localhost/admin")]
    [InlineData("https://user:password@docs.google.com/document/a")]
    [InlineData("https://docs.google.com:8443/document/a")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://unapproved.example.com/document/a")]
    public async Task Gallery_RejectsUnsafeOrUnsupportedLinkDestinations(string url)
    {
        await using var f = await Fixture.Create(); using var manager = f.Client(200);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(manager, HttpMethod.Post, "/api/activities/1/resources", Link(url: url))).StatusCode);
    }

    [Fact]
    public async Task Gallery_BoundedStrictBodyAndRequiredKeyNeverAdvertiseFileSupport()
    {
        await using var f = await Fixture.Create(); using var manager = f.Client(200);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(manager, HttpMethod.Post, "/api/activities/1/resources", Link(), null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(manager, HttpMethod.Post, "/api/activities/1/resources", new { kind = "EMBED", title = "Link", visibility = "CLUB", url = "https://docs.google.com/document/a", objectKey = "forged" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(manager, HttpMethod.Post, "/api/activities/1/resources", Link(new string('a', 161)))).StatusCode);
        using var duplicate = new HttpRequestMessage(HttpMethod.Post, "/api/activities/1/resources") { Content = new StringContent("{\"kind\":\"EMBED\",\"Kind\":\"DOCUMENT\"}", Encoding.UTF8, "application/json") };
        duplicate.Headers.Add("Idempotency-Key", "dup"); Assert.Equal(HttpStatusCode.BadRequest, (await manager.SendAsync(duplicate)).StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, (await Send(manager, HttpMethod.Post, "/api/activities/1/resources", new { kind = "DOCUMENT", title = "File", visibility = "CLUB", uploadIntentId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, (await Send(manager, HttpMethod.Post, "/api/activities/1/resources/upload-intents", new { kind = "DOCUMENT", fileName = "a.pdf", mimeType = "application/pdf", sizeBytes = 10 })).StatusCode);
        Assert.Empty((await Json(await manager.GetAsync("/api/activities/1/resources"))).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Gallery_RejectsCrossActivityAndHidesDraftsFromNonManagers()
    {
        await using var f = await Fixture.Create(); using var manager = f.Client(200); using var member = f.Client(100);
        var item = await Json(await Send(manager, HttpMethod.Post, "/api/activities/1/resources", Link()));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(manager, HttpMethod.Patch, $"/api/activities/2/resources/{item.GetProperty("id").GetGuid()}", new { expectedVersion = item.GetProperty("version").GetGuid(), title = "wrong activity" })).StatusCode);
        await f.Db(async db => { (await db.Activities.SingleAsync(x => x.Id == 1)).PublicationStatus = "DRAFT"; await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/activities/1/resources")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/activities/1/resources")).StatusCode);
        using var anon = f.App.GetTestClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/activities/1/resources")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/public/activities/1")).StatusCode);
    }

    [Fact]
    public async Task Gallery_SqlUpgradePreservesActivityAndConcurrentCasHasOneWinner()
    {
        await using var f = await Fixture.Create(sql: true); using var first = f.Client(200); using var second = f.Client(200);
        var original = await Json(await Send(first, HttpMethod.Post, "/api/activities/1/resources", Link()));
        var path = $"/api/activities/1/resources/{original.GetProperty("id").GetGuid()}";
        var changes = await Task.WhenAll(Send(first, HttpMethod.Patch, path, new { expectedVersion = original.GetProperty("version").GetGuid(), title = "First" }, "one"),
            Send(second, HttpMethod.Patch, path, new { expectedVersion = original.GetProperty("version").GetGuid(), title = "Second" }, "two"));
        Assert.Single(changes, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(changes, x => x.StatusCode == HttpStatusCode.Conflict);
        await f.Db(async db => Assert.Equal("Preserved activity", (await db.Activities.SingleAsync(x => x.Id == 1)).Title));
        Assert.Equal(2, (await Json(await first.GetAsync("/api/activities/1/resources/history"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Gallery_InvalidPatchRollsBackAndVisibilityChangesPreservePrivateAudit()
    {
        await using var f = await Fixture.Create(); using var manager = f.Client(200); using var visitor = f.Client(101);
        var original = await Json(await Send(manager, HttpMethod.Post, "/api/activities/1/resources", Link()));
        var path = $"/api/activities/1/resources/{original.GetProperty("id").GetGuid()}"; var version = original.GetProperty("version").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(manager, HttpMethod.Patch, path, new { expectedVersion = version, title = "Rolled back", visibility = "INVALID" }, "invalid")).StatusCode);
        var stillOriginal = Assert.Single((await Json(await manager.GetAsync("/api/activities/1/resources"))).GetProperty("items").EnumerateArray());
        Assert.Equal("Workshop", stillOriginal.GetProperty("title").GetString()); Assert.Equal(version, stillOriginal.GetProperty("version").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Patch, path, new { expectedVersion = version, visibility = "PUBLIC" }, "publish")).StatusCode);
        var visible = Assert.Single((await Json(await visitor.GetAsync("/api/activities/1/resources"))).GetProperty("items").EnumerateArray());
        Assert.False(visible.TryGetProperty("uploadedByUserId", out _)); Assert.False(visible.TryGetProperty("beforeJson", out _));
        Assert.Equal(2, (await Json(await manager.GetAsync("/api/activities/1/resources/history"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Gallery_AuditCannotBeChangedOrDeletedThroughDbContext()
    {
        await using var f = await Fixture.Create(); using var manager = f.Client(200);
        await Send(manager, HttpMethod.Post, "/api/activities/1/resources", Link());
        await f.Db(async db => { var audit = await db.Set<ActivityResourceAudit>().SingleAsync(); audit.Reason = "Rewrite"; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); });
        await f.Db(async db => { db.Remove(await db.Set<ActivityResourceAudit>().SingleAsync()); await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); });
        Assert.Equal(1, (await Json(await manager.GetAsync("/api/activities/1/resources/history"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Gallery_QuotaAndCancelledActivityKeepExistingResourcesReadable()
    {
        await using var f = await Fixture.Create(); using var manager = f.Client(200);
        await f.Db(async db =>
        {
            db.AddRange(Enumerable.Range(0, 100).Select(position => new ActivityResource { ActivityId = 1, Title = "Link", Url = "https://docs.google.com/document/a", Position = position, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow, UploadedByUserId = 200 }));
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Conflict, (await Send(manager, HttpMethod.Post, "/api/activities/1/resources", Link())).StatusCode);
        await f.Db(async db => { (await db.Activities.SingleAsync(x => x.Id == 1)).Status = ActivityStatuses.Cancelled; await db.SaveChangesAsync(); });
        var gallery = await Json(await manager.GetAsync("/api/activities/1/resources"));
        Assert.Equal(100, gallery.GetProperty("items").GetArrayLength()); Assert.False(gallery.GetProperty("resourceCapabilities").GetProperty("canManage").GetBoolean());
        var item = gallery.GetProperty("items")[0];
        Assert.Equal(HttpStatusCode.Conflict, (await Send(manager, HttpMethod.Delete, $"/api/activities/1/resources/{item.GetProperty("id").GetGuid()}", new { expectedVersion = item.GetProperty("version").GetGuid(), reason = "Cancelled" })).StatusCode);
    }

    [Fact]
    public async Task Gallery_UnavailableIdentityDisablesCapabilitiesWithoutPretendingEmptyGallery()
    {
        await using var f = await Fixture.Create(); using var manager = f.Client(200);
        await Send(manager, HttpMethod.Post, "/api/activities/1/resources", Link()); f.ProfilesAvailable = false;
        var detail = await Json(await manager.GetAsync("/api/activities/1"));
        Assert.Equal("AUTHORITY_UNAVAILABLE", detail.GetProperty("resourceAvailability").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("resources").ValueKind);
        Assert.False(detail.GetProperty("resourceCapabilities").GetProperty("canManage").GetBoolean());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await manager.GetAsync("/api/activities/1/resources")).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/activities/1/resources")]
    [InlineData("GET", "/api/activities/1/resources/history")]
    [InlineData("POST", "/api/activities/1/resources")]
    [InlineData("PATCH", "/api/activities/1/resources/11111111-1111-1111-1111-111111111111")]
    [InlineData("DELETE", "/api/activities/1/resources/11111111-1111-1111-1111-111111111111")]
    [InlineData("POST", "/api/activities/1/resources/upload-intents")]
    public Task Gallery_GatewayRoutesToActivityService(string method, string path) =>
        new CrossServiceWorkflowAuthorizationTests().ClubManager_GatewayRoutesNewApisToOwningServiceBeforeLegacyCatchAll(method, path, "activity-service");

    private sealed class Fixture(SqliteConnection? sqlite, WebApplication app, string? sqlConnection) : IAsyncDisposable
    {
        internal WebApplication App => app;
        internal bool ManagerEnabled = true;
        internal bool AuthorityAvailable = true;
        internal bool ProfilesAvailable = true;
        internal static async Task<Fixture> Create(bool sql = false)
        {
            SqliteConnection? sqlite = sql ? null : new("Data Source=:memory:"); if (sqlite is not null) await sqlite.OpenAsync();
            var sqlConnection = sql ? new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ADMIN_SERVICE_TEST_CONNECTION_STRING")
                ?? "Server=(localdb)\\ClubManagerApiTests;Database=master;Trusted_Connection=True;TrustServerCertificate=True")
            { InitialCatalog = $"ClubHub_ResourceTests_{Guid.NewGuid():N}" }.ConnectionString : null;
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Issuer"] = "ClubReportHub", ["Jwt:Audience"] = "ClubReportHub.Client", ["Jwt:SigningKey"] = Key, ["Security:InternalWorkflowToken"] = "test-workflow" });
            Fixture f = null!;
            var handler = new Handler(async request =>
            {
                if (!f.AuthorityAvailable) return new(HttpStatusCode.ServiceUnavailable);
                var path = request.RequestUri!.AbsolutePath; object? result = null;
                var actor = request.Headers.Authorization?.Parameter is { } token ? int.Parse(new JwtSecurityTokenHandler().ReadJwtToken(token).Subject) : 0;
                if (path.EndsWith("/user-profiles", StringComparison.Ordinal))
                {
                    if (!f.ProfilesAvailable) return new(HttpStatusCode.ServiceUnavailable);
                    result = (await request.Content!.ReadFromJsonAsync<int[]>())!.Select(id => new InternalUserProfile(id, "User " + id, id + "@fpt.edu.vn", id == 204 ? "HCM" : "HN", true, id == 203, null)).ToArray();
                }
                else if (path.StartsWith("/api/internal/experience/clubs/", StringComparison.Ordinal))
                { var member = int.Parse(request.RequestUri.Query.Split('=').Last()); result = new ClubExperienceAuthority(1, "Club One", "HN", true, member is 100 or 200, DateTimeOffset.UtcNow.AddDays(-10)); }
                else if (path == "/api/clubs/me/access") result = actor == 200 && f.ManagerEnabled ? new[] { new ClubAccessSnapshot(1, "Club One", true, false, true, [200]) }
                    : actor == 100 ? new[] { new ClubAccessSnapshot(1, "Club One", false, false, true, [200]) } : Array.Empty<ClubAccessSnapshot>();
                return result is null ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.OK) { Content = JsonContent.Create(result) };
            });
            HttpClient Http() => new(handler, false) { BaseAddress = new("http://authority/") };
            builder.Services.AddDbContext<ActivityDbContext>(options => { if (sqlConnection is null) options.UseSqlite(sqlite!); else options.UseSqlServer(sqlConnection); });
            builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
            builder.Services.AddSingleton(new ClubAccessClient(Http(), new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }), NullLogger<ClubAccessClient>.Instance));
            builder.Services.AddSingleton(new CurrentUserProfileClient(Http(), builder.Configuration)); builder.Services.AddSingleton(new ClubExperienceAuthorityClient(Http(), builder.Configuration));
            builder.Services.AddSingleton(new ReportVerificationClient(Http(), NullLogger<ReportVerificationClient>.Instance));
            builder.Services.AddSingleton(new ClubMemberRosterClient(Http()));
            var app = builder.Build(); f = new(sqlite, app, sqlConnection); app.UseAuthentication(); app.UseAuthorization(); app.MapActivityEndpoints();
            try
            {
                await f.Db(async db =>
                {
                    if (sql) await db.GetService<IMigrator>().MigrateAsync("20261022090451_ActivityActionWorkflowWithoutScoring"); else await db.Database.EnsureCreatedAsync();
                    db.Activities.AddRange(Enumerable.Range(1, 2).Select(id => new ClubActivity { Id = sql ? 0 : id, ClubId = 1, ClubName = "Club One", Title = "Preserved activity", PublicationStatus = "PUBLISHED", DiscoveryVisibility = "CAMPUS_PUBLIC", StartTimeUtc = DateTimeOffset.UtcNow.AddDays(1), EndTimeUtc = DateTimeOffset.UtcNow.AddDays(2), CreatedByUserId = 200 }));
                    await db.SaveChangesAsync(); if (sql) await db.Database.MigrateAsync();
                });
                await app.StartAsync(); return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        internal HttpClient Client(int actor)
        {
            var role = actor is 200 or 201 or 203 or 204 ? AuthRoles.ClubManager : actor == 202 ? AuthRoles.StudentAffairsAdmin : AuthRoles.ClubMember;
            var token = new JwtSecurityToken("ClubReportHub", "ClubReportHub.Client", [new Claim(JwtRegisteredClaimNames.Sub, actor.ToString()), new Claim(ClaimTypes.Role, role)], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(20), new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
            var client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token)); return client;
        }
        internal async Task Db(Func<ActivityDbContext, Task> action) { await using var scope = app.Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<ActivityDbContext>()); }
        public async ValueTask DisposeAsync()
        {
            if (sqlConnection is not null) { if (!new SqlConnectionStringBuilder(sqlConnection).InitialCatalog.StartsWith("ClubHub_ResourceTests_", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected database."); await Db(async db => await db.Database.EnsureDeletedAsync()); }
            await app.DisposeAsync(); if (sqlite is not null) await sqlite.DisposeAsync();
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request); }
}
