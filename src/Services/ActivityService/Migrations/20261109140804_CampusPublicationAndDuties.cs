using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityService.Migrations
{
    /// <inheritdoc />
    public partial class CampusPublicationAndDuties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CampusPublishAtUtc",
                table: "Activities",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CampusActivityDuty",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Responsibility = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedBy = table.Column<int>(type: "int", nullable: false),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConfirmedBy = table.Column<int>(type: "int", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampusActivityDuty", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampusActivityDuty_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_CampusPublishAtUtc",
                table: "Activities",
                column: "CampusPublishAtUtc",
                filter: "[CampusPublishAtUtc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CampusActivityDuty_ActivityId_UserId_Role",
                table: "CampusActivityDuty",
                columns: new[] { "ActivityId", "UserId", "Role" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampusActivityDuty");

            migrationBuilder.DropIndex(
                name: "IX_Activities_CampusPublishAtUtc",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "CampusPublishAtUtc",
                table: "Activities");
        }
    }
}
