using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminService.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClubDeclarationReferral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClubReviewReason",
                table: "Contributions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClubReviewStatus",
                table: "Contributions",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClubReviewedBy",
                table: "Contributions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CtsvSubmissionStatus",
                table: "Contributions",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CtsvSubmittedBy",
                table: "Contributions",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClubReviewReason",
                table: "Contributions");

            migrationBuilder.DropColumn(
                name: "ClubReviewStatus",
                table: "Contributions");

            migrationBuilder.DropColumn(
                name: "ClubReviewedBy",
                table: "Contributions");

            migrationBuilder.DropColumn(
                name: "CtsvSubmissionStatus",
                table: "Contributions");

            migrationBuilder.DropColumn(
                name: "CtsvSubmittedBy",
                table: "Contributions");
        }
    }
}
