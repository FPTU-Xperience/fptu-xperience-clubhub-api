using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnomaliesAndLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "XpAnomalies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Club = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Student = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    StudentUserId = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    SemesterCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CampusCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Adjustment = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: true),
                    ResolvedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XpAnomalies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "XpLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentUserId = table.Column<int>(type: "int", nullable: false),
                    StudentName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    RubricVersion = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    PillarCategory = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SemesterCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CampusCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RelatedAnomalyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XpLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_XpAnomalies_CampusCode",
                table: "XpAnomalies",
                column: "CampusCode");

            migrationBuilder.CreateIndex(
                name: "IX_XpAnomalies_SemesterCode",
                table: "XpAnomalies",
                column: "SemesterCode");

            migrationBuilder.CreateIndex(
                name: "IX_XpAnomalies_Severity",
                table: "XpAnomalies",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_XpAnomalies_Status",
                table: "XpAnomalies",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_XpLedgerEntries_CampusCode",
                table: "XpLedgerEntries",
                column: "CampusCode");

            migrationBuilder.CreateIndex(
                name: "IX_XpLedgerEntries_CreatedAtUtc",
                table: "XpLedgerEntries",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_XpLedgerEntries_SemesterCode",
                table: "XpLedgerEntries",
                column: "SemesterCode");

            migrationBuilder.CreateIndex(
                name: "IX_XpLedgerEntries_StudentUserId",
                table: "XpLedgerEntries",
                column: "StudentUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "XpAnomalies");

            migrationBuilder.DropTable(
                name: "XpLedgerEntries");
        }
    }
}
