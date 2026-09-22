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

namespace Backend.StabilizationTests;

public sealed class RefreshTokenSecurityTests
{
    private const string SigningKey = "test-only-signing-key-at-least-32-characters";

    [Fact]
    public async Task RefreshToken_StoredInDatabaseAsSha256Hash_NotPlaintext()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var token = await fixture.RefreshTokenService.CreateRefreshTokenAsync(fixture.UserId);

        Assert.NotNull(token.RawToken);
        Assert.NotEqual(token.RawToken, token.Token);

        // Verify SHA-256 hash
        var expectedHash = RefreshTokenService.HashToken(token.RawToken);
        Assert.Equal(expectedHash, token.Token);
        Assert.Equal(64, token.Token.Length);

        // Verify direct DB query shows only the hash
        var dbRecord = await fixture.Db.RefreshTokens.AsNoTracking().FirstAsync(r => r.Id == token.Id);
        Assert.Equal(expectedHash, dbRecord.Token);
        Assert.DoesNotContain(token.RawToken, dbRecord.Token);
    }

    [Fact]
    public async Task RefreshToken_LookupWithPlaintext_SucceedsViaHash()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var token = await fixture.RefreshTokenService.CreateRefreshTokenAsync(fixture.UserId);
        var rawToken = token.RawToken!;

        var found = await fixture.RefreshTokenService.GetRefreshTokenAsync(rawToken);
        Assert.NotNull(found);
        Assert.Equal(token.Id, found.Id);
        Assert.Equal(token.Token, found.Token);
    }

    [Fact]
    public async Task RefreshToken_StoredHash_CannotBeUsedAsBearerCredential()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var token = await fixture.RefreshTokenService.CreateRefreshTokenAsync(fixture.UserId);

        var found = await fixture.RefreshTokenService.GetRefreshTokenAsync(token.Token);
        var rotation = await fixture.RefreshTokenService.RotateRefreshTokenAsync(token.Token, "127.0.0.1");

        Assert.Null(found);
        Assert.Equal(RefreshTokenStatus.Invalid, rotation.Status);
    }

    [Fact]
    public async Task RotateRefreshToken_StoresHashedReplacementAndRevokesOld()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var initialToken = await fixture.RefreshTokenService.CreateRefreshTokenAsync(fixture.UserId);
        var initialRaw = initialToken.RawToken!;

        var result = await fixture.RefreshTokenService.RotateRefreshTokenAsync(initialRaw, "127.0.0.1");

        Assert.Equal(RefreshTokenStatus.Success, result.Status);
        Assert.NotNull(result.Response);
        Assert.NotNull(result.Response.RefreshToken);

        var newRaw = result.Response.RefreshToken;
        var expectedNewHash = RefreshTokenService.HashToken(newRaw);

        // Check old token in DB
        var oldDbRecord = await fixture.Db.RefreshTokens.AsNoTracking().FirstAsync(r => r.Id == initialToken.Id);
        Assert.NotNull(oldDbRecord.RevokedAtUtc);
        Assert.Equal(expectedNewHash, oldDbRecord.ReplacedByToken);

        // Check new token in DB
        var newDbRecord = await fixture.Db.RefreshTokens.AsNoTracking().FirstAsync(r => r.Token == expectedNewHash);
        Assert.NotNull(newDbRecord);
        Assert.Equal(initialToken.FamilyId, newDbRecord.FamilyId);
        Assert.Null(newDbRecord.RevokedAtUtc);
    }

    [Fact]
    public async Task RotationGracePeriod_WithoutCachedRawToken_DoesNotExposeReplacementHash()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var initialToken = await fixture.RefreshTokenService.CreateRefreshTokenAsync(fixture.UserId);
        var initialRaw = initialToken.RawToken!;

        var firstResult = await fixture.RefreshTokenService.RotateRefreshTokenAsync(initialRaw, "127.0.0.1");
        Assert.Equal(RefreshTokenStatus.Success, firstResult.Status);

        var memoryCache = fixture.App.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        memoryCache.Remove($"rotated_refresh:{RefreshTokenService.HashToken(initialRaw)}");

        var repeatedResult = await fixture.RefreshTokenService.RotateRefreshTokenAsync(initialRaw, "127.0.0.1");

        Assert.Equal(RefreshTokenStatus.Invalid, repeatedResult.Status);
        Assert.Null(repeatedResult.Response);
    }

    [Fact]
    public async Task ConcurrentRotation_WithinGracePeriod_DoesNotRevokeFamily()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var initialToken = await fixture.RefreshTokenService.CreateRefreshTokenAsync(fixture.UserId);
        var rawToken = initialToken.RawToken!;

        using var client = fixture.CreateClient();

        // Launch two parallel / concurrent refresh requests with the same token
        var task1 = client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(rawToken));
        var task2 = client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(rawToken));

        var responses = await Task.WhenAll(task1, task2);

        // Both requests should succeed (HTTP 200)
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);

        // Verify the token family was NOT revoked
        var activeTokens = await fixture.Db.RefreshTokens
            .AsNoTracking()
            .Where(r => r.FamilyId == initialToken.FamilyId && r.RevokedAtUtc == null)
            .ToListAsync();

        Assert.NotEmpty(activeTokens);
    }

    [Fact]
    public async Task ReplayAttack_OutsideGracePeriod_RevokesEntireFamily()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var initialToken = await fixture.RefreshTokenService.CreateRefreshTokenAsync(fixture.UserId);
        var rawToken = initialToken.RawToken!;

        // First rotation succeeds
        var firstResult = await fixture.RefreshTokenService.RotateRefreshTokenAsync(rawToken, "127.0.0.1");
        Assert.Equal(RefreshTokenStatus.Success, firstResult.Status);

        // Simulate token was revoked 60 seconds ago (outside 30s grace period)
        var oldToken = await fixture.Db.RefreshTokens.FirstAsync(r => r.Id == initialToken.Id);
        oldToken.RevokedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-60);
        await fixture.Db.SaveChangesAsync();

        // Evict memory cache to simulate expiration of the 30-second window
        var memoryCache = fixture.App.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        memoryCache.Remove($"rotated_refresh:{RefreshTokenService.HashToken(rawToken)}");

        // Attacker attempts to replay old token
        using var client = fixture.CreateClient();
        var attackResponse = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(rawToken));
        Assert.Equal(HttpStatusCode.Unauthorized, attackResponse.StatusCode);

        // Entire family must now be revoked
        var unrevokedTokens = await fixture.Db.RefreshTokens
            .AsNoTracking()
            .Where(r => r.FamilyId == initialToken.FamilyId && r.RevokedAtUtc == null)
            .ToListAsync();

        Assert.Empty(unrevokedTokens);
    }

    [Fact]
    public async Task Logout_WithPlaintextRefreshToken_SuccessfullyRevokesFamily()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        var initialToken = await fixture.RefreshTokenService.CreateRefreshTokenAsync(fixture.UserId);
        var rawToken = initialToken.RawToken!;

        var token = fixture.TokenFactory.CreateToken(fixture.UserId, "refreshtest", "Refresh Test", [AuthRoles.ClubMember]);

        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var logoutResponse = await client.PostAsJsonAsync("/api/auth/logout", new RefreshTokenRequest(rawToken));
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        // Verify token in DB was revoked
        var dbRecord = await fixture.Db.RefreshTokens.AsNoTracking().FirstAsync(r => r.Id == initialToken.Id);
        Assert.NotNull(dbRecord.RevokedAtUtc);
    }

    [Fact]
    public async Task LegacyUnhashedToken_IsSupportedAndRotatedIntoHashedToken()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        const string legacyPlaintext = "legacy-plaintext-token-from-older-system";

        // Insert legacy plaintext token directly
        var legacyToken = new RefreshToken
        {
            UserId = fixture.UserId,
            Token = legacyPlaintext,
            FamilyId = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7)
        };
        fixture.Db.RefreshTokens.Add(legacyToken);
        await fixture.Db.SaveChangesAsync();

        // Rotate legacy token
        var result = await fixture.RefreshTokenService.RotateRefreshTokenAsync(legacyPlaintext, "127.0.0.1");
        Assert.Equal(RefreshTokenStatus.Success, result.Status);
        Assert.NotNull(result.Response);

        // New token is strictly hashed
        var newRaw = result.Response.RefreshToken;
        Assert.NotNull(newRaw);
        var expectedHash = RefreshTokenService.HashToken(newRaw);

        var newDbRecord = await fixture.Db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(r => r.Token == expectedHash);
        Assert.NotNull(newDbRecord);
    }

    private sealed class RefreshTestFixture : IAsyncDisposable
    {
        public SqliteConnection Connection { get; }
        public WebApplication App { get; }
        public AuthDbContext Db { get; }
        public RefreshTokenService RefreshTokenService { get; }
        public JwtTokenFactory TokenFactory { get; }
        public int UserId { get; }

        private RefreshTestFixture(
            SqliteConnection connection,
            WebApplication app,
            AuthDbContext db,
            RefreshTokenService refreshTokenService,
            JwtTokenFactory tokenFactory,
            int userId)
        {
            Connection = connection;
            App = app;
            Db = db;
            RefreshTokenService = refreshTokenService;
            TokenFactory = tokenFactory;
            UserId = userId;
        }

        public HttpClient CreateClient() => App.GetTestClient();

        public static async Task<RefreshTestFixture> CreateAsync()
        {
            var dbName = $"refresh_test_{Guid.NewGuid():N}";
            var connStr = $"Data Source=file:{dbName}?mode=memory&cache=shared";
            var connection = new SqliteConnection(connStr);
            await connection.OpenAsync();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connStr,
                ["Jwt:Issuer"] = "ClubReportHub",
                ["Jwt:Audience"] = "ClubReportHub.Client",
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:ExpirationMinutes"] = "30",
                ["Jwt:RefreshTokenExpirationDays"] = "7"
            });

            builder.Services.AddAuthServices(builder.Configuration, builder.Environment);
            var descriptor = builder.Services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AuthDbContext>));
            if (descriptor != null)
            {
                builder.Services.Remove(descriptor);
            }
            builder.Services.AddDbContext<AuthDbContext>(options =>
                options.UseSqlite(connStr));

            var app = builder.Build();
            app.UseRateLimiter();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapAuthEndpoints();
            app.MapUserEndpoints();

            await app.StartAsync();

            var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await db.Database.EnsureCreatedAsync();

            // Seed user
            var role = new Role { Name = AuthRoles.ClubMember };
            db.Roles.Add(role);

            var user = new User
            {
                Username = "refreshtest",
                FullName = "Refresh Test User",
                Email = "refresh@fpt.edu.vn",
                IsActive = true,
                IsLocked = false,
                SecurityVersion = 1
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            await db.SaveChangesAsync();

            var refreshTokenService = scope.ServiceProvider.GetRequiredService<RefreshTokenService>();
            var tokenFactory = scope.ServiceProvider.GetRequiredService<JwtTokenFactory>();

            return new RefreshTestFixture(connection, app, db, refreshTokenService, tokenFactory, user.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
