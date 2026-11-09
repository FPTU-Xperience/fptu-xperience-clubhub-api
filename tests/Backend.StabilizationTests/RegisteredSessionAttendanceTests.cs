using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Data;
using ActivityService.Endpoints;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    private const string RegisteredAttendanceUrl = "/api/clubs/1/activities/1/sessions/1/registered-attendance";

    [Fact]
    public async Task AttendanceRegistered_OutsideClubUsesRealUserIdsAndWritesOneVerifiedEventOnRetry()
    {
        await using var app = await CreateManagerSessionAppAsync(uiRoster: true, outside: true);
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        using var member = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(RegisteredAttendanceUrl)).StatusCode);
        var read = await manager.GetAsync(RegisteredAttendanceUrl);
        var content = await read.Content.ReadAsStringAsync();
        Assert.True(read.IsSuccessStatusCode, content);
        var roster = JsonSerializer.Deserialize<JsonElement>(content);
        Assert.Equal("REGISTERED_USER", roster.GetProperty("identityKind").GetString());
        var row = Assert.Single(roster.GetProperty("items").EnumerateArray());
        Assert.Equal(104, row.GetProperty("userId").GetInt32()); Assert.False(row.TryGetProperty("memberId", out _));
        Assert.True(row.GetProperty("canMarkPresent").GetBoolean()); Assert.False(row.GetProperty("canMarkAbsent").GetBoolean());
        var input = new RegisteredAttendanceInput(roster.GetProperty("sessionVersion").GetGuid(), "Confirmed independently", [new(104, "Present", null, null)]);
        manager.DefaultRequestHeaders.Add("Idempotency-Key", "outside-attendance");
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsJsonAsync(RegisteredAttendanceUrl, input)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsJsonAsync(RegisteredAttendanceUrl, input)).StatusCode);
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
        var attendance = await db.ActivityAttendances.SingleAsync(); Assert.Equal(104, attendance.UserId);
        Assert.Equal("VERIFIED", attendance.VerificationStatus); Assert.Equal("PENDING", attendance.RecognitionStatus);
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EventType == ExperienceRoutingKeys.AttendanceVerified));
        manager.DefaultRequestHeaders.Remove("Idempotency-Key"); manager.DefaultRequestHeaders.Add("Idempotency-Key", "protect-verified");
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PutAsJsonAsync(RegisteredAttendanceUrl,
            input with { Items = [new(104, "Absent", null, attendance.Version)] })).StatusCode);
    }

    [Fact]
    public async Task AttendanceRegistered_CancelledRegistrationWrongCampusSelfReviewAndInternalRosterAreRejected()
    {
        await using var app = await CreateManagerSessionAppAsync(uiRoster: true, outside: true);
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
        var session = await db.Sessions.SingleAsync(); var version = session.Version;
        db.ActivityParticipants.AddRange(new ActivityParticipant { ActivityId = 1, UserId = 106, FullName = "Other campus" },
            new ActivityParticipant { ActivityId = 1, UserId = 100, FullName = "Manager" });
        var participant = await db.ActivityParticipants.SingleAsync(x => x.UserId == 104); participant.CancelledAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        async Task<HttpStatusCode> Write(int user, string key)
        {
            manager.DefaultRequestHeaders.Remove("Idempotency-Key"); manager.DefaultRequestHeaders.Add("Idempotency-Key", key);
            var response = await manager.PutAsJsonAsync(RegisteredAttendanceUrl, new RegisteredAttendanceInput(version, "Independent review", [new(user, "Present", null, null)]));
            return response.StatusCode;
        }
        Assert.Equal(HttpStatusCode.Conflict, await Write(104, "cancelled"));
        Assert.Equal(HttpStatusCode.Forbidden, await Write(106, "campus"));
        Assert.Equal(HttpStatusCode.Forbidden, await Write(100, "self"));
        Assert.Equal(HttpStatusCode.Conflict, await Write(105, "not-registered"));
        Assert.Empty(await db.ActivityAttendances.ToListAsync());
        var activity = await db.Activities.SingleAsync(); activity.IsInternal = true; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync(RegisteredAttendanceUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, await Write(106, "internal"));
    }
}

public sealed partial class ClubManagerSqlIntegrityTests
{
    [ClubManagerSqlFact]
    public async Task AttendanceRegistered_SqlConcurrentManualCommandsProduceOneAttendanceAndOneRecognitionEvent()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var app = await CrossServiceWorkflowAuthorizationTests.CreateManagerSessionAppAsync(database.ConnectionString, uiRoster: true, outside: true);
        using var first = app.GetTestClient(); using var second = app.GetTestClient();
        foreach (var client in new[] { first, second }) client.DefaultRequestHeaders.Authorization = new("Bearer", CrossServiceWorkflowAuthorizationTests.ManagerTestToken(100));
        first.DefaultRequestHeaders.Add("Idempotency-Key", "outside-concurrent-first");
        second.DefaultRequestHeaders.Add("Idempotency-Key", "outside-concurrent-second");
        const string url = "/api/clubs/1/activities/1/sessions/1/registered-attendance";
        var roster = await first.GetFromJsonAsync<JsonElement>(url);
        var input = new RegisteredAttendanceInput(roster.GetProperty("sessionVersion").GetGuid(), "Independent manual verification", [new(104, "Present", null, null)]);
        var responses = await Task.WhenAll(first.PutAsJsonAsync(url, input), second.PutAsJsonAsync(url, input));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
        Assert.Equal(1, await db.ActivityAttendances.CountAsync());
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EventType == ExperienceRoutingKeys.AttendanceVerified));
    }
}
