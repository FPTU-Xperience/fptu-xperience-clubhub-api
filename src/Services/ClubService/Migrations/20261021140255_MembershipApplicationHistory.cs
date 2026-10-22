using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClubService.Migrations
{
    /// <inheritdoc />
    public partial class MembershipApplicationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClubMemberships_ClubId_UserId",
                table: "ClubMemberships");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConsentedAtUtc",
                table: "ClubMemberships",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InvitedAtUtc",
                table: "ClubMemberships",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvitedByUserId",
                table: "ClubMemberships",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreviousApplicationId",
                table: "ClubMemberships",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SemesterId",
                table: "ClubMemberships",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "ClubMemberships",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "UNKNOWN");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WithdrawnAtUtc",
                table: "ClubMemberships",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WithdrawnByUserId",
                table: "ClubMemberships",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MembershipActions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MembershipId = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PreviousVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MembershipActions_ClubMemberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "ClubMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClubMemberships_ClubId_UserId",
                table: "ClubMemberships",
                columns: new[] { "ClubId", "UserId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ClubMemberships_PreviousApplicationId",
                table: "ClubMemberships",
                column: "PreviousApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipActions_MembershipId_Action",
                table: "MembershipActions",
                columns: new[] { "MembershipId", "Action" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ClubMemberships_ClubMemberships_PreviousApplicationId",
                table: "ClubMemberships",
                column: "PreviousApplicationId",
                principalTable: "ClubMemberships",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClubMemberships_ClubMemberships_PreviousApplicationId",
                table: "ClubMemberships");

            migrationBuilder.DropTable(
                name: "MembershipActions");

            migrationBuilder.DropIndex(
                name: "IX_ClubMemberships_ClubId_UserId",
                table: "ClubMemberships");

            migrationBuilder.DropIndex(
                name: "IX_ClubMemberships_PreviousApplicationId",
                table: "ClubMemberships");

            migrationBuilder.DropColumn(
                name: "ConsentedAtUtc",
                table: "ClubMemberships");

            migrationBuilder.DropColumn(
                name: "InvitedAtUtc",
                table: "ClubMemberships");

            migrationBuilder.DropColumn(
                name: "InvitedByUserId",
                table: "ClubMemberships");

            migrationBuilder.DropColumn(
                name: "PreviousApplicationId",
                table: "ClubMemberships");

            migrationBuilder.DropColumn(
                name: "SemesterId",
                table: "ClubMemberships");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ClubMemberships");

            migrationBuilder.DropColumn(
                name: "WithdrawnAtUtc",
                table: "ClubMemberships");

            migrationBuilder.DropColumn(
                name: "WithdrawnByUserId",
                table: "ClubMemberships");

            migrationBuilder.CreateIndex(
                name: "IX_ClubMemberships_ClubId_UserId",
                table: "ClubMemberships",
                columns: new[] { "ClubId", "UserId" },
                unique: true);
        }
    }
}
