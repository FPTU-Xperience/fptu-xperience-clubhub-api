using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfDeclarations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SelfDeclarations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    StudentName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    StudentEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OrganizationSource = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsOutsideClub = table.Column<bool>(type: "bit", nullable: false),
                    ClubId = table.Column<int>(type: "int", nullable: true),
                    EvidenceUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EvidenceDescription = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RoleProposed = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FinalCategory = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Tier = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Scale = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BonusResult = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RawPoints = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelfDeclarations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SelfDeclarations_Category",
                table: "SelfDeclarations",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_SelfDeclarations_CreatedAtUtc",
                table: "SelfDeclarations",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SelfDeclarations_Status",
                table: "SelfDeclarations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SelfDeclarations_StudentId",
                table: "SelfDeclarations",
                column: "StudentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SelfDeclarations");
        }
    }
}
