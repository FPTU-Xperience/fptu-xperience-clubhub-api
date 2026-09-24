using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceService.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileActiveSettlementIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'[dbo].[Settlements]')
                      AND name = N'IX_Settlements_BudgetProposalId')
                    DROP INDEX [IX_Settlements_BudgetProposalId] ON [dbo].[Settlements];
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_BudgetProposalId",
                table: "Settlements",
                column: "BudgetProposalId",
                unique: true,
                filter: "[Status] IN ('Submitted', 'Approved')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The preceding migration already specifies this index. Keep its invariant on rollback.
        }
    }
}
