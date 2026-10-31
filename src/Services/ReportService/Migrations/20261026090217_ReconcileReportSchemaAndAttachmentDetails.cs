using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportService.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileReportSchemaAndAttachmentDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- Reconcile columns previously supplied only by ReportSchemaUpgrader.
                -- Run before the generated alternate key/index/foreign-key operations.
                IF COL_LENGTH(N'dbo.Reports', N'BudgetProposalId') IS NULL
                    ALTER TABLE [dbo].[Reports] ADD [BudgetProposalId] int NULL;
                IF COL_LENGTH(N'dbo.Reports', N'BudgetRequestedAmount') IS NULL
                    ALTER TABLE [dbo].[Reports] ADD [BudgetRequestedAmount] decimal(18,2) NULL;
                IF COL_LENGTH(N'dbo.Reports', N'BudgetApprovedAmount') IS NULL
                    ALTER TABLE [dbo].[Reports] ADD [BudgetApprovedAmount] decimal(18,2) NULL;
                IF COL_LENGTH(N'dbo.Reports', N'BudgetDescription') IS NULL
                    ALTER TABLE [dbo].[Reports] ADD [BudgetDescription] nvarchar(2000) NULL;
                IF COL_LENGTH(N'dbo.Reports', N'FinanceSubmittedAtUtc') IS NULL
                    ALTER TABLE [dbo].[Reports] ADD [FinanceSubmittedAtUtc] datetimeoffset NULL;
                IF COL_LENGTH(N'dbo.Reports', N'PublishedActivityId') IS NULL
                    ALTER TABLE [dbo].[Reports] ADD [PublishedActivityId] int NULL;

                IF COL_LENGTH(N'dbo.ReportUploadedFiles', N'PreviewFileName') IS NULL
                    ALTER TABLE [dbo].[ReportUploadedFiles] ADD [PreviewFileName] nvarchar(260) NULL;
                IF COL_LENGTH(N'dbo.ReportUploadedFiles', N'PreviewStoragePath') IS NULL
                    ALTER TABLE [dbo].[ReportUploadedFiles] ADD [PreviewStoragePath] nvarchar(500) NULL;
                IF COL_LENGTH(N'dbo.ReportUploadedFiles', N'PreviewContentType') IS NULL
                    ALTER TABLE [dbo].[ReportUploadedFiles] ADD [PreviewContentType] nvarchar(120) NULL;
                IF COL_LENGTH(N'dbo.ReportUploadedFiles', N'PreviewStatus') IS NULL
                    ALTER TABLE [dbo].[ReportUploadedFiles] ADD [PreviewStatus] nvarchar(40) NULL;
                IF COL_LENGTH(N'dbo.ReportUploadedFiles', N'PreviewErrorMessage') IS NULL
                    ALTER TABLE [dbo].[ReportUploadedFiles] ADD [PreviewErrorMessage] nvarchar(1000) NULL;
                IF COL_LENGTH(N'dbo.ReportUploadedFiles', N'PreviewGeneratedAtUtc') IS NULL
                    ALTER TABLE [dbo].[ReportUploadedFiles] ADD [PreviewGeneratedAtUtc] datetimeoffset NULL;

                -- Dynamic SQL resolves BudgetProposalId after a guarded ALTER in this batch.
                EXEC(N'IF EXISTS (
                    SELECT [BudgetProposalId] FROM [dbo].[Reports]
                    WHERE [BudgetProposalId] IS NOT NULL
                    GROUP BY [BudgetProposalId] HAVING COUNT_BIG(*) > 1)
                    THROW 51020, ''Report schema upgrade blocked: duplicate BudgetProposalId values require review.'', 1;');

                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.Reports')
                      AND name = N'IX_Reports_BudgetProposalId'
                      AND (is_unique = 0 OR has_filter = 0))
                    DROP INDEX [IX_Reports_BudgetProposalId] ON [dbo].[Reports];

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.Reports')
                      AND name = N'IX_Reports_BudgetProposalId')
                    EXEC(N'CREATE UNIQUE INDEX [IX_Reports_BudgetProposalId]
                        ON [dbo].[Reports] ([BudgetProposalId]) WHERE [BudgetProposalId] IS NOT NULL');

                -- Preserve every file/attachment; detach only invalid detail associations.
                UPDATE attachment
                SET [ReportDetailId] = NULL
                FROM [dbo].[ReportAttachments] attachment
                WHERE attachment.[ReportDetailId] IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM [dbo].[ReportDetails] detail
                      WHERE detail.[Id] = attachment.[ReportDetailId]
                        AND detail.[ReportId] = attachment.[ReportId]);
                """);

            migrationBuilder.DropIndex(
                name: "IX_ReportDetails_ReportId",
                table: "ReportDetails");

            migrationBuilder.DropIndex(
                name: "IX_ReportAttachments_ReportId",
                table: "ReportAttachments");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ReportDetails_ReportId_Id",
                table: "ReportDetails",
                columns: new[] { "ReportId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportAttachments_ReportId_ReportDetailId",
                table: "ReportAttachments",
                columns: new[] { "ReportId", "ReportDetailId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ReportAttachments_ReportDetails_ReportId_ReportDetailId",
                table: "ReportAttachments",
                columns: new[] { "ReportId", "ReportDetailId" },
                principalTable: "ReportDetails",
                principalColumns: new[] { "ReportId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Preserve the legacy finance/preview columns: the previous model and upgrader already used them.
            migrationBuilder.DropForeignKey(
                name: "FK_ReportAttachments_ReportDetails_ReportId_ReportDetailId",
                table: "ReportAttachments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ReportDetails_ReportId_Id",
                table: "ReportDetails");

            migrationBuilder.DropIndex(
                name: "IX_ReportAttachments_ReportId_ReportDetailId",
                table: "ReportAttachments");

            migrationBuilder.CreateIndex(
                name: "IX_ReportDetails_ReportId",
                table: "ReportDetails",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAttachments_ReportId",
                table: "ReportAttachments",
                column: "ReportId");
        }
    }
}
