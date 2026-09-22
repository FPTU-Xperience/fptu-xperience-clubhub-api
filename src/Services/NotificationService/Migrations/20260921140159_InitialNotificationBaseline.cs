using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotificationBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql("""
                    IF OBJECT_ID(N'[dbo].[Notifications]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[Notifications] (
                            [Id] int NOT NULL IDENTITY,
                            [RecipientUserId] int NULL,
                            [RecipientRole] nvarchar(60) NULL,
                            [EventType] nvarchar(100) NOT NULL,
                            [Title] nvarchar(250) NOT NULL,
                            [Message] nvarchar(2000) NOT NULL,
                            [IsRead] bit NOT NULL,
                            [CreatedAtUtc] datetimeoffset NOT NULL,
                            CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id])
                        );
                    END;

                    IF OBJECT_ID(N'[dbo].[ProcessedEvents]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[ProcessedEvents] (
                            [EventId] uniqueidentifier NOT NULL,
                            [RoutingKey] nvarchar(100) NOT NULL,
                            [ProcessedAtUtc] datetimeoffset NOT NULL,
                            CONSTRAINT [PK_ProcessedEvents] PRIMARY KEY ([EventId])
                        );
                    END;

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Notifications_RecipientRole_IsRead_CreatedAtUtc' AND object_id = OBJECT_ID(N'[dbo].[Notifications]'))
                        CREATE INDEX [IX_Notifications_RecipientRole_IsRead_CreatedAtUtc] ON [dbo].[Notifications] ([RecipientRole], [IsRead], [CreatedAtUtc]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Notifications_RecipientUserId_IsRead_CreatedAtUtc' AND object_id = OBJECT_ID(N'[dbo].[Notifications]'))
                        CREATE INDEX [IX_Notifications_RecipientUserId_IsRead_CreatedAtUtc] ON [dbo].[Notifications] ([RecipientUserId], [IsRead], [CreatedAtUtc]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProcessedEvents_ProcessedAtUtc' AND object_id = OBJECT_ID(N'[dbo].[ProcessedEvents]'))
                        CREATE INDEX [IX_ProcessedEvents_ProcessedAtUtc] ON [dbo].[ProcessedEvents] ([ProcessedAtUtc]);
                """);
                return;
            }

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecipientUserId = table.Column<int>(type: "int", nullable: true),
                    RecipientRole = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoutingKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedEvents", x => x.EventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientRole_IsRead_CreatedAtUtc",
                table: "Notifications",
                columns: new[] { "RecipientRole", "IsRead", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_IsRead_CreatedAtUtc",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "IsRead", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedEvents_ProcessedAtUtc",
                table: "ProcessedEvents",
                column: "ProcessedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "ProcessedEvents");
        }
    }
}
