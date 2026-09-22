using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReportPeriodTagUniqueConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'[dbo].[OutboxMessages]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[OutboxMessages] (
                        [Id] uniqueidentifier NOT NULL,
                        [OccurredAtUtc] datetimeoffset NOT NULL,
                        [EventType] nvarchar(100) NOT NULL,
                        [EventTypeName] nvarchar(300) NOT NULL,
                        [Payload] nvarchar(max) NOT NULL,
                        [Status] nvarchar(50) NOT NULL,
                        [RetryCount] int NOT NULL,
                        [ProcessedAtUtc] datetimeoffset NULL,
                        [ErrorMessage] nvarchar(max) NULL,
                        [CorrelationId] nvarchar(100) NULL,
                        CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([Id])
                    );
                END

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Reports_ClubId_Period_Tag' AND object_id = OBJECT_ID(N'[dbo].[Reports]'))
                    DROP INDEX [IX_Reports_ClubId_Period_Tag] ON [dbo].[Reports];

                CREATE UNIQUE INDEX [IX_Reports_ClubId_Period_Tag]
                    ON [dbo].[Reports] ([ClubId], [Period], [Tag])
                    WHERE [ReportType] <> 'FUTURE_EVENT';

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Reports_CreatedByUserId_Status' AND object_id = OBJECT_ID(N'[dbo].[Reports]'))
                    CREATE INDEX [IX_Reports_CreatedByUserId_Status] ON [dbo].[Reports] ([CreatedByUserId], [Status]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Reports_UpdatedAtUtc' AND object_id = OBJECT_ID(N'[dbo].[Reports]'))
                    CREATE INDEX [IX_Reports_UpdatedAtUtc] ON [dbo].[Reports] ([UpdatedAtUtc] DESC) INCLUDE ([ClubId], [Period], [Status]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLogs_ReportId' AND object_id = OBJECT_ID(N'[dbo].[AuditLogs]'))
                    CREATE INDEX [IX_AuditLogs_ReportId] ON [dbo].[AuditLogs] ([ReportId]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OutboxMessages_Status_OccurredAtUtc' AND object_id = OBJECT_ID(N'[dbo].[OutboxMessages]'))
                    CREATE INDEX [IX_OutboxMessages_Status_OccurredAtUtc] ON [dbo].[OutboxMessages] ([Status], [OccurredAtUtc]);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_Reports_ClubId_Period_Tag",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_Reports_CreatedByUserId_Status",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_Reports_UpdatedAtUtc",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_ReportId",
                table: "AuditLogs");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ClubId_Period_Tag",
                table: "Reports",
                columns: new[] { "ClubId", "Period", "Tag" });
        }
    }
}
