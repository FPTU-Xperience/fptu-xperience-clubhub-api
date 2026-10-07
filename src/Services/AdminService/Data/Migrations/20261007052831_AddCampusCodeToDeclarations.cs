using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCampusCodeToDeclarations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CampusCode",
                table: "SelfDeclarations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "HAN");

            migrationBuilder.CreateIndex(
                name: "IX_SelfDeclarations_CampusCode",
                table: "SelfDeclarations",
                column: "CampusCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SelfDeclarations_CampusCode",
                table: "SelfDeclarations");

            migrationBuilder.DropColumn(
                name: "CampusCode",
                table: "SelfDeclarations");
        }
    }
}
