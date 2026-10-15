using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityService.Migrations
{
    /// <inheritdoc />
    public partial class StudentActivityDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscoveryVisibility",
                table: "Activities",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "CLUB_ONLY");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_DiscoveryVisibility_PublicationStatus_ClubId",
                table: "Activities",
                columns: new[] { "DiscoveryVisibility", "PublicationStatus", "ClubId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Activities_DiscoveryVisibility_PublicationStatus_ClubId",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "DiscoveryVisibility",
                table: "Activities");
        }
    }
}
