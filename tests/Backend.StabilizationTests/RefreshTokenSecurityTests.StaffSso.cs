using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using AuthService.Contracts;
using AuthService.Data;
using AuthService.Models;
using AuthService.Services;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Backend.StabilizationTests;

public sealed partial class RefreshTokenSecurityTests
{
    private const string AdminOrigin = "http://localhost:5174";
    private const string ClubOrigin = "http://localhost:5173";
    private static readonly string Verifier = new('v', 43);
    private static string Challenge => WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)));

    private static async Task<AuthResponse> StaffSession(RefreshTestFixture fixture)
    {
        var membership = await fixture.Db.UserRoles.SingleAsync(x => x.UserId == fixture.UserId);
        fixture.Db.UserRoles.Remove(membership);
        var role = new Role { Name = AuthRoles.StudentAffairsAdmin };
        fixture.Db.Roles.Add(role);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.UserRoles.Add(new UserRole { UserId = fixture.UserId, RoleId = role.Id });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var token = await fixture.RefreshTokenService.CreateRefreshTokenAsync(fixture.UserId);
        var loaded = await fixture.RefreshTokenService.GetRefreshTokenAsync(token.RawToken!);
        return fixture.RefreshTokenService.CreateAuthResponse(loaded!.User, token);
    }

    private static HttpClient StaffClient(RefreshTestFixture fixture, AuthResponse session, string origin = AdminOrigin)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        client.DefaultRequestHeaders.Add("Origin", origin);
        return client;
    }

    private static async Task<string> Ticket(HttpClient source, AuthResponse session, string target = ClubOrigin)
    {
        var response = await source.PostAsJsonAsync("/api/auth/sso/tickets", new { refreshToken = session.RefreshToken, targetOrigin = target, codeChallenge = Challenge });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var payload = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return payload.GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task Production_LocalEmailLoginRequiresPrivateProxyKey()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync("Production");
        await StaffSession(fixture);
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", AdminOrigin);
        var denied = await client.PostAsJsonAsync("/api/auth/email-login", new { email = "refresh@fpt.edu.vn", password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        var result = await client.PostAsJsonAsync("/api/auth/email-login", new { email = "refresh@fpt.edu.vn", password = EmailPassword });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var identity = await result.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.Contains(AuthRoles.StudentAffairsAdmin, identity!.User.Roles);
    }

    [Fact]
    public async Task Production_LocalSsoUsesSeparateOriginPair()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync("Production");
        fixture.App.Configuration["Sso:AllowLocalOrigins"] = "false";
        var session = await StaffSession(fixture);
        using var source = StaffClient(fixture, session);
        var disabled = await source.PostAsJsonAsync("/api/auth/sso/tickets", new { refreshToken = session.RefreshToken, targetOrigin = ClubOrigin, codeChallenge = Challenge });
        Assert.Equal(HttpStatusCode.BadRequest, disabled.StatusCode);
        fixture.App.Configuration["Sso:AllowLocalOrigins"] = "true";
        var mixed = await source.PostAsJsonAsync("/api/auth/sso/tickets", new { refreshToken = session.RefreshToken, targetOrigin = "https://fptux-clubhub-ui.pages.dev", codeChallenge = Challenge });
        Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);
        var code = await Ticket(source, session);
        using var target = fixture.CreateClient();
        target.DefaultRequestHeaders.Add("Origin", ClubOrigin);
        var result = await target.PostAsJsonAsync("/api/auth/sso/redeem", new { code, codeVerifier = Verifier });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Theory]
    [InlineData(AdminOrigin, ClubOrigin)]
    [InlineData(ClubOrigin, AdminOrigin)]
    public async Task StaffSso_RedeemsOnce_WithIndependentRefreshChainInSameFamily(string sourceOrigin, string targetOrigin)
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var session = await StaffSession(fixture);
        using var source = StaffClient(fixture, session, sourceOrigin);
        using var target = fixture.CreateClient();
        target.DefaultRequestHeaders.Add("Origin", targetOrigin);
        var code = await Ticket(source, session, targetOrigin);
        var response = await target.PostAsJsonAsync("/api/auth/sso/redeem", new { code, codeVerifier = Verifier });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var branch = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.NotEqual(session.RefreshToken, branch.RefreshToken);
        Assert.Contains(AuthRoles.StudentAffairsAdmin, branch.User.Roles);
        var tokens = await fixture.Db.RefreshTokens.AsNoTracking().ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.All(tokens, token => Assert.True(token.IsActive));
        Assert.Single(tokens.Select(x => x.FamilyId).Distinct());
        var replay = await target.PostAsJsonAsync("/api/auth/sso/redeem", new { code, codeVerifier = Verifier });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        // Rotating source must not revoke the other app's independent token chain.
        var rotated = await source.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var status = await target.PostAsJsonAsync("/api/auth/sso/status", new { refreshToken = branch.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, status.StatusCode);
    }

    [Theory]
    [InlineData("https://attacker.example")]
    [InlineData("http://localhost:5174")]
    public async Task StaffSso_RejectsUntrustedOrSameOriginTarget(string targetOrigin)
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var session = await StaffSession(fixture);
        using var source = StaffClient(fixture, session);
        var response = await source.PostAsJsonAsync("/api/auth/sso/tickets", new { refreshToken = session.RefreshToken, targetOrigin, codeChallenge = Challenge });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StaffSso_RejectsNonStaffAndUntrustedSourceOrigin()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var token = await fixture.RefreshTokenService.CreateRefreshTokenAsync(fixture.UserId);
        var user = (await fixture.RefreshTokenService.GetRefreshTokenAsync(token.RawToken!))!.User;
        var member = fixture.RefreshTokenService.CreateAuthResponse(user, token);
        using var source = StaffClient(fixture, member);
        var denied = await source.PostAsJsonAsync("/api/auth/sso/tickets", new { refreshToken = member.RefreshToken, targetOrigin = ClubOrigin, codeChallenge = Challenge });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        // Use a fresh fixture so the auth security-stamp cache cannot retain a seeded role.
        await using var staffFixture = await RefreshTestFixture.CreateAsync();
        var staff = await StaffSession(staffFixture);
        using var badOrigin = StaffClient(staffFixture, staff, "https://attacker.example");
        var rejected = await badOrigin.PostAsJsonAsync("/api/auth/sso/tickets", new { refreshToken = staff.RefreshToken, targetOrigin = ClubOrigin, codeChallenge = Challenge });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task StaffSso_RequiresCorrectPkceAndTargetOrigin()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var session = await StaffSession(fixture);
        using var source = StaffClient(fixture, session);
        var code = await Ticket(source, session);
        var wrongTarget = await source.PostAsJsonAsync("/api/auth/sso/redeem", new { code, codeVerifier = Verifier });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongTarget.StatusCode);
        using var target = fixture.CreateClient(); target.DefaultRequestHeaders.Add("Origin", ClubOrigin);
        var wrongVerifier = await target.PostAsJsonAsync("/api/auth/sso/redeem", new { code, codeVerifier = new string('x', 43) });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongVerifier.StatusCode);
        var correct = await target.PostAsJsonAsync("/api/auth/sso/redeem", new { code, codeVerifier = Verifier });
        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("locked")]
    [InlineData("inactive")]
    [InlineData("version")]
    [InlineData("role")]
    public async Task StaffSso_RechecksExpiryAndAccountStateAtRedemption(string condition)
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var session = await StaffSession(fixture);
        using var source = StaffClient(fixture, session);
        var code = await Ticket(source, session);
        fixture.Db.ChangeTracker.Clear();
        var ticket = await fixture.Db.StaffSsoTickets.SingleAsync();
        var user = await fixture.Db.Users.SingleAsync(x => x.Id == fixture.UserId);
        if (condition == "expired") ticket.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        if (condition == "locked") user.IsLocked = true;
        if (condition == "inactive") user.IsActive = false;
        if (condition == "version") user.SecurityVersion++;
        if (condition == "role") fixture.Db.UserRoles.RemoveRange(await fixture.Db.UserRoles.ToListAsync());
        await fixture.Db.SaveChangesAsync();
        using var target = fixture.CreateClient(); target.DefaultRequestHeaders.Add("Origin", ClubOrigin);
        var response = await target.PostAsJsonAsync("/api/auth/sso/redeem", new { code, codeVerifier = Verifier });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StaffSso_LogoutRevokesBothAppBranches_PendingTickets_AndCachedRotation()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var initial = await StaffSession(fixture);
        var rotation = await fixture.RefreshTokenService.RotateRefreshTokenAsync(initial.RefreshToken!, "127.0.0.1");
        var session = rotation.Response!;
        using var source = StaffClient(fixture, session);
        using var target = fixture.CreateClient(); target.DefaultRequestHeaders.Add("Origin", ClubOrigin);
        var code = await Ticket(source, session);
        var response = await target.PostAsJsonAsync("/api/auth/sso/redeem", new { code, codeVerifier = Verifier });
        var branch = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        var pending = await Ticket(source, session);
        var logout = await source.PostAsJsonAsync("/api/auth/logout", new { refreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var status = await target.PostAsJsonAsync("/api/auth/sso/status", new { refreshToken = branch.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, status.StatusCode);
        var oldRotation = await target.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = initial.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRotation.StatusCode);
        var pendingRedeem = await target.PostAsJsonAsync("/api/auth/sso/redeem", new { code = pending, codeVerifier = Verifier });
        Assert.Equal(HttpStatusCode.Unauthorized, pendingRedeem.StatusCode);
        Assert.All(await fixture.Db.RefreshTokens.AsNoTracking().ToListAsync(), token => Assert.True(token.IsRevoked));
    }

    [Fact]
    public async Task Logout_CannotRevokeAnotherUsersRefreshFamily()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var session = await StaffSession(fixture);
        var other = new User { Username = "other", FullName = "Other", Email = "other@example.test" };
        fixture.Db.Users.Add(other); await fixture.Db.SaveChangesAsync();
        var foreign = await fixture.RefreshTokenService.CreateRefreshTokenAsync(other.Id);
        using var client = StaffClient(fixture, session);
        var result = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = foreign.RawToken });
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.True((await fixture.Db.RefreshTokens.AsNoTracking().SingleAsync(x => x.Id == foreign.Id)).IsActive);
    }

    [Fact]
    public async Task StaffSso_ConcurrentRedemptionCreatesExactlyOneBranch()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var session = await StaffSession(fixture);
        using var source = StaffClient(fixture, session);
        var code = await Ticket(source, session);
        using var first = fixture.CreateClient(); first.DefaultRequestHeaders.Add("Origin", ClubOrigin);
        using var second = fixture.CreateClient(); second.DefaultRequestHeaders.Add("Origin", ClubOrigin);
        var results = await Task.WhenAll(first.PostAsJsonAsync("/api/auth/sso/redeem", new { code, codeVerifier = Verifier }),
            second.PostAsJsonAsync("/api/auth/sso/redeem", new { code, codeVerifier = Verifier }));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Unauthorized);
        Assert.Equal(2, await fixture.Db.RefreshTokens.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Refresh_CachedRotationCannotBypassAccountLockOrDeactivation(bool locked)
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var session = await StaffSession(fixture);
        var rotated = await fixture.RefreshTokenService.RotateRefreshTokenAsync(session.RefreshToken!, null);
        Assert.Equal(RefreshTokenStatus.Success, rotated.Status);
        var user = await fixture.Db.Users.SingleAsync(x => x.Id == fixture.UserId);
        if (locked) user.IsLocked = true; else user.IsActive = false;
        await fixture.Db.SaveChangesAsync(); fixture.Db.ChangeTracker.Clear();
        var rejected = await fixture.RefreshTokenService.RotateRefreshTokenAsync(session.RefreshToken!, null);
        Assert.Equal(RefreshTokenStatus.AccountNotAllowed, rejected.Status);
    }

    [Fact]
    public void StaffSso_SqlServerMigrationSnapshotMatchesCurrentModel()
    {
        using var db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlServer("Server=localhost;Database=NotConnected;Integrated Security=true;TrustServerCertificate=true").Options);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
