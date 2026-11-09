using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Migrations;

public partial class StudentOnboarding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("DeclaredMajor", "UserProfiles", type: "nvarchar(120)", maxLength: 120, nullable: true);
        migrationBuilder.AddColumn<int>("OnboardingVersion", "UserProfiles", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<bool>("SyncTimetable", "UserProfiles", type: "bit", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<DateTimeOffset>("OnboardingCompletedAtUtc", "UserProfiles", type: "datetimeoffset", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("DeclaredMajor", "UserProfiles");
        migrationBuilder.DropColumn("OnboardingVersion", "UserProfiles");
        migrationBuilder.DropColumn("SyncTimetable", "UserProfiles");
        migrationBuilder.DropColumn("OnboardingCompletedAtUtc", "UserProfiles");
    }
}
