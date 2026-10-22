using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminService.Data.Migrations
{
    /// <inheritdoc />
    public partial class SemesterStudentRosterEmailIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SemesterRoster",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SemesterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampusCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemesterRoster", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SemesterRoster_Semesters_SemesterId",
                        column: x => x.SemesterId,
                        principalTable: "Semesters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SemesterRosterImport",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SemesterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampusCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    BaseVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    CandidatesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErrorsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemesterRosterImport", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SemesterRosterImport_Semesters_SemesterId",
                        column: x => x.SemesterId,
                        principalTable: "Semesters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SemesterRosterRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RosterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemesterRosterRevision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SemesterRosterRevision_SemesterRosterImport_ImportId",
                        column: x => x.ImportId,
                        principalTable: "SemesterRosterImport",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SemesterRosterRevision_SemesterRosterRevision_SourceRevisionId",
                        column: x => x.SourceRevisionId,
                        principalTable: "SemesterRosterRevision",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SemesterRosterRevision_SemesterRoster_RosterId",
                        column: x => x.RosterId,
                        principalTable: "SemesterRoster",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SemesterRosterRow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StudentCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CampusCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemesterRosterRow", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SemesterRosterRow_SemesterRosterRevision_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "SemesterRosterRevision",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SemesterRoster_SemesterId_CampusCode",
                table: "SemesterRoster",
                columns: new[] { "SemesterId", "CampusCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemesterRosterImport_SemesterId_CampusCode",
                table: "SemesterRosterImport",
                columns: new[] { "SemesterId", "CampusCode" });

            migrationBuilder.CreateIndex(
                name: "IX_SemesterRosterRevision_ImportId",
                table: "SemesterRosterRevision",
                column: "ImportId",
                unique: true,
                filter: "[ImportId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SemesterRosterRevision_RosterId",
                table: "SemesterRosterRevision",
                column: "RosterId");

            migrationBuilder.CreateIndex(
                name: "IX_SemesterRosterRevision_SourceRevisionId",
                table: "SemesterRosterRevision",
                column: "SourceRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_SemesterRosterRow_RevisionId_StudentCode",
                table: "SemesterRosterRow",
                columns: new[] { "RevisionId", "StudentCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemesterRosterRow_RevisionId_UserId",
                table: "SemesterRosterRow",
                columns: new[] { "RevisionId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SemesterRosterRow");

            migrationBuilder.DropTable(
                name: "SemesterRosterRevision");

            migrationBuilder.DropTable(
                name: "SemesterRosterImport");

            migrationBuilder.DropTable(
                name: "SemesterRoster");
        }
    }
}
