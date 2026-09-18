using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClubService.Migrations
{
    /// <inheritdoc />
    public partial class AddClubDiscoveryFieldsAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Earlier releases created some of these objects from the startup
            // schema upgrader. Conditional DDL lets those databases record this
            // migration without attempting duplicate, destructive changes.
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'[dbo].[Clubs]', N'IsRecruiting') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[Clubs]
                        ADD [IsRecruiting] BIT NOT NULL
                        CONSTRAINT [DF_Clubs_IsRecruiting] DEFAULT 0;
                END

                IF COL_LENGTH(N'[dbo].[Clubs]', N'ScheduleLabel') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[Clubs]
                        ADD [ScheduleLabel] NVARCHAR(500) NULL;
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'[dbo].[Clubs]')
                      AND name = N'IX_Clubs_IsActive')
                BEGIN
                    CREATE INDEX [IX_Clubs_IsActive] ON [dbo].[Clubs] ([IsActive]);
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'[dbo].[ClubOwnershipTransfers]')
                      AND name = N'IX_ClubOwnershipTransfers_Status')
                BEGIN
                    CREATE INDEX [IX_ClubOwnershipTransfers_Status]
                        ON [dbo].[ClubOwnershipTransfers] ([Status]);
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'[dbo].[ClubMemberships]')
                      AND name = N'IX_ClubMemberships_UserId_Status')
                BEGIN
                    CREATE INDEX [IX_ClubMemberships_UserId_Status]
                        ON [dbo].[ClubMemberships] ([UserId], [Status]);
                END

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'[dbo].[ClubDisbandRequests]')
                      AND name = N'IX_ClubDisbandRequests_Status')
                BEGIN
                    CREATE INDEX [IX_ClubDisbandRequests_Status]
                        ON [dbo].[ClubDisbandRequests] ([Status]);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally non-destructive. These columns and indexes may have
            // existed before this migration was recorded, so a rollback must not
            // discard production data or remove compatibility-managed indexes.
        }
    }
}
