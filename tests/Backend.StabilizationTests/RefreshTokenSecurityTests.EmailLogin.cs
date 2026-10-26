using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AuthService.Contracts;
using AuthService.Endpoints;
using AuthService.Models;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.StabilizationTests;

public sealed partial class RefreshTokenSecurityTests
{
    [Theory]
    [InlineData(AuthRoles.Admin, HttpStatusCode.NoContent)]
    [InlineData(AuthRoles.StudentAffairsAdmin, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.ClubMember, HttpStatusCode.Forbidden)]
    public async Task PasswordEndpointRequiresAdministrator(string roleName, HttpStatusCode expected)
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        (await fixture.Db.Roles.SingleAsync()).Name = roleName;
        await fixture.Db.SaveChangesAsync();
        using var client = fixture.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/email-login", new { email = "refresh@fpt.edu.vn", password = EmailPassword });
        var credentials = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        var result = await client.PutAsJsonAsync($"/api/users/{fixture.UserId}/password", new { password = "new-test-password-2026" });
        Assert.Equal(expected, result.StatusCode);
        if (expected == HttpStatusCode.NoContent)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(credentials.RefreshToken!))).StatusCode);
    }

    [Fact]
    public async Task EmailLoginRateLimitsRepeatedFailures()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        using var client = fixture.CreateClient();
        for (var attempt = 0; attempt < 10; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/email-login", new { email = "refresh@fpt.edu.vn", password = "wrong-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/auth/email-login", new { email = "refresh@fpt.edu.vn", password = "wrong-password" })).StatusCode);
        Assert.Empty(await fixture.Db.RefreshTokens.ToArrayAsync());
    }

    [Theory]
    [InlineData("unknown@fpt.edu.vn", "wrong-password")]
    [InlineData("refresh@fpt.edu.vn", "wrong-password")]
    [InlineData("refresh@fpt.edu.vn", "")]
    public async Task EmailLogin_RejectsInvalidCredentialsWithoutCreatingSession(string email, string password)
    {
        await using var fixture = await RefreshTestFixture.CreateAsync("Production");
        using var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/email-login", new { email, password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await fixture.Db.RefreshTokens.ToListAsync());
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("inactive")]
    [InlineData("no-password")]
    [InlineData("corrupt-hash")]
    public async Task EmailLogin_RejectsUnavailableAccounts(string state)
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var user = await fixture.Db.Users.SingleAsync();
        user.IsLocked = state == "locked";
        user.IsActive = state != "inactive";
        if (state == "no-password") user.PasswordHash = null;
        if (state == "corrupt-hash") user.PasswordHash = "not-a-password-hash";
        await fixture.Db.SaveChangesAsync();
        using var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/email-login", new { email = user.Email, password = EmailPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(AuthRoles.Admin, AdminOrigin, ClubOrigin)]
    [InlineData(AuthRoles.Admin, ClubOrigin, AdminOrigin)]
    [InlineData(AuthRoles.StudentAffairsAdmin, AdminOrigin, ClubOrigin)]
    [InlineData(AuthRoles.StudentAffairsAdmin, ClubOrigin, AdminOrigin)]
    public async Task EmailLogin_Sso_BranchesRefresh_AndLogoutRevokesBothSites(string roleName, string sourceOrigin, string targetOrigin)
    {
        await using var fixture = await RefreshTestFixture.CreateAsync("Production");
        var role = await fixture.Db.Roles.SingleAsync();
        role.Name = roleName;
        await fixture.Db.SaveChangesAsync();
        using var source = fixture.CreateClient();
        source.DefaultRequestHeaders.Add("Origin", sourceOrigin);
        var login = await source.PostAsJsonAsync("/api/auth/email-login", new { email = " REFRESH@FPT.EDU.VN ", password = EmailPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        source.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var code = await Ticket(source, session, targetOrigin);
        using var target = fixture.CreateClient();
        target.DefaultRequestHeaders.Add("Origin", targetOrigin);
        var redeem = await target.PostAsJsonAsync("/api/auth/sso/redeem", new { code, codeVerifier = Verifier });
        Assert.Equal(HttpStatusCode.OK, redeem.StatusCode);
        var peer = (await redeem.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.NotEqual(session.RefreshToken, peer.RefreshToken);
        Assert.Contains(roleName, peer.User.Roles);
        var rotated = await target.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(peer.RefreshToken!));
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var logout = await source.PostAsJsonAsync("/api/auth/logout", new RefreshTokenRequest(session.RefreshToken!));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var after = await rotated.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(after!.RefreshToken!))).StatusCode);
    }

    [Fact]
    public async Task PasswordSetup_HashesPassword_RevokesPriorSessions_AndDoesNotGrantRoles()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var token = await fixture.RefreshTokenService.CreateRefreshTokenAsync(fixture.UserId);
        var user = await fixture.Db.Users.SingleAsync();
        const string replacement = "new-test-password-2026";
        await EmailLoginEndpoints.SetPassword(user, replacement, fixture.Db, fixture.RefreshTokenService,
            fixture.App.Services.GetRequiredService<IMemoryCache>());
        Assert.NotEqual(replacement, user.PasswordHash);
        Assert.True(user.SecurityVersion > 1);
        Assert.Equal(AuthRoles.ClubMember, (await fixture.Db.UserRoles.Include(x => x.Role).SingleAsync()).Role.Name);
        Assert.Equal(AuthService.Services.RefreshTokenStatus.Invalid,
            (await fixture.RefreshTokenService.RotateRefreshTokenAsync(token.RawToken!, null)).Status);
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"/api/users/{user.Id}/password", new { password = replacement })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/email-login", new { email = user.Email, password = replacement })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/email-login", new { email = user.Email, password = EmailPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/auth/dev-login", new { email = user.Email })).StatusCode);
    }
}
