using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClubService.Migrations
{
    /// <inheritdoc />
    public partial class AddClubDataIntegrityInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClubOwnershipTransfers_ClubId",
                table: "ClubOwnershipTransfers");

            migrationBuilder.DropIndex(
                name: "IX_ClubDisbandRequests_ClubId",
                table: "ClubDisbandRequests");

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "Clubs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<int>(
                name: "TreasurerSlot",
                table: "ClubMemberships",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(@"
                ;WITH ExistingTreasurers AS (
                    SELECT Id, ROW_NUMBER() OVER (PARTITION BY ClubId ORDER BY Id) AS SlotNum
                    FROM ClubMemberships
                    WHERE Role = 'TREASURER' AND Status = 'Approved' AND IsDeleted = 0
                )
                UPDATE m
                SET m.TreasurerSlot = t.SlotNum
                FROM ClubMemberships m
                INNER JOIN ExistingTreasurers t ON m.Id = t.Id
                WHERE t.SlotNum <= 2;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_ClubOwnershipTransfers_ClubId",
                table: "ClubOwnershipTransfers",
                column: "ClubId",
                unique: true,
                filter: "[Status] = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_ClubMemberships_ClubId_TreasurerSlot",
                table: "ClubMemberships",
                columns: new[] { "ClubId", "TreasurerSlot" },
                unique: true,
                filter: "[TreasurerSlot] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClubMemberships_TreasurerSlot",
                table: "ClubMemberships",
                sql: "[TreasurerSlot] IS NULL OR [TreasurerSlot] IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_ClubManagerAssignments_ClubId",
                table: "ClubManagerAssignments",
                column: "ClubId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ClubManagerAssignments_ManagerUserId",
                table: "ClubManagerAssignments",
                column: "ManagerUserId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ClubDisbandRequests_ClubId",
                table: "ClubDisbandRequests",
                column: "ClubId",
                unique: true,
                filter: "[Status] = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClubOwnershipTransfers_ClubId",
                table: "ClubOwnershipTransfers");

            migrationBuilder.DropIndex(
                name: "IX_ClubMemberships_ClubId_TreasurerSlot",
                table: "ClubMemberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClubMemberships_TreasurerSlot",
                table: "ClubMemberships");

            migrationBuilder.DropIndex(
                name: "IX_ClubManagerAssignments_ClubId",
                table: "ClubManagerAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ClubManagerAssignments_ManagerUserId",
                table: "ClubManagerAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ClubDisbandRequests_ClubId",
                table: "ClubDisbandRequests");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "TreasurerSlot",
                table: "ClubMemberships");

            migrationBuilder.CreateIndex(
                name: "IX_ClubOwnershipTransfers_ClubId",
                table: "ClubOwnershipTransfers",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubDisbandRequests_ClubId",
                table: "ClubDisbandRequests",
                column: "ClubId");
        }
    }
}
