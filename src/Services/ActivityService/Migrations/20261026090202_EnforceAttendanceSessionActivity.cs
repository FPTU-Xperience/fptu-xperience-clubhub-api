using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityService.Migrations
{
    /// <inheritdoc />
    public partial class EnforceAttendanceSessionActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM [dbo].[ActivityAttendances] a
                    LEFT JOIN [dbo].[Sessions] s ON s.[Id] = a.[SessionId] AND s.[ActivityId] = a.[ActivityId]
                    WHERE a.[SessionId] IS NOT NULL AND s.[Id] IS NULL)
                    THROW 51041, 'Attendance session does not belong to its activity. Reconcile existing attendance links before retrying this migration.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ActivityAttendances_Sessions_SessionId",
                table: "ActivityAttendances");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Sessions_ActivityId_Id",
                table: "Sessions",
                columns: new[] { "ActivityId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAttendances_ActivityId_SessionId",
                table: "ActivityAttendances",
                columns: new[] { "ActivityId", "SessionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ActivityAttendances_Sessions_ActivityId_SessionId",
                table: "ActivityAttendances",
                columns: new[] { "ActivityId", "SessionId" },
                principalTable: "Sessions",
                principalColumns: new[] { "ActivityId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActivityAttendances_Sessions_ActivityId_SessionId",
                table: "ActivityAttendances");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Sessions_ActivityId_Id",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_ActivityAttendances_ActivityId_SessionId",
                table: "ActivityAttendances");

            migrationBuilder.AddForeignKey(
                name: "FK_ActivityAttendances_Sessions_SessionId",
                table: "ActivityAttendances",
                column: "SessionId",
                principalTable: "Sessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
