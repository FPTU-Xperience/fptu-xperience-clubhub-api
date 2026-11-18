using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityService.Migrations
{
    /// <inheritdoc />
    public partial class BookingMultipleRoomsAndSharedFloors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SharedFloorAreas",
                table: "CampusVenue",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActivityBookingVenue",
                columns: table => new
                {
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    VenueId = table.Column<int>(type: "int", nullable: false),
                    IsConfirmed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityBookingVenue", x => new { x.BookingId, x.VenueId, x.IsConfirmed });
                    table.ForeignKey(
                        name: "FK_ActivityBookingVenue_ActivityBooking_BookingId",
                        column: x => x.BookingId,
                        principalTable: "ActivityBooking",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivityBookingVenue_CampusVenue_VenueId",
                        column: x => x.VenueId,
                        principalTable: "CampusVenue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityBookingVenue_VenueId_IsConfirmed",
                table: "ActivityBookingVenue",
                columns: new[] { "VenueId", "IsConfirmed" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityBookingVenue");

            migrationBuilder.DropColumn(
                name: "SharedFloorAreas",
                table: "CampusVenue");
        }
    }
}
