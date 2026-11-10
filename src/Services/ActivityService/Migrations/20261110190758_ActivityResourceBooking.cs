using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityService.Migrations
{
    /// <inheritdoc />
    public partial class ActivityResourceBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CampusBookingLayout",
                columns: table => new
                {
                    CampusCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Building = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Floor = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    PlanX = table.Column<int>(type: "int", nullable: true),
                    PlanY = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampusBookingLayout", x => new { x.CampusCode, x.Building, x.Floor });
                });

            migrationBuilder.CreateTable(
                name: "CampusBookingPolicy",
                columns: table => new
                {
                    CampusCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    CutoffDay = table.Column<int>(type: "int", nullable: false),
                    CutoffTime = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampusBookingPolicy", x => x.CampusCode);
                    table.CheckConstraint("CK_CampusBookingPolicy_Day", "CutoffDay BETWEEN 1 AND 7");
                });

            migrationBuilder.CreateTable(
                name: "CampusEquipment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CampusCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    IsBookable = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampusEquipment", x => x.Id);
                    table.CheckConstraint("CK_CampusEquipment_Quantity", "Quantity >= 0");
                });

            migrationBuilder.CreateTable(
                name: "CampusVenue",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CampusCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    VenueType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Building = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Floor = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    PlanX = table.Column<int>(type: "int", nullable: true),
                    PlanY = table.Column<int>(type: "int", nullable: true),
                    IsBookable = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampusVenue", x => x.Id);
                    table.CheckConstraint("CK_CampusVenue_Capacity", "Capacity >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ActivityBooking",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    CampusCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    RequestedVenueId = table.Column<int>(type: "int", nullable: true),
                    ConfirmedVenueId = table.Column<int>(type: "int", nullable: true),
                    OriginalLocation = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RequestedVenueType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    ProcessedByUserId = table.Column<int>(type: "int", nullable: true),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CutoffAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityBooking", x => x.Id);
                    table.CheckConstraint("CK_ActivityBooking_Window", "EndsAtUtc > StartsAtUtc");
                    table.ForeignKey(
                        name: "FK_ActivityBooking_CampusVenue_ConfirmedVenueId",
                        column: x => x.ConfirmedVenueId,
                        principalTable: "CampusVenue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityBooking_CampusVenue_RequestedVenueId",
                        column: x => x.RequestedVenueId,
                        principalTable: "CampusVenue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityBooking_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivityBookingEquipment",
                columns: table => new
                {
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    RequestedQuantity = table.Column<int>(type: "int", nullable: false),
                    ConfirmedQuantity = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityBookingEquipment", x => new { x.BookingId, x.EquipmentId });
                    table.CheckConstraint("CK_ActivityBookingEquipment_Quantity", "RequestedQuantity > 0 AND ConfirmedQuantity BETWEEN 0 AND RequestedQuantity");
                    table.ForeignKey(
                        name: "FK_ActivityBookingEquipment_ActivityBooking_BookingId",
                        column: x => x.BookingId,
                        principalTable: "ActivityBooking",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivityBookingEquipment_CampusEquipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "CampusEquipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityBooking_CampusCode_StartsAtUtc",
                table: "ActivityBooking",
                columns: new[] { "CampusCode", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityBooking_ConfirmedVenueId_StartsAtUtc_EndsAtUtc",
                table: "ActivityBooking",
                columns: new[] { "ConfirmedVenueId", "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityBooking_RequestedVenueId",
                table: "ActivityBooking",
                column: "RequestedVenueId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityBooking_SessionId",
                table: "ActivityBooking",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityBookingEquipment_EquipmentId",
                table: "ActivityBookingEquipment",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CampusEquipment_CampusCode_Code",
                table: "CampusEquipment",
                columns: new[] { "CampusCode", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampusVenue_CampusCode_Code",
                table: "CampusVenue",
                columns: new[] { "CampusCode", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityBookingEquipment");

            migrationBuilder.DropTable(
                name: "CampusBookingLayout");

            migrationBuilder.DropTable(
                name: "CampusBookingPolicy");

            migrationBuilder.DropTable(
                name: "ActivityBooking");

            migrationBuilder.DropTable(
                name: "CampusEquipment");

            migrationBuilder.DropTable(
                name: "CampusVenue");
        }
    }
}
