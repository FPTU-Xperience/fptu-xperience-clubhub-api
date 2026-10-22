using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Migrations
{
    /// <inheritdoc />
    public partial class PrivateStudySchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudyScheduleBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Term = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Block = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    SemesterKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyScheduleBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudyScheduleBlocks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudyScheduleCells",
                columns: table => new
                {
                    BlockId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Week = table.Column<int>(type: "int", nullable: false),
                    Day = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    SlotNumber = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Room = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyScheduleCells", x => new { x.BlockId, x.Week, x.Day, x.SlotNumber });
                    table.ForeignKey(
                        name: "FK_StudyScheduleCells_StudyScheduleBlocks_BlockId",
                        column: x => x.BlockId,
                        principalTable: "StudyScheduleBlocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudyScheduleBlocks_UserId_Year_Term_Block",
                table: "StudyScheduleBlocks",
                columns: new[] { "UserId", "Year", "Term", "Block" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudyScheduleCells");

            migrationBuilder.DropTable(
                name: "StudyScheduleBlocks");
        }
    }
}
