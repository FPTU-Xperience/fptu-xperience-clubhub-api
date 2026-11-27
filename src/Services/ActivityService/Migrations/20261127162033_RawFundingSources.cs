using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityService.Migrations
{
    /// <inheritdoc />
    public partial class RawFundingSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FundingReservationId",
                table: "Activities",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RawBudget",
                table: "Activities",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActivityFundingCandidate",
                columns: table => new
                {
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityFundingCandidate", x => new { x.ActivityId, x.UserId, x.SourceKey });
                    table.ForeignKey(
                        name: "FK_ActivityFundingCandidate_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityFundingCandidate");

            migrationBuilder.DropColumn(
                name: "FundingReservationId",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "RawBudget",
                table: "Activities");
        }
    }
}
