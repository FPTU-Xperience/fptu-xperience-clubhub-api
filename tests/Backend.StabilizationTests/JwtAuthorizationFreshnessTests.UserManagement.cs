using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AuthService.Contracts;
using AuthService.Models;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;

namespace Backend.StabilizationTests;

public sealed partial class JwtAuthorizationFreshnessTests
{
    [Fact]
    public async Task UserManagement_DeleteSoftDisablesAndRevokesOldAccessToken()
    {
        await using var fixture = await AuthTestFixture.CreateAsync();
        var adminRole = new Role { Name = AuthRoles.Admin };
        var admin = new User
        {
            Username = "admin",
            FullName = "Admin",
            Email = "admin@fpt.edu.vn",
            IsActive = true,
            SecurityVersion = 1
        };
        fixture.Db.Roles.Add(adminRole);
        fixture.Db.Users.Add(admin);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = adminRole.Id });
        await fixture.Db.SaveChangesAsync();

        var oldMemberToken = fixture.TokenFactory.CreateToken(fixture.UserId, "testuser", "Test User",
            [AuthRoles.ClubMember], securityVersion: 1);
        var adminToken = fixture.TokenFactory.CreateToken(admin.Id, "admin", "Admin", [AuthRoles.Admin], securityVersion: 1);
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken.AccessToken);

        var response = await client.DeleteAsync($"/api/users/{fixture.UserId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("success").GetBoolean());
        var target = await fixture.Db.Users.AsNoTracking().SingleAsync(x => x.Id == fixture.UserId);
        Assert.False(target.IsActive);
        Assert.Equal(2, target.SecurityVersion);
        Assert.Single(await fixture.Db.UserRoles.Where(x => x.UserId == fixture.UserId).ToListAsync());

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/users/{fixture.UserId}")).StatusCode);
        Assert.Equal(2, (await fixture.Db.Users.AsNoTracking().SingleAsync(x => x.Id == fixture.UserId)).SecurityVersion);
        using var memberClient = fixture.CreateClient();
        memberClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldMemberToken.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await memberClient.GetAsync("/api/users/me")).StatusCode);
    }

    [Fact]
    public async Task UserManagement_DeleteCannotDeactivateSelf()
    {
        await using var fixture = await AuthTestFixture.CreateAsync();
        var role = await fixture.Db.Roles.SingleAsync();
        role.Name = AuthRoles.Admin;
        await fixture.Db.SaveChangesAsync();
        var token = fixture.TokenFactory.CreateToken(fixture.UserId, "testuser", "Test User",
            [AuthRoles.Admin], securityVersion: 1);
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/users/{fixture.UserId}")).StatusCode);
        Assert.True((await fixture.Db.Users.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task UserManagement_RoleAssignmentReplacesRoleAndRemovalRestoresClubMember()
    {
        await using var fixture = await AuthTestFixture.CreateAsync();
        var memberRole = await fixture.Db.Roles.SingleAsync();
        var managerRole = new Role { Name = AuthRoles.ClubManager };
        var adminRole = new Role { Name = AuthRoles.Admin };
        var admin = new User
        {
            Username = "admin",
            FullName = "Admin",
            Email = "admin@fpt.edu.vn",
            IsActive = true,
            SecurityVersion = 1
        };
        fixture.Db.Roles.AddRange(managerRole, adminRole);
        fixture.Db.Users.Add(admin);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = adminRole.Id });
        await fixture.Db.SaveChangesAsync();

        var oldMemberToken = fixture.TokenFactory.CreateToken(fixture.UserId, "testuser", "Test User",
            [AuthRoles.ClubMember], securityVersion: 1);
        var adminToken = fixture.TokenFactory.CreateToken(admin.Id, "admin", "Admin", [AuthRoles.Admin], securityVersion: 1);
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken.AccessToken);

        var assign = await client.PostAsJsonAsync($"/api/users/{fixture.UserId}/roles", new { roleId = managerRole.Id.ToString() });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        Assert.True((await assign.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("success").GetBoolean());
        Assert.Equal(managerRole.Id, (await fixture.Db.UserRoles.AsNoTracking().Where(x => x.UserId == fixture.UserId).ToListAsync()).Single().RoleId);
        Assert.Equal(2, (await fixture.Db.Users.AsNoTracking().SingleAsync(x => x.Id == fixture.UserId)).SecurityVersion);
        using var memberClient = fixture.CreateClient();
        memberClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldMemberToken.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await memberClient.GetAsync("/api/users/me")).StatusCode);

        var remove = await client.DeleteAsync($"/api/users/{fixture.UserId}/roles/{managerRole.Id}");
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.True((await remove.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("success").GetBoolean());
        Assert.Equal(memberRole.Id, (await fixture.Db.UserRoles.AsNoTracking().Where(x => x.UserId == fixture.UserId).ToListAsync()).Single().RoleId);
        Assert.Equal(3, (await fixture.Db.Users.AsNoTracking().SingleAsync(x => x.Id == fixture.UserId)).SecurityVersion);

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/users/{fixture.UserId}/roles/{memberRole.Id}")).StatusCode);
        Assert.Equal(3, (await fixture.Db.Users.AsNoTracking().SingleAsync(x => x.Id == fixture.UserId)).SecurityVersion);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/users/{fixture.UserId}/roles/{managerRole.Id}")).StatusCode);
    }

    [Fact]
    public async Task UserManagement_RoleAssignmentRejectsInvalidAndSelfChanges()
    {
        await using var fixture = await AuthTestFixture.CreateAsync();
        var role = await fixture.Db.Roles.SingleAsync();
        role.Name = AuthRoles.Admin;
        await fixture.Db.SaveChangesAsync();
        var memberRole = new Role { Name = AuthRoles.ClubMember };
        fixture.Db.Roles.Add(memberRole);
        await fixture.Db.SaveChangesAsync();
        var token = fixture.TokenFactory.CreateToken(fixture.UserId, "testuser", "Test User",
            [AuthRoles.Admin], securityVersion: 1);
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/users/{fixture.UserId}/roles", new { roleId = "not-a-number" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/users/{fixture.UserId}/roles", new { roleId = memberRole.Id.ToString() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.DeleteAsync($"/api/users/{fixture.UserId}/roles/{role.Id}")).StatusCode);
        Assert.Equal(role.Id, (await fixture.Db.UserRoles.AsNoTracking().SingleAsync()).RoleId);
    }

    [Fact]
    public async Task UserManagement_RoleAssignmentRejectsAccountWithMultipleCurrentRoles()
    {
        await using var fixture = await AuthTestFixture.CreateAsync();
        var managerRole = new Role { Name = AuthRoles.ClubManager };
        var adminRole = new Role { Name = AuthRoles.Admin };
        var admin = new User
        {
            Username = "admin",
            FullName = "Admin",
            Email = "admin@fpt.edu.vn",
            IsActive = true,
            SecurityVersion = 1
        };
        fixture.Db.Roles.AddRange(managerRole, adminRole);
        fixture.Db.Users.Add(admin);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.UserRoles.AddRange(
            new UserRole { UserId = admin.Id, RoleId = adminRole.Id },
            new UserRole { UserId = fixture.UserId, RoleId = managerRole.Id });
        await fixture.Db.SaveChangesAsync();

        var adminToken = fixture.TokenFactory.CreateToken(admin.Id, "admin", "Admin",
            [AuthRoles.Admin], securityVersion: 1);
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken.AccessToken);

        var response = await client.PostAsJsonAsync($"/api/users/{fixture.UserId}/roles", new { roleId = managerRole.Id });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2, await fixture.Db.UserRoles.CountAsync(x => x.UserId == fixture.UserId));
        Assert.Equal(1, (await fixture.Db.Users.AsNoTracking().SingleAsync(x => x.Id == fixture.UserId)).SecurityVersion);
    }
}
