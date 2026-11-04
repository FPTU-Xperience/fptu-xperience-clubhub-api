using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClubService.Migrations
{
    /// <inheritdoc />
    public partial class ClubGovernanceGroupsAndLeadership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClubGovernanceTerm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    SemesterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubGovernanceTerm", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubGovernanceTerm_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClubViceAppointment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    MembershipId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    MemberName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProposedBy = table.Column<int>(type: "int", nullable: false),
                    ProposedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    From = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Until = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubViceAppointment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubViceAppointment_ClubMemberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "ClubMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClubViceAppointment_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClubGovernanceRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    PreviousId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MemberChangesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AffectedGroupIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubGovernanceRevision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubGovernanceRevision_ClubGovernanceTerm_TermId",
                        column: x => x.TermId,
                        principalTable: "ClubGovernanceTerm",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClubGovernanceLeadershipEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubGovernanceLeadershipEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubGovernanceLeadershipEvent_ClubViceAppointment_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "ClubViceAppointment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClubGovernanceAuditEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MembershipId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubGovernanceAuditEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubGovernanceAuditEvent_ClubGovernanceRevision_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "ClubGovernanceRevision",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClubGovernanceAuditEvent_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClubGovernanceMemberChange",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MembershipId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubGovernanceMemberChange", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubGovernanceMemberChange_ClubGovernanceRevision_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "ClubGovernanceRevision",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClubGovernanceReport",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    SourceRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LeadershipDigest = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubGovernanceReport", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubGovernanceReport_ClubGovernanceRevision_SourceRevisionId",
                        column: x => x.SourceRevisionId,
                        principalTable: "ClubGovernanceRevision",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClubGovernanceReport_ClubGovernanceTerm_TermId",
                        column: x => x.TermId,
                        principalTable: "ClubGovernanceTerm",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClubGovernanceAuditEvent_ClubId_At",
                table: "ClubGovernanceAuditEvent",
                columns: new[] { "ClubId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ClubGovernanceAuditEvent_MembershipId",
                table: "ClubGovernanceAuditEvent",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubGovernanceAuditEvent_RevisionId",
                table: "ClubGovernanceAuditEvent",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubGovernanceLeadershipEvent_AppointmentId_At",
                table: "ClubGovernanceLeadershipEvent",
                columns: new[] { "AppointmentId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ClubGovernanceMemberChange_MembershipId",
                table: "ClubGovernanceMemberChange",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubGovernanceMemberChange_RevisionId_MembershipId",
                table: "ClubGovernanceMemberChange",
                columns: new[] { "RevisionId", "MembershipId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubGovernanceReport_SourceRevisionId",
                table: "ClubGovernanceReport",
                column: "SourceRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubGovernanceReport_TermId_Number",
                table: "ClubGovernanceReport",
                columns: new[] { "TermId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubGovernanceRevision_TermId_Sequence",
                table: "ClubGovernanceRevision",
                columns: new[] { "TermId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubGovernanceTerm_ClubId_SemesterId",
                table: "ClubGovernanceTerm",
                columns: new[] { "ClubId", "SemesterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubViceAppointment_ClubId_UserId_Status",
                table: "ClubViceAppointment",
                columns: new[] { "ClubId", "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClubViceAppointment_MembershipId",
                table: "ClubViceAppointment",
                column: "MembershipId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClubGovernanceAuditEvent");

            migrationBuilder.DropTable(
                name: "ClubGovernanceLeadershipEvent");

            migrationBuilder.DropTable(
                name: "ClubGovernanceMemberChange");

            migrationBuilder.DropTable(
                name: "ClubGovernanceReport");

            migrationBuilder.DropTable(
                name: "ClubViceAppointment");

            migrationBuilder.DropTable(
                name: "ClubGovernanceRevision");

            migrationBuilder.DropTable(
                name: "ClubGovernanceTerm");
        }
    }
}
