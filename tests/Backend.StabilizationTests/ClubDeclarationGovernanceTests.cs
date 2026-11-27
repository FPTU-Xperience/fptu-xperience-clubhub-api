using System.Net.Http.Json;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubService.Data;
using ClubService.Endpoints;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task Governance_DeclarationAuthorityUsesCurrentSecretaryOrExplicitDelegationAndRevokesImmediately()
    {
        var state = new GovernanceState();
        await using var app = await GovernanceApp(state);
        using var owner = FormationClient(app, 100, AuthRoles.ClubMember, "HAN");
        using var secretary = FormationClient(app, 104, AuthRoles.ClubMember, "HAN");
        using var member = FormationClient(app, 106, AuthRoles.ClubMember, "HAN");
        var catalog = await owner.GetFromJsonAsync<JsonElement>("/api/clubs/1/governance/permission-catalog");
        Assert.Contains(catalog.GetProperty("permissions").EnumerateArray(), p => p.GetProperty("code").GetString() == "club.declarations.process");
        Assert.Empty(catalog.GetProperty("unsupportedCapabilities").EnumerateArray());
        await InitializeGovernance(owner);
        var term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        var groups = term.GetProperty("configuration").GetProperty("groups").EnumerateArray().ToArray();
        var secretaryGroup = groups.Single(g => g.GetProperty("presetCode").GetString() == "SECRETARY").GetProperty("id").GetGuid();
        var contentGroup = groups.Single(g => g.GetProperty("presetCode").GetString() == "CONTENT").GetProperty("id").GetGuid();
        await using var scope = app.Services.CreateAsyncScope();
        var members = await scope.ServiceProvider.GetRequiredService<ClubDbContext>().ClubMemberships.ToDictionaryAsync(x => x.UserId, x => x.Id);
        term = await GovernanceWrite(owner, GovernanceUrl + "/assignment-changes", new GovernanceAssignmentInput(term.GetProperty("version").GetGuid(), "Assign secretary", "ADD", secretaryGroup, [members[104]]));
        async Task<bool> Process(HttpClient client)
        {
            var access = (await client.GetFromJsonAsync<JsonElement[]>("/api/clubs/me/access"))!.Single(x => x.GetProperty("clubId").GetInt32() == 1);
            return access.TryGetProperty("capabilities", out var permissions) && permissions.ValueKind == JsonValueKind.Array
                && permissions.EnumerateArray().Any(x => x.GetString() == "club.declarations.process");
        }
        Assert.True(await Process(secretary));
        Assert.False(await Process(member));
        term = await GovernanceWrite(owner, GovernanceUrl + "/groups/" + contentGroup,
            new GovernanceGroupInput(term.GetProperty("version").GetGuid(), "Delegate declaration processing", PermissionCodes: ["club.declarations.process"]), method: "PATCH");
        term = await GovernanceWrite(owner, GovernanceUrl + "/assignment-changes", new GovernanceAssignmentInput(term.GetProperty("version").GetGuid(), "Assign delegated reviewer", "ADD", contentGroup, [members[106]]));
        Assert.True(await Process(member));
        term = await GovernanceWrite(owner, GovernanceUrl + "/assignment-changes", new GovernanceAssignmentInput(term.GetProperty("version").GetGuid(), "Remove delegated reviewer", "REMOVE", contentGroup, [members[106]]));
        Assert.False(await Process(member));
        state.Current = false;
        Assert.False(await Process(secretary));
    }
}
