using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityService.Migrations
{
    /// <inheritdoc />
    public partial class RecruitmentApprovalAndEvidenceResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityCompletionResource",
                columns: table => new
                {
                    CompletionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityCompletionResource", x => new { x.CompletionId, x.ResourceId });
                    table.ForeignKey(
                        name: "FK_ActivityCompletionResource_ActivityActionCompletion_CompletionId",
                        column: x => x.CompletionId,
                        principalTable: "ActivityActionCompletion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityCompletionResource_ActivityResource_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "ActivityResource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivityRecruitmentApplication",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    PositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedBy = table.Column<int>(type: "int", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityRecruitmentApplication", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityRecruitmentApplication_ActivityExecution_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "ActivityExecution",
                        principalColumn: "ActivityId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityRecruitmentApplication_ActivityRecruitmentPosition_PositionId",
                        column: x => x.PositionId,
                        principalTable: "ActivityRecruitmentPosition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityRecruitmentApplication_ActivityStaffAssignment_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "ActivityStaffAssignment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityCompletionResource_ResourceId",
                table: "ActivityCompletionResource",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityRecruitmentApplication_ActivityId_Status",
                table: "ActivityRecruitmentApplication",
                columns: new[] { "ActivityId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityRecruitmentApplication_AssignmentId",
                table: "ActivityRecruitmentApplication",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityRecruitmentApplication_PositionId_UserId",
                table: "ActivityRecruitmentApplication",
                columns: new[] { "PositionId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityCompletionResource");

            migrationBuilder.DropTable(
                name: "ActivityRecruitmentApplication");
        }
    }
}
