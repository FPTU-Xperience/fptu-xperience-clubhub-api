using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestsAndParticipants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Quests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RewardXp = table.Column<int>(type: "int", nullable: false),
                    TargetParticipants = table.Column<int>(type: "int", nullable: false),
                    SemesterCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CampusCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DeadlineUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuestParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentUserId = table.Column<int>(type: "int", nullable: false),
                    StudentName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    StudentEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    CampusCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    JoinedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    VerifiedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestParticipants_Quests_QuestId",
                        column: x => x.QuestId,
                        principalTable: "Quests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestParticipants_QuestId_StudentUserId",
                table: "QuestParticipants",
                columns: new[] { "QuestId", "StudentUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestParticipants_StudentUserId",
                table: "QuestParticipants",
                column: "StudentUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Quests_CampusCode",
                table: "Quests",
                column: "CampusCode");

            migrationBuilder.CreateIndex(
                name: "IX_Quests_SemesterCode",
                table: "Quests",
                column: "SemesterCode");

            migrationBuilder.CreateIndex(
                name: "IX_Quests_Status",
                table: "Quests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestParticipants");

            migrationBuilder.DropTable(
                name: "Quests");
        }
    }
}
