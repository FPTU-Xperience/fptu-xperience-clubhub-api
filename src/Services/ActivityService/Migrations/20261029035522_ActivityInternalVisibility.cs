using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityService.Migrations
{
    /// <inheritdoc />
    public partial class ActivityInternalVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsInternal",
                table: "Activities",
                type: "bit",
                nullable: false,
                defaultValue: true);
            // Preserve existing audience decisions; all other records stay internal.
            migrationBuilder.Sql("UPDATE [Activities] SET [IsInternal] = 0 WHERE [DiscoveryVisibility] = N'CAMPUS_PUBLIC';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsInternal",
                table: "Activities");
        }
    }
}
