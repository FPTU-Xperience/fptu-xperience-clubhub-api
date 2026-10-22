using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityService.Migrations
{
    /// <inheritdoc />
    public partial class ActivityActionWorkflowWithoutScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityApplication",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    CampusCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SemesterId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityApplication", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ActivityApplicationHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityApplicationHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityApplicationHistory_ActivityApplication_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "ActivityApplication",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivityExecution",
                columns: table => new
                {
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    ApplicationId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    PublicationVersion = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityExecution", x => x.ActivityId);
                    table.ForeignKey(
                        name: "FK_ActivityExecution_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityExecution_ActivityApplication_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "ActivityApplication",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivityActionRule",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityActionRule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityActionRule_ActivityExecution_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "ActivityExecution",
                        principalColumn: "ActivityId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivityExecutionAudit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityExecutionAudit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityExecutionAudit_ActivityExecution_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "ActivityExecution",
                        principalColumn: "ActivityId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivityRecruitment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Visibility = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Eligibility = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityRecruitment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityRecruitment_ActivityExecution_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "ActivityExecution",
                        principalColumn: "ActivityId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivityRecruitmentPosition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecruitmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionRuleId = table.Column<int>(type: "int", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    AssignmentKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityRecruitmentPosition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityRecruitmentPosition_ActivityActionRule_ActionRuleId",
                        column: x => x.ActionRuleId,
                        principalTable: "ActivityActionRule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityRecruitmentPosition_ActivityRecruitment_RecruitmentId",
                        column: x => x.RecruitmentId,
                        principalTable: "ActivityRecruitment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivityStaffAssignment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    ActionRuleId = table.Column<int>(type: "int", nullable: false),
                    PositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AssignmentKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CancelledBy = table.Column<int>(type: "int", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityStaffAssignment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityStaffAssignment_ActivityActionRule_ActionRuleId",
                        column: x => x.ActionRuleId,
                        principalTable: "ActivityActionRule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityStaffAssignment_ActivityExecution_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "ActivityExecution",
                        principalColumn: "ActivityId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityStaffAssignment_ActivityRecruitmentPosition_PositionId",
                        column: x => x.PositionId,
                        principalTable: "ActivityRecruitmentPosition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivityActionCompletion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionRuleId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    OccurrenceKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    OccurrenceLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    VerifiedBy = table.Column<int>(type: "int", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityActionCompletion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityActionCompletion_ActivityActionRule_ActionRuleId",
                        column: x => x.ActionRuleId,
                        principalTable: "ActivityActionRule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityActionCompletion_ActivityExecution_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "ActivityExecution",
                        principalColumn: "ActivityId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityActionCompletion_ActivityStaffAssignment_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "ActivityStaffAssignment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityActionCompletion_ActionRuleId_UserId_OccurrenceKey",
                table: "ActivityActionCompletion",
                columns: new[] { "ActionRuleId", "UserId", "OccurrenceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityActionCompletion_ActivityId",
                table: "ActivityActionCompletion",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityActionCompletion_AssignmentId",
                table: "ActivityActionCompletion",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityActionRule_ActivityId_ClientId",
                table: "ActivityActionRule",
                columns: new[] { "ActivityId", "ClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityApplication_ClubId_SemesterId_Status",
                table: "ActivityApplication",
                columns: new[] { "ClubId", "SemesterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityApplicationHistory_ApplicationId_Version",
                table: "ActivityApplicationHistory",
                columns: new[] { "ApplicationId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityExecution_ApplicationId",
                table: "ActivityExecution",
                column: "ApplicationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityExecutionAudit_ActivityId_Version",
                table: "ActivityExecutionAudit",
                columns: new[] { "ActivityId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityRecruitment_ActivityId",
                table: "ActivityRecruitment",
                column: "ActivityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityRecruitmentPosition_ActionRuleId",
                table: "ActivityRecruitmentPosition",
                column: "ActionRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityRecruitmentPosition_RecruitmentId_ActionRuleId",
                table: "ActivityRecruitmentPosition",
                columns: new[] { "RecruitmentId", "ActionRuleId" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityStaffAssignment_ActionRuleId",
                table: "ActivityStaffAssignment",
                column: "ActionRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityStaffAssignment_ActivityId_ActionRuleId_UserId",
                table: "ActivityStaffAssignment",
                columns: new[] { "ActivityId", "ActionRuleId", "UserId" },
                unique: true,
                filter: "[Status] <> 'REMOVED'");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityStaffAssignment_PositionId",
                table: "ActivityStaffAssignment",
                column: "PositionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityActionCompletion");

            migrationBuilder.DropTable(
                name: "ActivityApplicationHistory");

            migrationBuilder.DropTable(
                name: "ActivityExecutionAudit");

            migrationBuilder.DropTable(
                name: "ActivityStaffAssignment");

            migrationBuilder.DropTable(
                name: "ActivityRecruitmentPosition");

            migrationBuilder.DropTable(
                name: "ActivityActionRule");

            migrationBuilder.DropTable(
                name: "ActivityRecruitment");

            migrationBuilder.DropTable(
                name: "ActivityExecution");

            migrationBuilder.DropTable(
                name: "ActivityApplication");
        }
    }
}
