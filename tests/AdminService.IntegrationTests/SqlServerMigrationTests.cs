using System.Net;
using AdminService.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AdminService.IntegrationTests;

public sealed class SqlServerMigrationTests
{
    [Fact]
    public async Task EmptySqlServerDatabase_MigratesAndApplicationBecomesReady()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            "ADMIN_SERVICE_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            Assert.False(
                string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase),
                "ADMIN_SERVICE_TEST_CONNECTION_STRING must be configured in CI.");
            return;
        }

        var connection = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = $"AdminServiceMigrationTests_{Guid.NewGuid():N}",
            TrustServerCertificate = true
        };
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseSqlServer(connection.ConnectionString)
            .Options;
        var values = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Test",
            ["ConnectionStrings__DefaultConnection"] = connection.ConnectionString,
            ["Database__ApplyMigrationsAtStartup"] = "true",
            ["Jwt__Issuer"] = "ClubReportHub",
            ["Jwt__Audience"] = "ClubReportHub.Client",
            ["Jwt__SigningKey"] = "test-only-signing-key-at-least-32-characters"
        };
        var previous = values.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        foreach (var pair in values)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }

        WebApplicationFactory<Program>? factory = null;
        try
        {
            factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
                host.UseEnvironment("Test"));
            using var client = factory.CreateClient();
            var readiness = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);

            await using var dbContext = new AdminDbContext(options);
            Assert.True(await dbContext.Database.CanConnectAsync());
            var expected = dbContext.Database.GetMigrations().ToArray();
            var applied = (await dbContext.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.NotEmpty(expected);
            Assert.Equal(expected, applied);
        }
        finally
        {
            if (factory is not null)
            {
                await factory.DisposeAsync();
            }

            await using var cleanup = new AdminDbContext(options);
            await cleanup.Database.EnsureDeletedAsync();
            foreach (var pair in previous)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}
