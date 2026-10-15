using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExportService.Migrations
{
    /// <inheritdoc />
    public partial class ClubExperiencePdfReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "ClubExperienceReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    SemesterId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SnapshotHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PdfBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubExperienceReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiOperationReceipt_ActorId_Operation_Key",
                table: "ApiOperationReceipt",
                columns: new[] { "ActorId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubExperienceReports_ClubId_SemesterId",
                table: "ClubExperienceReports",
                columns: new[] { "ClubId", "SemesterId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClubExperienceReports_Status_LeaseUntil",
                table: "ClubExperienceReports",
                columns: new[] { "Status", "LeaseUntil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiOperationReceipt");

            migrationBuilder.DropTable(
                name: "ClubExperienceReports");
        }
    }
}
