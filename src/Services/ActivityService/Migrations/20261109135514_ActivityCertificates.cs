using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityService.Migrations
{
    /// <inheritdoc />
    public partial class ActivityCertificates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityCertificateDefinition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BadgePng = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FramePng = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityCertificateDefinition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityCertificateDefinition_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivityCertificate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientKind = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    RecipientId = table.Column<int>(type: "int", nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ActivityTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IssuerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityCertificate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityCertificate_ActivityCertificateDefinition_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "ActivityCertificateDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityCertificate_ActivityId_RecipientKind_RecipientId_DefinitionId",
                table: "ActivityCertificate",
                columns: new[] { "ActivityId", "RecipientKind", "RecipientId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityCertificate_DefinitionId",
                table: "ActivityCertificate",
                column: "DefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityCertificate_RecipientKind_RecipientId_IssuedAtUtc",
                table: "ActivityCertificate",
                columns: new[] { "RecipientKind", "RecipientId", "IssuedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityCertificateDefinition_ActivityId_Role",
                table: "ActivityCertificateDefinition",
                columns: new[] { "ActivityId", "Role" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityCertificate");

            migrationBuilder.DropTable(
                name: "ActivityCertificateDefinition");
        }
    }
}
