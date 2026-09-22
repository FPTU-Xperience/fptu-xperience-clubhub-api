using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxMessageClaimFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'[dbo].[OutboxMessages]', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'[dbo].[OutboxMessages]', N'ClaimExpiresAtUtc') IS NULL
                        ALTER TABLE [dbo].[OutboxMessages] ADD [ClaimExpiresAtUtc] datetimeoffset NULL;

                    IF COL_LENGTH(N'[dbo].[OutboxMessages]', N'ClaimedAtUtc') IS NULL
                        ALTER TABLE [dbo].[OutboxMessages] ADD [ClaimedAtUtc] datetimeoffset NULL;

                    IF COL_LENGTH(N'[dbo].[OutboxMessages]', N'ClaimedByInstanceId') IS NULL
                        ALTER TABLE [dbo].[OutboxMessages] ADD [ClaimedByInstanceId] nvarchar(100) NULL;

                    IF COL_LENGTH(N'[dbo].[OutboxMessages]', N'ConcurrencyToken') IS NULL
                        ALTER TABLE [dbo].[OutboxMessages] ADD [ConcurrencyToken] uniqueidentifier NOT NULL DEFAULT NEWID();

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OutboxMessages_Status_ClaimExpiresAtUtc_OccurredAtUtc' AND object_id = OBJECT_ID(N'[dbo].[OutboxMessages]'))
                        CREATE INDEX [IX_OutboxMessages_Status_ClaimExpiresAtUtc_OccurredAtUtc] ON [dbo].[OutboxMessages] ([Status], [ClaimExpiresAtUtc], [OccurredAtUtc]);
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Status_ClaimExpiresAtUtc_OccurredAtUtc",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ClaimExpiresAtUtc",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ClaimedAtUtc",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ClaimedByInstanceId",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "OutboxMessages");
        }
    }
}
