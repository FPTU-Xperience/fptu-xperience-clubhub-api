using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Backend.StabilizationTests;

public sealed partial class RefreshTokenSecurityTests
{
    private static async Task<(AuthResponse Auth, string Cookie)> BrowserLogin(RefreshTestFixture fixture)
    {
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", ClubOrigin);
        var response = await client.PostAsJsonAsync("/api/auth/email-login", new { email = "refresh@fpt.edu.vn", password = EmailPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);
        return ((await response.Content.ReadFromJsonAsync<AuthResponse>())!, setCookie.Split(';')[0]);
    }

    private static HttpClient BrowserClient(RefreshTestFixture fixture, string cookie, string origin = ClubOrigin)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", origin);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        client.DefaultRequestHeaders.Add("X-FPTUX-Session", "1");
        return client;
    }

    [Fact]
    public async Task BrowserSession_ProductionCookieIsHostOnlySecureAndHttpOnly()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync("Production");
        fixture.App.Configuration["Sso:ClubHubOrigin"] = ClubOrigin;
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", ClubOrigin);
        var login = await client.PostAsJsonAsync("/api/auth/email-login", new { email = "refresh@fpt.edu.vn", password = EmailPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-fptux-session=", cookie);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=none", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BrowserSession_BothSitesCheckCookie_AndRestoreIndependentRefreshBranches()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var (auth, cookie) = await BrowserLogin(fixture);
        using var club = BrowserClient(fixture, cookie);
        using var admin = BrowserClient(fixture, cookie, AdminOrigin);
        var clubStatus = await club.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var adminStatus = await admin.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal(clubStatus.GetProperty("browserSessionId").GetString(), adminStatus.GetProperty("browserSessionId").GetString());
        var restored = await admin.PostAsync("/api/auth/session", null);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        var body = await restored.Content.ReadFromJsonAsync<JsonElement>();
        var branch = body.GetProperty("session").Deserialize<AuthResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(auth.User.Id, branch.User.Id);
        Assert.NotEqual(auth.RefreshToken, branch.RefreshToken);
        var sourceToken = await fixture.RefreshTokenService.GetRefreshTokenAsync(auth.RefreshToken!);
        var targetToken = await fixture.RefreshTokenService.GetRefreshTokenAsync(branch.RefreshToken!);
        Assert.Equal(sourceToken!.FamilyId, targetToken!.FamilyId);
        Assert.Equal("no-store", restored.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task BrowserSession_AnonymousOrTamperedCookieDoesNotRestoreOrRedirect()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var (auth, _) = await BrowserLogin(fixture);
        foreach (var cookie in new[] { "fptux-session=invalid", "fptux-session=" + auth.AccessToken, "unrelated=value" })
        {
            using var client = BrowserClient(fixture, cookie);
            var status = await client.GetAsync("/api/auth/session");
            Assert.Equal(HttpStatusCode.NoContent, status.StatusCode);
            Assert.Null(status.Headers.Location);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/session", null)).StatusCode);
        }
    }

    [Fact]
    public async Task BrowserSession_RejectsUntrustedOriginAndMissingCsrfHeader()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var (_, cookie) = await BrowserLogin(fixture);
        using var foreign = BrowserClient(fixture, cookie, "https://attacker.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await foreign.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await foreign.PostAsync("/api/auth/session", null)).StatusCode);
        using var simple = BrowserClient(fixture, cookie);
        simple.DefaultRequestHeaders.Remove("X-FPTUX-Session");
        Assert.Equal(HttpStatusCode.Forbidden, (await simple.PostAsync("/api/auth/session", null)).StatusCode);
    }

    [Fact]
    public async Task BrowserSession_SurvivesRefreshRotation_AndExpiresOnFamilyLogout()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var (auth, cookie) = await BrowserLogin(fixture);
        var rotation = await fixture.RefreshTokenService.RotateRefreshTokenAsync(auth.RefreshToken!, null);
        using var client = BrowserClient(fixture, cookie);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/session")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotation.Response!.AccessToken);
        var logout = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = rotation.Response.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), value => value.StartsWith("fptux-session=;", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/session", null)).StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BrowserSession_RejectsLockedAccountsOrChangedSecurityVersion(bool locked)
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var (_, cookie) = await BrowserLogin(fixture);
        var user = await fixture.Db.Users.SingleAsync(x => x.Id == fixture.UserId);
        if (locked) user.IsLocked = true;
        else user.SecurityVersion++;
        await fixture.Db.SaveChangesAsync();
        using var client = BrowserClient(fixture, cookie);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/session", null)).StatusCode);
    }
}
