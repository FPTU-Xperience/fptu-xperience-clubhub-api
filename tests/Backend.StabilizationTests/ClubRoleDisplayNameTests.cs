using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task Roles_DisplayNamesUseLiveProfilesForAuthorizedPageAndExcludePrivateFields()
    {
        var calls = 0; int[] requested = []; var displayName = "Tên hiển thị hiện tại"; var unavailable = false;
        await using var app = await CreateRolesAppAsync(profiles: request =>
        {
            calls++;
            requested = request.Content!.ReadFromJsonAsync<int[]>().GetAwaiter().GetResult()!;
            if (unavailable) return new(HttpStatusCode.ServiceUnavailable);
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(requested.Select(id => new InternalUserProfile(id, "username-" + id,
                    "private@example.test", "HAN", true, false,
                    JsonSerializer.SerializeToElement(new { profile = new { displayName }, personal = new { phoneNumber = "private-phone" } }))))
            };
        });
        using var outsider = CreateAdminContractClient(app, 999, AuthRoles.ClubMember);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/api/clubs/1/roles")).StatusCode);
        Assert.Equal(0, calls);
        using var owner = CreateAdminContractClient(app, 100, AuthRoles.ClubMember);
        var data = await owner.GetFromJsonAsync<JsonElement>("/api/clubs/1/roles?page=2&pageSize=1");
        var row = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(new[] { row.GetProperty("userId").GetInt32() }, requested);
        Assert.Equal(displayName, row.GetProperty("displayName").GetString());
        Assert.DoesNotContain("private@example.test", data.GetRawText());
        Assert.DoesNotContain("private-phone", data.GetRawText());
        Assert.DoesNotContain("username-", data.GetRawText());
        displayName = "Tên vừa cập nhật";
        using var member = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        data = await member.GetFromJsonAsync<JsonElement>("/api/clubs/1/roles?page=2&pageSize=1");
        Assert.Equal(displayName, Assert.Single(data.GetProperty("items").EnumerateArray()).GetProperty("displayName").GetString());
        unavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await member.GetAsync("/api/clubs/1/roles")).StatusCode);
    }
}
