using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClubReportHub.Shared.Auth;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task ClubContract_LegacyAdminUpdateInvalidatesAnExistingPublicEditorVersion()
    {
        await using var app = await CreateClubManagerAppAsync();
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        var before = await manager.GetFromJsonAsync<JsonElement>("/api/clubs/1");
        var version = before.GetProperty("version").GetGuid();
        using var admin = CreateAdminContractClient(app, 900, AuthRoles.Admin);
        var updated = await admin.PutAsJsonAsync("/api/clubs/1", new { name = "Updated by administration", isActive = true });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var after = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(version, after.GetProperty("version").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PatchAsJsonAsync("/api/clubs/1/public-profile",
            new { name = "Stale browser edit", expectedVersion = version })).StatusCode);
        Assert.Equal("Updated by administration", (await manager.GetFromJsonAsync<JsonElement>("/api/clubs/1")).GetProperty("name").GetString());
    }
}
