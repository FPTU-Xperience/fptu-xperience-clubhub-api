using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminService.Data.Migrations
{
    /// <inheritdoc />
    public partial class DiscoveryCaptureSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscoveryCaptureJson",
                table: "PlatformSettings",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscoveryCaptureJson",
                table: "PlatformSettings");
        }
    }
}
