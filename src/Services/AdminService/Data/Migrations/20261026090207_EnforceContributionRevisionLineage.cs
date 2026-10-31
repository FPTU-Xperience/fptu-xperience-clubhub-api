using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminService.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceContributionRevisionLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM [dbo].[ExperienceLedgerEffects] e
                    LEFT JOIN [dbo].[ContributionRevisions] r ON r.[Id] = e.[RevisionId] AND r.[ContributionId] = e.[ContributionId]
                    WHERE r.[Id] IS NULL)
                    THROW 51042, 'Ledger effects contain missing or foreign contribution revisions. Reconcile historical provenance before retrying this migration.', 1;
                IF EXISTS (
                    SELECT 1 FROM [dbo].[ContributionDecisions] d
                    LEFT JOIN [dbo].[ContributionRevisions] r ON r.[Id] = d.[RevisionId] AND r.[ContributionId] = d.[ContributionId]
                    WHERE r.[Id] IS NULL)
                    THROW 51043, 'Contribution decisions contain missing or foreign revisions. Reconcile historical provenance before retrying this migration.', 1;
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ContributionRevisions_ContributionId_Id",
                table: "ContributionRevisions",
                columns: new[] { "ContributionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionDecisions_ContributionId_RevisionId",
                table: "ContributionDecisions",
                columns: new[] { "ContributionId", "RevisionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ContributionDecisions_ContributionRevisions_ContributionId_RevisionId",
                table: "ContributionDecisions",
                columns: new[] { "ContributionId", "RevisionId" },
                principalTable: "ContributionRevisions",
                principalColumns: new[] { "ContributionId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ExperienceLedgerEffects_ContributionRevisions_ContributionId_RevisionId",
                table: "ExperienceLedgerEffects",
                columns: new[] { "ContributionId", "RevisionId" },
                principalTable: "ContributionRevisions",
                principalColumns: new[] { "ContributionId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContributionDecisions_ContributionRevisions_ContributionId_RevisionId",
                table: "ContributionDecisions");

            migrationBuilder.DropForeignKey(
                name: "FK_ExperienceLedgerEffects_ContributionRevisions_ContributionId_RevisionId",
                table: "ExperienceLedgerEffects");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ContributionRevisions_ContributionId_Id",
                table: "ContributionRevisions");

            migrationBuilder.DropIndex(
                name: "IX_ContributionDecisions_ContributionId_RevisionId",
                table: "ContributionDecisions");
        }
    }
}
