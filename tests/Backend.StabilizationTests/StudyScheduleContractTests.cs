using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AdminService.Data;
using AdminService.Endpoints;
using AdminService.Security;
using AuthService.Data;
using AuthService.Endpoints;
using AuthService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Schedules;
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

public sealed class StudyScheduleContractTests
{
    private const string Key = "StudyScheduleTests-Only-Long-Secret-For-Hmac-2026";
    private const string Secret = "CalendarInternalTestSecret-Only";
    private static readonly Guid SemesterId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Root = "/api/users/me/study-schedule/2026/FA/";

    [Fact]
    public async Task Cell_RoundTripPrivateWeekBlockAndAccountIsolationAndReplay()
    {
        await using var f = await Fixture.Create(); using var student = f.Student();
        var empty = await Read(student); Assert.Empty(empty.GetProperty("entries").EnumerateArray());
        Assert.Equal(Guid.Empty, empty.GetProperty("version").GetGuid());
        Assert.Equal("", empty.GetProperty("calendar").GetProperty("block10StartDate").GetString());
        var payload = Body(empty, "  SWP391  ", " AL-R101 ");
        var first = await Send(student, HttpMethod.Put, Root + "3W/1/mon/slot-1", payload, "put");
        var saved = await Json(first); Assert.Equal("SWP391", saved.GetProperty("subject").GetString()); Assert.Equal("AL-R101", saved.GetProperty("room").GetString());
        Assert.Equal(saved.GetRawText(), (await Json(await Send(student, HttpMethod.Put, Root + "3W/1/mon/slot-1", payload, "put"))).GetRawText());
        var read = await Read(student); Assert.Single(read.GetProperty("entries").EnumerateArray());
        Assert.Equal(1, read.GetProperty("entries")[0].GetProperty("week").GetInt32());
        Assert.False(read.GetRawText().Contains("userId", StringComparison.OrdinalIgnoreCase));
        Assert.Empty((await Read(student, "10W")).GetProperty("entries").EnumerateArray());
        using var other = f.Student(101); Assert.Empty((await Read(other)).GetProperty("entries").EnumerateArray());
        var second = await Json(await Send(student, HttpMethod.Put, Root + "3W/2/mon/slot-1", Body(read, "PRJ301"), "week2"));
        Assert.Equal("", second.GetProperty("room").GetString());
        read = await Read(student); Assert.Equal(2, read.GetProperty("entries").GetArrayLength());
        var delete = Root + "3W/1/mon/slot-1?expectedVersion=" + read.GetProperty("version").GetGuid() + "&expectedCalendarVersion=" + read.GetProperty("calendarVersion").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await Send(student, HttpMethod.Delete, delete, null, "delete")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(student, HttpMethod.Delete, delete, null, "delete")).StatusCode);
        read = await Read(student); Assert.Equal(2, read.GetProperty("entries")[0].GetProperty("week").GetInt32());
        Assert.Single(read.GetProperty("entries").EnumerateArray());
        using var anonymous = f.Auth.GetTestClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Root + "3W")).StatusCode);
    }

    [Fact]
    public async Task Calendar_CanonicalAliasCampusAuthorityAndDateChangesKeepEntries()
    {
        await using var f = await Fixture.Create(); using var student = f.Student(); using var admin = f.Admin();
        var calendar = await Json(await admin.GetAsync("/api/academic-calendar/2026/FA"));
        Assert.Equal(SemesterId, calendar.GetProperty("semesterKey").GetGuid());
        Assert.Equal("OFFICIAL-FALL", calendar.GetProperty("semesterId").GetString());
        using var studentCalendar = f.Client(f.AdminApp, 100);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(studentCalendar, HttpMethod.Put, "/api/academic-calendar/2026/FA", CalendarBody(calendar), "denied")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, HttpMethod.Put, "/api/academic-calendar/2026/FA?campusCode=HCM", CalendarBody(calendar), "cross-campus")).StatusCode);
        var configured = await Json(await Send(admin, HttpMethod.Put, "/api/academic-calendar/2026/FA", CalendarBody(calendar), "configure"));
        var before = await Read(student); Assert.Equal("2026-11-09", before.GetProperty("calendar").GetProperty("block3StartDate").GetString());
        await Json(await Send(student, HttpMethod.Put, Root + "3W/1/sun/slot-7", Body(before, "Subject"), "cell"));
        await Json(await Send(admin, HttpMethod.Put, "/api/academic-calendar/2026/FA", CalendarBody(configured, "2026-11-16"), "change"));
        Assert.Equal(HttpStatusCode.Conflict, (await Send(student, HttpMethod.Put, Root + "3W/1/mon/slot-1", Body(before, "Stale calendar"), "stale")).StatusCode);
        var after = await Read(student); Assert.Equal("2026-11-16", after.GetProperty("calendar").GetProperty("block3StartDate").GetString());
        Assert.Single(after.GetProperty("entries").EnumerateArray()); Assert.Equal("sun", after.GetProperty("entries")[0].GetProperty("day").GetString());
        Assert.NotEqual(before.GetProperty("calendarVersion").GetGuid(), after.GetProperty("calendarVersion").GetGuid());
        using var global = f.Admin(202);
        var hcm = await Json(await global.GetAsync("/api/academic-calendar/2026/FA?campusCode=HCM"));
        await Json(await Send(global, HttpMethod.Put, "/api/academic-calendar/2026/FA?campusCode=HCM", CalendarBody(hcm), "global"));
        using var hcmStudent = f.Student(101);
        Assert.Equal("2026-11-09", (await Read(hcmStudent)).GetProperty("calendar").GetProperty("block3StartDate").GetString());
        await f.AdminDb(async db => Assert.Equal(3, await db.Set<AcademicCalendarRevision>().CountAsync()));
    }

    [Theory]
    [InlineData("2026-08-30", "2026-09-01", "2026-11-16")]
    [InlineData("2026-08-30", "2026-08-31", "2026-11-02")]
    [InlineData("2026-08-30", "2026-08-31", "2026-12-28")]
    [InlineData("2026-08-31", "2026-08-31", "2026-11-09")]
    [InlineData("invalid", "2026-08-31", "2026-11-09")]
    public async Task Calendar_InvalidDatesNeverPersist(string term, string first, string second)
    {
        await using var f = await Fixture.Create(); using var admin = f.Admin();
        var calendar = await Json(await admin.GetAsync("/api/academic-calendar/2026/FA"));
        var result = await Send(admin, HttpMethod.Put, "/api/academic-calendar/2026/FA", new { termStartDate = term, block10StartDate = first, block3StartDate = second, expectedVersion = calendar.GetProperty("version").GetGuid() }, "invalid");
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        await f.AdminDb(async db => { Assert.Equal(0, await db.Set<AcademicCalendarConfiguration>().CountAsync()); Assert.Equal(0, await db.Set<AcademicCalendarRevision>().CountAsync()); });
    }

    [Fact]
    public async Task Bulk_TemplateExpandsAllWeeksSkipReplaceAndStaleWritesAreAtomic()
    {
        await using var f = await Fixture.Create(); using var student = f.Student();
        var initial = await Read(student); await Json(await Send(student, HttpMethod.Put, Root + "3W/2/mon/slot-1", Body(initial, "Manual"), "manual"));
        var read = await Read(student); var template = new[] { new { day = "wed", slotId = "slot-3", subject = "  English  ", room = "" }, new { day = "mon", slotId = "slot-1", subject = "Template", room = "R1" } };
        var input = new { mode = "skip", entries = template, expectedVersion = read.GetProperty("version").GetGuid(), expectedCalendarVersion = read.GetProperty("calendarVersion").GetGuid() };
        var skipped = await Json(await Send(student, HttpMethod.Post, Root + "3W/bulk-import", input, "skip"));
        Assert.Equal(5, skipped.GetProperty("imported").GetInt32()); Assert.Equal(1, skipped.GetProperty("skipped").GetInt32()); Assert.Equal(0, skipped.GetProperty("replaced").GetInt32());
        Assert.Equal(6, skipped.GetProperty("entries").GetArrayLength());
        Assert.Equal("mon", skipped.GetProperty("entries")[0].GetProperty("day").GetString());
        Assert.Contains(skipped.GetProperty("entries").EnumerateArray(), x => x.GetProperty("subject").GetString() == "Manual");
        Assert.Equal(HttpStatusCode.Conflict, (await Send(student, HttpMethod.Put, Root + "3W/3/fri/slot-3", Body(read, "Stale manual"), "stale")).StatusCode);
        var replaced = await Json(await Send(student, HttpMethod.Post, Root + "3W/bulk-import", new { mode = "replace", entries = template, expectedVersion = skipped.GetProperty("version").GetGuid(), expectedCalendarVersion = read.GetProperty("calendarVersion").GetGuid() }, "replace"));
        Assert.Equal(6, replaced.GetProperty("replaced").GetInt32()); Assert.Equal(0, replaced.GetProperty("imported").GetInt32());
        Assert.DoesNotContain(replaced.GetProperty("entries").EnumerateArray(), x => x.GetProperty("subject").GetString() == "Manual");
        read = await Read(student, "10W");
        var ten = await Json(await Send(student, HttpMethod.Post, Root + "10W/bulk-import", new { mode = "skip", entries = template, expectedVersion = Guid.Empty, expectedCalendarVersion = read.GetProperty("calendarVersion").GetGuid() }, "ten"));
        Assert.Equal(20, ten.GetProperty("imported").GetInt32()); Assert.Equal(10, ten.GetProperty("entries")[19].GetProperty("week").GetInt32());
    }

    [Theory]
    [InlineData("3W", 0, "mon", "slot-1")]
    [InlineData("3W", 4, "mon", "slot-1")]
    [InlineData("10W", 11, "mon", "slot-1")]
    [InlineData("10W", 1, "mon", "slot-6")]
    [InlineData("3W", 1, "MON", "slot-1")]
    [InlineData("3W", 1, "mon", "slot-8")]
    public async Task InvalidCellsReturn400WithoutWrites(string block, int week, string day, string slot)
    {
        await using var f = await Fixture.Create(); using var student = f.Student(); var snapshot = await Read(student, block);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(student, HttpMethod.Put, Root + $"{block}/{week}/{day}/{slot}", Body(snapshot, "Test"), "invalid")).StatusCode);
        await f.AuthDb(async db => Assert.Equal(0, await db.Set<StudyScheduleCell>().CountAsync()));
    }

    [Fact]
    public async Task MalformedDuplicateSpoofedAndOversizedPayloadsCannotWritePartOfBlock()
    {
        await using var f = await Fixture.Create(); using var student = f.Student(); var read = await Read(student);
        var version = read.GetProperty("calendarVersion").GetGuid();
        var duplicate = new[] { new { day = "mon", slotId = "slot-1", subject = "Valid" }, new { day = "mon", slotId = "slot-1", subject = "Duplicate" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(student, HttpMethod.Post, Root + "3W/bulk-import", new { mode = "skip", entries = duplicate, expectedVersion = Guid.Empty, expectedCalendarVersion = version }, "duplicate")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(student, HttpMethod.Put, Root + "3W/1/mon/slot-1", new { subject = "test", userId = 101, expectedVersion = Guid.Empty, expectedCalendarVersion = version }, "spoof")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(student, HttpMethod.Put, Root + "3W/1/mon/slot-1", new { subject = "test" }, "missing-version")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(student, HttpMethod.Put, Root + "3W/1/mon/slot-1", Body(read, new string('x', 121)), "long")).StatusCode);
        var raw = new HttpRequestMessage(HttpMethod.Put, Root + "3W/1/mon/slot-1") { Content = new StringContent("{\"subject\":\"a\",\"Subject\":\"b\"}", Encoding.UTF8, "application/json") };
        raw.Headers.Add("Idempotency-Key", "raw"); Assert.Equal(HttpStatusCode.BadRequest, (await student.SendAsync(raw)).StatusCode);
        Assert.Equal((HttpStatusCode)413, (await Send(student, HttpMethod.Put, Root + "3W/1/mon/slot-1", Body(read, new string('x', 9000)), "huge")).StatusCode);
        await f.AuthDb(async db => { Assert.Equal(0, await db.Set<StudyScheduleCell>().CountAsync()); Assert.Equal(0, await db.Set<StudyScheduleBlock>().CountAsync()); });
    }

    [Fact]
    public async Task InactiveUnknownAmbiguousLockedAndUnavailableSourcesFailClosedButHistoryRemains()
    {
        await using var f = await Fixture.Create(); using var student = f.Student(); var read = await Read(student);
        var input = Body(read, "History"); await Json(await Send(student, HttpMethod.Put, Root + "3W/1/mon/slot-1", input, "history"));
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync("/api/users/me/study-schedule/2027/FA/3W")).StatusCode);
        await f.AdminDb(async db => { (await db.Semesters.SingleAsync()).IsActive = false; await db.SaveChangesAsync(); });
        Assert.Single((await Read(student)).GetProperty("entries").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(student, HttpMethod.Put, Root + "3W/1/mon/slot-1", input, "history")).StatusCode);
        await f.AdminDb(async db => { (await db.Semesters.SingleAsync()).IsActive = true; var other = new Semester { SemesterCode = "DUPLICATE", StartDate = DateTimeOffset.UtcNow, EndDate = DateTimeOffset.UtcNow.AddDays(100), IsActive = true }; db.Add(other); await db.SaveChangesAsync(); f.Config[$"Workspace:SemesterAliases:{other.Id:D}:Year"] = "2026"; f.Config[$"Workspace:SemesterAliases:{other.Id:D}:Term"] = "FA"; });
        Assert.Equal(HttpStatusCode.Conflict, (await student.GetAsync(Root + "3W")).StatusCode);
        f.CalendarAvailable = false; Assert.Equal(HttpStatusCode.ServiceUnavailable, (await student.GetAsync(Root + "3W")).StatusCode);
        f.CalendarAvailable = true;
        await f.AuthDb(async db => { (await db.Users.SingleAsync(x => x.Id == 100)).IsLocked = true; await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(student, HttpMethod.Put, Root + "3W/1/mon/slot-1", input, "history")).StatusCode);
        using var direct = f.AdminApp.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await direct.GetAsync("/api/internal/academic-calendar/2026/FA")).StatusCode);
        using var actor = f.Client(f.AdminApp, 100);
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.GetAsync("/api/internal/academic-calendar/2026/FA")).StatusCode);
    }

    [Fact]
    public async Task Source_InvalidInternalCredentialIs503AndCannotBeMistakenForInvalidStudentInput()
    {
        await using var f = await Fixture.Create(); using var student = f.Student(); await Read(student);
        f.Config["Security:InternalWorkflowToken"] = "changed-only-at-calendar-service";
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await student.GetAsync(Root + "3W")).StatusCode);
    }

    [Fact]
    public async Task GlobalAdminCanSelectCampusWhileStudentCannotReadAnotherCampusCalendar()
    {
        await using var f = await Fixture.Create(); using var global = f.Student(202); using var student = f.Student();
        var selected = await Json(await global.GetAsync(Root + "3W?campusCode=HAN"));
        Assert.Equal("HAN", selected.GetProperty("campusCode").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(Root + "3W?campusCode=HCM")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await global.GetAsync(Root + "3W")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.GetAsync(Root + "3W?campusCode=GLOBAL")).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task CalendarHistoryIsAppendOnlyAcrossAllSaveOverloads(int mode)
    {
        await using var f = await Fixture.Create(); using var admin = f.Admin(); var read = await Json(await admin.GetAsync("/api/academic-calendar/2026/FA"));
        await Json(await Send(admin, HttpMethod.Put, "/api/academic-calendar/2026/FA", CalendarBody(read), "calendar"));
        await f.AdminDb(async db =>
        {
            var row = await db.Set<AcademicCalendarRevision>().SingleAsync(); row.ActorId = 999;
            if (mode == 0) Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
            else if (mode == 1) Assert.Throws<InvalidOperationException>(() => db.SaveChanges(false));
            else if (mode == 2) await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            else await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(false));
        });
    }

    [Fact]
    public async Task SqlServer_PriorSchemaUpgradeAndManualVersusBulkHaveOneWinnerWithoutPartialWrites()
    {
        await using var f = await Fixture.Create(sql: true); using var student = f.Student(); var read = await Read(student, "10W");
        await Json(await Send(student, HttpMethod.Put, Root + "10W/1/mon/slot-1", Body(read, "Existing"), "seed"));
        read = await Read(student, "10W"); var expectedVersion = read.GetProperty("version").GetGuid(); var expectedCalendarVersion = read.GetProperty("calendarVersion").GetGuid();
        var manual = Send(student, HttpMethod.Put, Root + "10W/5/tue/slot-5", Body(read, "Manual"), "race-manual");
        var bulk = Send(student, HttpMethod.Post, Root + "10W/bulk-import", new { mode = "replace", entries = new[] { new { day = "mon", slotId = "slot-1", subject = "Template", room = "" }, new { day = "wed", slotId = "slot-3", subject = "Other", room = "" } }, expectedVersion, expectedCalendarVersion }, "race-bulk");
        var outcomes = await Task.WhenAll(manual, bulk);
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.Conflict);
        read = await Read(student, "10W"); Assert.Equal(outcomes[0].StatusCode == HttpStatusCode.OK ? 2 : 20, read.GetProperty("entries").GetArrayLength());
        await f.AuthDb(async db => { Assert.Equal(5, await db.Users.CountAsync()); Assert.False(db.Database.HasPendingModelChanges()); });
        await f.AdminDb(async db => { Assert.Equal("OFFICIAL-FALL", (await db.Semesters.SingleAsync()).SemesterCode); Assert.False(db.Database.HasPendingModelChanges()); });
    }

    private static object Body(JsonElement state, string subject, string? room = null) => new { subject, room, expectedVersion = state.GetProperty("version").GetGuid(), expectedCalendarVersion = state.GetProperty("calendarVersion").GetGuid() };
    private static object CalendarBody(JsonElement state, string block3 = "2026-11-09") => new { termStartDate = "2026-08-30", block10StartDate = "2026-08-31", block3StartDate = block3, expectedVersion = state.GetProperty("version").GetGuid() };
    private static async Task<JsonElement> Read(HttpClient client, string block = "3W") => await Json(await client.GetAsync(Root + block));
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    { Assert.Equal(HttpStatusCode.OK, response.StatusCode); return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone(); }
    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object? body, string key)
    { var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) }; request.Headers.Add("Idempotency-Key", key); return client.SendAsync(request); }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri) { Content = request.Content };
            foreach (var header in request.Headers) forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return await send(forwarded, ct);
        }
    }
    private sealed class Fixture(WebApplication auth, WebApplication admin, SqliteConnection? authConnection, SqliteConnection? adminConnection,
        string? authSql, string? adminSql) : IAsyncDisposable
    {
        internal WebApplication Auth => auth;
        internal WebApplication AdminApp => admin;
        internal IConfiguration Config => admin.Configuration;
        internal bool CalendarAvailable { get; set; } = true;
        internal static async Task<Fixture> Create(bool sql = false)
        {
            var a = sql ? null : new SqliteConnection("Data Source=:memory:"); var b = sql ? null : new SqliteConnection("Data Source=:memory:");
            if (a is not null) await a.OpenAsync(); if (b is not null) await b.OpenAsync();
            string Connection(string name) => new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ADMIN_SERVICE_TEST_CONNECTION_STRING") ?? "Server=(localdb)\\ClubManagerApiTests;Integrated Security=True;TrustServerCertificate=True") { InitialCatalog = "ClubHub_ScheduleTests_" + name + Guid.NewGuid().ToString("N") }.ConnectionString;
            var authSql = sql ? Connection("Auth_") : null; var adminSql = sql ? Connection("Admin_") : null;
            Fixture f = null!; WebApplication admin = null!;
            var ab = Builder(); ab.Services.AddDbContext<AuthDbContext>(x => { if (a is null) x.UseSqlServer(authSql); else x.UseSqlite(a); });
            ab.Services.AddSingleton(new AcademicCalendarClient(new HttpClient(new Handler((r, ct) => f.CalendarAvailable ? admin.GetTestClient().SendAsync(r, ct) : Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))) { BaseAddress = new Uri("http://calendar/") }, ab.Configuration));
            var auth = ab.Build(); auth.UseAuthentication(); auth.UseAuthorization(); auth.MapCanonicalProfileEndpoints(); auth.MapStudyScheduleEndpoints();
            var bb = Builder(); bb.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [$"Workspace:SemesterAliases:{SemesterId:D}:Year"] = "2026", [$"Workspace:SemesterAliases:{SemesterId:D}:Term"] = "FA" });
            bb.Services.AddDbContext<AdminDbContext>(x => { if (b is null) x.UseSqlServer(adminSql); else x.UseSqlite(b); });
            bb.Services.AddSingleton(new CurrentUserProfileClient(new HttpClient(new Handler((r, ct) => auth.GetTestClient().SendAsync(r, ct))) { BaseAddress = new Uri("http://accounts/") }, bb.Configuration));
            bb.Services.AddAuthorization(o => { o.AddPolicy(AdminPolicies.AnyActor, p => p.RequireAuthenticatedUser()); o.AddPolicy(AdminPolicies.BackofficeUser, p => p.RequireRole(AuthRoles.Admin, AuthRoles.StudentAffairsAdmin)); });
            admin = bb.Build(); admin.UseAuthentication(); admin.UseAuthorization(); admin.MapAcademicCalendarEndpoints();
            f = new(auth, admin, a, b, authSql, adminSql);
            try
            {
                await f.AuthDb(async db =>
                {
                    if (sql) await db.GetService<IMigrator>().MigrateAsync("20261012162122_InternalAccountsProfilesAndMedia"); else await db.Database.EnsureCreatedAsync();
                    foreach (var id in new[] { 100, 101, 200, 201, 202 })
                        db.Add(new User { Id = id, Username = "schedule" + id, FullName = "Account " + id, Email = id + "@fpt.edu.vn", CampusCode = id is 101 or 201 ? "HCM" : id == 202 ? "GLOBAL" : "HAN", IsActive = true });
                    if (sql) { await db.Database.OpenConnectionAsync(); await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Users] ON"); }
                    await db.SaveChangesAsync();
                    if (sql) { await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Users] OFF"); await db.Database.CloseConnectionAsync(); await db.Database.MigrateAsync(); }
                });
                await f.AdminDb(async db =>
                {
                    if (sql) await db.GetService<IMigrator>().MigrateAsync("20261022094258_SemesterStudentRosterEmailIdentity"); else await db.Database.EnsureCreatedAsync();
                    db.Add(new Semester { Id = SemesterId, SemesterCode = "OFFICIAL-FALL", StartDate = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero), EndDate = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero), IsActive = true }); await db.SaveChangesAsync();
                    if (sql) await db.Database.MigrateAsync();
                });
                await auth.StartAsync(); await admin.StartAsync(); return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        private static WebApplicationBuilder Builder()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Issuer"] = "ClubReportHub", ["Jwt:Audience"] = "ClubReportHub.Client", ["Jwt:SigningKey"] = Key, ["Security:InternalWorkflowToken"] = Secret });
            builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment); return builder;
        }
        internal HttpClient Student(int id = 100) => Client(auth, id);
        internal HttpClient Admin(int id = 200) => Client(admin, id);
        internal HttpClient Client(WebApplication app, int id)
        {
            var role = id is 200 or 201 ? AuthRoles.StudentAffairsAdmin : id == 202 ? AuthRoles.Admin : AuthRoles.ClubMember;
            var token = new JwtSecurityToken("ClubReportHub", "ClubReportHub.Client", [new Claim(JwtRegisteredClaimNames.Sub, id.ToString()), new Claim(ClaimTypes.Role, role), new Claim("campus", id is 101 or 201 ? "HCM" : id == 202 ? "GLOBAL" : "HAN")],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(1), new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
            var client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token)); return client;
        }
        internal async Task AuthDb(Func<AuthDbContext, Task> action) { await using var scope = auth.Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<AuthDbContext>()); }
        internal async Task AdminDb(Func<AdminDbContext, Task> action) { await using var scope = admin.Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<AdminDbContext>()); }
        public async ValueTask DisposeAsync()
        {
            if (authSql is not null) { if (!new SqlConnectionStringBuilder(authSql).InitialCatalog.StartsWith("ClubHub_ScheduleTests_Auth_", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected SQL database."); await AuthDb(async db => await db.Database.EnsureDeletedAsync()); }
            if (adminSql is not null) { if (!new SqlConnectionStringBuilder(adminSql).InitialCatalog.StartsWith("ClubHub_ScheduleTests_Admin_", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected SQL database."); await AdminDb(async db => await db.Database.EnsureDeletedAsync()); }
            await auth.DisposeAsync(); await admin.DisposeAsync(); if (authConnection is not null) await authConnection.DisposeAsync(); if (adminConnection is not null) await adminConnection.DisposeAsync();
        }
    }
}
