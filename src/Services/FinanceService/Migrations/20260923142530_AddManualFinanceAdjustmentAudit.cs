using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceService.Migrations
{
    /// <inheritdoc />
    public partial class AddManualFinanceAdjustmentAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManualFinanceAdjustments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FinanceTransactionId = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualFinanceAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManualFinanceAdjustments_FinanceTransactions_FinanceTransactionId",
                        column: x => x.FinanceTransactionId,
                        principalTable: "FinanceTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManualFinanceAdjustments_FinanceTransactionId",
                table: "ManualFinanceAdjustments",
                column: "FinanceTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManualFinanceAdjustments_IdempotencyKey",
                table: "ManualFinanceAdjustments",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManualFinanceAdjustments");
        }
    }
}
