using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceService.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveSettlementInvariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Settlements_BudgetProposalId",
                table: "Settlements");

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_BudgetProposalId",
                table: "Settlements",
                column: "BudgetProposalId",
                unique: true,
                filter: "[Status] <> 'Rejected'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Settlements_BudgetProposalId",
                table: "Settlements");

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_BudgetProposalId",
                table: "Settlements",
                column: "BudgetProposalId");
        }
    }
}
