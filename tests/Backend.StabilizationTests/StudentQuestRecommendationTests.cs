using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Data;
using ActivityService.Models;
using AdminService.Contracts;
using AdminService.Data;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task Student_QuestRecommendationsRankAllEligibleQuestsBySelfRadarBeforePaging()
    {
        var state = new StudentSourceState();
        var start = DateTimeOffset.UtcNow.AddDays(2);
        state.Activities = [new(1, 1, "C1", "Club 1", "Academic activity", "Safe", null, start, start.AddHours(1), "Campus",
            "UPCOMING", "REGISTER", true, true, "FA26", 1, Guid.NewGuid(), Guid.NewGuid(), null, [new("Academic", 1)]),
            new(101, 1, "C1", "Club 1", "Research activity", "Safe", null, start.AddDays(1), start.AddDays(1).AddHours(1), "Campus",
            "UPCOMING", "REGISTER", true, true, "FA26", 101, Guid.NewGuid(), Guid.NewGuid(), null, [new("Research", 1)])];
        await using var app = await CreateStudentAdminAppAsync(state);
        string lowAxisId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            await SeedStudentContributionAsync(db, 104, "FA26", "Academic", 400);
            var policy = await db.ApprovedExperiencePolicies.SingleAsync();
            for (var i = 0; i < 30; i++)
                db.Quests.Add(new Quest
                {
                    Title = "Academic quest " + i,
                    SemesterCode = "FA26",
                    CampusCode = "GLOBAL",
                    CriteriaVersion = Guid.NewGuid(),
                    CriteriaSnapshotJson = JsonSerializer.Serialize(new QuestCriteria("Academic", [1], policy.Id), ExperienceJson)
                });
            var low = new Quest
            {
                Title = "Low Research axis",
                SemesterCode = "FA26",
                CampusCode = "GLOBAL",
                CriteriaVersion = Guid.NewGuid(),
                CriteriaSnapshotJson = JsonSerializer.Serialize(new QuestCriteria("Research", [101], policy.Id), ExperienceJson)
            };
            lowAxisId = low.Id.ToString(); db.Quests.Add(low);
            db.Quests.Add(new Quest
            {
                Title = "Other campus",
                CampusCode = "HCM",
                SemesterCode = "FA26",
                CriteriaVersion = Guid.NewGuid(),
                CriteriaSnapshotJson = low.CriteriaSnapshotJson
            });
            db.Quests.Add(new Quest
            {
                Title = "Expired",
                SemesterCode = "FA26",
                CriteriaVersion = Guid.NewGuid(),
                CriteriaSnapshotJson = low.CriteriaSnapshotJson,
                DeadlineUtc = DateTimeOffset.UtcNow.AddDays(-1)
            });
            await db.SaveChangesAsync();
        }
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var result = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests?semester=FA26&page=1&pageSize=1");
        Assert.Equal(31, result.GetProperty("totalItems").GetInt32());
        var first = result.GetProperty("items")[0];
        Assert.Equal(lowAxisId, first.GetProperty("id").GetString());
        Assert.Equal("Research", first.GetProperty("targetPillar").GetString());
        Assert.Equal("LOW_AXIS_RULE", first.GetProperty("sourceLabel").GetString());
        Assert.Equal(1, first.GetProperty("rank").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, first.GetProperty("radarSnapshot").ValueKind);
        using var anonymous = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/quests?recommended=true")).StatusCode);
        state.Unavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/v1/quests?semester=FA26&page=1")).StatusCode);
        await using var verify = app.Services.CreateAsyncScope();
        Assert.Equal(1, await verify.ServiceProvider.GetRequiredService<AdminDbContext>().ExperienceLedgerEffects.CountAsync());
        Assert.Equal(0, await verify.ServiceProvider.GetRequiredService<AdminDbContext>().QuestParticipants.CountAsync());
    }

    [ClubManagerSqlFact]
    public async Task Student_SqlServerQuestSessionSelectorFindsLaterEligibleSessionAndCountersTrackChangedSources()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var app = await CreateStudentActivityAppAsync(database.ConnectionString);
        int activityId, laterId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var activity = StudentActivity("Recurring selected source"); db.Activities.Add(activity); await db.SaveChangesAsync();
            activityId = activity.Id;
            var early = new ActivitySession { ActivityId = activityId, SemesterId = "FA26", StartTimeUtc = activity.StartTimeUtc, EndTimeUtc = activity.EndTimeUtc };
            var later = new ActivitySession { ActivityId = activityId, SemesterId = "FA26", StartTimeUtc = activity.StartTimeUtc.AddDays(1), EndTimeUtc = activity.EndTimeUtc.AddDays(1) };
            db.Sessions.AddRange(early, later); await db.SaveChangesAsync(); laterId = later.Id;
            db.ActivityAttendances.Add(new ActivityAttendance
            {
                ActivityId = activityId,
                SessionId = early.Id,
                UserId = 104,
                Status = AttendanceStatuses.Present,
                VerificationStatus = "VERIFIED",
                AttendanceDate = DateOnly.FromDateTime(DateTime.UtcNow)
            });
            await db.SaveChangesAsync();
        }
        using var client = app.GetTestClient(); client.DefaultRequestHeaders.Add("X-Internal-Workflow-Token", TestInternalWorkflowToken);
        var selected = await client.PostAsJsonAsync("/api/internal/experience/student/candidates", new StudentCandidateQuery(104, SessionIds: [laterId]));
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        Assert.Equal(laterId, Assert.Single((await selected.Content.ReadFromJsonAsync<StudentActivityPage>())!.Items).NextSessionId);
        var invalid = await client.PostAsJsonAsync("/api/internal/experience/student/candidates", new StudentCandidateQuery(104, SessionIds: [laterId + 9999]));
        Assert.Empty((await invalid.Content.ReadFromJsonAsync<StudentActivityPage>())!.Items);
        var before = await client.PostAsJsonAsync("/api/internal/experience/student/counters", new StudentCounterQuery(104, "PERIOD", "FA26"));
        var first = await before.Content.ReadFromJsonAsync<StudentCounters>(); Assert.Equal(1, first!.VerifiedAttendanceCount);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var attendance = await db.ActivityAttendances.SingleAsync(); attendance.SessionId = laterId; attendance.Version = Guid.NewGuid();
            await db.SaveChangesAsync();
        }
        var after = await client.PostAsJsonAsync("/api/internal/experience/student/counters", new StudentCounterQuery(104, "PERIOD", "FA26"));
        var second = await after.Content.ReadFromJsonAsync<StudentCounters>(); Assert.Equal(1, second!.VerifiedAttendanceCount);
        Assert.NotEqual(first.Version, second.Version);
    }
}
