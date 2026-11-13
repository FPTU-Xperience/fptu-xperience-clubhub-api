using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdminService.Data;
using AdminService.Services;
using AuthService.Data;
using AuthService.Endpoints;
using AuthService.Models;
using ClubReportHub.Shared.Experience;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Backend.StabilizationTests;

public sealed class RecommendationPipelineTests
{
    private const string Token = "Synthetic-Recommendation-Internal-Token-2026";

    [Fact]
    public async Task RecommendationClient_PostsLengthDelimitedJsonToEngine()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Recommendation:Token"] = Token
        }).Build();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var received = new List<(string Path, string[] Properties)>();
        app.MapPost("/internal/{operation}", async (Microsoft.AspNetCore.Http.HttpRequest request) =>
        {
            if (request.Headers.ContainsKey("Transfer-Encoding") || request.ContentLength is null or <= 0
                || request.ContentType?.Split(';')[0] != "application/json")
                return Microsoft.AspNetCore.Http.Results.StatusCode(415);
            using var payload = await JsonDocument.ParseAsync(request.Body);
            received.Add((request.Path.Value!, payload.RootElement.EnumerateObject().Select(field => field.Name).Order().ToArray()));
            return Microsoft.AspNetCore.Http.Results.Accepted();
        });
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var engine = new RecommendationClient(http, config);

        await engine.FeedAsync(new { schemaVersion = "1.0" }, DateTimeOffset.UtcNow, CancellationToken.None);
        await engine.InvalidateAsync([11], CancellationToken.None);

        Assert.Collection(received, feed =>
        {
            Assert.Equal("/internal/feeds", feed.Path);
            Assert.Equal(["readStartedAt", "snapshot"], feed.Properties);
        }, invalidation =>
        {
            Assert.Equal("/internal/invalidations", invalidation.Path);
            Assert.Equal(["userIds"], invalidation.Properties);
        });
    }

    [Fact]
    public async Task WorkerFeed_SerializesActivityDeadlinesAndSessionsAsUtc()
    {
        await using var sql = new SqliteConnection("DataSource=:memory:");
        await sql.OpenAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Recommendation:Enabled"] = "true",
            ["Security:InternalWorkflowToken"] = Token,
            ["Recommendation:Token"] = Token
        }).Build();
        var offset = TimeSpan.FromHours(7);
        var start = DateTimeOffset.UtcNow.ToOffset(offset).AddDays(2);
        var end = start.AddHours(1);
        var deadline = start.AddHours(-2);
        var club = new StudentClubMetadata(1, "TEST", "Test Club", "ACADEMIC", "HAN",
            false, false, null, Guid.NewGuid(), null)
        {
            IsRecruiting = true,
            DiscoveryTags = DiscoveryTags.ForClub(null, "ACADEMIC")
        };
        var activity = new StudentActivityItem(2, 1, club.ClubCode, club.ClubName, "Workshop", "", null,
            start, end, "", "UPCOMING", "REGISTER", true, true, "FALL2026", 3,
            Guid.NewGuid(), null, null, [])
        {
            IsInternal = false,
            DiscoveryTags = DiscoveryTags.ForActivity(DiscoveryTags.Store(new(ActivityCategory: "workshop")), []),
            RecommendationSource = new(deadline, "activity-version", [new(3, start, end, true)])
        };
        var student = new RecommendationStudentSource(11, "HAN", "account-version", Guid.Empty, [],
            null, "schedule-version", "UNKNOWN", []);
        using var auth = FeedHttp(async (request, ct) =>
        {
            var query = await request.Content!.ReadFromJsonAsync<RecommendationExportQuery>(ct);
            return FeedResponse(query!.AfterUserId == 0 ? new RecommendationStudentPage([student], 11) : new([], 11));
        });
        using var clubSource = FeedHttp((_, _) => Task.FromResult(FeedResponse(
            new StudentClubScope([club], DateTimeOffset.UtcNow, "scope-version"))));
        using var activitySource = FeedHttp((_, _) => Task.FromResult(FeedResponse(
            new StudentActivityPage([activity], 1, 100, 1, 1, DateTimeOffset.UtcNow, "scope-version"))));
        var feed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var engine = FeedHttp(async (request, ct) =>
        {
            feed.TrySetResult(await request.Content!.ReadFromJsonAsync<JsonElement>(ct));
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient("RecommendationAuth").Returns(auth);
        clients.CreateClient("ExperienceActivity").Returns(activitySource);
        var services = new ServiceCollection().AddDbContext<AdminDbContext>(x => x.UseSqlite(sql))
            .AddSingleton<IHttpClientFactory>(clients)
            .AddSingleton(new ClubExperienceAuthorityClient(clubSource, config))
            .AddSingleton(new StudentExperienceSourceClient(clients, config))
            .AddSingleton(new RecommendationClient(engine, config));
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Semesters.Add(new Semester { SemesterCode = "FALL2026", IsActive = true });
            await db.SaveChangesAsync();
        }
        using var worker = new RecommendationFeedWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            config, NullLogger<RecommendationFeedWorker>.Instance, new RecommendationWakeSignal());
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var message = await feed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var exported = message.GetProperty("snapshot").GetProperty("activities")[0];
            var session = exported.GetProperty("sessions")[0];
            Assert.Equal(deadline, exported.GetProperty("registrationDeadline").GetDateTimeOffset());
            Assert.Equal(start, session.GetProperty("start").GetDateTimeOffset());
            Assert.Equal(end, session.GetProperty("end").GetDateTimeOffset());
            Assert.Equal(TimeSpan.Zero, exported.GetProperty("registrationDeadline").GetDateTimeOffset().Offset);
            Assert.Equal(TimeSpan.Zero, session.GetProperty("start").GetDateTimeOffset().Offset);
            Assert.Equal(TimeSpan.Zero, session.GetProperty("end").GetDateTimeOffset().Offset);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    private static HttpClient FeedHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new FeedHandler(send)) { BaseAddress = new Uri("http://test/") };

    private static HttpResponseMessage FeedResponse<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private sealed class FeedHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    [Fact]
    public async Task AuthExport_IsPrivatePagedAndContainsOnlyFeatureData()
    {
        await using var sql = new SqliteConnection("DataSource=:memory:");
        await sql.OpenAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Security:InternalWorkflowToken"] = Token });
        builder.Services.AddAuthorization();
        builder.Services.AddAuthentication("Bearer").AddJwtBearer();
        builder.Services.AddDbContext<AuthDbContext>(x => x.UseSqlite(sql));
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapRecommendationSourceEndpoints();
        await app.StartAsync();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new User { Id = 11, Username = "u11", Email = "private@example.invalid", FullName = "Private person" },
                new User { Id = 12, Username = "u12", Email = "locked@example.invalid", FullName = "Locked", IsLocked = true });
            db.UserProfiles.Add(new CanonicalUserProfile { UserId = 11, PhoneNumber = "private-phone", Address = "private-address", InterestsJson = "[\"technology\"]" });
            await db.SaveChangesAsync();
        }
        using var client = app.GetTestClient();
        var query = new RecommendationExportQuery(0, 1, []);
        var denied = await client.PostAsJsonAsync("/api/internal/recommendation/students", query);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        client.DefaultRequestHeaders.Add("X-Internal-Workflow-Token", Token);
        var response = await client.PostAsJsonAsync("/api/internal/recommendation/students", query);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private@example", body);
        Assert.DoesNotContain("Private person", body);
        Assert.DoesNotContain("private-phone", body);
        Assert.DoesNotContain("private-address", body);
        var page = await response.Content.ReadFromJsonAsync<RecommendationStudentPage>();
        Assert.NotNull(page);
        Assert.Equal(11, page.NextUserId);
        Assert.Single(page.Items);
        Assert.Equal("UNKNOWN", page.Items[0].ScheduleAvailability);
        Assert.Equal("technology", Assert.Single(page.Items[0].Interests));
        response = await client.PostAsJsonAsync("/api/internal/recommendation/students", new RecommendationExportQuery(11, 50, []));
        Assert.Empty((await response.Content.ReadFromJsonAsync<RecommendationStudentPage>())!.Items);
    }

    [Fact]
    public async Task ProfileAndScheduleInvalidation_CommitWithSourceAndRollbackTogether()
    {
        await using var sql = new SqliteConnection("DataSource=:memory:");
        await sql.OpenAsync();
        await using var db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(sql).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new User { Id = 42, Username = "u42", Email = "u42@example.invalid", FullName = "Student" });
        await db.SaveChangesAsync();
        db.OutboxMessages.RemoveRange(db.OutboxMessages);
        await db.SaveChangesAsync();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.UserProfiles.Add(new CanonicalUserProfile { UserId = 42, DeclaredMajor = "Software engineering" });
            await db.SaveChangesAsync();
            var message = Assert.Single(await db.OutboxMessages.Where(x => x.EventType == RecommendationChangeCapture.Route).ToArrayAsync());
            Assert.Equal(42, JsonDocument.Parse(message.Payload).RootElement.GetProperty("userIds")[0].GetInt32());
            Assert.DoesNotContain("Software", message.Payload);
            await transaction.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        Assert.Empty(await db.UserProfiles.ToArrayAsync());
        Assert.Empty(await db.OutboxMessages.ToArrayAsync());
    }
}
