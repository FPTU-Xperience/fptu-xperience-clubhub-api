using AuthService.Data;
using AuthService.Endpoints;
using AuthService.Extensions;
using AuthService.Models;
using ClubReportHub.Shared.Auth;
using ClubService.Data;
using ClubService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Backend.StabilizationTests;

public sealed class SecurityAndSeederRegressionTests
{
    private const string ValidSigningKey = "test-only-signing-key-at-least-32-characters";

    [Theory]
    [InlineData("dev-only-signing-key-at-least-32-characters")]
    [InlineData("replace-with-a-random-secret-key-at-least-32-characters")]
    public void ProductionRejectsPlaceholderJwtSigningKeys(string signingKey)
    {
        var configuration = CreateConfiguration(signingKey);
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddClubReportJwtValidation(
                configuration,
                new TestHostEnvironment(Environments.Production)));

        Assert.Contains("cannot be used in Production", exception.Message);
    }

    [Fact]
    public async Task ProductionDoesNotMapDevLoginEvenWhenLegacyFlagIsTrue()
    {
        var routes = await GetAuthRoutesAsync(Environments.Production, enableDevLogin: true);

        Assert.DoesNotContain("/api/auth/dev-login", routes);
        Assert.DoesNotContain("/api/auth/test-login", routes);
    }

    [Fact]
    public async Task TestingMapsExplicitTestLoginEndpoints()
    {
        var routes = await GetAuthRoutesAsync("Testing", enableDevLogin: false);

        Assert.Contains("/api/auth/dev-login", routes);
        Assert.Contains("/api/auth/test-login", routes);
    }

    [Fact]
    public void ProductionCorsRejectsUnconfiguredCloudflarePagesOrigins()
    {
        const string allowedOrigin = "https://student-ui.example.edu";
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=unused",
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = ValidSigningKey,
            ["Cors:AllowedOrigins:0"] = allowedOrigin
        });
        builder.Services.AddAuthServices(builder.Configuration, builder.Environment);
        using var app = builder.Build();

        var corsOptions = app.Services.GetRequiredService<IOptions<CorsOptions>>().Value;
        var policy = corsOptions.GetPolicy("frontend");

        Assert.NotNull(policy);
        Assert.True(policy.IsOriginAllowed(allowedOrigin));
        Assert.False(policy.IsOriginAllowed("https://unapproved-preview.pages.dev"));
        Assert.False(policy.IsOriginAllowed("http://localhost:3000"));
    }

    [Fact]
    public async Task ProductionSeederCreatesRolesButNoDefaultAdministrators()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateAuthDbContext(connection);
        await db.Database.EnsureCreatedAsync();

        await AuthSeeder.SeedAsync(
            db,
            new ConfigurationBuilder().Build(),
            new TestHostEnvironment(Environments.Production));

        Assert.Equal(6, await db.Roles.CountAsync());
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task SeederDoesNotReactivateOrUnlockExistingBootstrapAccount()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateAuthDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new User
        {
            Username = "security-admin@example.edu",
            Email = "security-admin@example.edu",
            FullName = "Deliberately Disabled",
            IsActive = false,
            IsLocked = true
        });
        await db.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Email"] = "security-admin@example.edu",
                ["BootstrapAdmin:FullName"] = "Replacement Name"
            })
            .Build();
        await AuthSeeder.SeedAsync(
            db,
            configuration,
            new TestHostEnvironment(Environments.Production));
        db.ChangeTracker.Clear();

        var account = await db.Users
            .Include(user => user.UserRoles)
            .SingleAsync(user => user.Email == "security-admin@example.edu");
        Assert.False(account.IsActive);
        Assert.True(account.IsLocked);
        Assert.Equal("Deliberately Disabled", account.FullName);
        Assert.Empty(account.UserRoles);
    }

    [Fact]
    public async Task ClubSeederDoesNotRewriteExistingClubData()
    {
        var options = new DbContextOptionsBuilder<ClubDbContext>()
            .UseInMemoryDatabase($"club-seeder-{Guid.NewGuid():N}")
            .Options;
        await using var db = new ClubDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Clubs.Add(new Club
        {
            Code = "REAL-IT-CLUB",
            Name = "User Managed Name",
            Category = ClubCategories.Sports,
            Description = "User managed description",
            ContactEmail = "owner@example.edu",
            ContactPhone = "0123456789",
            ScheduleLabel = "User managed schedule",
            IsRecruiting = false
        });
        await db.SaveChangesAsync();

        await ClubSeeder.SeedAsync(db);
        db.ChangeTracker.Clear();

        var club = await db.Clubs.SingleAsync();
        Assert.Equal("User Managed Name", club.Name);
        Assert.Equal(ClubCategories.Sports, club.Category);
        Assert.Equal("User managed description", club.Description);
        Assert.Equal("owner@example.edu", club.ContactEmail);
        Assert.Equal("User managed schedule", club.ScheduleLabel);
        Assert.False(club.IsRecruiting);
    }

    private static AuthDbContext CreateAuthDbContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(connection).Options);

    private static IConfiguration CreateConfiguration(string signingKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "ClubReportHub",
                ["Jwt:Audience"] = "ClubReportHub.Client",
                ["Jwt:SigningKey"] = signingKey
            })
            .Build();

    private static async Task<HashSet<string>> GetAuthRoutesAsync(
        string environmentName,
        bool enableDevLogin)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=unused",
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = ValidSigningKey,
            ["Auth:EnableDevLogin"] = enableDevLogin.ToString()
        });
        builder.Services.AddAuthServices(builder.Configuration, builder.Environment);
        await using var app = builder.Build();
        app.MapAuthEndpoints();

        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
