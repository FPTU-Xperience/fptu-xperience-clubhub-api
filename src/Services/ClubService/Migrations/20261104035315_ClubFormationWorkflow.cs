using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClubService.Migrations
{
    /// <inheritdoc />
    public partial class ClubFormationWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AdvisorNeeded",
                table: "Clubs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EquipmentNeeds",
                table: "Clubs",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExpectedLocation",
                table: "Clubs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MajorEvents",
                table: "Clubs",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ApprovedManagerName",
                table: "ClubCreationApplications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedManagerUserId",
                table: "ClubCreationApplications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "ClubCreationApplications",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
            migrationBuilder.Sql("UPDATE ClubCreationApplications SET Version = NEWID() WHERE Version = '00000000-0000-0000-0000-000000000000'");
            migrationBuilder.Sql("UPDATE c SET c.ExpectedLocation=a.ExpectedLocation, c.MajorEvents=a.MajorEvents, c.EquipmentNeeds=a.EquipmentNeeds, c.AdvisorNeeded=a.AdvisorNeeded FROM Clubs c INNER JOIN ClubCreationApplications a ON a.CreatedClubId=c.Id WHERE a.Status='Approved'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdvisorNeeded",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "EquipmentNeeds",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "ExpectedLocation",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "MajorEvents",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "ApprovedManagerName",
                table: "ClubCreationApplications");

            migrationBuilder.DropColumn(
                name: "ApprovedManagerUserId",
                table: "ClubCreationApplications");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "ClubCreationApplications");
        }
    }
}
