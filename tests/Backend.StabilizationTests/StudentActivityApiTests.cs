using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Data;
using ActivityService.Endpoints;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task Student_DiscoveryPagesOnlyVisiblePublishedActivitiesAndNeverReturnsRoster()
    {
        await using var app = await CreateStudentActivityAppAsync();
        int hidden;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            db.Activities.AddRange(Enumerable.Range(1, 55).Select(i => StudentActivity("Public " + i)));
            var privateActivity = StudentActivity("Private"); privateActivity.DiscoveryVisibility = "CLUB_ONLY"; db.Activities.Add(privateActivity);
            var draft = StudentActivity("Draft"); draft.PublicationStatus = "DRAFT"; db.Activities.Add(draft);
            var cancelled = StudentActivity("Cancelled"); cancelled.Status = ActivityStatuses.Cancelled; db.Activities.Add(cancelled);
            var ended = StudentActivity("Ended"); ended.EndTimeUtc = DateTimeOffset.UtcNow.AddDays(-1); ended.StartTimeUtc = ended.EndTimeUtc.AddHours(-1); db.Activities.Add(ended);
            var otherCampus = StudentActivity("Other campus"); otherCampus.ClubId = 2; db.Activities.Add(otherCampus);
            await db.SaveChangesAsync(); hidden = privateActivity.Id;
            db.ActivityParticipants.Add(new ActivityParticipant { ActivityId = 1, UserId = 105, FullName = "PRIVATE STUDENT NAME" });
            await db.SaveChangesAsync();
        }
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var page = await client.GetFromJsonAsync<JsonElement>("/api/v2/activities/feed?page=1&pageSize=50");
        Assert.Equal(55, page.GetProperty("totalItems").GetInt32());
        Assert.Equal(50, page.GetProperty("items").GetArrayLength());
        var second = await client.GetFromJsonAsync<JsonElement>("/api/v2/activities/feed?page=2&pageSize=50");
        Assert.Equal(5, second.GetProperty("items").GetArrayLength());
        Assert.DoesNotContain("PRIVATE STUDENT", page.GetRawText());
        Assert.DoesNotContain("participants", page.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", page.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v2/activities/{hidden}")).StatusCode);
        var visible = page.GetProperty("items")[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v2/activities/{visible}")).StatusCode);
        using var anonymous = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v2/activities/feed")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.PostAsJsonAsync("/api/internal/experience/student/candidates", new StudentCandidateQuery(104))).StatusCode);
    }

    [Fact]
    public async Task Student_StartedRecurringSeriesStillShowsItsNextFutureSession()
    {
        await using var app = await CreateStudentActivityAppAsync();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var activity = StudentActivity("Recurring"); activity.StartTimeUtc = DateTimeOffset.UtcNow.AddDays(-3); activity.EndTimeUtc = DateTimeOffset.UtcNow.AddDays(-2);
            db.Activities.Add(activity); await db.SaveChangesAsync();
            db.Sessions.Add(new ActivitySession
            {
                ActivityId = activity.Id,
                SemesterId = "FA26",
                StartTimeUtc = DateTimeOffset.UtcNow.AddDays(2),
                EndTimeUtc = DateTimeOffset.UtcNow.AddDays(2).AddHours(1),
                OpensAt = DateTimeOffset.UtcNow.AddDays(2),
                ClosesAt = DateTimeOffset.UtcNow.AddDays(2).AddHours(1)
            });
            await db.SaveChangesAsync();
        }
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var response = await client.GetFromJsonAsync<JsonElement>("/api/v2/activities/feed?status=UPCOMING");
        Assert.Equal(1, response.GetProperty("items").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, response.GetProperty("items")[0].GetProperty("nextSessionId").ValueKind);
        Assert.True(response.GetProperty("items")[0].GetProperty("startTime").GetDateTimeOffset() > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Student_HistoryIsSelfOnlyAndSeparatesRegisteredVerifiedAndLegacy()
    {
        await using var app = await CreateStudentActivityAppAsync();
        await SeedStudentHistoryAsync(app);
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var response = await client.GetFromJsonAsync<JsonElement>("/api/users/me/activity-history?page=1&pageSize=20");
        Assert.Equal(2, response.GetProperty("totalItems").GetInt32());
        var items = response.GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(items, x => x.GetProperty("isLegacy").GetBoolean() && x.GetProperty("verificationStatus").GetString() == "UNVERIFIED");
        Assert.Contains(items, x => !x.GetProperty("isLegacy").GetBoolean() && x.GetProperty("verificationStatus").GetString() == "VERIFIED");
        Assert.DoesNotContain("Other student's event", response.GetRawText());
        var filtered = await client.GetFromJsonAsync<JsonElement>("/api/users/me/activity-history?semesterId=FA26&verificationStatus=VERIFIED");
        Assert.Equal(1, filtered.GetProperty("totalItems").GetInt32());
    }

    [ClubManagerSqlFact]
    public async Task Student_SqlServerDiscoveryAndHistoryQueriesUseRealPagingAndPrivateDefaultMigration()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var app = await CreateStudentActivityAppAsync(database.ConnectionString);
        await SeedStudentHistoryAsync(app);
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v2/activities/feed?page=1&pageSize=1")).StatusCode);
        var history = await client.GetFromJsonAsync<JsonElement>("/api/users/me/activity-history?page=1&pageSize=1");
        Assert.Equal(2, history.GetProperty("totalItems").GetInt32());
        Assert.Equal(1, history.GetProperty("items").GetArrayLength());
    }

    private static ClubActivity StudentActivity(string title) => new()
    {
        ClubId = 1,
        ClubName = "Club 1",
        Title = title,
        Description = "Safe public description",
        Location = "Campus",
        PublicationStatus = "PUBLISHED",
        DiscoveryVisibility = "CAMPUS_PUBLIC",
        Eligibility = "CAMPUS_PUBLIC",
        SemesterId = "FA26",
        PillarWeightsJson = "[{\"pillar\":\"Academic\",\"weight\":1}]",
        RegistrationOpensAt = DateTimeOffset.UtcNow.AddDays(-1),
        RegistrationClosesAt = DateTimeOffset.UtcNow.AddDays(1),
        StartTimeUtc = DateTimeOffset.UtcNow.AddDays(2),
        EndTimeUtc = DateTimeOffset.UtcNow.AddDays(2).AddHours(1),
        Capacity = 100
    };

    private static async Task SeedStudentHistoryAsync(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
        var canonical = StudentActivity("Own verified event"); var legacy = StudentActivity("Own historical event");
        var other = StudentActivity("Other student's event");
        db.Activities.AddRange(canonical, legacy, other); await db.SaveChangesAsync();
        var session = new ActivitySession
        {
            ActivityId = canonical.Id,
            SemesterId = "FA26",
            StartTimeUtc = DateTimeOffset.UtcNow.AddDays(-1),
            EndTimeUtc = DateTimeOffset.UtcNow.AddDays(-1).AddHours(1)
        };
        db.Sessions.Add(session); await db.SaveChangesAsync();
        db.ActivityParticipants.Add(new ActivityParticipant { ActivityId = canonical.Id, UserId = 104, FullName = "Canonical student" });
        db.ActivityAttendances.AddRange(new ActivityAttendance
        {
            ActivityId = canonical.Id,
            SessionId = session.Id,
            UserId = 104,
            AttendanceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            Status = AttendanceStatuses.Present,
            VerificationStatus = "VERIFIED"
        },
            new ActivityAttendance
            {
                ActivityId = legacy.Id,
                UserId = 104,
                AttendanceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)),
                Status = AttendanceStatuses.Present,
                VerificationStatus = "UNVERIFIED"
            },
            new ActivityAttendance
            {
                ActivityId = other.Id,
                UserId = 105,
                AttendanceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)),
                Status = AttendanceStatuses.Present,
                VerificationStatus = "UNVERIFIED"
            });
        await db.SaveChangesAsync();
    }

    private static async Task<WebApplication> CreateStudentActivityAppAsync(string? connection = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey,
            ["Security:InternalWorkflowToken"] = TestInternalWorkflowToken
        });
        var database = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<ActivityDbContext>(o => { if (connection is null) o.UseInMemoryDatabase(database); else o.UseSqlServer(connection); });
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddAuthorization(o => o.AddPolicy(AuthPolicies.BusinessAccess, p => p.RequireAuthenticatedUser()));
        builder.Services.AddSingleton(new CurrentUserProfileClient(CreateFakeHttpClient(_ => new(HttpStatusCode.OK)
        { Content = JsonContent.Create(new[] { new InternalUserProfile(104, "Canonical student", "student@example.invalid", "HN", true, false, null) }) }), builder.Configuration));
        builder.Services.AddSingleton(new ClubExperienceAuthorityClient(CreateFakeHttpClient(_ => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new StudentClubScope([new(1, "C1", "Club 1", "Academic", "HN", false, false, null, Guid.NewGuid(), null),
            new(2, "C2", "Club 2", "Research", "HCM", false, false, null, Guid.NewGuid(), null)], DateTimeOffset.UtcNow, "TEST_SCOPE_ONLY"))
        }), builder.Configuration));
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();
        app.MapStudentActivityEndpoints(); app.MapStudentActivityHistoryEndpoints();
        if (connection is not null) { await using var scope = app.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<ActivityDbContext>().Database.MigrateAsync(); }
        await app.StartAsync(); return app;
    }
}

