using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdminService.Data;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task Recommendations_MissingMatchingPolicyReturnsExplicitBasicModeWithoutApprovingPolicyOrAwardingXp()
    {
        var now = DateTimeOffset.UtcNow;
        StudentActivityItem Item(int id, int days) => new(id, 1, "C1", "Club 1", "Activity " + id, "Description", null,
            now.AddDays(days), now.AddDays(days).AddHours(1), "Campus", "UPCOMING", "REGISTER", true, true,
            "FA26", null, Guid.NewGuid(), null, null, []);
        var state = new StudentSourceState { Activities = [Item(3, 3), Item(2, 2), Item(1, 2), Item(4, 1) with { CanRegister = false }] };
        await using var app = await CreateStudentAdminAppAsync(state);
        app.Configuration["Experience:Environment"] = "DEMO"; // Existing fixture policy is PROJECT.
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var result = await client.GetFromJsonAsync<JsonElement>("/api/v2/recommendations/activities?semesterId=FA26&limit=2");
        Assert.True(result.GetProperty("available").GetBoolean());
        Assert.Equal("BASIC", result.GetProperty("mode").GetString());
        Assert.False(result.GetProperty("personalizationAvailable").GetBoolean());
        Assert.Equal("SCORE_POLICY_UNAPPROVED", result.GetProperty("personalizationUnavailableReason").GetString());
        Assert.Equal("ELIGIBLE_UPCOMING_V1", result.GetProperty("ruleVersion").GetString());
        var items = result.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new[] { 1, 2 }, items.Select(x => x.GetProperty("activity").GetProperty("id").GetInt32()).ToArray());
        Assert.All(items, x => Assert.Equal(JsonValueKind.Null, x.GetProperty("radarSnapshot").ValueKind));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        Assert.False(await db.ApprovedExperiencePolicies.AnyAsync(x => x.Environment == "DEMO"));
        Assert.Equal(0, await db.ExperienceLedgerEffects.CountAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v2/recommendations/activities?semesterId=UNKNOWN")).StatusCode);
        state.Unavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/v2/recommendations/activities?semesterId=FA26")).StatusCode);
    }

    [Fact]
    public async Task Recommendations_ApprovedPolicyKeepsPersonalizedMode()
    {
        var start = DateTimeOffset.UtcNow.AddDays(2);
        var state = new StudentSourceState
        {
            Activities = [new(1, 1, "C1", "Club 1", "Activity", "Description", null,
            start, start.AddHours(1), "Campus", "UPCOMING", "REGISTER", true, true,
            "FA26", null, Guid.NewGuid(), null, null, [new PillarWeight("Academic", 1)])]
        };
        await using var app = await CreateStudentAdminAppAsync(state);
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var result = await client.GetFromJsonAsync<JsonElement>("/api/v2/recommendations/activities?semesterId=FA26");
        Assert.Equal("PERSONALIZED", result.GetProperty("mode").GetString());
        Assert.True(result.GetProperty("personalizationAvailable").GetBoolean());
        Assert.Equal("LOW_AXIS_UNROUNDED_START_ID_V1", result.GetProperty("ruleVersion").GetString());
        Assert.Equal(1, result.GetProperty("items").GetArrayLength());
    }
}
