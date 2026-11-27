using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityService.Migrations
{
    /// <inheritdoc />
    public partial class CertificateDisplayAccountKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQL Server cannot ALTER an IDENTITY property. Rebuild this standalone
            // table in the migration transaction, copying every account/version/asset.
            migrationBuilder.Sql("""
                CREATE TABLE [ActivityCertificateDisplay_AccountKey] (
                    [UserId] int NOT NULL,
                    [Version] uniqueidentifier NOT NULL,
                    [BadgeId] uniqueidentifier NULL, [FrameId] uniqueidentifier NULL,
                    [BadgeX] int NOT NULL, [BadgeY] int NOT NULL, [BadgeSize] int NOT NULL,
                    CONSTRAINT [PK_ActivityCertificateDisplay_AccountKey] PRIMARY KEY ([UserId]));
                INSERT INTO [ActivityCertificateDisplay_AccountKey]
                    ([UserId],[Version],[BadgeId],[FrameId],[BadgeX],[BadgeY],[BadgeSize])
                SELECT [UserId],[Version],[BadgeId],[FrameId],[BadgeX],[BadgeY],[BadgeSize]
                FROM [ActivityCertificateDisplay] WITH (TABLOCKX, HOLDLOCK);
                DROP TABLE [ActivityCertificateDisplay];
                EXEC sp_rename N'ActivityCertificateDisplay_AccountKey', N'ActivityCertificateDisplay';
                EXEC sp_rename N'PK_ActivityCertificateDisplay_AccountKey', N'PK_ActivityCertificateDisplay', N'OBJECT';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE [ActivityCertificateDisplay_Identity] (
                    [UserId] int IDENTITY(1,1) NOT NULL,
                    [Version] uniqueidentifier NOT NULL,
                    [BadgeId] uniqueidentifier NULL, [FrameId] uniqueidentifier NULL,
                    [BadgeX] int NOT NULL, [BadgeY] int NOT NULL, [BadgeSize] int NOT NULL,
                    CONSTRAINT [PK_ActivityCertificateDisplay_Identity] PRIMARY KEY ([UserId]));
                SET IDENTITY_INSERT [ActivityCertificateDisplay_Identity] ON;
                INSERT INTO [ActivityCertificateDisplay_Identity]
                    ([UserId],[Version],[BadgeId],[FrameId],[BadgeX],[BadgeY],[BadgeSize])
                SELECT [UserId],[Version],[BadgeId],[FrameId],[BadgeX],[BadgeY],[BadgeSize]
                FROM [ActivityCertificateDisplay] WITH (TABLOCKX, HOLDLOCK);
                SET IDENTITY_INSERT [ActivityCertificateDisplay_Identity] OFF;
                DROP TABLE [ActivityCertificateDisplay];
                EXEC sp_rename N'ActivityCertificateDisplay_Identity', N'ActivityCertificateDisplay';
                EXEC sp_rename N'PK_ActivityCertificateDisplay_Identity', N'PK_ActivityCertificateDisplay', N'OBJECT';
                """);
        }
    }
}
