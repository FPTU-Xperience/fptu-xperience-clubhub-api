using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminService.Data.Migrations
{
    /// <inheritdoc />
    public partial class VerifiedExperienceLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalBasis",
                table: "SemesterBenchmarkConfigs",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "SemesterBenchmarkConfigs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "SemesterBenchmarkConfigs",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedByUserId",
                table: "SemesterBenchmarkConfigs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Environment",
                table: "SemesterBenchmarkConfigs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "SemesterBenchmarkConfigs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "ApiOperationReceipt",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiOperationReceipt", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovedExperiencePolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BenchmarkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SemesterId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApprovalBasis = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ApprovedBy = table.Column<int>(type: "int", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovedExperiencePolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovedExperiencePolicies_SemesterBenchmarkConfigs_BenchmarkId",
                        column: x => x.BenchmarkId,
                        principalTable: "SemesterBenchmarkConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContributionEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Bytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionEvidence", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Contributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ClubId = table.Column<int>(type: "int", nullable: true),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    SessionId = table.Column<int>(type: "int", nullable: true),
                    SemesterId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionOrdinal = table.Column<int>(type: "int", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposedAttributesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EffectiveAttributesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EvidenceRefsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PolicySnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawPoints = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contributions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExperienceInbox",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoutingKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperienceInbox", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "InvalidatedExperienceSources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<int>(type: "int", nullable: true),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    InvalidatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvalidatedExperienceSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManualAttendanceRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EvidenceRefsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecognitionStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReviewedBy = table.Column<int>(type: "int", nullable: true),
                    ReviewReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualAttendanceRequests", x => x.Id);
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
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClaimExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClaimedByInstanceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VerificationAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClubId = table.Column<int>(type: "int", nullable: true),
                    SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CapabilitiesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedBy = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContributionDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewerUserId = table.Column<int>(type: "int", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignmentVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Decision = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContributionDecisions_Contributions_ContributionId",
                        column: x => x.ContributionId,
                        principalTable: "Contributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContributionRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AttributesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceRefsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContributionRevisions_Contributions_ContributionId",
                        column: x => x.ContributionId,
                        principalTable: "Contributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContributionUpgradeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContributionVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposedRole = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ProposedResult = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    EvidenceRefsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReviewReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedBy = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionUpgradeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContributionUpgradeRequests_Contributions_ContributionId",
                        column: x => x.ContributionId,
                        principalTable: "Contributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExperienceLedgerEffects",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SemesterId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EffectType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VerificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VerifiedByUserId = table.Column<int>(type: "int", nullable: true),
                    VerificationMethod = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PolicyVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormulaVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperienceLedgerEffects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExperienceLedgerEffects_Contributions_ContributionId",
                        column: x => x.ContributionId,
                        principalTable: "Contributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExperienceLedgerAllocations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EffectId = table.Column<long>(type: "bigint", nullable: false),
                    Pillar = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperienceLedgerAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExperienceLedgerAllocations_ExperienceLedgerEffects_EffectId",
                        column: x => x.EffectId,
                        principalTable: "ExperienceLedgerEffects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiOperationReceipt_ActorId_Operation_Key",
                table: "ApiOperationReceipt",
                columns: new[] { "ActorId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedExperiencePolicies_BenchmarkId_ApprovedAt",
                table: "ApprovedExperiencePolicies",
                columns: new[] { "BenchmarkId", "ApprovedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionDecisions_ContributionId_TargetRevisionId",
                table: "ContributionDecisions",
                columns: new[] { "ContributionId", "TargetRevisionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContributionEvidence_OwnerUserId",
                table: "ContributionEvidence",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionRevisions_ContributionId_Ordinal",
                table: "ContributionRevisions",
                columns: new[] { "ContributionId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contributions_ActivityId_SessionId",
                table: "Contributions",
                columns: new[] { "ActivityId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Contributions_ClubId_SemesterId_Status",
                table: "Contributions",
                columns: new[] { "ClubId", "SemesterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Contributions_UserId_SourceKey",
                table: "Contributions",
                columns: new[] { "UserId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContributionUpgradeRequests_ContributionId",
                table: "ContributionUpgradeRequests",
                column: "ContributionId",
                unique: true,
                filter: "[Status] = 'SUBMITTED'");

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceLedgerAllocations_EffectId_Pillar",
                table: "ExperienceLedgerAllocations",
                columns: new[] { "EffectId", "Pillar" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceLedgerEffects_ContributionId_RevisionId_EffectType",
                table: "ExperienceLedgerEffects",
                columns: new[] { "ContributionId", "RevisionId", "EffectType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceLedgerEffects_UserId_SemesterId",
                table: "ExperienceLedgerEffects",
                columns: new[] { "UserId", "SemesterId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvalidatedExperienceSources_ActivityId_SessionId",
                table: "InvalidatedExperienceSources",
                columns: new[] { "ActivityId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ManualAttendanceRequests_ClubId_Status",
                table: "ManualAttendanceRequests",
                columns: new[] { "ClubId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ManualAttendanceRequests_SessionId_UserId",
                table: "ManualAttendanceRequests",
                columns: new[] { "SessionId", "UserId" },
                unique: true,
                filter: "[Status] = 'PENDING'");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_ClaimExpiresAtUtc_OccurredAtUtc",
                table: "OutboxMessages",
                columns: new[] { "Status", "ClaimExpiresAtUtc", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_OccurredAtUtc",
                table: "OutboxMessages",
                columns: new[] { "Status", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationAssignments_UserId_SourceType_ClubId",
                table: "VerificationAssignments",
                columns: new[] { "UserId", "SourceType", "ClubId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiOperationReceipt");

            migrationBuilder.DropTable(
                name: "ApprovedExperiencePolicies");

            migrationBuilder.DropTable(
                name: "ContributionDecisions");

            migrationBuilder.DropTable(
                name: "ContributionEvidence");

            migrationBuilder.DropTable(
                name: "ContributionRevisions");

            migrationBuilder.DropTable(
                name: "ContributionUpgradeRequests");

            migrationBuilder.DropTable(
                name: "ExperienceInbox");

            migrationBuilder.DropTable(
                name: "ExperienceLedgerAllocations");

            migrationBuilder.DropTable(
                name: "InvalidatedExperienceSources");

            migrationBuilder.DropTable(
                name: "ManualAttendanceRequests");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "VerificationAssignments");

            migrationBuilder.DropTable(
                name: "ExperienceLedgerEffects");

            migrationBuilder.DropTable(
                name: "Contributions");

            migrationBuilder.DropColumn(
                name: "ApprovalBasis",
                table: "SemesterBenchmarkConfigs");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "SemesterBenchmarkConfigs");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "SemesterBenchmarkConfigs");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "SemesterBenchmarkConfigs");

            migrationBuilder.DropColumn(
                name: "Environment",
                table: "SemesterBenchmarkConfigs");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "SemesterBenchmarkConfigs");
        }
    }
}
