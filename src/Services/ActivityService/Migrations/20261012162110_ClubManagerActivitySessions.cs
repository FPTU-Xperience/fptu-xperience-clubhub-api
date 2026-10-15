using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityService.Migrations
{
    /// <inheritdoc />
    public partial class ClubManagerActivitySessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActivityAttendances_ActivityId_UserId_AttendanceDate",
                table: "ActivityAttendances");

            migrationBuilder.AddColumn<Guid>(
                name: "ContributionId",
                table: "ActivityAttendances",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ManualRequestId",
                table: "ActivityAttendances",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecognitionStatus",
                table: "ActivityAttendances",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "NOT_REQUESTED");

            migrationBuilder.AddColumn<int>(
                name: "SessionId",
                table: "ActivityAttendances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationMethod",
                table: "ActivityAttendances",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationStatus",
                table: "ActivityAttendances",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "UNVERIFIED");

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "ActivityAttendances",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "Capacity",
                table: "Activities",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverImageUrl",
                table: "Activities",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EffortTier",
                table: "Activities",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "T1");

            migrationBuilder.AddColumn<string>(
                name: "Eligibility",
                table: "Activities",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "CLUB_MEMBER");

            migrationBuilder.AddColumn<string>(
                name: "EvidenceRequirements",
                table: "Activities",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PillarWeightsJson",
                table: "Activities",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<Guid>(
                name: "PolicyVersion",
                table: "Activities",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicationStatus",
                table: "Activities",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "LEGACY");

            migrationBuilder.AddColumn<int>(
                name: "RegisteredCount",
                table: "Activities",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RegistrationClosesAt",
                table: "Activities",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RegistrationOpensAt",
                table: "Activities",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Scale",
                table: "Activities",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Club");

            migrationBuilder.AddColumn<string>(
                name: "ScoringSnapshotJson",
                table: "Activities",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SemesterId",
                table: "Activities",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Activities",
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
                name: "ExperienceInbox",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperienceInbox", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "SessionAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SemesterId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StartTimeUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndTimeUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Timezone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OpensAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosesAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    RadiusMeters = table.Column<double>(type: "float", nullable: true),
                    ManualConfirmationEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ScoringSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAttendances_ActivityId_UserId_AttendanceDate",
                table: "ActivityAttendances",
                columns: new[] { "ActivityId", "UserId", "AttendanceDate" },
                unique: true,
                filter: "[SessionId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAttendances_SessionId_UserId",
                table: "ActivityAttendances",
                columns: new[] { "SessionId", "UserId" },
                unique: true,
                filter: "[SessionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ApiOperationReceipt_ActorId_Operation_Key",
                table: "ApiOperationReceipt",
                columns: new[] { "ActorId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionAudits_ActivityId_CreatedAtUtc",
                table: "SessionAudits",
                columns: new[] { "ActivityId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ActivityId_StartTimeUtc_EndTimeUtc",
                table: "Sessions",
                columns: new[] { "ActivityId", "StartTimeUtc", "EndTimeUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_SemesterId_Status",
                table: "Sessions",
                columns: new[] { "SemesterId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_ActivityAttendances_Sessions_SessionId",
                table: "ActivityAttendances",
                column: "SessionId",
                principalTable: "Sessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            // Existing registrations remain real registrations. Legacy presence
            // stays UNVERIFIED and cannot be promoted into experience points.
            migrationBuilder.Sql("UPDATE a SET RegisteredCount = (SELECT COUNT(*) FROM ActivityParticipants p WHERE p.ActivityId = a.Id), Version = NEWID() FROM Activities a;");
            migrationBuilder.Sql("UPDATE ActivityAttendances SET Version = NEWID();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActivityAttendances_Sessions_SessionId",
                table: "ActivityAttendances");

            migrationBuilder.DropTable(
                name: "ApiOperationReceipt");

            migrationBuilder.DropTable(
                name: "ExperienceInbox");

            migrationBuilder.DropTable(
                name: "SessionAudits");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_ActivityAttendances_ActivityId_UserId_AttendanceDate",
                table: "ActivityAttendances");

            migrationBuilder.DropIndex(
                name: "IX_ActivityAttendances_SessionId_UserId",
                table: "ActivityAttendances");

            migrationBuilder.DropColumn(
                name: "ContributionId",
                table: "ActivityAttendances");

            migrationBuilder.DropColumn(
                name: "ManualRequestId",
                table: "ActivityAttendances");

            migrationBuilder.DropColumn(
                name: "RecognitionStatus",
                table: "ActivityAttendances");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "ActivityAttendances");

            migrationBuilder.DropColumn(
                name: "VerificationMethod",
                table: "ActivityAttendances");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "ActivityAttendances");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "ActivityAttendances");

            migrationBuilder.DropColumn(
                name: "Capacity",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "CoverImageUrl",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "EffortTier",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "Eligibility",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "EvidenceRequirements",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "PillarWeightsJson",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "PolicyVersion",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "PublicationStatus",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "RegisteredCount",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "RegistrationClosesAt",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "RegistrationOpensAt",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "Scale",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "ScoringSnapshotJson",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "SemesterId",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Activities");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAttendances_ActivityId_UserId_AttendanceDate",
                table: "ActivityAttendances",
                columns: new[] { "ActivityId", "UserId", "AttendanceDate" },
                unique: true);
        }
    }
}
