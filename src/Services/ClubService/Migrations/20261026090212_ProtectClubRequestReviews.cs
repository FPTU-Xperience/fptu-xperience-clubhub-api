using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClubService.Migrations
{
    /// <inheritdoc />
    public partial class ProtectClubRequestReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- Run inside the generated Club migration before the new filtered unique index.
                -- Existing data must be reconciled explicitly; preserve all application history.
                IF EXISTS (
                    SELECT [RequesterUserId]
                    FROM [dbo].[ClubCreationApplications]
                    WHERE [Status] = N'Submitted'
                    GROUP BY [RequesterUserId]
                    HAVING COUNT(*) > 1
                )
                    THROW 51031, 'Cannot apply club pending-application uniqueness: multiple Submitted applications exist for one requester. Reconcile these applications before retrying the migration.', 1;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "ClubOwnershipTransfers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "ClubDisbandRequests",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "ClubCreationApplications",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.CreateIndex(
                name: "IX_ClubCreationApplications_RequesterUserId",
                table: "ClubCreationApplications",
                column: "RequesterUserId",
                unique: true,
                filter: "[Status] = 'Submitted'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClubCreationApplications_RequesterUserId",
                table: "ClubCreationApplications");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "ClubOwnershipTransfers");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "ClubDisbandRequests");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "ClubCreationApplications");
        }
    }
}
