using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExportService.Migrations
{
    /// <inheritdoc />
    public partial class IndexExportsByRequester : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ExportRequests_RequestedByUserId_CreatedAtUtc",
                table: "ExportRequests",
                columns: new[] { "RequestedByUserId", "CreatedAtUtc" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExportRequests_RequestedByUserId_CreatedAtUtc",
                table: "ExportRequests");
        }
    }
}
