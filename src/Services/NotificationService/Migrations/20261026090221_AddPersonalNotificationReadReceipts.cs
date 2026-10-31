using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalNotificationReadReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationReadReceipts",
                columns: table => new
                {
                    NotificationId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ReadAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationReadReceipts", x => new { x.NotificationId, x.UserId });
                    table.ForeignKey(
                        name: "FK_NotificationReadReceipts_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationReadReceipts_UserId_NotificationId",
                table: "NotificationReadReceipts",
                columns: new[] { "UserId", "NotificationId" });
            migrationBuilder.Sql("""
                -- Run after creating dbo.NotificationReadReceipts. Preserve only identifiable legacy owners.
                -- Historical role-wide acknowledgements cannot identify readers; broadcasts become unread per actor.
                DECLARE @migratedAt datetimeoffset = SYSUTCDATETIME();
                INSERT INTO [dbo].[NotificationReadReceipts] ([NotificationId], [UserId], [ReadAtUtc])
                SELECT n.[Id], n.[RecipientUserId], @migratedAt
                FROM [dbo].[Notifications] n
                WHERE n.[IsRead] = 1 AND n.[RecipientUserId] IS NOT NULL AND n.[RecipientUserId] > 0
                  AND NOT EXISTS (SELECT 1 FROM [dbo].[NotificationReadReceipts] r
                                  WHERE r.[NotificationId] = n.[Id] AND r.[UserId] = n.[RecipientUserId]);
                UPDATE [dbo].[Notifications] SET [IsRead] = 0
                WHERE [RecipientUserId] IS NULL AND [IsRead] = 1;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationReadReceipts");
        }
    }
}
