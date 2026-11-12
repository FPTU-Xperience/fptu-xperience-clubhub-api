using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminService.Data.Migrations
{
    /// <inheritdoc />
    public partial class DiscoveryInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiscoveryInteractions",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityKind = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TagIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryInteractions", x => new { x.UserId, x.EventId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryInteractions_OccurredAtUtc",
                table: "DiscoveryInteractions",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryInteractions_UserId_OccurredAtUtc",
                table: "DiscoveryInteractions",
                columns: new[] { "UserId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscoveryInteractions");
        }
    }
}
