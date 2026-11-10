using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityService.Migrations
{
    /// <inheritdoc />
    public partial class ScheduleActivityPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishAtUtc",
                table: "ActivityExecution",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityExecution_PublishAtUtc",
                table: "ActivityExecution",
                column: "PublishAtUtc",
                filter: "[PublishAtUtc] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActivityExecution_PublishAtUtc",
                table: "ActivityExecution");

            migrationBuilder.DropColumn(
                name: "PublishAtUtc",
                table: "ActivityExecution");
        }
    }
}
