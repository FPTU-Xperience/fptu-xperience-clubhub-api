using AuthService.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Migrations;

[DbContext(typeof(AuthDbContext))]
[Migration("20260921200000_AddSecurityVersionToUser")]
public partial class AddSecurityVersionToUser : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "SecurityVersion",
            table: "Users",
            type: "int",
            nullable: false,
            defaultValue: 1);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SecurityVersion",
            table: "Users");
    }
}
