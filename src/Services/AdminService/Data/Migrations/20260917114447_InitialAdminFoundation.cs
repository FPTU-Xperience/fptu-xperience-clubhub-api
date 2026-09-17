using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAdminFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorSubjectId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ActorEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    ActorRolesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ResourceId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_Action_TimestampUtc",
                table: "AuditRecords",
                columns: new[] { "Action", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_ActorSubjectId_TimestampUtc",
                table: "AuditRecords",
                columns: new[] { "ActorSubjectId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_TimestampUtc",
                table: "AuditRecords",
                column: "TimestampUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditRecords");
        }
    }
}
