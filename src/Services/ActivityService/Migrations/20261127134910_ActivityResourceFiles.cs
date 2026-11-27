using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityService.Migrations
{
    /// <inheritdoc />
    public partial class ActivityResourceFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "ActivityResource",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MimeType",
                table: "ActivityResource",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sha256",
                table: "ActivityResource",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                table: "ActivityResource",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActivityResourceFile",
                columns: table => new
                {
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Bytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityResourceFile", x => x.ResourceId);
                    table.ForeignKey(
                        name: "FK_ActivityResourceFile_ActivityResource_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "ActivityResource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityResourceFile");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "ActivityResource");

            migrationBuilder.DropColumn(
                name: "MimeType",
                table: "ActivityResource");

            migrationBuilder.DropColumn(
                name: "Sha256",
                table: "ActivityResource");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                table: "ActivityResource");
        }
    }
}
