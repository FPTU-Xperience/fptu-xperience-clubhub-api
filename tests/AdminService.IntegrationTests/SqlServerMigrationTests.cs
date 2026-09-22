using System.Net;
using AdminService.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AdminService.IntegrationTests;

public sealed class SqlServerMigrationTests
{
    /// <summary>
    /// Environment variable carrying the base SQL Server connection string used to
    /// provision a throwaway database for this migration test.
    /// </summary>
    internal const string ConnectionStringVariable = "ADMIN_SERVICE_TEST_CONNECTION_STRING";

    /// <summary>
    /// <see cref="ConnectionStringVariable"/> is deliberately absent from local
    /// development machines but is always provided by the CI job. A missing value is
    /// therefore reported as an explicit, visible skip rather than a silent
    /// green-pass (TEST-F04), and a missing value under CI is a hard failure.
    /// </summary>
    internal static bool IsRunningInContinuousIntegration =>
        string.Equals(
            Environment.GetEnvironmentVariable("CI"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    [SkippableFact]
    public async Task EmptySqlServerDatabase_MigratesAndApplicationBecomesReady()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            ConnectionStringVariable);

        // TEST-F04: never pass silently. Under CI the connection string is mandatory, so a
        // missing value is a hard failure. Outside CI the absence is expected on a developer
        // machine, and the test is reported as an explicit, visible skip carrying the reason
        // instead of a silent green pass.
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            Assert.False(
                IsRunningInContinuousIntegration,
                $"{ConnectionStringVariable} must be configured in CI.");

            Skip.If(
                true,
                $"{ConnectionStringVariable} is not configured; SQL Server-backed migration "
                + "validation was skipped. Export it to run this test locally.");
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
