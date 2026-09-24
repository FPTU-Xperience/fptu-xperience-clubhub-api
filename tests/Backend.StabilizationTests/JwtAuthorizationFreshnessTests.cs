using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using AuthService.Contracts;
using AuthService.Data;
using AuthService.Endpoints;
using AuthService.Extensions;
using AuthService.Models;
using AuthService.Services;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.StabilizationTests;

public sealed partial class JwtAuthorizationFreshnessTests
{
    private const string SigningKey = "test-only-signing-key-at-least-32-characters";

    [Theory]
    [InlineData(AuthRoles.Admin)]
    [InlineData(AuthRoles.SystemAdmin)]
    [InlineData(AuthRoles.StudentAffairsAdmin)]
    [InlineData(AuthRoles.ClubManager)]
    [InlineData(AuthRoles.Treasurer)]
    [InlineData(AuthRoles.ClubMember)]
    public async Task UserReadContract_MeUsesAuthenticatedIdentityAndSafeSummary(string roleName)
    {
        await using var fixture = await AuthTestFixture.CreateAsync();
        var role = await fixture.Db.Roles.SingleAsync();
        role.Name = roleName;
        var user = await fixture.Db.Users.SingleAsync();
        user.GoogleSubject = "private-google-subject";
        await fixture.Db.SaveChangesAsync();
        var token = fixture.TokenFactory.CreateToken(fixture.UserId, "testuser", "Test User", [roleName], securityVersion: 1);
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync("/api/users/me?id=999&userId=999");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(fixture.UserId, body.GetProperty("id").GetInt32());
        Assert.Equal("testuser@fpt.edu.vn", body.GetProperty("email").GetString());
        Assert.Equal(roleName, body.GetProperty("roles")[0].GetString());
        Assert.Equal(new[] { "email", "fullName", "id", "isActive", "isLocked", "roles", "username" },
            body.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray());
    }

    [Theory]
    [InlineData(AuthRoles.Admin, HttpStatusCode.OK)]
    [InlineData(AuthRoles.SystemAdmin, HttpStatusCode.OK)]
    [InlineData(AuthRoles.StudentAffairsAdmin, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.ClubManager, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Treasurer, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.ClubMember, HttpStatusCode.Forbidden)]
    public async Task UserReadContract_DetailKeepsManagementPolicy(string roleName, HttpStatusCode expected)
    {
        await using var fixture = await AuthTestFixture.CreateAsync();
        var role = await fixture.Db.Roles.SingleAsync();
        role.Name = roleName;
        await fixture.Db.SaveChangesAsync();
        var token = fixture.TokenFactory.CreateToken(fixture.UserId, "testuser", "Test User", [roleName], securityVersion: 1);
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync($"/api/users/{fixture.UserId}");
        Assert.Equal(expected, response.StatusCode);
        var list = await client.GetAsync("/api/users");
        Assert.Equal(expected, list.StatusCode);
        var missing = await client.GetAsync("/api/users/999999");
        Assert.Equal(expected == HttpStatusCode.OK ? HttpStatusCode.NotFound : expected, missing.StatusCode);

        if (expected == HttpStatusCode.OK)
        {
            var detail = await response.Content.ReadFromJsonAsync<UserSummary>();
            Assert.NotNull(detail);
            Assert.Equal(fixture.UserId, detail.Id);
            Assert.Equal(new[] { roleName }, detail.Roles);
        }
    }

    [Theory]
    [InlineData("/api/users/me")]
    [InlineData("/api/users/1")]
    public async Task UserReadContract_AnonymousIsRejected(string path)
    {
        await using var fixture = await AuthTestFixture.CreateAsync();
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task UserReadContract_MeRejectsRevokedToken()
    {
        await using var fixture = await AuthTestFixture.CreateAsync();
        var token = fixture.TokenFactory.CreateToken(fixture.UserId, "testuser", "Test User", [AuthRoles.ClubMember], securityVersion: 1);
        var user = await fixture.Db.Users.SingleAsync();
        user.IsLocked = true;
        user.SecurityVersion++;
        await fixture.Db.SaveChangesAsync();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
    }

    [Fact]
    public async Task ActiveUserToken_WithMatchingSecurityVersion_IsAccepted()
    {
        await using var fixture = await AuthTestFixture.CreateAsync();
        var token = fixture.TokenFactory.CreateToken(fixture.UserId, "testuser", "Test User", [AuthRoles.ClubMember], securityVersion: 1);

        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StaleToken_AfterUserLocked_IsRejectedWith401()
    {
        await using var fixture = await AuthTestFixture.CreateAsync();
        var token = fixture.TokenFactory.CreateToken(fixture.UserId, "testuser", "Test User", [AuthRoles.ClubMember], securityVersion: 1);

        // Lock user directly or via DB
        var user = await fixture.Db.Users.FindAsync(fixture.UserId);
        Assert.NotNull(user);
        user.IsLocked = true;
        user.SecurityVersion++;
        await fixture.Db.SaveChangesAsync();

        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StaleToken_AfterUserDeactivated_IsRejectedWith401()
    {
        await using var fixture = await AuthTestFixture.CreateAsync();
        var token = fixture.TokenFactory.CreateToken(fixture.UserId, "testuser", "Test User", [AuthRoles.ClubMember], securityVersion: 1);

        // Deactivate user
        var user = await fixture.Db.Users.FindAsync(fixture.UserId);
        Assert.NotNull(user);
        user.IsActive = false;
        user.SecurityVersion++;
        await fixture.Db.SaveChangesAsync();

        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StaleToken_AfterSecurityVersionIncrement_IsRejected_AndNewTokenAccepted()
    {
        await using var fixture = await AuthTestFixture.CreateAsync();
        var oldToken = fixture.TokenFactory.CreateToken(fixture.UserId, "testuser", "Test User", [AuthRoles.ClubMember], securityVersion: 1);

        // Increment security version (e.g. role change or password/permission reset)
        var user = await fixture.Db.Users.FindAsync(fixture.UserId);
        Assert.NotNull(user);
        user.SecurityVersion = 2;
        await fixture.Db.SaveChangesAsync();

        using var client = fixture.CreateClient();

        // Old token rejected
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldToken.AccessToken);
        var oldResponse = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, oldResponse.StatusCode);

        // New token with version 2 accepted
        var newToken = fixture.TokenFactory.CreateToken(fixture.UserId, "testuser", "Test User", [AuthRoles.ClubMember], securityVersion: 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newToken.AccessToken);
        var newResponse = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.OK, newResponse.StatusCode);
    }

    [Fact]
    public async Task StaleToken_WithRevokedRoleClaim_IsRejectedWith401()
    {
        await using var fixture = await AuthTestFixture.CreateAsync();
        // User in DB only has ClubMember, but token claims Admin
        var illegitimateToken = fixture.TokenFactory.CreateToken(fixture.UserId, "testuser", "Test User", [AuthRoles.Admin], securityVersion: 1);

        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", illegitimateToken.AccessToken);

        var response = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_IncrementsSecurityVersion_AndInvalidatesAccessToken()
    {
        await using var fixture = await AuthTestFixture.CreateAsync();
        var token = fixture.TokenFactory.CreateToken(fixture.UserId, "testuser", "Test User", [AuthRoles.ClubMember], securityVersion: 1);

        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        // Token works before logout
        var preResponse = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.OK, preResponse.StatusCode);

        // Call Logout endpoint
        var logoutResponse = await client.PostAsJsonAsync("/api/auth/logout", new RefreshTokenRequest(string.Empty));
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        // Verify DB security version was incremented
        var user = await fixture.Db.Users.AsNoTracking().FirstAsync(u => u.Id == fixture.UserId);
        Assert.True(user.SecurityVersion > 1);

        // Same access token is now rejected
        var postResponse = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);
    }

    [Fact]
    public async Task UserStatusEndpoint_ReturnsAccurateDetailsAndVersion()
    {
        await using var fixture = await AuthTestFixture.CreateAsync();
        using var client = fixture.CreateClient();

        var response = await client.GetAsync($"/api/auth/user-status/{fixture.UserId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var status = await response.Content.ReadFromJsonAsync<RemoteUserStatus>();
        Assert.NotNull(status);
        Assert.Equal(fixture.UserId, status.UserId);
        Assert.True(status.IsActive);
        Assert.False(status.IsLocked);
        Assert.Equal(1, status.SecurityVersion);
        Assert.Contains(AuthRoles.ClubMember, status.Roles);

        // Non-existent user returns 404
        var notFoundResponse = await client.GetAsync("/api/auth/user-status/99999");
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
    }

    [Fact]
    public async Task RemoteSecurityStampValidator_GracefulDegradation_OnNetworkOutage()
    {
        var handler = new MockHttpMessageHandler(_ =>
            throw new HttpRequestException("Simulated AuthService network disconnection"));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://authservice:8080") };
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<RemoteUserSecurityStampValidator>.Instance;

        var validator = new RemoteUserSecurityStampValidator(httpClient, cache, logger);

        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "10"),
            new Claim("ver", "1"),
            new Claim(ClaimTypes.Role, AuthRoles.ClubMember)
        ], "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Fail-open: should return true gracefully so transient auth-service blips don't bring down the entire system
        var isValid = await validator.ValidateSecurityStampAsync(10, principal);
        Assert.True(isValid);
    }

    [Fact]
    public async Task RemoteSecurityStampValidator_RejectsWhenSecurityVersionHigherOnServer()
    {
        var handler = new MockHttpMessageHandler(_ =>
        {
            var status = new RemoteUserStatus(10, IsActive: true, IsLocked: false, SecurityVersion: 3, Roles: [AuthRoles.ClubMember]);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(status)
            };
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://authservice:8080") };
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<RemoteUserSecurityStampValidator>.Instance;

        var validator = new RemoteUserSecurityStampValidator(httpClient, cache, logger);

        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "10"),
            new Claim("ver", "1"), // Token only has ver 1, server is at ver 3
            new Claim(ClaimTypes.Role, AuthRoles.ClubMember)
        ], "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var isValid = await validator.ValidateSecurityStampAsync(10, principal);
        Assert.False(isValid);
    }

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(send(request));
        }
    }

    private sealed class AuthTestFixture : IAsyncDisposable
    {
        public SqliteConnection Connection { get; }
        public WebApplication App { get; }
        public AuthDbContext Db { get; }
        public JwtTokenFactory TokenFactory { get; }
        public int UserId { get; }

        private AuthTestFixture(
            SqliteConnection connection,
            WebApplication app,
            AuthDbContext db,
            JwtTokenFactory tokenFactory,
            int userId)
        {
            Connection = connection;
            App = app;
            Db = db;
            TokenFactory = tokenFactory;
            UserId = userId;
        }

        public HttpClient CreateClient() => App.GetTestClient();

        public static async Task<AuthTestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                ["Jwt:Issuer"] = "ClubReportHub",
                ["Jwt:Audience"] = "ClubReportHub.Client",
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:ExpirationMinutes"] = "30"
            });

            builder.Services.AddAuthServices(builder.Configuration, builder.Environment);
            var descriptor = builder.Services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AuthDbContext>));
            if (descriptor != null)
            {
                builder.Services.Remove(descriptor);
            }
            builder.Services.AddDbContext<AuthDbContext>(options =>
                options.UseSqlite(connection));

            var app = builder.Build();
            app.UseRateLimiter();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapAuthEndpoints();
            app.MapUserEndpoints();
            app.MapGet("/protected", () => Results.Ok(new { message = "authorized" }))
                .RequireAuthorization();

            await app.StartAsync();

            var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await db.Database.EnsureCreatedAsync();

            // Seed role and user
            var role = new Role { Name = AuthRoles.ClubMember };
            db.Roles.Add(role);

            var user = new User
            {
                Username = "testuser",
                FullName = "Test User",
                Email = "testuser@fpt.edu.vn",
                IsActive = true,
                IsLocked = false,
                SecurityVersion = 1
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            await db.SaveChangesAsync();

            var tokenFactory = app.Services.GetRequiredService<JwtTokenFactory>();

            return new AuthTestFixture(connection, app, db, tokenFactory, user.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
