using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AdminService.Data;
using AdminService.Security;
using AuthService.Data;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Rosters;
using ClubService.Data;
using ClubService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using AdminRosterEndpoints = AdminService.Endpoints.SemesterRosterEndpoints;
using AuthProfiles = AuthService.Endpoints.CanonicalProfileEndpoints;
using AuthRosterEndpoints = AuthService.Endpoints.SchoolAccountEndpoints;
using ClubEligible = ClubService.Endpoints.ClubRosterEligibilityEndpoints;
using ClubMembers = ClubService.Endpoints.MemberManagementEndpoints;
using ClubJoins = ClubService.Endpoints.MembershipEndpoints;

namespace Backend.StabilizationTests;

public sealed class SemesterRosterContractTests
{
    private const string Key = "SemesterRosterTests-Only-Long-Signing-Key-2026-Not-Production";
    private const string Secret = "semester-test-internal-only";
    private static readonly DateTimeOffset Instant = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);
    private static object Row(int userId = 100, string status = "ACTIVE", string campus = CampusCodes.Hanoi) => new
    { email = $"student{userId}@fpt.edu.vn", studentCode = "SE" + userId, campusCode = campus, status, userId };

    [Fact]
    public async Task Roster_PreviewCommitUsesCanonicalEmailKeepsAccountsAndHistoryAndReplaysOneRevision()
    {
        await using var f = await Fixture.Create(); using var admin = f.Admin();
        var empty = await Json(await admin.GetAsync("/api/v1/semesters/FALL2026/student-roster"));
        Assert.Equal("ROSTER_UNAVAILABLE", empty.GetProperty("availability").GetString());
        var preview = await f.Preview(admin, "FALL2026", Guid.Empty, [Row(), Row(101, "INACTIVE")]);
        Assert.True(preview.GetProperty("canCommit").GetBoolean());
        await f.AdminDb(async db => Assert.Empty(await db.Set<SemesterRoster>().ToArrayAsync()));
        var importId = preview.GetProperty("importId").GetGuid();
        var committed = await Post(admin, $"/api/v1/semesters/FALL2026/roster-imports/{importId}/commit", new { expectedRosterVersion = Guid.Empty }, "commit");
        Assert.Equal(HttpStatusCode.OK, committed.StatusCode); var receipt = await Json(committed);
        Assert.Equal(1, receipt.GetProperty("activeCount").GetInt32());
        Assert.Equal(receipt.GetProperty("revisionId").GetGuid(), (await Json(await Post(admin, $"/api/v1/semesters/FALL2026/roster-imports/{importId}/commit", new { expectedRosterVersion = Guid.Empty }, "commit"))).GetProperty("revisionId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, $"/api/v1/semesters/FALL2026/roster-imports/{importId}/commit", new { expectedRosterVersion = Guid.Empty }, "another")).StatusCode);
        await f.AdminDb(async db =>
        {
            Assert.Single(await db.Set<SemesterRosterRevision>().ToArrayAsync()); Assert.Equal(2, await db.Set<SemesterRosterRow>().CountAsync());
            Assert.Single(await db.OutboxMessages.Where(x => x.Payload.Contains("SEMESTER_ROSTER_IMPORTED")).ToArrayAsync());
        });
        await f.AuthDb(async db => Assert.Equal(9, await db.Users.CountAsync()));
        using var manager = f.Manager();
        var eligible = await Json(await manager.GetAsync("/api/clubs/1/eligible-members?semesterId=FALL2026&search=Student&pageSize=1"));
        Assert.Equal(1, eligible.GetProperty("totalItems").GetInt32());
        Assert.Equal(100, eligible.GetProperty("items")[0].GetProperty("userId").GetInt32());
        Assert.False(eligible.GetProperty("items")[0].TryGetProperty("email", out _));
        await f.ClubDb(async db => Assert.Empty(await db.ClubMemberships.ToArrayAsync()));
    }

    [Fact]
    public async Task Roster_RejectsDuplicateUnknownNonstudentWrongCampusAndLockedRowsWithoutLiveReplacement()
    {
        await using var f = await Fixture.Create(); using var admin = f.Admin();
        var rows = new object[] { Row(), Row(), Row(999), Row(104), Row(105, campus: CampusCodes.HoChiMinh), Row(102), Row(103) };
        var preview = await f.Preview(admin, "FALL2026", Guid.Empty, rows);
        Assert.False(preview.GetProperty("canCommit").GetBoolean());
        Assert.Equal(6, preview.GetProperty("errors").GetArrayLength());
        var import = preview.GetProperty("importId").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, $"/api/v1/semesters/FALL2026/roster-imports/{import}/commit", new { expectedRosterVersion = Guid.Empty }, "bad")).StatusCode);
        await f.AdminDb(async db => Assert.Empty(await db.Set<SemesterRoster>().ToArrayAsync()));
        await f.AuthDb(async db => Assert.Equal(9, await db.Users.CountAsync()));
    }

    [Fact]
    public async Task Roster_CarryForwardRetainsSourceAndExplicitEmptyOverridesIt()
    {
        await using var f = await Fixture.Create(); using var admin = f.Admin();
        await f.Import(admin, "SUMMER2026", Guid.Empty, [Row()]);
        var inherited = await Json(await admin.GetAsync("/api/v1/semesters/FALL2026/student-roster"));
        Assert.Equal("CARRY_FORWARD", inherited.GetProperty("source").GetString());
        Assert.Single(inherited.GetProperty("items").EnumerateArray());
        var source = inherited.GetProperty("sourceRevisionId").GetGuid();
        Assert.NotEqual(Guid.Empty, source);
        await f.Import(admin, "FALL2026", inherited.GetProperty("version").GetGuid(), []);
        var read = await Json(await admin.GetAsync("/api/v1/semesters/FALL2026/student-roster"));
        Assert.Equal("IMPORT", read.GetProperty("source").GetString()); Assert.Empty(read.GetProperty("items").EnumerateArray());
        var next = await Json(await admin.GetAsync("/api/v1/semesters/SPRING2027/student-roster"));
        Assert.Equal("CARRY_FORWARD", next.GetProperty("source").GetString()); Assert.Empty(next.GetProperty("items").EnumerateArray());
        Assert.Equal(next.GetProperty("version").GetGuid(), (await Json(await admin.GetAsync("/api/v1/semesters/SPRING2027/student-roster"))).GetProperty("version").GetGuid());
    }

    [Fact]
    public async Task Roster_StaleExpiredAndIdentityChangedPreviewsCannotCommit()
    {
        await using var f = await Fixture.Create(); using var admin = f.Admin();
        var one = await f.Preview(admin, "FALL2026", Guid.Empty, [Row()]);
        var two = await f.Preview(admin, "FALL2026", Guid.Empty, [Row(101)]);
        Assert.Equal(HttpStatusCode.OK, (await Post(admin, $"/api/v1/semesters/FALL2026/roster-imports/{one.GetProperty("importId").GetGuid()}/commit", new { expectedRosterVersion = Guid.Empty }, "one")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, $"/api/v1/semesters/FALL2026/roster-imports/{two.GetProperty("importId").GetGuid()}/commit", new { expectedRosterVersion = Guid.Empty }, "two")).StatusCode);
        var current = await Json(await admin.GetAsync("/api/v1/semesters/FALL2026/student-roster")); var version = current.GetProperty("version").GetGuid();
        var expired = await f.Preview(admin, "FALL2026", version, [Row()]); f.Clock.Utc = Instant.AddHours(2);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, $"/api/v1/semesters/FALL2026/roster-imports/{expired.GetProperty("importId").GetGuid()}/commit", new { expectedRosterVersion = version }, "expired")).StatusCode);
        var changed = await f.Preview(admin, "FALL2026", version, [Row()]);
        await f.AuthDb(async db => { (await db.Users.SingleAsync(x => x.Id == 100)).IsLocked = true; await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, $"/api/v1/semesters/FALL2026/roster-imports/{changed.GetProperty("importId").GetGuid()}/commit", new { expectedRosterVersion = version }, "changed")).StatusCode);
        await f.AdminDb(async db => Assert.Single(await db.Set<SemesterRosterRevision>().ToArrayAsync()));
    }

    [Fact]
    public async Task Roster_CsvIsUtf8BoundedStrictAndUsesTheSamePreviewValidation()
    {
        await using var f = await Fixture.Create(); using var admin = f.Admin();
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("replace"), "mode"); multipart.Add(new StringContent(Guid.Empty.ToString()), "expectedRosterVersion");
        multipart.Add(new StringContent("email,studentCode,campusCode,status\r\n\"STUDENT100@FPT.EDU.VN\",SE100,HAN,ACTIVE\r\n", Encoding.UTF8), "file", "students.csv");
        var response = await admin.PostAsync("/api/v1/semesters/FALL2026/roster-imports/preview", multipart);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True((await Json(response)).GetProperty("canCommit").GetBoolean());
        using var invalid = new MultipartFormDataContent();
        invalid.Add(new StringContent("replace"), "mode"); invalid.Add(new StringContent(Guid.Empty.ToString()), "expectedRosterVersion");
        invalid.Add(new StringContent("name,email\nFake,student100@fpt.edu.vn"), "file", "bad.csv");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/semesters/FALL2026/roster-imports/preview", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/semesters/FALL2026/roster-imports/preview", new { schemaVersion = 1, rows = new[] { Row() } })).StatusCode);
        using var oversized = new StringContent(new string('x', 5 * 1024 * 1024 + 1), Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await admin.PostAsync("/api/v1/semesters/FALL2026/roster-imports/preview", oversized)).StatusCode);
    }

    [Fact]
    public async Task Roster_AdministrationAndInternalAccountsAreNotManagerDirectories()
    {
        await using var f = await Fixture.Create(); using var managerAtAdmin = f.Client(f.AdminApp, 200, AuthRoles.ClubManager);
        Assert.Equal(HttpStatusCode.Forbidden, (await managerAtAdmin.GetAsync("/api/v1/semesters/FALL2026/student-roster")).StatusCode);
        using var admin = f.Admin();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/semesters/FALL2026/student-roster?campusCode=HCM")).StatusCode);
        using var stranger = f.Client(f.ClubApp, 100, AuthRoles.ClubMember);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync("/api/clubs/1/eligible-members?semesterId=FALL2026&search=Student")).StatusCode);
        using var raw = f.AuthApp.GetTestClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await raw.PostAsJsonAsync("/api/internal/semester-roster/accounts/resolve", new { emails = new[] { "student100@fpt.edu.vn" } })).StatusCode);
        using var manager = f.Manager();
        Assert.Equal(HttpStatusCode.Conflict, (await manager.GetAsync("/api/clubs/1/eligible-members?semesterId=FALL2026&search=Student")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/clubs/1/eligible-members?semesterId=FALL2026&search=a")).StatusCode);
    }

    [Fact]
    public async Task Invitation_RequiresSemesterAndFreshRosterAtInviteConsentAndApprovalWithOneInboxEvent()
    {
        await using var f = await Fixture.Create(); using var admin = f.Admin(); using var manager = f.Manager();
        await f.Import(admin, "FALL2026", Guid.Empty, [Row()]);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync("/api/clubs/1/members", new { userId = 100, fullName = "Fake", role = AuthRoles.ClubMember })).StatusCode);
        var body = new { userId = 100, fullName = "Forged name", role = AuthRoles.ClubMember, semesterId = "FALL2026" };
        var invited = await Post(manager, "/api/clubs/1/members", body, "invite"); Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        var value = await Json(invited); var id = value.GetProperty("id").GetInt32();
        Assert.Equal("Student 100", value.GetProperty("fullName").GetString());
        Assert.Equal(id, (await Json(await Post(manager, "/api/clubs/1/members", body, "invite"))).GetProperty("id").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(manager, $"/api/clubs/memberships/{id}/approve", new { note = "Manager cannot consent" }, "early")).StatusCode);
        var roster = await Json(await admin.GetAsync("/api/v1/semesters/FALL2026/student-roster"));
        await f.Import(admin, "FALL2026", roster.GetProperty("version").GetGuid(), [Row(status: "INACTIVE")]);
        using var student = f.Client(f.ClubApp, 100, AuthRoles.ClubMember);
        Assert.Equal((HttpStatusCode)422, (await student.PostAsJsonAsync("/api/clubs/1/join", Join())).StatusCode);
        roster = await Json(await admin.GetAsync("/api/v1/semesters/FALL2026/student-roster"));
        await f.Import(admin, "FALL2026", roster.GetProperty("version").GetGuid(), [Row()]);
        Assert.Equal(HttpStatusCode.OK, (await student.PostAsJsonAsync("/api/clubs/1/join", Join())).StatusCode);
        await f.AuthDb(async db => { (await db.Users.SingleAsync(x => x.Id == 100)).IsLocked = true; await db.SaveChangesAsync(); });
        Assert.Equal((HttpStatusCode)422, (await Post(manager, $"/api/clubs/memberships/{id}/approve", new { note = "Locked student" }, "locked")).StatusCode);
        await f.AuthDb(async db => { (await db.Users.SingleAsync(x => x.Id == 100)).IsLocked = false; await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.OK, (await Post(manager, $"/api/clubs/memberships/{id}/approve", new { note = "Approved" }, "approve")).StatusCode);
        await f.ClubDb(async db =>
        {
            var row = await db.ClubMemberships.SingleAsync(); Assert.Equal("Approved", row.Status); Assert.True(row.AcceptedClubRules); Assert.NotNull(row.RosterRevisionId);
            Assert.NotNull(row.InvitedRosterRevisionId); Assert.NotEqual(row.InvitedRosterRevisionId, row.RosterRevisionId);
            Assert.NotEqual(row.InvitedRosterVersion, row.RosterVersion);
            Assert.Single(await db.MembershipActions.Where(x => x.Action == "INVITED").ToArrayAsync());
            Assert.Single(await db.OutboxMessages.Where(x => x.Payload.Contains("CLUB_INVITATION")).ToArrayAsync());
        });
        var eligible = await Json(await manager.GetAsync("/api/clubs/1/eligible-members?semesterId=FALL2026&search=Student"));
        Assert.Empty(eligible.GetProperty("items").EnumerateArray());
    }

    private static object Join() => new
    {
        fullName = "Student",
        gender = "Other",
        email = "student100@fpt.edu.vn",
        phoneNumber = "0900000100",
        reason = "Participate in club activities",
        acceptedClubRules = true,
        committedToParticipate = true
    };

    [Fact]
    public async Task Roster_SqlServerMigrationAndConcurrentReplacementCommitExactlyOneRevision()
    {
        await using var f = await Fixture.Create(sqlAdmin: true); using var admin = f.Admin();
        var one = await f.Preview(admin, "FALL2026", Guid.Empty, [Row()]);
        var two = await f.Preview(admin, "FALL2026", Guid.Empty, [Row(101)]);
        var replies = await Task.WhenAll(Post(admin, $"/api/v1/semesters/FALL2026/roster-imports/{one.GetProperty("importId").GetGuid()}/commit", new { expectedRosterVersion = Guid.Empty }, "one"),
            Post(admin, $"/api/v1/semesters/FALL2026/roster-imports/{two.GetProperty("importId").GetGuid()}/commit", new { expectedRosterVersion = Guid.Empty }, "two"));
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(replies, x => x.StatusCode == HttpStatusCode.Conflict);
        await f.AdminDb(async db =>
        {
            Assert.Equal(3, await db.Semesters.CountAsync()); Assert.Single(await db.Set<SemesterRosterRevision>().ToArrayAsync());
            Assert.Single(await db.Set<SemesterRosterRow>().ToArrayAsync()); Assert.False(db.Database.HasPendingModelChanges());
            Assert.Single(await db.OutboxMessages.Where(x => x.Payload.Contains("SEMESTER_ROSTER_IMPORTED")).ToArrayAsync());
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Roster_ImportRevisionAndRowsCannotBeRewrittenOrDeleted(int mode)
    {
        await using var f = await Fixture.Create(); using var admin = f.Admin(); await f.Import(admin, "FALL2026", Guid.Empty, [Row()]);
        await f.AdminDb(async db =>
        {
            async Task Save()
            {
                if (mode == 0) db.SaveChanges();
                else if (mode == 1) db.SaveChanges(false);
                else if (mode == 2) await db.SaveChangesAsync(); else await db.SaveChangesAsync(false);
            }
            (await db.Set<SemesterRosterImport>().SingleAsync()).CandidatesJson = "[]";
            await Assert.ThrowsAsync<InvalidOperationException>(Save); db.ChangeTracker.Clear();
            db.Remove(await db.Set<SemesterRosterRevision>().SingleAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(Save); db.ChangeTracker.Clear();
            (await db.Set<SemesterRosterRow>().SingleAsync()).Active = false;
            await Assert.ThrowsAsync<InvalidOperationException>(Save);
        });
    }
    private static Task<JsonElement> Json(HttpResponseMessage value) => value.Content.ReadFromJsonAsync<JsonElement>();
    private static Task<HttpResponseMessage> Post(HttpClient client, string url, object body, string key)
    { var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) }; request.Headers.Add("Idempotency-Key", key); return client.SendAsync(request); }
    private sealed class Clock : TimeProvider { internal DateTimeOffset Utc { get; set; } = Instant; public override DateTimeOffset GetUtcNow() => Utc; }
    private sealed class Fixture(SqliteConnection authConnection, SqliteConnection? adminConnection, SqliteConnection clubConnection,
        WebApplication auth, WebApplication admin, WebApplication club, Clock clock, string? sqlConnection) : IAsyncDisposable
    {
        internal WebApplication AuthApp => auth; internal WebApplication AdminApp => admin; internal WebApplication ClubApp => club;
        internal Clock Clock => clock;
        internal static async Task<Fixture> Create(bool sqlAdmin = false)
        {
            var a = new SqliteConnection("Data Source=:memory:"); var b = sqlAdmin ? null : new SqliteConnection("Data Source=:memory:"); var c = new SqliteConnection("Data Source=:memory:");
            await a.OpenAsync(); if (b is not null) await b.OpenAsync(); await c.OpenAsync(); var clock = new Clock();
            var sqlConnection = sqlAdmin ? new SqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("ADMIN_SERVICE_TEST_CONNECTION_STRING")
                ?? "Server=(localdb)\\ClubManagerApiTests;Integrated Security=True;TrustServerCertificate=True")
            {
                InitialCatalog = "ClubHub_RosterTests_" + Guid.NewGuid().ToString("N"),
            }.ConnectionString : null;
            var ab = Builder(clock); ab.Services.AddDbContext<AuthDbContext>(x => x.UseSqlite(a));
            var auth = ab.Build(); Pipeline(auth); AuthRosterEndpoints.MapSchoolAccountEndpoints(auth); AuthProfiles.MapCanonicalProfileEndpoints(auth);
            await Db<AuthDbContext>(auth, async db =>
            {
                await db.Database.EnsureCreatedAsync();
                var student = new AuthService.Models.Role { Name = AuthRoles.ClubMember }; var manager = new AuthService.Models.Role { Name = AuthRoles.ClubManager };
                var sa = new AuthService.Models.Role { Name = AuthRoles.StudentAffairsAdmin }; var global = new AuthService.Models.Role { Name = AuthRoles.Admin };
                foreach (var id in new[] { 100, 101, 102, 103, 104, 105, 200, 900, 901 })
                {
                    var user = new AuthService.Models.User
                    {
                        Id = id,
                        Username = "student" + id,
                        Email = $"student{id}@fpt.edu.vn",
                        FullName = "Student " + id,
                        CampusCode = id == 105 ? CampusCodes.HoChiMinh : CampusCodes.Hanoi,
                        IsLocked = id == 102,
                        IsActive = id != 103
                    };
                    user.UserRoles.Add(new AuthService.Models.UserRole { Role = id == 200 ? manager : id is 104 or 900 ? sa : id == 901 ? global : student }); db.Add(user);
                }
                await db.SaveChangesAsync();
            }); await auth.StartAsync();
            var bb = Builder(clock); bb.Services.AddDbContext<AdminDbContext>(x => { if (sqlConnection is null) x.UseSqlite(b!); else x.UseSqlServer(sqlConnection); });
            bb.Services.AddAuthorization(options => options.AddPolicy(AdminPolicies.BackofficeUser, p => p.RequireRole(AuthRoles.Admin, AuthRoles.StudentAffairsAdmin)));
            bb.Services.AddSingleton(new SchoolAccountClient(auth.GetTestClient(), bb.Configuration));
            var admin = bb.Build(); Pipeline(admin); AdminRosterEndpoints.MapSemesterRosterEndpoints(admin);
            await Db<AdminDbContext>(admin, async db =>
            {
                if (sqlAdmin) await db.GetService<IMigrator>().MigrateAsync("20261013145324_StudentCompletionDefaults");
                else await db.Database.EnsureCreatedAsync();
                db.Semesters.AddRange(new Semester { SemesterCode = "SUMMER2026", StartDate = Instant.AddMonths(-4), EndDate = Instant.AddMonths(-2), IsActive = false },
                    new Semester { SemesterCode = "FALL2026", StartDate = Instant.AddMonths(-1), EndDate = Instant.AddMonths(2), IsActive = true },
                    new Semester { SemesterCode = "SPRING2027", StartDate = Instant.AddMonths(3), EndDate = Instant.AddMonths(5), IsActive = false });
                await db.SaveChangesAsync();
                if (sqlAdmin) await db.Database.MigrateAsync();
            }); await admin.StartAsync();
            var cb = Builder(clock); cb.Services.AddDbContext<ClubDbContext>(x => x.UseSqlite(c));
            cb.Services.AddSingleton(new SemesterRosterClient(admin.GetTestClient(), cb.Configuration));
            cb.Services.AddSingleton(new CurrentUserProfileClient(auth.GetTestClient(), cb.Configuration));
            var club = cb.Build(); Pipeline(club); ClubEligible.MapClubRosterEligibilityEndpoints(club); ClubMembers.MapMemberManagementEndpoints(club); ClubJoins.MapMembershipEndpoints(club);
            await Db<ClubDbContext>(club, async db =>
            {
                await db.Database.EnsureCreatedAsync(); var row = new Club
                {
                    Id = 1,
                    Code = "ROSTER",
                    Name = "Roster Club",
                    Category = ClubCategories.Other,
                    CampusCode = CampusCodes.Hanoi,
                    IsActive = true,
                    IsRecruiting = true
                };
                row.ManagerAssignments.Add(new ClubManagerAssignment { ManagerUserId = 200, ManagerName = "Manager", IsActive = true }); db.Add(row); await db.SaveChangesAsync();
            }); await club.StartAsync(); return new(a, b, c, auth, admin, club, clock, sqlConnection);
        }
        private static WebApplicationBuilder Builder(Clock clock)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "ClubReportHub",
                ["Jwt:Audience"] = "ClubReportHub.Client",
                ["Jwt:SigningKey"] = Key,
                ["Security:InternalWorkflowToken"] = Secret,
                ["Roster:SchoolEmailDomains"] = "fpt.edu.vn"
            });
            builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment); builder.Services.AddSingleton<TimeProvider>(clock); return builder;
        }
        private static void Pipeline(WebApplication app) { app.UseAuthentication(); app.UseAuthorization(); }
        internal HttpClient Admin() => Client(admin, 900, AuthRoles.StudentAffairsAdmin);
        internal HttpClient Manager() => Client(club, 200, AuthRoles.ClubManager);
        internal HttpClient Client(WebApplication app, int id, string role)
        {
            var token = new JwtSecurityToken("ClubReportHub", "ClubReportHub.Client", [new Claim(JwtRegisteredClaimNames.Sub, id.ToString()), new Claim(ClaimTypes.Role, role), new Claim("campus", CampusCodes.Hanoi)],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(1), new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
            var client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token)); return client;
        }
        internal async Task<JsonElement> Preview(HttpClient actor, string semester, Guid version, object[] rows)
        {
            var response = await actor.PostAsJsonAsync($"/api/v1/semesters/{semester}/roster-imports/preview", new { schemaVersion = 1, rows, expectedRosterVersion = version });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await Json(response);
        }
        internal async Task<JsonElement> Import(HttpClient actor, string semester, Guid version, object[] rows)
        {
            var preview = await Preview(actor, semester, version, rows); Assert.True(preview.GetProperty("canCommit").GetBoolean());
            var result = await Post(actor, $"/api/v1/semesters/{semester}/roster-imports/{preview.GetProperty("importId").GetGuid()}/commit", new { expectedRosterVersion = version }, Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.OK, result.StatusCode); return await Json(result);
        }
        internal Task AuthDb(Func<AuthDbContext, Task> action) => Db(auth, action);
        internal Task AdminDb(Func<AdminDbContext, Task> action) => Db(admin, action);
        internal Task ClubDb(Func<ClubDbContext, Task> action) => Db(club, action);
        private static async Task Db<T>(WebApplication app, Func<T, Task> action) where T : DbContext
        { await using var scope = app.Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<T>()); }
        public async ValueTask DisposeAsync()
        {
            if (sqlConnection is not null)
            {
                if (!new SqlConnectionStringBuilder(sqlConnection).InitialCatalog.StartsWith("ClubHub_RosterTests_", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected test database.");
                await AdminDb(async db => await db.Database.EnsureDeletedAsync());
            }
            await club.DisposeAsync(); await admin.DisposeAsync(); await auth.DisposeAsync(); await clubConnection.DisposeAsync();
            if (adminConnection is not null) await adminConnection.DisposeAsync(); await authConnection.DisposeAsync();
        }
    }
}
