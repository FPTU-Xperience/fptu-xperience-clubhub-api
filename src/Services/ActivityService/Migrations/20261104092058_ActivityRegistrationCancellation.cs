using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityService.Migrations
{
    /// <inheritdoc />
    public partial class ActivityRegistrationCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAtUtc",
                table: "ActivityParticipants",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "ActivityParticipants",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
            migrationBuilder.Sql("UPDATE ActivityParticipants SET Version = NEWID() WHERE Version = '00000000-0000-0000-0000-000000000000'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "ActivityParticipants");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "ActivityParticipants");
        }
    }
}
