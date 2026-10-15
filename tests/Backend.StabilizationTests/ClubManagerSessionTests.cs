using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Endpoints;
using ActivityService.Infrastructure;
using ActivityService.Models;
using ActivityService.Services;
using AdminService.Contracts;
using AdminService.Data;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Experience;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    internal static string ManagerTestToken(int user) => CreateToken(user, AuthRoles.ClubMember);
    [Fact]
    public async Task ClubManager_SessionQrChecksTimeSourceLocationAndDeduplicatesThenCancellationRevokes()
    {
        await using var app = await CreateManagerSessionAppAsync();
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        using var member = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var clock = (ManagerClock)app.Services.GetRequiredService<TimeProvider>();
        var challenge = await manager.GetFromJsonAsync<JsonElement>("/api/activities/1/sessions/1/attendance-challenge");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/activities/1/sessions/1/attendance-challenge")).StatusCode);
        var qr = challenge.GetProperty("challenge").GetString()!;
        member.DefaultRequestHeaders.Add("Idempotency-Key", "qr-checkin");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/activities/1/check-in", new SessionCheckInRequest(1, qr, new(0, 0, 10)))).StatusCode);
        clock.Current += TimeSpan.FromSeconds(30);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostAsJsonAsync("/api/activities/1/check-in", new SessionCheckInRequest(1, qr, new(10, 106, 10)))).StatusCode);
        clock.Current -= TimeSpan.FromSeconds(30);
        var body = new SessionCheckInRequest(1, qr, new(10, 106, 10));
        Assert.Equal(HttpStatusCode.OK, (await member.PostAsJsonAsync("/api/activities/1/check-in", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.PostAsJsonAsync("/api/activities/1/check-in", body)).StatusCode);
        Guid sessionVersion;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            Assert.Equal(1, await db.ActivityAttendances.CountAsync());
            Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EventType == ExperienceRoutingKeys.AttendanceVerified));
            sessionVersion = (await db.Sessions.SingleAsync()).Version;
            Assert.Equal("PENDING", (await db.ActivityAttendances.SingleAsync()).RecognitionStatus);
        }
        manager.DefaultRequestHeaders.Add("Idempotency-Key", "cancel-session");
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync("/api/activities/1/sessions/1/cancel", new SessionDecisionRequest(sessionVersion, "Venue unavailable."))).StatusCode);
        member.DefaultRequestHeaders.Remove("Idempotency-Key"); member.DefaultRequestHeaders.Add("Idempotency-Key", "after-cancellation");
        Assert.Equal(HttpStatusCode.Conflict, (await member.PostAsJsonAsync("/api/activities/1/check-in", body)).StatusCode);
        await using var verify = app.Services.CreateAsyncScope(); var actual = verify.ServiceProvider.GetRequiredService<ActivityDbContext>();
        Assert.Equal("REVOKED", (await actual.ActivityAttendances.SingleAsync()).VerificationStatus);
        Assert.Equal(1, await actual.OutboxMessages.CountAsync(x => x.EventType == ExperienceRoutingKeys.SourceCancelled));
    }

    [Fact]
    public async Task ClubManager_RecurringSessionsUseVietnamDatesExclusionsAndShiftAttendanceWindows()
    {
        await using var app = await CreateManagerSessionAppAsync(); using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        Guid version; await using (var scope = app.Services.CreateAsyncScope()) version = (await scope.ServiceProvider.GetRequiredService<ActivityDbContext>().Activities.SingleAsync()).Version;
        manager.DefaultRequestHeaders.Add("Idempotency-Key", "recurrence");
        var start = new DateTimeOffset(2026, 10, 19, 2, 0, 0, TimeSpan.Zero);
        var request = new CreateActivitySessionsRequest(version, new("ONLINE", start, start.AddMinutes(30), null, true),
            Weekdays: [1, 3], StartTime: new(9, 0), EndTime: new(10, 0), RangeStartDate: new(2026, 10, 19),
            RangeEndDate: new(2026, 11, 1), ExcludedDates: [new(2026, 10, 21)]);
        var response = await manager.PostAsJsonAsync("/api/activities/1/sessions", request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(3, created.GetProperty("total").GetInt32());
        var session = created.GetProperty("items")[0]; Assert.Equal(start, session.GetProperty("startTime").GetDateTimeOffset());
        Assert.Equal(start, session.GetProperty("attendancePolicy").GetProperty("opensAt").GetDateTimeOffset());
        foreach (var occurrence in created.GetProperty("items").EnumerateArray())
        {
            var occurrenceStart = occurrence.GetProperty("startTime").GetDateTimeOffset();
            Assert.Equal(occurrenceStart, occurrence.GetProperty("attendancePolicy").GetProperty("opensAt").GetDateTimeOffset());
            Assert.Equal(occurrenceStart.AddMinutes(30), occurrence.GetProperty("attendancePolicy").GetProperty("closesAt").GetDateTimeOffset());
        }
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync("/api/activities/1/sessions", request)).StatusCode);
        await using var verify = app.Services.CreateAsyncScope(); Assert.Equal(4, await verify.ServiceProvider.GetRequiredService<ActivityDbContext>().Sessions.CountAsync());
    }

    [Fact]
    public async Task ClubManager_ExternalDeclarationUsesScopedReviewsPrivateEvidenceAndUnifiedLedgerRadar()
    {
        var source = ManagerSource(); await using var app = await CreateManagerExperienceAppAsync(source);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            db.Semesters.Add(new Semester { SemesterCode = source.SemesterId, StartDate = source.Policy.StartDate, EndDate = source.Policy.EndDate });
            db.VerificationAssignments.Add(new VerificationAssignment { UserId = 100, SourceType = "EXTERNAL", StartsAt = DateTimeOffset.UtcNow.AddDays(-1), CapabilitiesJson = "[\"contribution.review\"]" });
            await db.SaveChangesAsync();
        }
        using var author = CreateAdminContractClient(app, 104, AuthRoles.ClubMember); using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        using var unassigned = CreateAdminContractClient(app, 101, AuthRoles.ClubManager);
        author.DefaultRequestHeaders.Add("Idempotency-Key", "declaration-intake");
        var input = new SubmitDeclarationRequest("Outside effort", "Academic", "Community partner", true, null, "", "Private proof", null,
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow), SemesterId: source.SemesterId,
            OccurredAt: DateTimeOffset.UtcNow.AddDays(-1), ProposedAttributes: ManagerAttributes(), EvidenceRefs: []);
        var created = await author.PostAsJsonAsync("/api/v1/declarations", input); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var row = (await created.Content.ReadFromJsonAsync<ContributionResponse>())!;
        Assert.Equal(HttpStatusCode.Forbidden, (await unassigned.GetAsync($"/api/v1/declarations/{row.Id}")).StatusCode);
        manager.DefaultRequestHeaders.Add("Idempotency-Key", "external-review");
        var review = new ReviewDeclarationRequest("APPROVE", "Verified.", ExpectedVersion: row.Version, VerifiedAttributes: ManagerAttributes());
        var approved = await manager.PostAsJsonAsync($"/api/v1/student-affairs/declarations/{row.Id}/review", review);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(78m, (await approved.Content.ReadFromJsonAsync<ContributionResponse>())!.RawPoints);
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync($"/api/v1/student-affairs/declarations/{row.Id}/review", review)).StatusCode);
        var ledger = await author.GetFromJsonAsync<JsonElement>($"/api/users/me/experience-ledger?semesterId={source.SemesterId}");
        Assert.Equal(2, ledger.GetProperty("total").GetInt32());
        var radar = await author.GetFromJsonAsync<StudentRadarResponse>($"/api/v1/declarations/me/radar?semester={source.SemesterId}");
        Assert.True(radar!.IsAuthoritative); Assert.Equal(78m, radar.Pillars.Sum(x => x.RawPointsSum));
    }

    internal static async Task<WebApplication> CreateManagerSessionAppAsync(string? connection = null, bool register = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey,
            ["Security:InternalWorkflowToken"] = TestInternalWorkflowToken,
            ["Attendance:SigningKey"] = "TEST-ONLY-ATTENDANCE-KEY-AT-LEAST-32-CHARS"
        });
        var name = Guid.NewGuid().ToString(); builder.Services.AddDbContext<ActivityDbContext>(o => { if (connection is null) o.UseInMemoryDatabase(name); else o.UseSqlServer(connection, sql => sql.EnableRetryOnFailure()); });
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment); builder.Services.AddAuthorization(o => o.AddPolicy(AuthPolicies.BusinessAccess, p => p.RequireAuthenticatedUser()));
        builder.Services.AddScoped<MemberActivityStatisticsService>();
        builder.Services.AddSingleton(new ReportVerificationClient(CreateFakeHttpClient(_ => new(HttpStatusCode.NotFound)), NullLogger<ReportVerificationClient>.Instance));
        var clock = new ManagerClock(new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero)); builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton(new CurrentUserProfileClient(CreateFakeHttpClient(_ => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[] {
            new InternalUserProfile(104, "Student A", "student-a@example.invalid", "HN", true, false, null),
            new InternalUserProfile(105, "Student B", "student-b@example.invalid", "HN", true, false, null) })
        }), builder.Configuration));
        builder.Services.AddSingleton(new ClubExperienceAuthorityClient(CreateFakeHttpClient(_ => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ClubExperienceAuthority(1, "Club 1", "HN", true, true, clock.Current.AddMonths(-1)))
        }), builder.Configuration));
        builder.Services.AddSingleton(new ClubAccessClient(CreateFakeHttpClient(request =>
        {
            var user = int.Parse(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter).Subject);
            var access = new ClubAccessSnapshot(1, "Club 1", user == 100, false, user is 104 or 105, [100], [104, 105], []);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { access }) };
        }), new MemoryCache(new MemoryCacheOptions()), NullLogger<ClubAccessClient>.Instance));
        builder.Services.AddSingleton(new ClubMemberRosterClient(CreateFakeHttpClient(_ => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[] {
            new MyClubMembership(1, 1, 104, "Student A", "Approved", clock.Current.AddMonths(-1), clock.Current.AddMonths(-1)),
            new MyClubMembership(2, 1, 105, "Student B", "Approved", clock.Current.AddMonths(-1), clock.Current.AddMonths(-1)) })
        })));
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapActivityEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>(); if (connection is not null) await db.Database.MigrateAsync();
            var source = ManagerSource(); var activity = new ClubActivity
            {
                ClubId = 1,
                ClubName = "Club 1",
                Title = "Published activity",
                Description = "Real activity",
                Location = "Campus",
                StartTimeUtc = clock.Current.AddDays(-1),
                EndTimeUtc = clock.Current.AddDays(30),
                PublicationStatus = "PUBLISHED",
                SemesterId = source.SemesterId,
                Capacity = register ? 10 : 1,
                RegistrationOpensAt = clock.Current.AddHours(-1),
                RegistrationClosesAt = clock.Current.AddHours(1),
                ScoringSnapshotJson = JsonSerializer.Serialize(source, ExperienceJson),
                PillarWeightsJson = JsonSerializer.Serialize(ManagerAttributes().PillarWeights, ExperienceJson)
            };
            db.Activities.Add(activity); await db.SaveChangesAsync();
            db.Sessions.Add(new ActivitySession
            {
                ActivityId = activity.Id,
                SemesterId = source.SemesterId,
                StartTimeUtc = clock.Current.AddMinutes(-5),
                EndTimeUtc = clock.Current.AddMinutes(55),
                OpensAt = clock.Current.AddMinutes(-5),
                ClosesAt = clock.Current.AddMinutes(25),
                Latitude = 10,
                Longitude = 106,
                RadiusMeters = 100,
                ManualConfirmationEnabled = true,
                ScoringSnapshotJson = activity.ScoringSnapshotJson!
            });
            if (register) { db.ActivityParticipants.Add(new ActivityParticipant { ActivityId = activity.Id, UserId = 104, FullName = "Student A" }); activity.RegisteredCount = 1; }
            await db.SaveChangesAsync();
        }
        await app.StartAsync(); return app;
    }
    private sealed class ManagerClock(DateTimeOffset now) : TimeProvider { internal DateTimeOffset Current = now; public override DateTimeOffset GetUtcNow() => Current; }
}

public sealed partial class ClubManagerSqlIntegrityTests
{
    [ClubManagerSqlFact]
    public async Task ClubManager_SqlServerLastSeatRaceAcceptsOneAndReloadKeepsCapacityAndReceipt()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var app = await CrossServiceWorkflowAuthorizationTests.CreateManagerSessionAppAsync(database.ConnectionString, false);
        using var first = app.GetTestClient(); using var second = app.GetTestClient();
        first.DefaultRequestHeaders.Authorization = new("Bearer", CrossServiceWorkflowAuthorizationTests.ManagerTestToken(104));
        second.DefaultRequestHeaders.Authorization = new("Bearer", CrossServiceWorkflowAuthorizationTests.ManagerTestToken(105));
        first.DefaultRequestHeaders.Add("Idempotency-Key", "last-seat-a"); second.DefaultRequestHeaders.Add("Idempotency-Key", "last-seat-b");
        var responses = await Task.WhenAll(first.PostAsJsonAsync("/api/activities/1/participants", new RegisterActivityParticipantRequest(null, null)),
            second.PostAsJsonAsync("/api/activities/1/participants", new RegisterActivityParticipantRequest(null, null)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var db = new ActivityDbContext(new DbContextOptionsBuilder<ActivityDbContext>().UseSqlServer(database.ConnectionString).Options);
        Assert.Equal(1, await db.ActivityParticipants.CountAsync()); Assert.Equal(1, (await db.Activities.SingleAsync()).RegisteredCount);
        Assert.Equal(1, await db.Set<ClubReportHub.Shared.Data.ApiOperationReceipt>().CountAsync());
    }
}
