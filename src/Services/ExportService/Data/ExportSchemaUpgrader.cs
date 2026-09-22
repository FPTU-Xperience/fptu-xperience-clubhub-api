using Microsoft.EntityFrameworkCore;

namespace ExportService.Data;

public static class ExportSchemaUpgrader
{
    public static async Task ApplyAsync(ExportDbContext db, CancellationToken cancellationToken = default)
    {
        const string sql = """
            IF OBJECT_ID(N'[dbo].[OutboxMessages]', N'U') IS NULL
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

        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }
}
