using System.Net.Http.Json;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubService.Data;
using ClubService.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task Governance_CurrentDisplayNamesRefreshWithoutChangingImmutableConfigurationOrUnassignedProfiles()
    {
        var state = new GovernanceState();
        state.Accounts[104] = state.Accounts[104] with { Profile = JsonSerializer.SerializeToElement(new { profile = new { displayName = "Tên hiện tại" } }) };
        await using var app = await GovernanceApp(state);
        using var owner = FormationClient(app, 100, AuthRoles.ClubMember, "HAN");
        await InitializeGovernance(owner);
        var term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        var group = term.GetProperty("configuration").GetProperty("groups").EnumerateArray().Single(row => row.GetProperty("presetCode").GetString() == "CONTENT");
        await using var scope = app.Services.CreateAsyncScope();
        var membership = await scope.ServiceProvider.GetRequiredService<ClubDbContext>().ClubMemberships.SingleAsync(row => row.UserId == 104);
        await GovernanceWrite(owner, GovernanceUrl + "/assignment-changes", new GovernanceAssignmentInput(term.GetProperty("version").GetGuid(), "Assign current member", "ADD", group.GetProperty("id").GetGuid(), [membership.Id]));
        term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        var frozenConfiguration = term.GetProperty("configuration").GetRawText();
        var version = term.GetProperty("version").GetGuid();
        Assert.Equal("Tên hiện tại", term.GetProperty("currentMemberNames").EnumerateArray().Single(row => row.GetProperty("userId").GetInt32() == 104).GetProperty("displayName").GetString());
        Assert.DoesNotContain(term.GetProperty("currentMemberNames").EnumerateArray(), row => row.GetProperty("userId").GetInt32() == 106);
        state.Accounts[104] = state.Accounts[104] with { Profile = JsonSerializer.SerializeToElement(new { profile = new { displayName = "Tên vừa cập nhật" } }) };
        using var member = FormationClient(app, 104, AuthRoles.ClubMember, "HAN");
        var refreshed = await member.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        Assert.Equal("Tên vừa cập nhật", refreshed.GetProperty("currentMemberNames").EnumerateArray().Single(row => row.GetProperty("userId").GetInt32() == 104).GetProperty("displayName").GetString());
        Assert.Equal(frozenConfiguration, refreshed.GetProperty("configuration").GetRawText());
        Assert.Equal(version, refreshed.GetProperty("version").GetGuid());
        Assert.DoesNotContain("@example.invalid", refreshed.GetProperty("currentMemberNames").GetRawText());
    }
}
