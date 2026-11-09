using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AuthService.Data;
using AuthService.Endpoints;
using AuthService.Models;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Backend.StabilizationTests;

public sealed class SelfProfileExtensionsContractTests
{
    private const string Key = "SelfProfileExtensions-Only-Test-Secret-2026-Not-Production";
    private const string Secret = "SelfProfileExtensions-Internal-Test-Secret";
    private const string Route = "/api/users/me/profile";

    [Fact]
    public async Task SelfFields_RoundTripAcrossClientsOmissionAndNullClearWithEditableMetadata()
    {
        await using var f = await Fixture.Create(); using var first = f.Client();
        var original = await Get(first);
        var response = await Patch(first, new { expectedVersion = original.GetProperty("version").GetGuid(), profile = new { headline = "  Club coordinator  " }, personal = new { dateOfBirth = "2004-02-29", address = "  Đường 1, Hà Nội  " } }, "save");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var second = f.Client(); var read = await Get(second);
        Assert.Equal("Club coordinator", read.GetProperty("profile").GetProperty("headline").GetString());
        Assert.Equal("2004-02-29", read.GetProperty("personal").GetProperty("dateOfBirth").GetString());
        Assert.Equal("Đường 1, Hà Nội", read.GetProperty("personal").GetProperty("address").GetString());
        Assert.Contains(read.GetProperty("editableFields").EnumerateArray(), x => x.GetString() == "personal.dateOfBirth");
        Assert.DoesNotContain(read.GetProperty("editableFields").EnumerateArray(), x => x.GetString()!.Contains("studentCode", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.OK, (await Patch(second, new { expectedVersion = read.GetProperty("version").GetGuid(), profile = new { about = "Another field" } }, "about")).StatusCode);
        read = await Get(first); Assert.Equal("Đường 1, Hà Nội", read.GetProperty("personal").GetProperty("address").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Patch(first, new { expectedVersion = read.GetProperty("version").GetGuid(), profile = new { headline = (string?)null }, personal = new { dateOfBirth = (string?)null, address = (string?)null } }, "clear")).StatusCode);
        read = await Get(second); Assert.Equal("", read.GetProperty("profile").GetProperty("headline").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("personal").GetProperty("dateOfBirth").ValueKind);
        Assert.Equal(JsonValueKind.Null, read.GetProperty("personal").GetProperty("address").ValueKind);
    }

    [Fact]
    public async Task ExtendedPrivateFieldsAreAbsentFromInternalMemberProjectionAndOtherAccounts()
    {
        await using var f = await Fixture.Create(); using var owner = f.Client();
        Assert.Equal(HttpStatusCode.OK, (await Patch(owner, new { expectedVersion = Guid.Empty, profile = new { headline = "Private headline" }, personal = new { dateOfBirth = "2001-12-31", address = "Private address", phoneNumber = "0901234567" } }, "private")).StatusCode);
        using var other = f.Client(101); var otherProfile = await Get(other);
        Assert.Equal(JsonValueKind.Null, otherProfile.GetProperty("personal").GetProperty("dateOfBirth").ValueKind);
        using var internalClient = f.App.GetTestClient(); internalClient.DefaultRequestHeaders.Add("X-Internal-Workflow-Token", Secret);
        var profiles = await internalClient.PostAsJsonAsync("/api/internal/experience/user-profiles", new[] { 100 });
        Assert.Equal(HttpStatusCode.OK, profiles.StatusCode); var value = JsonDocument.Parse(await profiles.Content.ReadAsStringAsync()).RootElement[0].GetProperty("profile");
        Assert.False(value.GetProperty("personal").TryGetProperty("dateOfBirth", out _));
        Assert.False(value.GetProperty("personal").TryGetProperty("address", out _));
        Assert.False(value.GetProperty("profile").TryGetProperty("headline", out _));
        Assert.Equal("0901234567", value.GetProperty("personal").GetProperty("phoneNumber").GetString());
    }

    [Theory]
    [InlineData("dateOfBirth", "2027-01-01")]
    [InlineData("dateOfBirth", "2003-02-29")]
    [InlineData("dateOfBirth", "01/01/2004")]
    [InlineData("dateOfBirth", "not a date")]
    [InlineData("headline", "CONTROL")]
    [InlineData("headline", "LONG")]
    [InlineData("address", "LONG")]
    public async Task InvalidNewFieldsAreRejectedWithoutCreatingProfile(string field, string value)
    {
        await using var f = await Fixture.Create(); using var client = f.Client();
        if (value == "LONG") value = new string('x', field == "headline" ? 161 : 301);
        if (value == "CONTROL") value = "bad\u0001headline";
        var section = field == "headline" ? "profile" : "personal";
        var input = new Dictionary<string, object> { ["expectedVersion"] = Guid.Empty, [section] = new Dictionary<string, object> { [field] = value } };
        Assert.Equal(HttpStatusCode.BadRequest, (await Patch(client, input, "invalid")).StatusCode);
        await f.Db(async db => Assert.Equal(0, await db.UserProfiles.CountAsync()));
    }

    [Fact]
    public async Task StaleReplayAtomicValidationAndCurrentAccountChecksProtectWrites()
    {
        await using var f = await Fixture.Create(); using var client = f.Client();
        var invalid = new { expectedVersion = Guid.Empty, profile = new { about = "Must roll back" }, personal = new { address = new string('x', 301) } };
        Assert.Equal(HttpStatusCode.BadRequest, (await Patch(client, invalid, "invalid")).StatusCode);
        Assert.Equal(Guid.Empty, (await Get(client)).GetProperty("version").GetGuid());
        var payload = new { expectedVersion = Guid.Empty, profile = new { headline = "Saved" } };
        var first = await Patch(client, payload, "saved"); Assert.Equal(HttpStatusCode.OK, first.StatusCode); var receipt = await first.Content.ReadAsStringAsync();
        Assert.Equal(receipt, await (await Patch(client, payload, "saved")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await Patch(client, payload, "stale")).StatusCode);
        await f.Db(async db => { (await db.Users.SingleAsync(x => x.Id == 100)).IsLocked = true; await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.Forbidden, (await Patch(client, payload, "saved")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Route)).StatusCode);
    }

    [Fact]
    public async Task IdentityAcademicAndOtherUsersRemainReadOnlyAndJsonIsBoundedAndUnique()
    {
        await using var f = await Fixture.Create(); using var client = f.Client();
        foreach (var field in new[] { "studentCode", "email", "major", "year", "campusCode" })
        {
            var input = new { expectedVersion = Guid.Empty, personal = new Dictionary<string, object> { [field] = "forged" } };
            Assert.Equal(HttpStatusCode.Forbidden, (await Patch(client, input, field)).StatusCode);
        }
        using var manager = f.Client(200);
        Assert.Equal(HttpStatusCode.BadRequest, (await Patch(manager, new { userId = 100, expectedVersion = Guid.Empty, profile = new { headline = "forged" } }, "user")).StatusCode);
        var raw = new HttpRequestMessage(HttpMethod.Patch, Route) { Content = new StringContent("{\"expectedVersion\":\"00000000-0000-0000-0000-000000000000\",\"profile\":{\"headline\":\"one\",\"Headline\":\"two\"}}", Encoding.UTF8, "application/json") };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(raw)).StatusCode);
        Assert.Equal((HttpStatusCode)413, (await Patch(client, new { expectedVersion = Guid.Empty, profile = new { about = new string('x', 40000) } }, "large")).StatusCode);
        await f.Db(async db => Assert.Equal(0, await db.UserProfiles.CountAsync()));
    }

    [Fact]
    public async Task SqlServer_MigrationPreservesOldProfileAndNewFieldsPersist()
    {
        await using var f = await Fixture.Create(sql: true); using var client = f.Client(); var old = await Get(client);
        Assert.Equal("Legacy display", old.GetProperty("profile").GetProperty("displayName").GetString());
        Assert.Equal("Legacy about", old.GetProperty("profile").GetProperty("about").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Patch(client, new { expectedVersion = old.GetProperty("version").GetGuid(), profile = new { headline = "New" }, personal = new { dateOfBirth = "2004-02-29", address = "Saved" } }, "sql")).StatusCode);
        var read = await Get(client); Assert.Equal("2004-02-29", read.GetProperty("personal").GetProperty("dateOfBirth").GetString());
        await f.Db(async db => { Assert.False(db.Database.HasPendingModelChanges()); Assert.Equal(3, await db.Users.CountAsync()); });
    }

    [Fact]
    public async Task Onboarding_IsCanonicalPrivateVersionedAndSurvivesLaterProfileEdits()
    {
        await using var f = await Fixture.Create(); using var client = f.Client();
        var payload = new
        {
            expectedVersion = Guid.Empty,
            academic = new { major = "Kỹ thuật phần mềm" },
            profile = new { interests = new[] { "Công nghệ", "Thiết kế", "Cộng đồng" } },
            onboarding = new { version = 1, completed = true, syncTimetable = true }
        };
        var response = await Patch(client, payload, "onboarding");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var second = f.Client(); var read = await Get(second);
        Assert.Equal("Kỹ thuật phần mềm", read.GetProperty("academic").GetProperty("major").GetString());
        var completed = read.GetProperty("onboarding").GetProperty("completedAt").GetString();
        Assert.NotNull(completed);
        Assert.True(read.GetProperty("onboarding").GetProperty("syncTimetable").GetBoolean());
        Assert.Equal("SELF_DECLARED", read.GetProperty("academic").GetProperty("majorAvailability").GetString());
        using var internalClient = f.App.GetTestClient();
        internalClient.DefaultRequestHeaders.Add("X-Internal-Workflow-Token", Secret);
        var projection = await internalClient.PostAsJsonAsync("/api/internal/experience/user-profiles", new[] { 100 });
        Assert.Equal(HttpStatusCode.OK, projection.StatusCode);
        using var projectionJson = JsonDocument.Parse(await projection.Content.ReadAsStringAsync());
        var projectedProfile = projectionJson.RootElement[0].GetProperty("profile");
        Assert.False(projectedProfile.TryGetProperty("academic", out _));
        Assert.False(projectedProfile.TryGetProperty("onboarding", out _));
        Assert.Equal(HttpStatusCode.Conflict, (await Patch(client, payload, "stale-onboarding")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Patch(second, new { expectedVersion = read.GetProperty("version").GetGuid(), profile = new { interests = Array.Empty<string>() } }, "later-edit")).StatusCode);
        Assert.Equal(completed, (await Get(client)).GetProperty("onboarding").GetProperty("completedAt").GetString());
        using var other = f.Client(101);
        Assert.Equal(JsonValueKind.Null, (await Get(other)).GetProperty("onboarding").GetProperty("completedAt").ValueKind);
        await f.Db(async db => { (await db.Users.SingleAsync(x => x.Id == 100)).IsLocked = true; await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.Forbidden, (await Patch(client, payload, "onboarding")).StatusCode);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("major")]
    [InlineData("version")]
    [InlineData("identity")]
    [InlineData("timestamp")]
    public async Task Onboarding_InvalidCompletionIsAtomic(string mode)
    {
        await using var f = await Fixture.Create(); using var client = f.Client();
        var academic = new Dictionary<string, object> { ["major"] = mode == "major" ? "Unknown major" : "Kỹ thuật phần mềm" };
        var onboarding = new Dictionary<string, object> { ["version"] = mode == "version" ? "1" : (object)1, ["completed"] = true };
        if (mode == "identity") academic["studentCode"] = "forged";
        if (mode == "timestamp") onboarding["completedAt"] = "2020-01-01";
        var payload = new
        {
            expectedVersion = Guid.Empty,
            academic,
            onboarding,
            profile = new { interests = mode == "duplicate" ? new[] { "Code", "CODE", "code" } : new[] { "Code", "Art", "Sport" } }
        };
        Assert.Equal(mode is "identity" or "timestamp" ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest,
            (await Patch(client, payload, "invalid-onboarding")).StatusCode);
        await f.Db(async db => Assert.Equal(0, await db.UserProfiles.CountAsync()));
    }

    private static async Task<JsonElement> Get(HttpClient client)
    { var response = await client.GetAsync(Route); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone(); }
    private static Task<HttpResponseMessage> Patch(HttpClient client, object input, string key)
    { var request = new HttpRequestMessage(HttpMethod.Patch, Route) { Content = JsonContent.Create(input) }; request.Headers.Add("Idempotency-Key", key); return client.SendAsync(request); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero); }
    private sealed class Fixture(WebApplication app, SqliteConnection? sqlite, string? sql) : IAsyncDisposable
    {
        internal WebApplication App => app;
        internal static async Task<Fixture> Create(bool sql = false)
        {
            var connection = sql ? null : new SqliteConnection("Data Source=:memory:"); if (connection is not null) await connection.OpenAsync();
            var sqlString = sql ? new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ADMIN_SERVICE_TEST_CONNECTION_STRING") ?? "Server=(localdb)\\ClubManagerApiTests;Integrated Security=True;TrustServerCertificate=True") { InitialCatalog = "ClubHub_ProfileExtensionsTests_" + Guid.NewGuid().ToString("N") }.ConnectionString : null;
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Issuer"] = "ClubReportHub", ["Jwt:Audience"] = "ClubReportHub.Client", ["Jwt:SigningKey"] = Key, ["Security:InternalWorkflowToken"] = Secret });
            builder.Services.AddDbContext<AuthDbContext>(x => { if (connection is null) x.UseSqlServer(sqlString); else x.UseSqlite(connection); });
            builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment); builder.Services.AddSingleton<TimeProvider>(new Clock());
            var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapCanonicalProfileEndpoints(); var f = new Fixture(app, connection, sqlString);
            try
            {
                await f.Db(async db =>
                {
                    if (sql) await db.GetService<IMigrator>().MigrateAsync("20261022143758_PrivateStudySchedules"); else await db.Database.EnsureCreatedAsync();
                    foreach (var id in new[] { 100, 101, 200 }) db.Add(new User { Id = id, Username = "profile" + id, FullName = "Account " + id, Email = id + "@fpt.edu.vn", CampusCode = "HAN", IsActive = true });
                    if (sql) { await db.Database.OpenConnectionAsync(); await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Users] ON"); }
                    if (sql) await HistoricalAuthFixture.SeedTrackedUsersAsync(db); else await db.SaveChangesAsync();
                    if (sql)
                    {
                        await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Users] OFF");
                        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [UserProfiles] ([UserId],[Version],[DisplayName],[About],[SkillsJson],[InterestsJson],[PhoneNumber],[UpdatedAtUtc]) VALUES ({100},{Guid.NewGuid()},{"Legacy display"},{"Legacy about"},{"[]"},{"[]"},{"0901234567"},{DateTimeOffset.UtcNow})");
                        await db.Database.CloseConnectionAsync(); await db.Database.MigrateAsync();
                    }
                }); await app.StartAsync(); return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        internal HttpClient Client(int id = 100)
        {
            var token = new JwtSecurityToken("ClubReportHub", "ClubReportHub.Client", [new Claim(JwtRegisteredClaimNames.Sub, id.ToString()), new Claim(ClaimTypes.Role, id == 200 ? AuthRoles.ClubManager : AuthRoles.ClubMember)], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(1), new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
            var client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token)); return client;
        }
        internal async Task Db(Func<AuthDbContext, Task> action) { await using var scope = app.Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<AuthDbContext>()); }
        public async ValueTask DisposeAsync()
        {
            if (sql is not null) { if (!new SqlConnectionStringBuilder(sql).InitialCatalog.StartsWith("ClubHub_ProfileExtensionsTests_", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected test database."); await Db(async db => await db.Database.EnsureDeletedAsync()); }
            await app.DisposeAsync(); if (sqlite is not null) await sqlite.DisposeAsync();
        }
    }
}
