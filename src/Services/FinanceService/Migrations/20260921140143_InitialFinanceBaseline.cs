using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceService.Migrations
{
    /// <inheritdoc />
    public partial class InitialFinanceBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql("""
                    IF OBJECT_ID(N'[dbo].[BudgetProposals]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[BudgetProposals] (
                            [Id] int NOT NULL IDENTITY,
                            [ClubId] int NOT NULL,
                            [ClubName] nvarchar(200) NOT NULL,
                            [ActivityId] int NULL,
                            [SourceReportId] int NULL,
                            [Title] nvarchar(200) NOT NULL,
                            [Description] nvarchar(2000) NOT NULL,
                            [RequestedAmount] decimal(18,2) NOT NULL,
                            [ApprovedAmount] decimal(18,2) NULL,
                            [Status] nvarchar(40) NOT NULL,
                            [ProposedByUserId] int NOT NULL,
                            [ProposedAtUtc] datetimeoffset NOT NULL,
                            [ManagerReviewedByUserId] int NULL,
                            [ManagerReviewedAtUtc] datetimeoffset NULL,
                            [ManagerReviewNote] nvarchar(1000) NULL,
                            [ReviewedByUserId] int NULL,
                            [ReviewedAtUtc] datetimeoffset NULL,
                            [ReviewNote] nvarchar(1000) NULL,
                            [Version] int NOT NULL,
                            CONSTRAINT [PK_BudgetProposals] PRIMARY KEY ([Id])
                        );
                    END;

                    IF OBJECT_ID(N'[dbo].[FinanceTransactions]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[FinanceTransactions] (
                            [Id] int NOT NULL IDENTITY,
                            [ClubId] int NOT NULL,
                            [Amount] decimal(18,2) NOT NULL,
                            [Type] nvarchar(80) NOT NULL,
                            [Description] nvarchar(1000) NOT NULL,
                            [ReferenceId] int NULL,
                            [TransactionDateUtc] datetimeoffset NOT NULL,
                            CONSTRAINT [PK_FinanceTransactions] PRIMARY KEY ([Id])
                        );
                    END;

                    IF OBJECT_ID(N'[dbo].[OutboxMessages]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[OutboxMessages] (
                            [Id] uniqueidentifier NOT NULL,
                            [OccurredAtUtc] datetimeoffset NOT NULL,
                            [EventType] nvarchar(100) NOT NULL,
                            [EventTypeName] nvarchar(300) NOT NULL,
                            [Payload] nvarchar(max) NOT NULL,
                            [Status] nvarchar(50) NOT NULL,
                            [RetryCount] int NOT NULL,
                            [ProcessedAtUtc] datetimeoffset NULL,
                            [ErrorMessage] nvarchar(max) NULL,
                            [CorrelationId] nvarchar(100) NULL,
                            CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([Id])
                        );
                    END;

                    IF OBJECT_ID(N'[dbo].[Settlements]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[Settlements] (
                            [Id] int NOT NULL IDENTITY,
                            [BudgetProposalId] int NOT NULL,
                            [TotalSpent] decimal(18,2) NOT NULL,
                            [ReceiptUrl] nvarchar(500) NOT NULL,
                            [Status] nvarchar(40) NOT NULL,
                            [SubmittedAtUtc] datetimeoffset NOT NULL,
                            [ReviewedByUserId] int NULL,
                            [ReviewedAtUtc] datetimeoffset NULL,
                            [ReviewNote] nvarchar(1000) NULL,
                            CONSTRAINT [PK_Settlements] PRIMARY KEY ([Id]),
                            CONSTRAINT [FK_Settlements_BudgetProposals_BudgetProposalId] FOREIGN KEY ([BudgetProposalId]) REFERENCES [dbo].[BudgetProposals] ([Id]) ON DELETE CASCADE
                        );
                    END;

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BudgetProposals_ClubId_Status' AND object_id = OBJECT_ID(N'[dbo].[BudgetProposals]'))
                        CREATE INDEX [IX_BudgetProposals_ClubId_Status] ON [dbo].[BudgetProposals] ([ClubId], [Status]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BudgetProposals_ProposedAtUtc' AND object_id = OBJECT_ID(N'[dbo].[BudgetProposals]'))
                        CREATE INDEX [IX_BudgetProposals_ProposedAtUtc] ON [dbo].[BudgetProposals] ([ProposedAtUtc]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BudgetProposals_ProposedByUserId' AND object_id = OBJECT_ID(N'[dbo].[BudgetProposals]'))
                        CREATE INDEX [IX_BudgetProposals_ProposedByUserId] ON [dbo].[BudgetProposals] ([ProposedByUserId]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BudgetProposals_SourceReportId' AND object_id = OBJECT_ID(N'[dbo].[BudgetProposals]'))
                        CREATE UNIQUE INDEX [IX_BudgetProposals_SourceReportId] ON [dbo].[BudgetProposals] ([SourceReportId]) WHERE [SourceReportId] IS NOT NULL;

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FinanceTransactions_ClubId_TransactionDateUtc' AND object_id = OBJECT_ID(N'[dbo].[FinanceTransactions]'))
                        CREATE INDEX [IX_FinanceTransactions_ClubId_TransactionDateUtc] ON [dbo].[FinanceTransactions] ([ClubId], [TransactionDateUtc]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OutboxMessages_Status_OccurredAtUtc' AND object_id = OBJECT_ID(N'[dbo].[OutboxMessages]'))
                        CREATE INDEX [IX_OutboxMessages_Status_OccurredAtUtc] ON [dbo].[OutboxMessages] ([Status], [OccurredAtUtc]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Settlements_BudgetProposalId' AND object_id = OBJECT_ID(N'[dbo].[Settlements]'))
                        CREATE INDEX [IX_Settlements_BudgetProposalId] ON [dbo].[Settlements] ([BudgetProposalId]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Settlements_Status' AND object_id = OBJECT_ID(N'[dbo].[Settlements]'))
                        CREATE INDEX [IX_Settlements_Status] ON [dbo].[Settlements] ([Status]);
                """);
                return;
            }

            migrationBuilder.CreateTable(
                name: "BudgetProposals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    ClubName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    SourceReportId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ProposedByUserId = table.Column<int>(type: "int", nullable: false),
                    ProposedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ManagerReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ManagerReviewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ManagerReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetProposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinanceTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ReferenceId = table.Column<int>(type: "int", nullable: true),
                    TransactionDateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EventTypeName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BudgetProposalId = table.Column<int>(type: "int", nullable: false),
                    TotalSpent = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReceiptUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Settlements_BudgetProposals_BudgetProposalId",
                        column: x => x.BudgetProposalId,
                        principalTable: "BudgetProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetProposals_ClubId_Status",
                table: "BudgetProposals",
                columns: new[] { "ClubId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetProposals_ProposedAtUtc",
                table: "BudgetProposals",
                column: "ProposedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetProposals_ProposedByUserId",
                table: "BudgetProposals",
                column: "ProposedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetProposals_SourceReportId",
                table: "BudgetProposals",
                column: "SourceReportId",
                unique: true,
                filter: "[SourceReportId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceTransactions_ClubId_TransactionDateUtc",
                table: "FinanceTransactions",
                columns: new[] { "ClubId", "TransactionDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_OccurredAtUtc",
                table: "OutboxMessages",
                columns: new[] { "Status", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_BudgetProposalId",
                table: "Settlements",
                column: "BudgetProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_Status",
                table: "Settlements",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinanceTransactions");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "Settlements");

            migrationBuilder.DropTable(
                name: "BudgetProposals");
        }
    }
}
