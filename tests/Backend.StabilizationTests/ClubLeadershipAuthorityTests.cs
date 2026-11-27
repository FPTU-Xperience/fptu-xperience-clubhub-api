using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubService.Data;
using ClubService.Endpoints;
using ClubService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task Governance_CurrentSecretaryHasLeadershipAndHostAuthorityWithoutEventOrTreasurerDuty()
    {
        var state = new GovernanceState(); await using var app = await GovernanceApp(state);
        using var owner = FormationClient(app, 100, AuthRoles.ClubMember, "HAN");
        using var secretary = FormationClient(app, 104, AuthRoles.ClubMember, "HAN");
        using var regular = FormationClient(app, 106, AuthRoles.ClubMember, "HAN");
        await InitializeGovernance(owner);
        var term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        var group = term.GetProperty("configuration").GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("presetCode").GetString() == "SECRETARY");
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var id = await db.ClubMemberships.Where(x => x.UserId == 104).Select(x => x.Id).SingleAsync();
        await GovernanceWrite(owner, GovernanceUrl + "/assignment-changes", new GovernanceAssignmentInput(term.GetProperty("version").GetGuid(), "Assign current secretary", "ADD", group.GetProperty("id").GetGuid(), [id]));
        var access = (await secretary.GetFromJsonAsync<JsonElement[]>("/api/clubs/me/access"))!.Single(x => x.GetProperty("clubId").GetInt32() == 1);
        Assert.Equal(ClubGovernancePolicy.Delegable.Order(), access.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()).Order());
        Assert.False(access.GetProperty("isTreasurer").GetBoolean());
        Assert.True(access.GetProperty("isSecretary").GetBoolean());
        var snapshot = JsonSerializer.Deserialize<ClubReportHub.Shared.Auth.ClubAccessSnapshot>(access.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(snapshot.CanManage);
        var roles = await secretary.GetFromJsonAsync<JsonElement>("/api/clubs/1/roles?includeStaffing=true");
        Assert.True(roles.GetProperty("capabilities").GetProperty("canManageRoles").GetBoolean());
        var candidates = roles.GetProperty("items").EnumerateArray().ToDictionary(x => x.GetProperty("userId").GetInt32(), x => x.GetProperty("staffingKind").GetString());
        Assert.Equal("HOST", candidates[100]); Assert.Equal("HOST", candidates[104]); Assert.Equal("STAFF", candidates[106]);
        Assert.Equal(HttpStatusCode.Forbidden, (await regular.GetAsync("/api/clubs/1/roles?includeStaffing=true")).StatusCode);
        state.Current = false;
        Assert.Equal(HttpStatusCode.Forbidden, (await secretary.GetAsync("/api/clubs/1/roles?includeStaffing=true")).StatusCode);
    }
}
