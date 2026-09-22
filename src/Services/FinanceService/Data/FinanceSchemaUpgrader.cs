using Microsoft.EntityFrameworkCore;

namespace FinanceService.Data;

public static class FinanceSchemaUpgrader
{
    public static Task ApplyAsync(FinanceDbContext db, CancellationToken cancellationToken = default)
    {
        const string sql = """
            IF COL_LENGTH(N'dbo.BudgetProposals', N'ManagerReviewedByUserId') IS NULL
                ALTER TABLE [dbo].[BudgetProposals] ADD [ManagerReviewedByUserId] int NULL;

            IF COL_LENGTH(N'dbo.BudgetProposals', N'ManagerReviewedAtUtc') IS NULL
                ALTER TABLE [dbo].[BudgetProposals] ADD [ManagerReviewedAtUtc] datetimeoffset NULL;

            IF COL_LENGTH(N'dbo.BudgetProposals', N'ManagerReviewNote') IS NULL
                ALTER TABLE [dbo].[BudgetProposals] ADD [ManagerReviewNote] nvarchar(1000) NULL;

            IF COL_LENGTH(N'dbo.BudgetProposals', N'Version') IS NULL
                ALTER TABLE [dbo].[BudgetProposals] ADD [Version] int NOT NULL DEFAULT 1;

            IF COL_LENGTH(N'dbo.BudgetProposals', N'SourceReportId') IS NULL
                ALTER TABLE [dbo].[BudgetProposals] ADD [SourceReportId] int NULL;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[dbo].[BudgetProposals]')
                  AND name = N'IX_BudgetProposals_SourceReportId')
                EXEC(N'CREATE UNIQUE INDEX [IX_BudgetProposals_SourceReportId]
                    ON [dbo].[BudgetProposals] ([SourceReportId])
                    WHERE [SourceReportId] IS NOT NULL');

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[dbo].[BudgetProposals]')
                  AND name = N'IX_BudgetProposals_ProposedAtUtc')
                EXEC(N'CREATE INDEX [IX_BudgetProposals_ProposedAtUtc]
                    ON [dbo].[BudgetProposals] ([ProposedAtUtc] DESC)');

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[dbo].[BudgetProposals]')
                  AND name = N'IX_BudgetProposals_ProposedByUserId')
                EXEC(N'CREATE INDEX [IX_BudgetProposals_ProposedByUserId]
                    ON [dbo].[BudgetProposals] ([ProposedByUserId])');

            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[dbo].[Settlements]')
                  AND name = N'IX_Settlements_BudgetProposalId'
                  AND is_unique = 0)
                DROP INDEX [IX_Settlements_BudgetProposalId] ON [dbo].[Settlements];

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[dbo].[Settlements]')
                  AND name = N'IX_Settlements_BudgetProposalId')
                EXEC(N'CREATE UNIQUE INDEX [IX_Settlements_BudgetProposalId]
                    ON [dbo].[Settlements] ([BudgetProposalId])
                    WHERE [Status] = ''Submitted'' OR [Status] = ''Approved''');

            IF OBJECT_ID(N'dbo.OutboxMessages', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[OutboxMessages] (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [OccurredAtUtc] datetimeoffset NOT NULL,
                    [EventType] nvarchar(100) NOT NULL,
                    [EventTypeName] nvarchar(300) NOT NULL,
                    [Payload] nvarchar(max) NOT NULL,
                    [Status] nvarchar(50) NOT NULL,
                    [RetryCount] int NOT NULL DEFAULT 0,
                    [ProcessedAtUtc] datetimeoffset NULL,
                    [ErrorMessage] nvarchar(max) NULL,
                    [CorrelationId] nvarchar(100) NULL,
                    [ClaimedAtUtc] datetimeoffset NULL,
                    [ClaimExpiresAtUtc] datetimeoffset NULL,
                    [ClaimedByInstanceId] nvarchar(100) NULL,
                    [ConcurrencyToken] uniqueidentifier NOT NULL DEFAULT NEWID()
                );
                CREATE INDEX [IX_OutboxMessages_Status_OccurredAtUtc]
                    ON [dbo].[OutboxMessages] ([Status], [OccurredAtUtc]);
                CREATE INDEX [IX_OutboxMessages_Status_ClaimExpiresAtUtc_OccurredAtUtc]
                    ON [dbo].[OutboxMessages] ([Status], [ClaimExpiresAtUtc], [OccurredAtUtc]);
            END
            ELSE
            BEGIN
                IF COL_LENGTH(N'dbo.OutboxMessages', N'ClaimedAtUtc') IS NULL
                    ALTER TABLE [dbo].[OutboxMessages] ADD [ClaimedAtUtc] datetimeoffset NULL;
                IF COL_LENGTH(N'dbo.OutboxMessages', N'ClaimExpiresAtUtc') IS NULL
                    ALTER TABLE [dbo].[OutboxMessages] ADD [ClaimExpiresAtUtc] datetimeoffset NULL;
                IF COL_LENGTH(N'dbo.OutboxMessages', N'ClaimedByInstanceId') IS NULL
                    ALTER TABLE [dbo].[OutboxMessages] ADD [ClaimedByInstanceId] nvarchar(100) NULL;
                IF COL_LENGTH(N'dbo.OutboxMessages', N'ConcurrencyToken') IS NULL
                    ALTER TABLE [dbo].[OutboxMessages] ADD [ConcurrencyToken] uniqueidentifier NOT NULL DEFAULT NEWID();
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OutboxMessages_Status_ClaimExpiresAtUtc_OccurredAtUtc' AND object_id = OBJECT_ID(N'dbo.OutboxMessages'))
                    CREATE INDEX [IX_OutboxMessages_Status_ClaimExpiresAtUtc_OccurredAtUtc]
                        ON [dbo].[OutboxMessages] ([Status], [ClaimExpiresAtUtc], [OccurredAtUtc]);
            END
            """;

        return db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}
