using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClubService.Migrations
{
    /// <inheritdoc />
    public partial class AddCampusCodeToClubs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CampusCode",
                table: "Clubs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "HAN");

            migrationBuilder.AddColumn<string>(
                name: "CampusCode",
                table: "ClubCreationApplications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "HAN");

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_CampusCode",
                table: "Clubs",
                column: "CampusCode");

            migrationBuilder.CreateIndex(
                name: "IX_ClubCreationApplications_CampusCode",
                table: "ClubCreationApplications",
                column: "CampusCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clubs_CampusCode",
                table: "Clubs");

            migrationBuilder.DropIndex(
                name: "IX_ClubCreationApplications_CampusCode",
                table: "ClubCreationApplications");

            migrationBuilder.DropColumn(
                name: "CampusCode",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "CampusCode",
                table: "ClubCreationApplications");
        }
    }
}
