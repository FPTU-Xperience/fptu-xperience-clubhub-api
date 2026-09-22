using Microsoft.EntityFrameworkCore;

namespace ActivityService.Data;

public static class ActivitySchemaUpgrader
{
    public static Task ApplyAsync(ActivityDbContext db, CancellationToken cancellationToken = default)
    {
        const string sql = """
            -- 1. IX_ActivityAttendances_UserId_AttendanceDate: Drop if exists without included columns, then create with INCLUDE
            IF EXISTS (
                SELECT 1 FROM sys.indexes i
                WHERE i.object_id = OBJECT_ID(N'[dbo].[ActivityAttendances]')
                  AND i.name = N'IX_ActivityAttendances_UserId_AttendanceDate'
                  AND NOT EXISTS (
                      SELECT 1 FROM sys.index_columns ic
                      WHERE ic.object_id = i.object_id
                        AND ic.index_id = i.index_id
                        AND ic.is_included_column = 1
                  )
            )
            BEGIN
                DROP INDEX [IX_ActivityAttendances_UserId_AttendanceDate] ON [dbo].[ActivityAttendances];
            END

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[dbo].[ActivityAttendances]')
                  AND name = N'IX_ActivityAttendances_UserId_AttendanceDate')
            BEGIN
                CREATE INDEX [IX_ActivityAttendances_UserId_AttendanceDate]
                    ON [dbo].[ActivityAttendances] ([UserId], [AttendanceDate])
                    INCLUDE ([ActivityId], [Status]);
            END

            -- 2. IX_ActivityParticipants_UserId: Drop if exists without included columns, then create with INCLUDE
            IF EXISTS (
                SELECT 1 FROM sys.indexes i
                WHERE i.object_id = OBJECT_ID(N'[dbo].[ActivityParticipants]')
                  AND i.name = N'IX_ActivityParticipants_UserId'
                  AND NOT EXISTS (
                      SELECT 1 FROM sys.index_columns ic
                      WHERE ic.object_id = i.object_id
                        AND ic.index_id = i.index_id
                        AND ic.is_included_column = 1
                  )
            )
            BEGIN
                DROP INDEX [IX_ActivityParticipants_UserId] ON [dbo].[ActivityParticipants];
            END

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[dbo].[ActivityParticipants]')
                  AND name = N'IX_ActivityParticipants_UserId')
            BEGIN
                CREATE INDEX [IX_ActivityParticipants_UserId]
                    ON [dbo].[ActivityParticipants] ([UserId])
                    INCLUDE ([ActivityId], [AttendanceStatus]);
            END

            -- 3. IX_Activities_StartTimeUtc: Drop if exists without included columns, then create with INCLUDE
            IF EXISTS (
                SELECT 1 FROM sys.indexes i
                WHERE i.object_id = OBJECT_ID(N'[dbo].[Activities]')
                  AND i.name = N'IX_Activities_StartTimeUtc'
                  AND NOT EXISTS (
                      SELECT 1 FROM sys.index_columns ic
                      WHERE ic.object_id = i.object_id
                        AND ic.index_id = i.index_id
                        AND ic.is_included_column = 1
                  )
            )
            BEGIN
                DROP INDEX [IX_Activities_StartTimeUtc] ON [dbo].[Activities];
            END

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[dbo].[Activities]')
                  AND name = N'IX_Activities_StartTimeUtc')
            BEGIN
                CREATE INDEX [IX_Activities_StartTimeUtc]
                    ON [dbo].[Activities] ([StartTimeUtc])
                    INCLUDE ([ClubId], [Title], [Status]);
            END

            -- 4. OutboxMessages table and claim indexes
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
            return db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }

        return Task.CompletedTask;
    }
}
