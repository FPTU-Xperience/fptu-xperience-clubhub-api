using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AdminService.Contracts;
using AdminService.Data;
using AdminService.Endpoints;
using AdminService.Errors;
using AdminService.Security;
using AdminService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Experience;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Backend.StabilizationTests;

public sealed class RawFundingWorkflowTests
{
    private const string SigningKey = "RawFunding-Tests-Only-Not-Production-Long-2026";
    private const string InternalToken = "raw-funding-test-only-internal-token";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SqlConcurrentGrantsKeepOneVersionedAllocationAndReconcile()
    {
        await using var f = await Fixture.Create(sql: true); using var first = f.Client(202); using var second = f.Client(202);
        var grant = new { semesterId = "FA2026", expectedVersion = Guid.Empty, amount = 100, reason = "Concurrent allocation from the same account version" };
        var responses = await Task.WhenAll(Send(first, "/api/v1/raw-funding/1/grants", grant, "grant-first"), Send(second, "/api/v1/raw-funding/1/grants", grant, "grant-second"));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await f.Db(async db =>
        {
            Assert.Equal(100, (await db.Set<RawFundingAccount>().SingleAsync()).Available);
            Assert.Single(await db.Set<RawFundingEntry>().ToArrayAsync());
        });
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/v1/raw-funding/1?semesterId=FA2026")).StatusCode);
    }

    [Fact]
    public async Task SourceCancellationRefundsTheUnspentReservationOnce()
    {
        await using var f = await Fixture.Create(); using var staff = f.Client(202);
        var reservation = await f.Reserve(staff, 50); var id = reservation.GetProperty("id").GetGuid();
        var message = new ExperienceSourceCancelledEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, 20, null, 1, 202, "Activity not held") { FundingReservationId = id };
        await f.Process(message.EventId, ExperienceRoutingKeys.SourceCancelled, message);
        await f.Process(message.EventId, ExperienceRoutingKeys.SourceCancelled, message);
        await f.Db(async db =>
        {
            var account = await db.Set<RawFundingAccount>().SingleAsync();
            Assert.Equal(100, account.Available); Assert.Equal(0, account.Reserved); Assert.Equal(0, account.Spent);
            Assert.Equal("REFUNDED", (await db.Set<RawFundingReservation>().SingleAsync()).Status);
            Assert.Single(await db.Set<RawFundingEntry>().Where(x => x.Kind == "REFUND").ToArrayAsync());
            Assert.Empty(await db.ExperienceLedgerEffects.ToArrayAsync());
        });
    }

    [Fact]
    public async Task GateTwoSettlesEveryCandidateAtomicallyAndReplaysWithoutDuplicateXpOrRawDebits()
    {
        await using var f = await Fixture.Create(); using var staff = f.Client(202);
        var reservation = await f.Reserve(staff, 50);
        await f.Candidates(reservation.GetProperty("id").GetGuid());
        var detail = await staff.GetFromJsonAsync<JsonElement>("/api/v1/raw-funding/reservations/" + reservation.GetProperty("id").GetGuid());
        Assert.True(detail.GetProperty("canSettle").GetBoolean());
        var decisions = detail.GetProperty("candidates").EnumerateArray().Select(x => new
        { id = x.GetProperty("id").GetGuid(), expectedVersion = x.GetProperty("version").GetGuid(), decision = "APPROVE", role = x.GetProperty("sourceType").GetString() == "CLOSING_DUTY" ? "Organizer" : null }).ToArray();
        var path = $"/api/v1/raw-funding/reservations/{reservation.GetProperty("id").GetGuid()}/settle";
        var body = new { expectedVersion = detail.GetProperty("version").GetGuid(), reason = "Independent closing evidence confirmed", decisions };
        var response = await Send(staff, path, body, "settle-all"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(await response.Content.ReadAsStringAsync(), await (await Send(staff, path, body, "settle-all")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(staff, path, body, "settle-again")).StatusCode);
        await f.Db(async db =>
        {
            var account = await db.Set<RawFundingAccount>().SingleAsync();
            Assert.Equal(100, account.Granted); Assert.Equal(60, account.Available); Assert.Equal(0, account.Reserved); Assert.Equal(40, account.Spent);
            Assert.Equal(2, await db.ExperienceLedgerEffects.CountAsync());
            Assert.Equal(40m, (await db.ExperienceLedgerEffects.Select(x => x.Amount).ToArrayAsync()).Sum());
            Assert.Equal(2, await db.Set<RawFundingEntry>().CountAsync(x => x.Kind == "AWARD"));
            Assert.Single(await db.Set<RawFundingEntry>().Where(x => x.Kind == "SETTLE").ToListAsync());
            Assert.All(await db.Contributions.ToArrayAsync(), x => Assert.Equal("APPROVED", x.Status));
        });
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/v1/raw-funding/1?semesterId=FA2026")).StatusCode);
    }

    [Fact]
    public async Task BudgetIncreaseRequiresLiveSourceAvailableFundsAndBothVersionsBeforeWholeSettlement()
    {
        await using var f = await Fixture.Create(); using var staff = f.Client(202);
        var reservation = await f.Reserve(staff, 15); var id = reservation.GetProperty("id").GetGuid();
        await f.Candidates(id);
        var balance = await staff.GetFromJsonAsync<JsonElement>("/api/v1/raw-funding/1?semesterId=FA2026");
        var version = await f.Version(id);
        var body = new { expectedVersion = version, expectedAccountVersion = balance.GetProperty("version").GetGuid(), amount = 30, reason = "Independent approval of the verified closing budget shortfall" };
        var path = $"/api/v1/raw-funding/reservations/{id}/increase-budget";
        f.Source = f.Source with { Status = "CANCELLED" };
        Assert.Equal(HttpStatusCode.Conflict, (await Send(staff, path, body, "cancelled-budget")).StatusCode);
        f.Source = f.Source with { Status = "APPROVED" };
        Assert.Equal(HttpStatusCode.OK, (await Send(staff, path, body, "increase-budget")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(staff, path, body, "increase-budget")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(staff, path, body, "stale-increase")).StatusCode);
        var detail = await staff.GetFromJsonAsync<JsonElement>($"/api/v1/raw-funding/reservations/{id}");
        var decisions = detail.GetProperty("candidates").EnumerateArray().Select(x => new
        { id = x.GetProperty("id").GetGuid(), expectedVersion = x.GetProperty("version").GetGuid(), decision = "APPROVE", role = "Organizer" }).ToArray();
        var result = await Send(staff, $"/api/v1/raw-funding/reservations/{id}/settle", new { expectedVersion = detail.GetProperty("version").GetGuid(), reason = "Settle after explicitly approved allocation increase", decisions }, "settle-increased-budget");
        Assert.True(result.StatusCode == HttpStatusCode.OK, await result.Content.ReadAsStringAsync());
        await f.Db(async db =>
        {
            var account = await db.Set<RawFundingAccount>().SingleAsync();
            Assert.Equal(60, account.Available); Assert.Equal(0, account.Reserved); Assert.Equal(40, account.Spent);
            Assert.Single(await db.Set<RawFundingEntry>().Where(x => x.Kind == "BUDGET_INCREASE").ToArrayAsync());
            Assert.Equal(45, (await db.Set<RawFundingReservation>().SingleAsync()).Budget);
        });
    }

    [Fact]
    public async Task ShortageAndMissingCandidateCannotPartiallyPostAnyAward()
    {
        await using var f = await Fixture.Create(); using var staff = f.Client(202);
        var reservation = await f.Reserve(staff, 15); var id = reservation.GetProperty("id").GetGuid();
        await f.Candidates(id);
        var detail = await staff.GetFromJsonAsync<JsonElement>($"/api/v1/raw-funding/reservations/{id}");
        var decisions = detail.GetProperty("candidates").EnumerateArray().Select(x => new
        { id = x.GetProperty("id").GetGuid(), expectedVersion = x.GetProperty("version").GetGuid(), decision = "APPROVE", role = "Organizer" }).ToArray();
        var response = await Send(staff, $"/api/v1/raw-funding/reservations/{id}/settle", new { expectedVersion = detail.GetProperty("version").GetGuid(), reason = "No partial posting on insufficient funding", decisions }, "shortage");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("RAW_BUDGET_EXCEEDED", await response.Content.ReadAsStringAsync());
        f.Source = f.Source with { Candidates = [.. f.Source.Candidates, new(999, "CLUB:20:SESSION:2")] };
        response = await Send(staff, $"/api/v1/raw-funding/reservations/{id}/settle", new { expectedVersion = detail.GetProperty("version").GetGuid(), reason = "Wait for all candidates", decisions }, "missing-candidate");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await f.Db(async db =>
        {
            Assert.Empty(await db.ExperienceLedgerEffects.ToListAsync()); Assert.Empty(await db.ContributionDecisions.ToListAsync());
            Assert.Empty(await db.Set<RawFundingEntry>().Where(x => x.Kind == "AWARD").ToListAsync());
            Assert.Equal(15, (await db.Set<RawFundingAccount>().SingleAsync()).Reserved);
        });
    }

    [Fact]
    public async Task SourceRecoveryRefundsOnceButNeverTreatsDependencyFailureAsAbandonment()
    {
        await using var f = await Fixture.Create(); using var staff = f.Client(202);
        var reservation = await f.Reserve(staff, 50); var id = reservation.GetProperty("id").GetGuid();
        var path = $"/api/v1/raw-funding/reservations/{id}/reconcile";
        var body = new { expectedVersion = await f.Version(id), reason = "Reconcile the abandoned opening revision" };
        f.SourceAvailable = false;
        Assert.Equal(HttpStatusCode.BadGateway, (await Send(staff, path, body, "unavailable-source")).StatusCode);
        await f.Db(async db => Assert.Equal(50, (await db.Set<RawFundingAccount>().SingleAsync()).Reserved));
        f.SourceAvailable = true; f.Source = f.Source with { Status = "REVISION_REQUESTED", Version = "3:changed" };
        Assert.Equal(HttpStatusCode.OK, (await Send(staff, path, body, "recover-source")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(staff, path, body, "recover-source")).StatusCode);
        await f.Db(async db =>
        {
            Assert.Equal(100, (await db.Set<RawFundingAccount>().SingleAsync()).Available);
            Assert.Single(await db.Set<RawFundingEntry>().Where(x => x.Kind == "REFUND").ToArrayAsync());
        });
    }

    [Fact]
    public async Task FundingEnforcesCampusLiveAccountInternalAuthorityAndImmutableReconciliation()
    {
        await using var f = await Fixture.Create(); using var staff = f.Client(202); using var foreign = f.Client(203); using var student = f.Client(100);
        var grant = new { semesterId = "FA2026", expectedVersion = Guid.Empty, amount = 100, reason = "Campus grant for the active semester" };
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(foreign, "/api/v1/raw-funding/1/grants", grant, "foreign-grant")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(student, "/api/v1/raw-funding/1/grants", grant, "student-grant")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(staff, "/api/internal/experience/raw-funding/reserve", new ReserveRawFundingRequest("APPLICATION:7", "2:original"), "no-internal-token")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(staff, "/api/v1/raw-funding/1/grants", grant, "grant-once")).StatusCode);
        f.StaffLocked = true;
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(staff, "/api/v1/raw-funding/1/grants", grant, "grant-once")).StatusCode);
        f.StaffLocked = false;
        await f.Db(async db =>
        {
            var entry = await db.Set<RawFundingEntry>().SingleAsync(); entry.Reason = "Attempted ledger rewrite";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        });
        await f.Db(async db => { (await db.Set<RawFundingAccount>().SingleAsync()).Available = 99; await db.SaveChangesAsync(); });
        var read = await staff.GetAsync("/api/v1/raw-funding/1?semesterId=FA2026"); Assert.Equal(HttpStatusCode.ServiceUnavailable, read.StatusCode);
        Assert.Contains("RAW_RECONCILIATION_REQUIRED", await read.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object body, string key, bool internalCall = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key); if (internalCall) request.Headers.Add("X-Internal-Workflow-Token", InternalToken);
        return await client.SendAsync(request);
    }

    private sealed class Fixture(SqliteConnection? connection, WebApplication app, string? sqlConnection = null) : IAsyncDisposable
    {
        internal bool StaffLocked;
        internal bool SourceAvailable = true;
        internal readonly Guid PolicyId = Guid.NewGuid();
        internal RawFundingSource Source = new("APPLICATION:7", "2:original", "SUBMITTED", 1, "HAN", "FA2026", 200, null, 50, null, null, false, []);
        internal static async Task<Fixture> Create(bool sql = false)
        {
            var connection = sql ? null : new SqliteConnection("Data Source=:memory:"); if (connection is not null) await connection.OpenAsync();
            var sqlConnection = sql ? new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ADMIN_SERVICE_TEST_CONNECTION_STRING")
                ?? "Server=(localdb)\\ClubManagerApiTests;Integrated Security=True;TrustServerCertificate=True")
                { InitialCatalog = "ClubHub_RawFundingTests_" + Guid.NewGuid().ToString("N") }.ConnectionString : null;
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Issuer"] = "ClubReportHub", ["Jwt:Audience"] = "ClubReportHub.Client", ["Jwt:SigningKey"] = SigningKey,
                ["Security:InternalWorkflowToken"] = InternalToken, ["Experience:Environment"] = "PROJECT" });
            builder.Services.AddDbContext<AdminDbContext>(o => { if (sqlConnection is null) o.UseSqlite(connection!); else o.UseSqlServer(sqlConnection, options => options.EnableRetryOnFailure()); }); builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
            builder.Services.AddAuthorization(o =>
            {
                o.AddPolicy(AdminPolicies.AnyActor, p => p.RequireAuthenticatedUser());
                o.AddPolicy(AdminPolicies.BackofficeUser, p => p.RequireRole(AuthRoles.StudentAffairsAdmin, AuthRoles.Admin));
            });
            Fixture f = null!;
            var profileHandler = new Handler(async request =>
            {
                var ids = (await request.Content!.ReadFromJsonAsync<int[]>())!;
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(ids.Select(id => new InternalUserProfile(id, "Student " + id, id + "@example.test", id == 203 ? "HCM" : "HAN", true, id == 202 && f.StaffLocked, null))) };
            });
            builder.Services.AddSingleton(new CurrentUserProfileClient(new(profileHandler) { BaseAddress = new("http://profiles/") }, builder.Configuration));
            builder.Services.AddSingleton(new ClubExperienceAuthorityClient(new(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new ClubExperienceAuthority(1, "Club One", "HAN", true, true, DateTimeOffset.UtcNow.AddMonths(-1))) }))) { BaseAddress = new("http://clubs/") }, builder.Configuration));
            builder.Services.AddSingleton(new ClubAccessClient(new(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<ClubAccessSnapshot>()) }))) { BaseAddress = new("http://clubs/") },
                new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }), NullLogger<ClubAccessClient>.Instance));
            builder.Services.AddHttpClient("ExperienceActivity", c => c.BaseAddress = new("http://activity/")).ConfigurePrimaryHttpMessageHandler(() => new Handler(_ => Task.FromResult(new HttpResponseMessage(f.SourceAvailable ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable) { Content = JsonContent.Create(f.Source) })));
            builder.Services.AddRawFunding();
            var app = builder.Build(); f = new(connection, app, sqlConnection);
            app.Use(async (http, next) =>
            {
                try { await next(http); }
                catch (ApplicationExceptionBase error) { http.Response.StatusCode = error.StatusCode; await http.Response.WriteAsJsonAsync(new { code = error.Code, message = error.Message }); }
                catch (HttpRequestException) { http.Response.StatusCode = 502; }
            });
            app.UseAuthentication(); app.UseAuthorization(); app.MapRawFundingEndpoints();
            await f.Db(async db =>
            {
                if (sqlConnection is null) await db.Database.EnsureCreatedAsync(); else await db.Database.MigrateAsync();
                db.Semesters.Add(new Semester { SemesterCode = "FA2026", IsActive = true, StartDate = DateTimeOffset.UtcNow.AddMonths(-1), EndDate = DateTimeOffset.UtcNow.AddMonths(1) });
                var benchmark = new SemesterBenchmarkConfig { SemesterCode = "FA2026" }; db.SemesterBenchmarkConfigs.Add(benchmark);
                var policy = f.Policy();
                db.ApprovedExperiencePolicies.Add(new ApprovedExperiencePolicy { Id = f.PolicyId, BenchmarkId = benchmark.Id, SemesterId = "FA2026", SnapshotJson = JsonSerializer.Serialize(new ApprovedExperiencePolicySnapshot(policy, benchmark.Id,
                    RadarPillars.AllSeven.ToDictionary(x => x, _ => 1000m), 20, 50, 80, 20, .9m), Json), ApprovedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
            });
            f.Source = f.Source with { PolicyVersion = f.PolicyId };
            await app.StartAsync(); return f;
        }
        internal ExperiencePolicyReference Policy() => new(PolicyId, "P_B_R_Q_K_V1", "FA2026", DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1), "APPROVED", "PROJECT", 4, "REVERSE_SOURCE_INVALIDATION");
        internal HttpClient Client(int id)
        {
            var role = id is 202 or 203 ? AuthRoles.StudentAffairsAdmin : AuthRoles.ClubMember;
            var token = new JwtSecurityToken("ClubReportHub", "ClubReportHub.Client", [new Claim(JwtRegisteredClaimNames.Sub, id.ToString()), new Claim(ClaimTypes.Role, role)], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(30),
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256));
            var client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token)); return client;
        }
        internal async Task<JsonElement> Reserve(HttpClient staff, decimal budget)
        {
            Assert.Equal(HttpStatusCode.OK, (await Send(staff, "/api/v1/raw-funding/1/grants", new { semesterId = "FA2026", expectedVersion = Guid.Empty, amount = 100, reason = "Semester raw-point grant" }, "grant")).StatusCode);
            Source = Source with { Budget = budget };
            var response = await Send(staff, "/api/internal/experience/raw-funding/reserve", new ReserveRawFundingRequest(Source.SourceKey, Source.Version), "reserve", true);
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        internal async Task<Guid> Version(Guid id)
        {
            Guid version = default; await Db(async db => version = (await db.Set<RawFundingReservation>().SingleAsync(x => x.Id == id)).Version); return version;
        }
        internal async Task Candidates(Guid reservation)
        {
            Source = Source with { Status = "APPROVED", ActivityId = 20, FundingReservationId = reservation, ClosingApproved = true,
                Candidates = [new(100, "CLUB:20:SESSION:1"), new(101, "closing-duty:20")] };
            await Db(async db =>
            {
                var attributes = new ContributionAttributes("T1", "Participant", "Club", "None", [new("Academic", 1)]);
                foreach (var expected in Source.Candidates)
                {
                    var duty = expected.UserId == 101;
                    var source = new ExperienceSourceSnapshot(20, 1, duty ? 0 : 1, "FA2026", "Funded workshop", DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1), Guid.NewGuid(), Guid.NewGuid(), "CLOSED", false, attributes, Policy()) { FundingReservationId = reservation };
                    var handler = new AdminExperienceEventHandler(db, app.Configuration);
                    if (duty)
                    {
                        var message = new ClosingDutyXpEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, expected.UserId, "STAFF", source);
                        await handler.ProcessAsync(message.EventId, ExperienceRoutingKeys.ClosingDutyXp, JsonSerializer.SerializeToElement(message, Json), default);
                    }
                    else
                    {
                        var message = new AttendanceVerifiedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, expected.UserId, 10, "ROTATING_QR", null, source) { ClosingReportAuthorized = true };
                        await handler.ProcessAsync(message.EventId, ExperienceRoutingKeys.AttendanceVerified, JsonSerializer.SerializeToElement(message, Json), default);
                    }
                }
                Assert.Empty(await db.ExperienceLedgerEffects.ToListAsync());
                Assert.All(await db.Contributions.ToArrayAsync(), row => Assert.Equal("SUBMITTED", row.Status));
            });
        }
        internal async Task Db(Func<AdminDbContext, Task> action) { await using var scope = app.Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<AdminDbContext>()); }
        internal Task Process<T>(Guid id, string route, T message) => Db(db => new AdminExperienceEventHandler(db, app.Configuration).ProcessAsync(id, route, JsonSerializer.SerializeToElement(message, Json), default));
        public async ValueTask DisposeAsync()
        {
            if (sqlConnection is not null)
            {
                if (!new SqlConnectionStringBuilder(sqlConnection).InitialCatalog.StartsWith("ClubHub_RawFundingTests_", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected test database.");
                await Db(async db => await db.Database.EnsureDeletedAsync());
            }
            await app.DisposeAsync(); if (connection is not null) await connection.DisposeAsync();
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request); }
}
