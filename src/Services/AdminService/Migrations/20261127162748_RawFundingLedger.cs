using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminService.Migrations
{
    /// <inheritdoc />
    public partial class RawFundingLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RawFundingAccount",
                columns: table => new
                {
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    CampusCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SemesterId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Granted = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Available = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Reserved = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Spent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawFundingAccount", x => new { x.ClubId, x.CampusCode, x.SemesterId });
                });

            migrationBuilder.CreateTable(
                name: "RawFundingReservation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    CampusCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SemesterId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    SourceVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    PolicyVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Budget = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Spent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservedBy = table.Column<int>(type: "int", nullable: false),
                    ReservedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawFundingReservation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawFundingReservation_RawFundingAccount_ClubId_CampusCode_SemesterId",
                        columns: x => new { x.ClubId, x.CampusCode, x.SemesterId },
                        principalTable: "RawFundingAccount",
                        principalColumns: new[] { "ClubId", "CampusCode", "SemesterId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawFundingEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    CampusCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SemesterId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExperienceEffectId = table.Column<long>(type: "bigint", nullable: true),
                    OperationKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    GrantedDelta = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AvailableDelta = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReservedDelta = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SpentDelta = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawFundingEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawFundingEntry_ExperienceLedgerEffects_ExperienceEffectId",
                        column: x => x.ExperienceEffectId,
                        principalTable: "ExperienceLedgerEffects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawFundingEntry_RawFundingAccount_ClubId_CampusCode_SemesterId",
                        columns: x => new { x.ClubId, x.CampusCode, x.SemesterId },
                        principalTable: "RawFundingAccount",
                        principalColumns: new[] { "ClubId", "CampusCode", "SemesterId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawFundingEntry_RawFundingReservation_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "RawFundingReservation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawFundingEntry_ClubId_CampusCode_SemesterId_RecordedAt",
                table: "RawFundingEntry",
                columns: new[] { "ClubId", "CampusCode", "SemesterId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RawFundingEntry_ExperienceEffectId",
                table: "RawFundingEntry",
                column: "ExperienceEffectId");

            migrationBuilder.CreateIndex(
                name: "IX_RawFundingEntry_OperationKey",
                table: "RawFundingEntry",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawFundingEntry_ReservationId",
                table: "RawFundingEntry",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_RawFundingReservation_ActivityId",
                table: "RawFundingReservation",
                column: "ActivityId",
                unique: true,
                filter: "[ActivityId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RawFundingReservation_ClubId_CampusCode_SemesterId",
                table: "RawFundingReservation",
                columns: new[] { "ClubId", "CampusCode", "SemesterId" });

            migrationBuilder.CreateIndex(
                name: "IX_RawFundingReservation_SourceKey_SourceVersion",
                table: "RawFundingReservation",
                columns: new[] { "SourceKey", "SourceVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RawFundingEntry");

            migrationBuilder.DropTable(
                name: "RawFundingReservation");

            migrationBuilder.DropTable(
                name: "RawFundingAccount");
        }
    }
}
