using FinanceService.Data;
using FinanceService.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Backend.StabilizationTests;

public sealed class FinanceSqlServerMigrationTests
{
    private const string ConnectionStringVariable = "FINANCE_SERVICE_TEST_CONNECTION_STRING";

    [SkippableFact]
    public async Task FreshDatabase_MigratesAndEnforcesOneActiveSettlementPerProposal()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            Assert.False(
                string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase),
                $"{ConnectionStringVariable} must be configured in CI.");
            Skip.If(true, $"{ConnectionStringVariable} is not configured; SQL Server migration test was skipped.");
        }

        var connection = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = $"FinanceServiceMigrationTests_{Guid.NewGuid():N}",
            TrustServerCertificate = true
        };
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer(connection.ConnectionString)
            .Options;

        try
        {
            await using var db = new FinanceDbContext(options);
            await db.Database.MigrateAsync();
            Assert.Equal(
                db.Database.GetMigrations(),
                await db.Database.GetAppliedMigrationsAsync());

            var proposal = new BudgetProposal
            {
                ClubId = 1,
                ClubName = "Migration test club",
                Title = "Migration test proposal",
                Description = "Verifies the SQL Server active-settlement index",
                RequestedAmount = 100m,
                ProposedByUserId = 1
            };
            db.BudgetProposals.Add(proposal);
            await db.SaveChangesAsync();

            db.Settlements.AddRange(
                new Settlement { BudgetProposalId = proposal.Id, Status = FinanceStatuses.Rejected, ReceiptUrl = "receipt-1" },
                new Settlement { BudgetProposalId = proposal.Id, Status = FinanceStatuses.Rejected, ReceiptUrl = "receipt-2" });
            await db.SaveChangesAsync();

            db.Settlements.Add(new Settlement
            {
                BudgetProposalId = proposal.Id,
                Status = FinanceStatuses.Submitted,
                ReceiptUrl = "receipt-3"
            });
            await db.SaveChangesAsync();

            db.Settlements.Add(new Settlement
            {
                BudgetProposalId = proposal.Id,
                Status = FinanceStatuses.Approved,
                ReceiptUrl = "receipt-4"
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally
        {
            await using var cleanup = new FinanceDbContext(options);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [SkippableFact]
    public async Task ExistingDatabase_ReconcilesLegacySettlementIndexAndRestoresMissingIndex()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            Assert.False(
                string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase),
                $"{ConnectionStringVariable} must be configured in CI.");
            Skip.If(true, $"{ConnectionStringVariable} is not configured; SQL Server migration test was skipped.");
        }

        var connection = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = $"FinanceServiceMigrationTests_{Guid.NewGuid():N}",
            TrustServerCertificate = true
        };
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer(connection.ConnectionString)
            .Options;

        try
        {
            await using var db = new FinanceDbContext(options);
            await db.GetService<IMigrator>().MigrateAsync("20260921213929_AddActiveSettlementInvariant");
            await db.Database.ExecuteSqlRawAsync("""
                DROP INDEX [IX_Settlements_BudgetProposalId] ON [dbo].[Settlements];
                CREATE UNIQUE INDEX [IX_Settlements_BudgetProposalId]
                    ON [dbo].[Settlements] ([BudgetProposalId])
                    WHERE [Status] <> 'Rejected';
                """);
            Assert.Contains("<>", await GetSettlementIndexFilterAsync(connection.ConnectionString));

            await db.Database.MigrateAsync();
            var reconciledFilter = await GetSettlementIndexFilterAsync(connection.ConnectionString);
            Assert.Contains(" IN ", reconciledFilter, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<>", reconciledFilter);

            await db.Database.ExecuteSqlRawAsync(
                "DROP INDEX [IX_Settlements_BudgetProposalId] ON [dbo].[Settlements]");
            await FinanceSchemaUpgrader.ApplyAsync(db);
            var restoredFilter = await GetSettlementIndexFilterAsync(connection.ConnectionString);
            Assert.Contains(" IN ", restoredFilter, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<>", restoredFilter);
        }
        finally
        {
            await using var cleanup = new FinanceDbContext(options);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<string> GetSettlementIndexFilterAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT filter_definition
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'[dbo].[Settlements]')
              AND name = N'IX_Settlements_BudgetProposalId'
              AND is_unique = 1
            """, connection);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }
}
