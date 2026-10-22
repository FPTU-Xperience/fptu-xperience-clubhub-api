using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClubService.Migrations
{
    /// <inheritdoc />
    public partial class InvitationRosterValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RosterCampusCode",
                table: "ClubMemberships",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RosterRevisionId",
                table: "ClubMemberships",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RosterVersion",
                table: "ClubMemberships",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RosterCampusCode",
                table: "ClubMemberships");

            migrationBuilder.DropColumn(
                name: "RosterRevisionId",
                table: "ClubMemberships");

            migrationBuilder.DropColumn(
                name: "RosterVersion",
                table: "ClubMemberships");
        }
    }
}
