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
    public async Task SourceProjection_RegisteredCampusNonmemberReadsPublishedSnapshotAndCompletedHistoryWithoutPrivatePolicy()
    {
        await using var app = await CreateSessionSourceAppAsync();
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var first = await client.GetFromJsonAsync<JsonElement>("/api/v2/activities/1/sessions?page=1&pageSize=1");
        Assert.Equal(3, first.GetProperty("totalItems").GetInt32());
        Assert.Equal(3, first.GetProperty("totalPages").GetInt32());
        var past = first.GetProperty("items")[0];
        Assert.Equal("CLOSED", past.GetProperty("status").GetString());
        Assert.False(past.GetProperty("sourceActions").GetProperty("canCheckIn").GetBoolean());
        Assert.Equal("REQUIRES_SUBMISSION_VALIDATION", past.GetProperty("sourceActions").GetProperty("contribution").GetString());
        var source = past.GetProperty("source");
        Assert.Equal(past.GetProperty("id").GetInt32(), source.GetProperty("sessionId").GetInt32());
        Assert.Equal(past.GetProperty("version").GetGuid(), source.GetProperty("sessionVersion").GetGuid());
        Assert.Equal(past.GetProperty("activityVersion").GetGuid(), source.GetProperty("activityVersion").GetGuid());
        Assert.Equal("T3", source.GetProperty("attributes").GetProperty("tier").GetString());
        Assert.Equal("APPROVED", source.GetProperty("policy").GetProperty("approvalStatus").GetString());
        foreach (var secret in new[] { "attendancePolicy", "geofence", "latitude", "PRIVATE_OTHER_STUDENT", "challenge", "evidenceRefs" })
            Assert.DoesNotContain(secret, first.GetRawText());
        var second = await client.GetFromJsonAsync<JsonElement>("/api/v2/activities/1/sessions?page=2&pageSize=1");
        Assert.True(second.GetProperty("items")[0].GetProperty("sourceActions").GetProperty("canCheckIn").GetBoolean());
        var future = await client.GetFromJsonAsync<JsonElement>("/api/v2/activities/1/sessions?page=3&pageSize=1");
        Assert.Equal("UNAVAILABLE", future.GetProperty("items")[0].GetProperty("sourceActions").GetProperty("contribution").GetString());
    }

    [Theory]
    [InlineData(false, "CAMPUS_PUBLIC", "HN", false, false, "PUBLISHED", HttpStatusCode.Forbidden)]
    [InlineData(true, "CLUB_ONLY", "HN", false, false, "PUBLISHED", HttpStatusCode.NotFound)]
    [InlineData(true, "CAMPUS_PUBLIC", "HCM", false, false, "PUBLISHED", HttpStatusCode.NotFound)]
    [InlineData(true, "CAMPUS_PUBLIC", "HN", false, true, "PUBLISHED", HttpStatusCode.Forbidden)]
    [InlineData(true, "CAMPUS_PUBLIC", "HN", false, false, "DRAFT", HttpStatusCode.NotFound)]
    public async Task SourceProjection_EnforcesRegistrationVisibilityCampusAccountAndPublication(bool registered, string visibility,
        string campus, bool member, bool locked, string publication, HttpStatusCode expected)
    {
        await using var app = await CreateSessionSourceAppAsync(registered, visibility, campus, member, locked, publication);
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        Assert.Equal(expected, (await client.GetAsync("/api/v2/activities/1/sessions")).StatusCode);
    }

    [Fact]
    public async Task SourceProjection_FailsClosedWhenAuthorityOrSnapshotIsUnavailableAndIgnoresForgedUserId()
    {
        await using var app = await CreateSessionSourceAppAsync();
        using var client = CreateAdminContractClient(app, 105, AuthRoles.ClubMember);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v2/activities/1/sessions?userId=104")).StatusCode);
        using var owner = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
        (await db.Sessions.OrderBy(x => x.Id).FirstAsync()).ScoringSnapshotJson = "{}";
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await owner.GetAsync("/api/v2/activities/1/sessions")).StatusCode);
        await using var down = await CreateSessionSourceAppAsync(authorityDown: true);
        using var offline = CreateAdminContractClient(down, 104, AuthRoles.ClubMember);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await offline.GetAsync("/api/v2/activities/1/sessions")).StatusCode);
    }

    [Fact]
    public async Task SourceProjection_CancelledHistoryDisablesActionsAndMembershipCutoffFiltersOldSessions()
    {
        await using var app = await CreateSessionSourceAppAsync();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            (await db.Activities.SingleAsync()).PublicationStatus = "CANCELLED";
            await db.SaveChangesAsync();
        }
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var history = await client.GetFromJsonAsync<JsonElement>("/api/v2/activities/1/sessions");
        Assert.All(history.GetProperty("items").EnumerateArray(), x =>
        {
            Assert.Equal("CANCELLED", x.GetProperty("source").GetProperty("status").GetString());
            Assert.False(x.GetProperty("sourceActions").GetProperty("canCheckIn").GetBoolean());
            Assert.Equal("UNAVAILABLE", x.GetProperty("sourceActions").GetProperty("contribution").GetString());
            Assert.Equal("UNAVAILABLE", x.GetProperty("sourceActions").GetProperty("manualAttendance").GetString());
        });
        await using var members = await CreateSessionSourceAppAsync(member: true, eligibility: "CLUB_MEMBER", joinedAt: DateTimeOffset.UtcNow);
        using var newMember = CreateAdminContractClient(members, 104, AuthRoles.ClubMember);
        var available = await newMember.GetFromJsonAsync<JsonElement>("/api/v2/activities/1/sessions");
        Assert.Equal(1, available.GetProperty("totalItems").GetInt32());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task SourceProjection_RejectsInvalidPaging(string query)
    {
        await using var app = await CreateSessionSourceAppAsync();
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v2/activities/1/sessions?" + query)).StatusCode);
    }

    [Theory]
    [InlineData("/api/v2/activities/1/sessions", "activity-service")]
    [InlineData("/api/clubs/1/workspace-semesters", "club-service")]
    [InlineData("/api/clubs/1/invitation-semesters", "club-service")]
    [InlineData("/api/clubs/1/experience-policies?semesterId=FALL2026", "club-service")]
    public Task SourceProjection_GatewaySelectsOwningService(string path, string cluster)
        => ClubManager_GatewayRoutesNewApisToOwningServiceBeforeLegacyCatchAll("GET", path, cluster);

    [ClubManagerSqlFact]
    public async Task SourceProjection_SqlServerPagesCompletedAndLiveSourcesWithoutClientSideFiltering()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var app = await CreateSessionSourceAppAsync(connection: database.ConnectionString);
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var response = await client.GetFromJsonAsync<JsonElement>("/api/v2/activities/1/sessions?page=2&pageSize=1");
        Assert.Equal(3, response.GetProperty("totalItems").GetInt32());
        Assert.Single(response.GetProperty("items").EnumerateArray());
        Assert.True(response.GetProperty("items")[0].GetProperty("sourceActions").GetProperty("canCheckIn").GetBoolean());
    }

    [Fact]
    public async Task SourceProjection_VerifiedOwnAttendanceDisablesCheckInAndManualSourceButDoesNotExposeOtherAttendance()
    {
        await using var app = await CreateSessionSourceAppAsync();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            db.ActivityAttendances.Add(new ActivityAttendance
            {
                ActivityId = 1,
                SessionId = 2,
                UserId = 104,
                AttendanceDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Status = AttendanceStatuses.Present,
                VerificationStatus = "VERIFIED"
            });
            await db.SaveChangesAsync();
        }
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var response = await client.GetFromJsonAsync<JsonElement>("/api/v2/activities/1/sessions?page=2&pageSize=1");
        var item = response.GetProperty("items")[0]; Assert.True(item.GetProperty("alreadyVerified").GetBoolean());
        Assert.False(item.GetProperty("sourceActions").GetProperty("canCheckIn").GetBoolean());
        Assert.Equal("UNAVAILABLE", item.GetProperty("sourceActions").GetProperty("manualAttendance").GetString());
    }

    private static async Task<WebApplication> CreateSessionSourceAppAsync(bool registered = true, string visibility = "CAMPUS_PUBLIC",
        string campus = "HN", bool member = false, bool locked = false, string publication = "PUBLISHED",
        bool authorityDown = false, string eligibility = "CAMPUS_PUBLIC", DateTimeOffset? joinedAt = null, string? connection = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey,
            ["Security:InternalWorkflowToken"] = TestInternalWorkflowToken,
            ["Attendance:SigningKey"] = "source-projection-test-signing-key-32-characters"
        });
        var name = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<ActivityDbContext>(o => { if (connection is null) o.UseInMemoryDatabase(name); else o.UseSqlServer(connection); });
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(new CurrentUserProfileClient(CreateFakeHttpClient(_ => new(HttpStatusCode.OK)
        { Content = JsonContent.Create(new[] { new InternalUserProfile(104, "Student", "student@example.invalid", campus, true, locked, null) }) }), builder.Configuration));
        builder.Services.AddSingleton(new ClubExperienceAuthorityClient(CreateFakeHttpClient(_ => authorityDown ? new(HttpStatusCode.ServiceUnavailable)
            : new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new StudentClubScope(
                [new(1, "ONE", "One", "Academic", "HN", member, member, member ? joinedAt ?? DateTimeOffset.UtcNow.AddDays(-2) : null,
                    Guid.NewGuid(), null)], DateTimeOffset.UtcNow, "SOURCE_TEST_SCOPE"))
            }), builder.Configuration));
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapStudentSessionSourceEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            if (connection is not null) await db.Database.MigrateAsync();
            var activity = StudentActivity("Published source"); activity.DiscoveryVisibility = visibility;
            activity.Eligibility = eligibility; activity.PublicationStatus = publication; activity.SemesterId = "FALL2026";
            db.Activities.Add(activity); await db.SaveChangesAsync();
            if (registered) db.ActivityParticipants.Add(new ActivityParticipant { ActivityId = 1, UserId = 104, FullName = "Student" });
            db.ActivityParticipants.Add(new ActivityParticipant { ActivityId = 1, UserId = 999, FullName = "PRIVATE_OTHER_STUDENT" });
            var now = DateTimeOffset.UtcNow;
            for (var i = 1; i <= 3; i++)
            {
                var start = i == 1 ? now.AddHours(-3) : i == 2 ? now.AddMinutes(-10) : now.AddDays(1);
                var source = ManagerSource() with { SemesterId = "FALL2026", ActivityVersion = activity.Version };
                db.Sessions.Add(new ActivitySession
                {
                    ActivityId = 1,
                    SemesterId = "FALL2026",
                    StartTimeUtc = start,
                    EndTimeUtc = start.AddHours(1),
                    Status = i == 1 ? "CLOSED" : "SCHEDULED",
                    ManualConfirmationEnabled = true,
                    OpensAt = start.AddMinutes(-15),
                    ClosesAt = start.AddHours(1),
                    Latitude = 21,
                    Longitude = 105,
                    RadiusMeters = 100,
                    ScoringSnapshotJson = JsonSerializer.Serialize(source, ExperienceJson)
                });
            }
            await db.SaveChangesAsync();
        }
        await app.StartAsync(); return app;
    }
}
