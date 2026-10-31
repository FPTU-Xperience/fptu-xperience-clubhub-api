using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Contracts;
using AuthService.Data;
using AuthService.Endpoints;
using AuthService.Extensions;
using AuthService.Models;
using AuthService.Services;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.StabilizationTests;

public sealed class AuthSessionContractTests
{
    private const string Password = "StudentPassword!2026";

    [Theory]
    [InlineData("locked", false)]
    [InlineData("locked", true)]
    [InlineData("inactive", false)]
    [InlineData("inactive", true)]
    public async Task Refresh_RejectsIneligibleAccountBeforeRotationAndCachedReplay(string change, bool cached)
    {
        await using var fixture = await Fixture.CreateAsync();
        var login = await fixture.RegisterAndLogin("student");
        using var client = fixture.CreateClient();
        if (cached) Assert.Equal(HttpStatusCode.OK, (await Refresh(client, login.RefreshToken!)).StatusCode);
        var before = (await fixture.Tokens(login.User.Id)).Length;
        await fixture.UpdateUser(login.User.Id, user =>
        {
            if (change == "locked") user.IsLocked = true;
            else user.IsActive = false;
        });

        var response = await Refresh(client, login.RefreshToken!);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, (await fixture.Tokens(login.User.Id)).Length);
    }

    [Theory]
    [InlineData("family")]
    [InlineData("version")]
    [InlineData("role")]
    [InlineData("logout")]
    public async Task Refresh_CachedRotationCannotRestoreInvalidatedSession(string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var login = await fixture.RegisterAndLogin("student");
        using var client = fixture.CreateClient();
        var rotated = await (await Refresh(client, login.RefreshToken!)).Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(rotated);
        if (change == "family")
        {
            await using var scope = fixture.App.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RefreshTokenService>().RevokeForUserAsync(login.User.Id);
        }
        else if (change == "logout")
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotated.AccessToken);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/logout", new RefreshTokenRequest(rotated.RefreshToken!))).StatusCode);
            client.DefaultRequestHeaders.Authorization = null;
        }
        else
        {
            await fixture.UpdateUser(login.User.Id, user =>
            {
                if (change == "version") user.SecurityVersion++;
                else user.UserRoles.Single().Role.Name = AuthRoles.ClubManager;
            });
        }
        var before = (await fixture.Tokens(login.User.Id)).Length;

        var response = await Refresh(client, login.RefreshToken!);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.Equal(before, (await fixture.Tokens(login.User.Id)).Length);
    }

    [Fact]
    public async Task Logout_ForeignRefreshTokenCannotRevokeAnotherAccountsFamily()
    {
        await using var fixture = await Fixture.CreateAsync();
        var alice = await fixture.RegisterAndLogin("alice");
        var bob = await fixture.RegisterAndLogin("bob");
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", alice.AccessToken);

        var response = await client.PostAsJsonAsync("/api/auth/logout", new RefreshTokenRequest(bob.RefreshToken!));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(Assert.Single(await fixture.Tokens(bob.User.Id)).RevokedAtUtc);
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            Assert.Equal(2, (await db.Users.SingleAsync(x => x.Id == alice.User.Id)).SecurityVersion);
            Assert.Equal(1, (await db.Users.SingleAsync(x => x.Id == bob.User.Id)).SecurityVersion);
        }
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.OK, (await Refresh(client, bob.RefreshToken!)).StatusCode);
    }

    [Fact]
    public async Task Refresh_ParallelTabsReceiveSameWinningPairWithoutRevokingFamily()
    {
        await using var fixture = await Fixture.CreateAsync();
        var login = await fixture.RegisterAndLogin("student");
        using var client = fixture.CreateClient();

        var responses = await Task.WhenAll(Refresh(client, login.RefreshToken!), Refresh(client, login.RefreshToken!));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var first = await responses[0].Content.ReadFromJsonAsync<AuthResponse>();
        var second = await responses[1].Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(first); Assert.NotNull(second);
        Assert.Equal(first.AccessToken, second.AccessToken);
        Assert.Equal(first.RefreshToken, second.RefreshToken);
        Assert.Single(await fixture.Tokens(login.User.Id), x => x.IsActive);
    }

    [Fact]
    public async Task ProductionSizedCache_AllowsCurrentAccountAndPersistsRefreshRotation()
    {
        await using var fixture = await Fixture.CreateAsync(productionCache: true);
        var login = await fixture.RegisterAndLogin("student");
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        var response = await Refresh(client, login.RefreshToken!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rotated = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(rotated);
        Assert.NotEqual(login.RefreshToken, rotated.RefreshToken);
        var tokens = await fixture.Tokens(login.User.Id);
        Assert.Equal(2, tokens.Length);
        Assert.Equal(RefreshTokenService.HashToken(rotated.RefreshToken!), Assert.Single(tokens, x => x.IsActive).Token);
    }

    [Fact]
    public async Task LocalAccount_RegisterAndLoginHashPasswordAndUseOnlyServerStudentRole()
    {
        await using var fixture = await Fixture.CreateAsync();
        var login = await fixture.RegisterAndLogin("student");
        Assert.Equal(new[] { AuthRoles.ClubMember }, login.User.Roles);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(login.RefreshToken));
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var user = await db.Users.SingleAsync(x => x.Id == login.User.Id);
            Assert.NotNull(user.PasswordHash);
            Assert.NotEqual(Password, user.PasswordHash);
            Assert.Equal(RefreshTokenService.HashToken(login.RefreshToken!), Assert.Single(await fixture.Tokens(user.Id)).Token);
        }
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var me = await client.GetFromJsonAsync<UserSummary>("/api/users/me");
        Assert.Equal(login.User.Id, me!.Id);
        Assert.Equal(new[] { AuthRoles.ClubMember }, me.Roles);
    }

    [Fact]
    public async Task LocalAccount_RejectsDuplicateIdentitiesBadCredentialsAndClientRoleInjection()
    {
        await using var fixture = await Fixture.CreateAsync();
        var login = await fixture.RegisterAndLogin("student");
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/internal/register",
            new InternalRegisterRequest("another", "STUDENT@FPT.EDU.VN", Password, "Another"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/internal/register",
            new InternalRegisterRequest("STUDENT", "another@fpt.edu.vn", Password, "Another"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/internal/register", new
        {
            username = "injected",
            email = "injected@fpt.edu.vn",
            password = Password,
            displayName = "Injected",
            roles = new[] { AuthRoles.Admin }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/internal/register",
            new InternalRegisterRequest("short", "short@fpt.edu.vn", "short", "Short"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/internal/login",
            new InternalLoginRequest("student@fpt.edu.vn", "WrongPassword!2026"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/internal/login",
            new InternalLoginRequest("missing@fpt.edu.vn", Password))).StatusCode);
        Assert.Single(await fixture.Tokens(login.User.Id));
    }

    private static Task<HttpResponseMessage> Refresh(HttpClient client, string token) =>
        client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(token));

    private sealed class Fixture(WebApplication app, SqliteConnection connection) : IAsyncDisposable
    {
        internal WebApplication App { get; } = app;
        internal HttpClient CreateClient() => App.GetTestClient();
        internal async Task<AuthResponse> RegisterAndLogin(string username)
        {
            using var client = CreateClient();
            var email = username + "@fpt.edu.vn";
            var registered = await client.PostAsJsonAsync("/api/auth/internal/register",
                new InternalRegisterRequest(username, email, Password, username));
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
            var body = await registered.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("PROJECT_ACCOUNT", body.GetProperty("identityProvenance").GetString());
            Assert.Equal(new[] { "identityProvenance", "userId" }, body.EnumerateObject().Select(x => x.Name).Order());
            var response = await client.PostAsJsonAsync("/api/auth/internal/login", new InternalLoginRequest(email, Password));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        }

        internal async Task UpdateUser(int id, Action<User> update)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var user = await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role).SingleAsync(x => x.Id == id);
            update(user); await db.SaveChangesAsync();
        }

        internal async Task<RefreshToken[]> Tokens(int userId)
        {
            await using var scope = App.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<AuthDbContext>().RefreshTokens.AsNoTracking()
                .Where(x => x.UserId == userId).ToArrayAsync();
        }

        internal static async Task<Fixture> CreateAsync(bool productionCache = false)
        {
            var connectionString = $"Data Source=file:auth_session_{Guid.NewGuid():N}?mode=memory&cache=shared";
            var connection = new SqliteConnection(connectionString); await connection.OpenAsync();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Jwt:Issuer"] = "ClubReportHub",
                ["Jwt:Audience"] = "ClubReportHub.Client",
                ["Jwt:SigningKey"] = "test-only-auth-session-key-at-least-32-characters",
                ["Jwt:ExpirationMinutes"] = "30",
                ["Jwt:RefreshTokenExpirationDays"] = "7"
            });
            builder.Services.AddAuthServices(builder.Configuration, builder.Environment);
            if (productionCache) builder.Services.AddClubAccessClient(builder.Configuration);
            var options = builder.Services.Single(x => x.ServiceType == typeof(DbContextOptions<AuthDbContext>));
            builder.Services.Remove(options);
            builder.Services.AddDbContext<AuthDbContext>(options => options.UseSqlite(connectionString));
            var app = builder.Build(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
            app.MapAuthEndpoints(); app.MapInternalAccountEndpoints(); app.MapUserEndpoints();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
                await db.Database.EnsureCreatedAsync(); db.Roles.Add(new Role { Name = AuthRoles.ClubMember });
                await db.SaveChangesAsync();
            }
            await app.StartAsync(); return new(app, connection);
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync(); await connection.DisposeAsync();
        }
    }
}
