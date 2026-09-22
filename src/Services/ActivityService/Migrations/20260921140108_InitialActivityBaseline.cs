using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityService.Migrations
{
    /// <inheritdoc />
    public partial class InitialActivityBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql("""
                    IF OBJECT_ID(N'[dbo].[Activities]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[Activities] (
                            [Id] int NOT NULL IDENTITY,
                            [SourceReportId] int NULL,
                            [SourceReportDetailId] int NULL,
                            [ClubId] int NOT NULL,
                            [ClubName] nvarchar(200) NOT NULL,
                            [Title] nvarchar(200) NOT NULL,
                            [Description] nvarchar(2000) NOT NULL,
                            [StartTimeUtc] datetimeoffset NOT NULL,
                            [EndTimeUtc] datetimeoffset NOT NULL,
                            [MeetingDaysCsv] nvarchar(32) NOT NULL,
                            [Location] nvarchar(200) NOT NULL,
                            [Status] nvarchar(40) NOT NULL,
                            [CreatedByUserId] int NOT NULL,
                            [CreatedAtUtc] datetimeoffset NOT NULL,
                            [UpdatedAtUtc] datetimeoffset NOT NULL,
                            CONSTRAINT [PK_Activities] PRIMARY KEY ([Id])
                        );
                    END;

                    IF OBJECT_ID(N'[dbo].[ActivityAttendances]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[ActivityAttendances] (
                            [Id] int NOT NULL IDENTITY,
                            [ActivityId] int NOT NULL,
                            [UserId] int NOT NULL,
                            [FullName] nvarchar(200) NOT NULL,
                            [AttendanceDate] date NOT NULL,
                            [Status] nvarchar(40) NOT NULL,
                            [Note] nvarchar(1000) NULL,
                            [CheckedInAtUtc] datetimeoffset NULL,
                            [CheckedInByUserId] int NULL,
                            [CreatedAtUtc] datetimeoffset NOT NULL,
                            [UpdatedAtUtc] datetimeoffset NULL,
                            CONSTRAINT [PK_ActivityAttendances] PRIMARY KEY ([Id]),
                            CONSTRAINT [FK_ActivityAttendances_Activities_ActivityId] FOREIGN KEY ([ActivityId]) REFERENCES [dbo].[Activities] ([Id]) ON DELETE NO ACTION
                        );
                    END;

                    IF OBJECT_ID(N'[dbo].[ActivityParticipants]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [dbo].[ActivityParticipants] (
                            [Id] int NOT NULL IDENTITY,
                            [ActivityId] int NOT NULL,
                            [UserId] int NOT NULL,
                            [FullName] nvarchar(200) NOT NULL,
                            [AttendanceStatus] nvarchar(40) NOT NULL,
                            [RegisteredAtUtc] datetimeoffset NOT NULL,
                            CONSTRAINT [PK_ActivityParticipants] PRIMARY KEY ([Id]),
                            CONSTRAINT [FK_ActivityParticipants_Activities_ActivityId] FOREIGN KEY ([ActivityId]) REFERENCES [dbo].[Activities] ([Id]) ON DELETE CASCADE
                        );
                    END;

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Activities_ClubId_StartTimeUtc' AND object_id = OBJECT_ID(N'[dbo].[Activities]'))
                        CREATE INDEX [IX_Activities_ClubId_StartTimeUtc] ON [dbo].[Activities] ([ClubId], [StartTimeUtc]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Activities_SourceReportId' AND object_id = OBJECT_ID(N'[dbo].[Activities]'))
                        CREATE UNIQUE INDEX [IX_Activities_SourceReportId] ON [dbo].[Activities] ([SourceReportId]) WHERE [SourceReportId] IS NOT NULL;

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Activities_StartTimeUtc' AND object_id = OBJECT_ID(N'[dbo].[Activities]'))
                        CREATE INDEX [IX_Activities_StartTimeUtc] ON [dbo].[Activities] ([StartTimeUtc]) INCLUDE ([ClubId], [Title], [Status]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Activities_Status' AND object_id = OBJECT_ID(N'[dbo].[Activities]'))
                        CREATE INDEX [IX_Activities_Status] ON [dbo].[Activities] ([Status]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivityAttendances_ActivityId_Status' AND object_id = OBJECT_ID(N'[dbo].[ActivityAttendances]'))
                        CREATE INDEX [IX_ActivityAttendances_ActivityId_Status] ON [dbo].[ActivityAttendances] ([ActivityId], [Status]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivityAttendances_ActivityId_UserId_AttendanceDate' AND object_id = OBJECT_ID(N'[dbo].[ActivityAttendances]'))
                        CREATE UNIQUE INDEX [IX_ActivityAttendances_ActivityId_UserId_AttendanceDate] ON [dbo].[ActivityAttendances] ([ActivityId], [UserId], [AttendanceDate]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivityAttendances_UserId_AttendanceDate' AND object_id = OBJECT_ID(N'[dbo].[ActivityAttendances]'))
                        CREATE INDEX [IX_ActivityAttendances_UserId_AttendanceDate] ON [dbo].[ActivityAttendances] ([UserId], [AttendanceDate]) INCLUDE ([ActivityId], [Status]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivityParticipants_ActivityId_UserId' AND object_id = OBJECT_ID(N'[dbo].[ActivityParticipants]'))
                        CREATE UNIQUE INDEX [IX_ActivityParticipants_ActivityId_UserId] ON [dbo].[ActivityParticipants] ([ActivityId], [UserId]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivityParticipants_UserId' AND object_id = OBJECT_ID(N'[dbo].[ActivityParticipants]'))
                        CREATE INDEX [IX_ActivityParticipants_UserId] ON [dbo].[ActivityParticipants] ([UserId]) INCLUDE ([ActivityId], [AttendanceStatus]);
                """);
                return;
            }

            migrationBuilder.CreateTable(
                name: "Activities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceReportId = table.Column<int>(type: "int", nullable: true),
                    SourceReportDetailId = table.Column<int>(type: "int", nullable: true),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    ClubName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    StartTimeUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndTimeUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MeetingDaysCsv = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ActivityAttendances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AttendanceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CheckedInAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CheckedInByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityAttendances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityAttendances_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivityParticipants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AttendanceStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RegisteredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityParticipants_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_ClubId_StartTimeUtc",
                table: "Activities",
                columns: new[] { "ClubId", "StartTimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_SourceReportId",
                table: "Activities",
                column: "SourceReportId",
                unique: true,
                filter: "[SourceReportId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_StartTimeUtc",
                table: "Activities",
                column: "StartTimeUtc")
                .Annotation("SqlServer:Include", new[] { "ClubId", "Title", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_Status",
                table: "Activities",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAttendances_ActivityId_Status",
                table: "ActivityAttendances",
                columns: new[] { "ActivityId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAttendances_ActivityId_UserId_AttendanceDate",
                table: "ActivityAttendances",
                columns: new[] { "ActivityId", "UserId", "AttendanceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAttendances_UserId_AttendanceDate",
                table: "ActivityAttendances",
                columns: new[] { "UserId", "AttendanceDate" })
                .Annotation("SqlServer:Include", new[] { "ActivityId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityParticipants_ActivityId_UserId",
                table: "ActivityParticipants",
                columns: new[] { "ActivityId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityParticipants_UserId",
                table: "ActivityParticipants",
                column: "UserId")
                .Annotation("SqlServer:Include", new[] { "ActivityId", "AttendanceStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityAttendances");

            migrationBuilder.DropTable(
                name: "ActivityParticipants");

            migrationBuilder.DropTable(
                name: "Activities");
        }
    }
}
