using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClubService.Migrations
{
    /// <inheritdoc />
    public partial class DiscoveryTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TagSelectionJson",
                table: "Clubs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagSelectionJson",
                table: "ClubCreationApplications",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TagSelectionJson",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "TagSelectionJson",
                table: "ClubCreationApplications");
        }
    }
}
