using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminService.Data.Migrations
{
    /// <inheritdoc />
    public partial class StudentExperienceCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CriteriaSnapshotJson",
                table: "Quests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CriteriaVersion",
                table: "Quests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Quests",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "CompletedLedgerRevision",
                table: "QuestParticipants",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CriteriaSnapshotJson",
                table: "QuestParticipants",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CriteriaVersion",
                table: "QuestParticipants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedContributionIdsJson",
                table: "QuestParticipants",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "QuestParticipants",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "CalculationSnapshotJson",
                table: "ExperienceLedgerEffects",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EffectiveAt",
                table: "ApprovedExperiencePolicies",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "ApprovedExperiencePolicies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "PERIOD");

            migrationBuilder.CreateTable(
                name: "QuestCompletionRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CriteriaVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LedgerRevision = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestCompletionRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestCompletionRecords_QuestParticipants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "QuestParticipants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedExperiencePolicies_Scope_Environment_SemesterId_ApprovedAt",
                table: "ApprovedExperiencePolicies",
                columns: new[] { "Scope", "Environment", "SemesterId", "ApprovedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestCompletionRecords_ParticipantId_CriteriaVersion_LedgerRevision",
                table: "QuestCompletionRecords",
                columns: new[] { "ParticipantId", "CriteriaVersion", "LedgerRevision" },
                unique: true);
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql("UPDATE Quests SET Version = NEWID() WHERE Version = '00000000-0000-0000-0000-000000000000'");
                migrationBuilder.Sql("UPDATE QuestParticipants SET Version = NEWID() WHERE Version = '00000000-0000-0000-0000-000000000000'");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestCompletionRecords");

            migrationBuilder.DropIndex(
                name: "IX_ApprovedExperiencePolicies_Scope_Environment_SemesterId_ApprovedAt",
                table: "ApprovedExperiencePolicies");

            migrationBuilder.DropColumn(
                name: "CriteriaSnapshotJson",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "CriteriaVersion",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "CompletedLedgerRevision",
                table: "QuestParticipants");

            migrationBuilder.DropColumn(
                name: "CriteriaSnapshotJson",
                table: "QuestParticipants");

            migrationBuilder.DropColumn(
                name: "CriteriaVersion",
                table: "QuestParticipants");

            migrationBuilder.DropColumn(
                name: "LinkedContributionIdsJson",
                table: "QuestParticipants");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "QuestParticipants");

            migrationBuilder.DropColumn(
                name: "CalculationSnapshotJson",
                table: "ExperienceLedgerEffects");

            migrationBuilder.DropColumn(
                name: "EffectiveAt",
                table: "ApprovedExperiencePolicies");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "ApprovedExperiencePolicies");
        }
    }
}
