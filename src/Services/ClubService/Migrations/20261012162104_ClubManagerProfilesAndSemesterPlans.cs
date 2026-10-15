using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClubService.Migrations
{
    /// <inheritdoc />
    public partial class ClubManagerProfilesAndSemesterPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverImageUrl",
                table: "Clubs",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleJson",
                table: "Clubs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "ClubMemberships",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "ApiOperationReceipt",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiOperationReceipt", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SemesterPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    SemesterId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TargetActivityCount = table.Column<int>(type: "int", nullable: false),
                    TargetSessionCount = table.Column<int>(type: "int", nullable: false),
                    TargetUniqueParticipantCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemesterPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SemesterPlans_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanRevisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanRevisions_SemesterPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "SemesterPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiOperationReceipt_ActorId_Operation_Key",
                table: "ApiOperationReceipt",
                columns: new[] { "ActorId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanRevisions_PlanId_Version",
                table: "PlanRevisions",
                columns: new[] { "PlanId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemesterPlans_ClubId_SemesterId",
                table: "SemesterPlans",
                columns: new[] { "ClubId", "SemesterId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiOperationReceipt");

            migrationBuilder.DropTable(
                name: "PlanRevisions");

            migrationBuilder.DropTable(
                name: "SemesterPlans");

            migrationBuilder.DropColumn(
                name: "CoverImageUrl",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "ScheduleJson",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "ClubMemberships");
        }
    }
}
