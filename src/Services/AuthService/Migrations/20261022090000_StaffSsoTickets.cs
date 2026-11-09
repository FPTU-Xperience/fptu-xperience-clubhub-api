using AuthService.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace AuthService.Migrations;

[DbContext(typeof(AuthDbContext))]
[Migration("20261022090000_StaffSsoTickets")]
public partial class StaffSsoTickets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StaffSsoTickets",
            columns: table => new
            {
                CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                UserId = table.Column<int>(type: "int", nullable: false),
                FamilyId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                SecurityVersion = table.Column<int>(type: "int", nullable: false),
                TargetOrigin = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                CodeChallenge = table.Column<string>(type: "nvarchar(43)", maxLength: 43, nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StaffSsoTickets", x => x.CodeHash);
                table.ForeignKey("FK_StaffSsoTickets_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_StaffSsoTickets_ExpiresAtUtc", "StaffSsoTickets", "ExpiresAtUtc");
        migrationBuilder.CreateIndex("IX_StaffSsoTickets_UserId", "StaffSsoTickets", "UserId");
        // Both rotation and SSO revocation query the browser-session family.
        migrationBuilder.CreateIndex("IX_RefreshTokens_FamilyId", "RefreshTokens", "FamilyId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("StaffSsoTickets");
        migrationBuilder.DropIndex("IX_RefreshTokens_FamilyId", "RefreshTokens");
    }
}
