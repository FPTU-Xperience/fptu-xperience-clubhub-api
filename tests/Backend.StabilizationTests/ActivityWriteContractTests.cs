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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Backend.StabilizationTests;

public sealed class ActivityWriteContractTests
{
    private const string SigningKey = "ActivityWriteContractSigningKey1234567890!@#$";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivityWrite_CreateAcceptsEquivalentFieldNames(bool alias)
    {
        await using var fixture = await ActivityFixture.CreateAsync();
        using var client = fixture.Client(200, AuthRoles.ClubManager);
        var content = alias
            ? new { clubId = 1, clubName = "One", name = "New activity", description = "Description", startAt = "2026-10-01T09:00:00Z", endAt = "2026-10-01T11:00:00Z", location = "Hall" }
            : null;
        var response = content is null
            ? await client.PostAsJsonAsync("/api/activities/", new { clubId = 1, clubName = "One", title = "New activity", description = "Description", startTimeUtc = "2026-10-01T09:00:00Z", endTimeUtc = "2026-10-01T11:00:00Z", location = "Hall" })
            : await client.PostAsJsonAsync("/api/activities/", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("New activity", body.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("{\"clubId\":1,\"clubName\":\"One\",\"title\":\"First\",\"name\":\"Second\",\"description\":\"Description\",\"location\":\"Hall\",\"startTimeUtc\":\"2026-10-01T09:00:00Z\",\"endTimeUtc\":\"2026-10-01T11:00:00Z\"}")]
    [InlineData("{\"clubId\":1,\"clubName\":\"One\",\"title\":\"First\",\"description\":\"Description\",\"location\":\"Hall\",\"startTimeUtc\":\"2026-10-01T09:00:00Z\",\"startAt\":\"2026-10-01T10:00:00Z\",\"endTimeUtc\":\"2026-10-01T11:00:00Z\"}")]
    [InlineData("{\"clubId\":1,\"title\":\"First\",\"description\":\"Description\",\"location\":\"Hall\",\"startTimeUtc\":\"2026-10-01T09:00:00Z\",\"endTimeUtc\":\"2026-10-01T11:00:00Z\"}")]
    public async Task ActivityWrite_CreateRejectsAliasConflictAndMissingClubName(string json)
    {
        await using var fixture = await ActivityFixture.CreateAsync();
        using var client = fixture.Client(200, AuthRoles.ClubManager);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/activities/")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
    }

    [Theory]
    [InlineData(100, AuthRoles.ClubMember, HttpStatusCode.Forbidden)]
    [InlineData(200, AuthRoles.ClubManager, HttpStatusCode.OK)]
    [InlineData(201, AuthRoles.ClubManager, HttpStatusCode.Forbidden)]
    [InlineData(202, AuthRoles.StudentAffairsAdmin, HttpStatusCode.OK)]
    public async Task ActivityWrite_UpdateRequiresExactClubManagerOrReviewer(int actor, string role, HttpStatusCode expected)
    {
        await using var fixture = await ActivityFixture.CreateAsync();
        using var client = fixture.Client(actor, role);
        var response = await client.PutAsJsonAsync("/api/activities/1", new { name = " Updated title ", description = "Updated description" });
        Assert.Equal(expected, response.StatusCode);
        var row = await fixture.ActivityAsync();
        Assert.Equal(expected == HttpStatusCode.OK ? "Updated title" : "Original title", row.Title);
        Assert.Equal(1, row.ClubId);
        Assert.Equal(100, row.CreatedByUserId);
    }

    [Theory]
    [InlineData("{\"title\":\"One\",\"name\":\"Two\"}")]
    [InlineData("{\"startTimeUtc\":\"2026-09-23T10:00:00Z\",\"startAt\":\"2026-09-23T11:00:00Z\"}")]
    [InlineData("{\"clubId\":2,\"title\":\"Forged\"}")]
    [InlineData("{\"createdByUserId\":200}")]
    [InlineData("{\"sourceReportId\":0}")]
    [InlineData("{\"status\":\"Completed\"}")]
    [InlineData("{\"name\":\" \"}")]
    [InlineData("{\"endAt\":\"2026-09-21T09:00:00Z\"}")]
    [InlineData("{}")]
    public async Task ActivityWrite_UpdateRejectsConflictingAliasesProtectedFieldsAndInvalidSchedule(string json)
    {
        await using var fixture = await ActivityFixture.CreateAsync();
        using var client = fixture.Client(200, AuthRoles.ClubManager);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/activities/1")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
        Assert.Equal("Original title", (await fixture.ActivityAsync()).Title);
    }

    [Theory]
    [InlineData(ActivityStatuses.Completed)]
    [InlineData(ActivityStatuses.Cancelled)]
    public async Task ActivityWrite_TerminalActivityCannotBeEdited(string status)
    {
        await using var fixture = await ActivityFixture.CreateAsync(status: status);
        using var client = fixture.Client(200, AuthRoles.ClubManager);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PutAsJsonAsync("/api/activities/1", new { title = "Changed" })).StatusCode);
    }

    [Fact]
    public async Task ActivityWrite_ReportLinkedScheduleCannotBeEdited()
    {
        await using var fixture = await ActivityFixture.CreateAsync(sourceReportId: 10);
        using var client = fixture.Client(202, AuthRoles.StudentAffairsAdmin);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PutAsJsonAsync("/api/activities/1", new { title = "Changed" })).StatusCode);
    }

    [Theory]
    [InlineData(100, AuthRoles.ClubMember, HttpStatusCode.Forbidden)]
    [InlineData(200, AuthRoles.ClubManager, HttpStatusCode.OK)]
    [InlineData(201, AuthRoles.ClubManager, HttpStatusCode.Forbidden)]
    [InlineData(202, AuthRoles.StudentAffairsAdmin, HttpStatusCode.OK)]
    public async Task ActivityWrite_DeleteCancelsOnlyForClubManagerOrReviewer(int actor, string role, HttpStatusCode expected)
    {
        await using var fixture = await ActivityFixture.CreateAsync();
        using var client = fixture.Client(actor, role);
        var response = await client.DeleteAsync("/api/activities/1");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? ActivityStatuses.Cancelled : ActivityStatuses.Scheduled,
            (await fixture.ActivityAsync()).Status);
        if (expected == HttpStatusCode.OK)
        {
            Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/activities/1")).StatusCode);
        }
    }

    [Fact]
    public async Task ActivityWrite_DeletePreservesLinkedActivityAndRejectsCompletedActivity()
    {
        await using var linked = await ActivityFixture.CreateAsync(sourceReportId: 10);
        using var reviewer = linked.Client(202, AuthRoles.StudentAffairsAdmin);
        Assert.Equal(HttpStatusCode.OK, (await reviewer.DeleteAsync("/api/activities/1")).StatusCode);
        var linkedRow = await linked.ActivityAsync();
        Assert.Equal(10, linkedRow.SourceReportId);
        Assert.Equal(ActivityStatuses.Cancelled, linkedRow.Status);

        await using var completed = await ActivityFixture.CreateAsync(status: ActivityStatuses.Completed);
        using var manager = completed.Client(200, AuthRoles.ClubManager);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.DeleteAsync("/api/activities/1")).StatusCode);
        Assert.Equal(ActivityStatuses.Completed, (await completed.ActivityAsync()).Status);
    }

    [Theory]
    [InlineData(100, null, HttpStatusCode.OK)]
    [InlineData(100, 100, HttpStatusCode.OK)]
    [InlineData(100, 101, HttpStatusCode.Forbidden)]
    [InlineData(200, 100, HttpStatusCode.OK)]
    [InlineData(201, 100, HttpStatusCode.Forbidden)]
    [InlineData(101, null, HttpStatusCode.Forbidden)]
    public async Task ActivityWrite_RegistrationUsesTrustedMembership(int actor, int? requestedUser, HttpStatusCode expected)
    {
        await using var fixture = await ActivityFixture.CreateAsync();
        var role = actor is 200 or 201 ? AuthRoles.ClubManager : AuthRoles.ClubMember;
        using var client = fixture.Client(actor, role);
        var response = await client.PostAsJsonAsync("/api/activities/1/participants", new { userId = requestedUser });
        Assert.Equal(expected, response.StatusCode);
        var participants = await fixture.ParticipantsAsync();
        if (expected == HttpStatusCode.OK)
        {
            var participant = Assert.Single(participants);
            Assert.Equal(100, participant.UserId);
            Assert.Equal("Trusted member", participant.FullName);
        }
        else Assert.Empty(participants);
    }

    [Fact]
    public async Task ActivityWrite_RegistrationIsIdempotentAndRejectsForgedName()
    {
        await using var fixture = await ActivityFixture.CreateAsync();
        using var client = fixture.Client(100, AuthRoles.ClubMember);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/activities/1/participants", new { fullName = "Forged" })).StatusCode);
        var first = await client.PostAsJsonAsync("/api/activities/1/participants", new { });
        var second = await client.PostAsJsonAsync("/api/activities/1/participants", new { });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstBody.GetProperty("participant").GetProperty("id").GetInt32(),
            secondBody.GetProperty("participant").GetProperty("id").GetInt32());
        Assert.Single(await fixture.ParticipantsAsync());
    }

    [Fact]
    public async Task ActivityWrite_SelfCheckInUsesVietnamDateAndIsIdempotent()
    {
        var instant = new DateTimeOffset(2026, 9, 22, 18, 30, 0, TimeSpan.Zero);
        await using var fixture = await ActivityFixture.CreateAsync(now: instant);
        using var client = fixture.Client(100, AuthRoles.ClubMember);
        var first = await client.PostAsync("/api/activities/1/check-in", null);
        var second = await client.PostAsync("/api/activities/1/check-in", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var attendance = Assert.Single(await fixture.AttendancesAsync());
        Assert.Equal(new DateOnly(2026, 9, 23), attendance.AttendanceDate);
        Assert.Equal(100, attendance.UserId);
        Assert.Equal(100, attendance.CheckedInByUserId);
        Assert.Equal(instant, attendance.CheckedInAtUtc);
        var history = await client.GetFromJsonAsync<JsonElement>("/api/activities/1/my-attendance?page=1&pageSize=10&userId=101");
        Assert.Equal(100, history.GetProperty("userId").GetInt32());
        Assert.Equal(1, history.GetProperty("total").GetInt32());
        Assert.Single(history.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData(101, AuthRoles.ClubMember, HttpStatusCode.Forbidden)]
    [InlineData(200, AuthRoles.ClubManager, HttpStatusCode.Forbidden)]
    public async Task ActivityWrite_CheckInDoesNotAcceptAnotherActor(int actor, string role, HttpStatusCode expected)
    {
        await using var fixture = await ActivityFixture.CreateAsync();
        using var client = fixture.Client(actor, role);
        Assert.Equal(expected, (await client.PostAsync("/api/activities/1/check-in", null)).StatusCode);
        Assert.Empty(await fixture.AttendancesAsync());
    }

    [Fact]
    public async Task ActivityWrite_CheckInRejectsForgedDateAndTimeWithoutWriting()
    {
        await using var fixture = await ActivityFixture.CreateAsync();
        using var client = fixture.Client(100, AuthRoles.ClubMember);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/activities/1/check-in", new { userId = 101, attendanceDate = "2026-09-20" })).StatusCode);
        Assert.Empty(await fixture.AttendancesAsync());
    }

    [Fact]
    public async Task ActivityWrite_CompleteBeforeActivityEndsReturnsConflict()
    {
        await using var fixture = await ActivityFixture.CreateAsync();
        using var client = fixture.Client(200, AuthRoles.ClubManager);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsync("/api/activities/1/complete", null)).StatusCode);
        Assert.Equal(ActivityStatuses.Scheduled, (await fixture.ActivityAsync()).Status);
    }

    [Fact]
    public async Task ActivityWrite_CompleteIsGuardedAndRepeatDoesNotChangeState()
    {
        await using var fixture = await ActivityFixture.CreateAsync(now: new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero));
        using var member = fixture.Client(100, AuthRoles.ClubMember);
        using var manager = fixture.Client(200, AuthRoles.ClubManager);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PatchAsync("/api/activities/1/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PatchAsync("/api/activities/1/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PatchAsync("/api/activities/1/complete", null)).StatusCode);
        Assert.Equal(ActivityStatuses.Completed, (await fixture.ActivityAsync()).Status);
    }

    private sealed class ActivityFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly WebApplication _app;

        private ActivityFixture(SqliteConnection connection, WebApplication app)
        {
            _connection = connection;
            _app = app;
        }

        public static async Task<ActivityFixture> CreateAsync(
            string status = ActivityStatuses.Scheduled,
            int? sourceReportId = null,
            DateTimeOffset? now = null)
        {
            var instant = now ?? new DateTimeOffset(2026, 9, 23, 9, 0, 0, TimeSpan.Zero);
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "ClubReportHub",
                ["Jwt:Audience"] = "ClubReportHub.Client",
                ["Jwt:SigningKey"] = SigningKey
            });
            builder.Services.AddDbContext<ActivityDbContext>(options => options.UseSqlite(connection));
            builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
            builder.Services.AddSingleton<TimeProvider>(new FixedClock(instant));
            builder.Services.AddSingleton(CreateClubAccess());
            builder.Services.AddSingleton(CreateRoster());
            builder.Services.AddSingleton(new ReportVerificationClient(
                new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))) { BaseAddress = new Uri("http://localhost:5103/") },
                NullLogger<ReportVerificationClient>.Instance));
            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapActivityEndpoints();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
                await db.Database.EnsureCreatedAsync();
                db.Activities.Add(new ClubActivity
                {
                    Id = 1,
                    ClubId = 1,
                    ClubName = "One",
                    Title = "Original title",
                    Description = "Original description",
                    Location = "Hall",
                    StartTimeUtc = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero),
                    EndTimeUtc = new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero),
                    SourceReportId = sourceReportId,
                    Status = status,
                    CreatedByUserId = 100
                });
                await db.SaveChangesAsync();
            }
            await app.StartAsync();
            return new ActivityFixture(connection, app);
        }

        public HttpClient Client(int actor, string role)
        {
            var client = _app.GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(actor, role));
            return client;
        }

        public async Task<ClubActivity> ActivityAsync()
        {
            await using var scope = _app.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ActivityDbContext>().Activities.AsNoTracking().SingleAsync();
        }

        public async Task<ActivityParticipant[]> ParticipantsAsync()
        {
            await using var scope = _app.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ActivityDbContext>().ActivityParticipants.AsNoTracking().ToArrayAsync();
        }

        public async Task<ActivityAttendance[]> AttendancesAsync()
        {
            await using var scope = _app.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ActivityDbContext>().ActivityAttendances.AsNoTracking().ToArrayAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static ClubAccessClient CreateClubAccess()
        {
            var handler = new StubHandler(request =>
            {
                var actor = Actor(request);
                var access = actor switch
                {
                    100 => new[] { new ClubAccessSnapshot(1, "One", false, false, true, [200], [100], []) },
                    200 => [new ClubAccessSnapshot(1, "One", true, false, true, [200], [100], [])],
                    201 => [new ClubAccessSnapshot(2, "Two", true, false, true, [201], [201], [])],
                    _ => []
                };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(access) };
            });
            return new ClubAccessClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5102/") },
                new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }), NullLogger<ClubAccessClient>.Instance);
        }

        private static ClubMemberRosterClient CreateRoster()
        {
            var handler = new StubHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/me/memberships", StringComparison.Ordinal))
                {
                    var memberships = Actor(request) == 100
                        ? new[] { new { id = 10, clubId = 1, userId = 100, fullName = "Trusted member", status = "Approved", requestedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero) } }
                        : [];
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(memberships) };
                }
                if (request.RequestUri.AbsolutePath.EndsWith("/member-roster/resolve", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(new[] { new ClubMemberRosterItem(10, 100, "Trusted member", "m@example.test", "090", "Member", "Approved", new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)) })
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            return new ClubMemberRosterClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5102/") });
        }
    }

    private static int Actor(HttpRequestMessage request)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter);
        return int.Parse(token.Subject);
    }

    private static string Token(int userId, string role)
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken("ClubReportHub", "ClubReportHub.Client",
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim(ClaimTypes.Role, role)],
            notBefore: DateTime.UtcNow.AddMinutes(-1), expires: DateTime.UtcNow.AddMinutes(10), credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handle(request));
    }
}
