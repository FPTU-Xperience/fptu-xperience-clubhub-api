using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task AttendanceUi_SessionCapabilitiesAreScopedAndDeferredSourceCannotCreateSessions()
    {
        await using var app = await CreateManagerSessionAppAsync();
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        using var member = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var data = await manager.GetFromJsonAsync<JsonElement>("/api/activities/1/sessions");
        Assert.True(data.GetProperty("capabilities").GetProperty("canCreate").GetBoolean());
        Assert.True(data.GetProperty("items")[0].GetProperty("capabilities").GetProperty("canIssueChallenge").GetBoolean());
        var own = await member.GetFromJsonAsync<JsonElement>("/api/activities/1/sessions");
        Assert.False(own.GetProperty("capabilities").GetProperty("canManage").GetBoolean());
        Assert.False(own.GetProperty("items")[0].GetProperty("capabilities").GetProperty("canIssueChallenge").GetBoolean());
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
        var activity = await db.Activities.SingleAsync(); activity.ScoringSnapshotJson = null; await db.SaveChangesAsync();
        data = await manager.GetFromJsonAsync<JsonElement>("/api/activities/1/sessions");
        Assert.False(data.GetProperty("capabilities").GetProperty("canCreate").GetBoolean());
        Assert.Equal("APPROVED_SCORING_SOURCE_REQUIRED", data.GetProperty("creationBlockReason").GetString());
        var clock = (ManagerClock)app.Services.GetRequiredService<TimeProvider>(); var start = clock.Current.AddDays(1);
        manager.DefaultRequestHeaders.Add("Idempotency-Key", "blocked-source");
        var created = await manager.PostAsJsonAsync("/api/activities/1/sessions", new CreateActivitySessionsRequest(activity.Version,
            new("ONLINE", start, start.AddHours(1), null, true), start, start.AddHours(1)));
        Assert.Equal(HttpStatusCode.Conflict, created.StatusCode);
        Assert.Equal(1, await db.Sessions.CountAsync());
    }

    [Fact]
    public async Task AttendanceUi_StudentWindowExposesModeButNeverGeofenceCoordinatesOrChallenge()
    {
        await using var app = await CreateSessionSourceAppAsync();
        using var student = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var data = await student.GetFromJsonAsync<JsonElement>("/api/v2/activities/1/sessions");
        foreach (var row in data.GetProperty("items").EnumerateArray())
        {
            Assert.Contains(row.GetProperty("attendanceWindow").GetProperty("mode").GetString(), new[] { "ONLINE", "ONSITE" });
            Assert.True(row.GetProperty("attendanceWindow").TryGetProperty("requiresLocation", out _));
            Assert.DoesNotContain("latitude", row.GetRawText()); Assert.DoesNotContain("longitude", row.GetRawText());
            Assert.DoesNotContain("challenge", row.GetRawText()); Assert.DoesNotContain("PRIVATE_OTHER_STUDENT", row.GetRawText());
        }
    }

    [Fact]
    public async Task AttendanceUi_RosterFlagsReflectRegistrationVerificationAndIndependentManualReview()
    {
        await using var app = await CreateManagerSessionAppAsync(uiRoster: true);
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        var data = await manager.GetFromJsonAsync<JsonElement>("/api/clubs/1/activities/1/attendance?sessionId=1");
        Assert.True(data.GetProperty("capabilities").GetProperty("canVerify").GetBoolean());
        var registered = data.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("userId").GetInt32() == 104);
        var unregistered = data.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("userId").GetInt32() == 105);
        Assert.True(registered.GetProperty("canMarkPresent").GetBoolean()); Assert.False(registered.GetProperty("canMarkAbsent").GetBoolean());
        Assert.False(unregistered.GetProperty("canMarkPresent").GetBoolean());
        Assert.False(data.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("userId").GetInt32() == 100).GetProperty("canMarkPresent").GetBoolean());
        var version = data.GetProperty("sessionVersion").GetGuid();
        manager.DefaultRequestHeaders.Add("Idempotency-Key", "manual-ui");
        var input = new BulkUpdateAttendanceRequest([new(1, "Present", "Confirmed in person")], 1, version, "Attendance independently verified");
        var written = await manager.PutAsJsonAsync("/api/clubs/1/activities/1/attendance", input);
        Assert.Equal(HttpStatusCode.OK, written.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsJsonAsync("/api/clubs/1/activities/1/attendance", input)).StatusCode);
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
        var attendance = await db.ActivityAttendances.SingleAsync(); Assert.Equal("VERIFIED", attendance.VerificationStatus);
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EventType == "experience.attendance.verified"));
        data = await manager.GetFromJsonAsync<JsonElement>("/api/clubs/1/activities/1/attendance?sessionId=1");
        Assert.False(data.GetProperty("items")[0].GetProperty("canMarkPresent").GetBoolean());
        attendance.VerificationStatus = "REVOKED"; await db.SaveChangesAsync();
        manager.DefaultRequestHeaders.Remove("Idempotency-Key"); manager.DefaultRequestHeaders.Add("Idempotency-Key", "must-not-restore");
        var restore = input with { Items = [new(1, "Present", "Do not restore", attendance.Version)] };
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PutAsJsonAsync("/api/clubs/1/activities/1/attendance", restore)).StatusCode);
        await db.Entry(attendance).ReloadAsync(); Assert.Equal("REVOKED", attendance.VerificationStatus);
    }
}
