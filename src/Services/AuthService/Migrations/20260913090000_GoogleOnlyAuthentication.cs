using System;
using AuthService.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Migrations;

[DbContext(typeof(AuthDbContext))]
[Migration("20260913090000_GoogleOnlyAuthentication")]
public partial class GoogleOnlyAuthentication : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "GoogleSubject",
            table: "Users",
            type: "nvarchar(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Users_GoogleSubject",
            table: "Users",
            column: "GoogleSubject",
            unique: true,
            filter: "[GoogleSubject] IS NOT NULL");

        // Google-only authentication intentionally removes all stored password
        // hashes. Existing users and their business data remain untouched.
        migrationBuilder.DropColumn(
            name: "PasswordHash",
            table: "Users");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Users_GoogleSubject",
            table: "Users");

        migrationBuilder.DropColumn(
            name: "GoogleSubject",
            table: "Users");

        migrationBuilder.AddColumn<string>(
            name: "PasswordHash",
            table: "Users",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: false,
            defaultValue: "");
    }
}
