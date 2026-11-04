using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminService.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClubCreditWalletAndRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClubCosmeticDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubCosmeticDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClubCreditWallets",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    Spent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubCreditWallets", x => new { x.UserId, x.ClubId });
                    table.CheckConstraint("CK_ClubCreditWallet_Spent", "[Spent] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ClubRewards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GrantIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    OpensAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    ClosesAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubRewards", x => x.Id);
                    table.CheckConstraint("CK_ClubReward_Price", "[Price] >= 0 AND [Price] <= 100000");
                });

            migrationBuilder.CreateTable(
                name: "ClubRewardAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RewardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubRewardAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubRewardAudits_ClubRewards_RewardId",
                        column: x => x.RewardId,
                        principalTable: "ClubRewards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClubRewardRedemptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RewardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GrantIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RewardLabel = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RewardVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceRevision = table.Column<long>(type: "bigint", nullable: false),
                    WalletVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubRewardRedemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubRewardRedemptions_ClubRewards_RewardId",
                        column: x => x.RewardId,
                        principalTable: "ClubRewards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClubCosmeticOwnerships",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CosmeticId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubCosmeticOwnerships", x => new { x.UserId, x.CosmeticId });
                    table.ForeignKey(
                        name: "FK_ClubCosmeticOwnerships_ClubCosmeticDefinitions_CosmeticId",
                        column: x => x.CosmeticId,
                        principalTable: "ClubCosmeticDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClubCosmeticOwnerships_ClubRewardRedemptions_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "ClubRewardRedemptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClubCosmeticDefinitions_ClubId",
                table: "ClubCosmeticDefinitions",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubCosmeticOwnerships_CosmeticId",
                table: "ClubCosmeticOwnerships",
                column: "CosmeticId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubCosmeticOwnerships_ReceiptId",
                table: "ClubCosmeticOwnerships",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubRewardAudits_RewardId",
                table: "ClubRewardAudits",
                column: "RewardId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubRewardRedemptions_RewardId",
                table: "ClubRewardRedemptions",
                column: "RewardId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubRewardRedemptions_UserId_ClubId_CreatedAt",
                table: "ClubRewardRedemptions",
                columns: new[] { "UserId", "ClubId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClubRewardRedemptions_UserId_RewardId",
                table: "ClubRewardRedemptions",
                columns: new[] { "UserId", "RewardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubRewards_ClubId_Status_CreatedAt",
                table: "ClubRewards",
                columns: new[] { "ClubId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClubCosmeticOwnerships");

            migrationBuilder.DropTable(
                name: "ClubCreditWallets");

            migrationBuilder.DropTable(
                name: "ClubRewardAudits");

            migrationBuilder.DropTable(
                name: "ClubCosmeticDefinitions");

            migrationBuilder.DropTable(
                name: "ClubRewardRedemptions");

            migrationBuilder.DropTable(
                name: "ClubRewards");
        }
    }
}
