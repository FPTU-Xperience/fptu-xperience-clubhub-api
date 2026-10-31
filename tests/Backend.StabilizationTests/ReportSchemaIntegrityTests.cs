using System.Net;
using System.Net.Http.Json;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReportService.Contracts;
using ReportService.Data;
using ReportService.Endpoints;
using ReportService.Models;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    private const string ReportSchemaPreviousMigration = "20260921215039_AddOutboxMessageClaimFields";

    [Fact]
    public async Task ReportSchema_FreshMigrationsSupportFinanceAndPreviewFieldsWithoutUpgrader()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var db = CreateReportSchemaContext(database.ConnectionString);
        await db.Database.MigrateAsync();
        var generatedAt = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        var report = NewReportSchemaReport("FINANCE-SCHEMA");
        report.BudgetProposalId = 700;
        report.BudgetRequestedAmount = 123.45m;
        report.BudgetApprovedAmount = 100.25m;
        report.BudgetDescription = "Schema reconciliation keeps finance provenance";
        report.FinanceSubmittedAtUtc = generatedAt;
        report.PublishedActivityId = 800;
        report.UploadedFile = new ReportUploadedFile
        {
            OriginalFileName = "report.docx",
            StoredFileName = "stored.docx",
            ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            FileExtension = ".docx",
            StoragePath = "audit/original.docx",
            PreviewFileName = "preview.pdf",
            PreviewStoragePath = "audit/preview.pdf",
            PreviewContentType = "application/pdf",
            PreviewStatus = "Failed",
            PreviewErrorMessage = "Historical generation diagnostic",
            PreviewGeneratedAtUtc = generatedAt
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var saved = await db.Reports.Include(x => x.UploadedFile).SingleAsync();
        Assert.Equal(700, saved.BudgetProposalId);
        Assert.Equal(123.45m, saved.BudgetRequestedAmount);
        Assert.Equal(100.25m, saved.BudgetApprovedAmount);
        Assert.Equal(report.BudgetDescription, saved.BudgetDescription);
        Assert.Equal(generatedAt, saved.FinanceSubmittedAtUtc);
        Assert.Equal(800, saved.PublishedActivityId);
        Assert.NotNull(saved.UploadedFile);
        Assert.Equal("preview.pdf", saved.UploadedFile.PreviewFileName);
        Assert.Equal("audit/preview.pdf", saved.UploadedFile.PreviewStoragePath);
        Assert.Equal("application/pdf", saved.UploadedFile.PreviewContentType);
        Assert.Equal("Failed", saved.UploadedFile.PreviewStatus);
        Assert.Equal("Historical generation diagnostic", saved.UploadedFile.PreviewErrorMessage);
        Assert.Equal(generatedAt, saved.UploadedFile.PreviewGeneratedAtUtc);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task ReportSchema_LegacyUpgraderDataIsPreservedAndInvalidDetailLinksAreDetached()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var db = CreateReportSchemaContext(database.ConnectionString);
        await db.GetService<IMigrator>().MigrateAsync(ReportSchemaPreviousMigration);
        await ReportSchemaUpgrader.ApplyAsync(db);
        var first = NewReportSchemaReport("LEGACY-A");
        var second = NewReportSchemaReport("LEGACY-B");
        first.BudgetProposalId = 901;
        first.BudgetDescription = "Keep existing budget link";
        first.Details.Add(new ReportDetail { ActivityName = "Original A" });
        second.Details.Add(new ReportDetail { ActivityName = "Original B" });
        db.Reports.AddRange(first, second);
        await db.SaveChangesAsync();
        var validId = first.Details.Single().Id;
        db.ReportAttachments.AddRange(
            NewReportSchemaAttachment(first.Id, validId, "valid.pdf"),
            NewReportSchemaAttachment(first.Id, int.MaxValue, "orphan.pdf"),
            NewReportSchemaAttachment(first.Id, second.Details.Single().Id, "cross-report.pdf"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await db.Database.MigrateAsync();
        var saved = await db.Reports.SingleAsync(x => x.Id == first.Id);
        Assert.Equal(901, saved.BudgetProposalId);
        Assert.Equal("Keep existing budget link", saved.BudgetDescription);
        var files = await db.ReportAttachments.OrderBy(x => x.FileName).ToArrayAsync();
        Assert.Equal(3, files.Length);
        Assert.Equal(validId, Assert.Single(files, x => x.FileName == "valid.pdf").ReportDetailId);
        Assert.Null(Assert.Single(files, x => x.FileName == "orphan.pdf").ReportDetailId);
        Assert.Null(Assert.Single(files, x => x.FileName == "cross-report.pdf").ReportDetailId);
        Assert.All(files, x => Assert.Equal("evidence/" + x.FileName, x.StoragePath));
    }

    [Fact]
    public async Task ReportSchema_DuplicateBudgetLinksBlockUpgradeWithoutRewritingReports()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var db = CreateReportSchemaContext(database.ConnectionString);
        await db.GetService<IMigrator>().MigrateAsync(ReportSchemaPreviousMigration);
        await ReportSchemaUpgrader.ApplyAsync(db);
        await db.Database.ExecuteSqlRawAsync("DROP INDEX [IX_Reports_BudgetProposalId] ON [dbo].[Reports]");
        var first = NewReportSchemaReport("DUPLICATE-A");
        var second = NewReportSchemaReport("DUPLICATE-B");
        first.BudgetProposalId = 902;
        second.BudgetProposalId = 902;
        db.Reports.AddRange(first, second);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.MigrateAsync());
        Assert.Equal(51020, error.Number);
        Assert.Equal(2, await db.Reports.CountAsync(x => x.BudgetProposalId == 902));
        Assert.Contains(ReportSchemaPreviousMigration, await db.Database.GetAppliedMigrationsAsync());
        Assert.NotEmpty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task ReportSchema_ReplacementKeepsAttachmentAsReportEvidenceAndRejectsOtherOwners()
    {
        await using var fixture = await ReportSchemaFixture.CreateAsync();
        int reportId;
        int originalDetailId;
        int attachmentId;
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
            var report = NewReportSchemaReport("REPLACE");
            report.Details.Add(new ReportDetail { ActivityName = "Original", Description = "Original detail" });
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;
            originalDetailId = report.Details.Single().Id;
            var attachment = NewReportSchemaAttachment(reportId, originalDetailId, "retained.pdf");
            db.ReportAttachments.Add(attachment);
            await db.SaveChangesAsync();
            attachmentId = attachment.Id;
        }

        var request = new UpdateReportRequest("REPLACE", null, "Edited", null, null, null, null,
            [new UpsertReportDetailRequest(originalDetailId, "Replacement", new DateOnly(2026, 10, 1), "Updated detail", 5, "Kept evidence")]);
        using var otherOwner = CreateAdminContractClient(fixture.App, 101, AuthRoles.ClubManager);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await otherOwner.PutAsJsonAsync($"/api/reports/{reportId}", request)).StatusCode);
        using var manager = CreateAdminContractClient(fixture.App, 100, AuthRoles.ClubManager);
        var response = await manager.PutAsJsonAsync($"/api/reports/{reportId}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReportResponse>();
        Assert.NotNull(body);
        Assert.Null(Assert.Single(body.Attachments).ReportDetailId);
        Assert.Equal(attachmentId, Assert.Single(body.Attachments).Id);
        Assert.NotEqual(originalDetailId, Assert.Single(body.Details).Id);
        Assert.Equal(2, body.Version);

        await using var verify = fixture.App.Services.CreateAsyncScope();
        var saved = verify.ServiceProvider.GetRequiredService<ReportDbContext>();
        Assert.False(await saved.ReportDetails.AnyAsync(x => x.Id == originalDetailId));
        var file = await saved.ReportAttachments.SingleAsync();
        Assert.Equal(attachmentId, file.Id);
        Assert.Null(file.ReportDetailId);
        Assert.Equal("evidence/retained.pdf", file.StoragePath);
        Assert.Equal(1, await saved.AuditLogs.CountAsync(x => x.ReportId == reportId && x.Action == "Update"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportSchema_AttachmentForeignKeyRejectsMissingOrOtherReportDetail(bool missing)
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var db = CreateReportSchemaContext(database.ConnectionString);
        await db.Database.MigrateAsync();
        var first = NewReportSchemaReport("PARENT-A");
        var second = NewReportSchemaReport("PARENT-B");
        second.Details.Add(new ReportDetail { ActivityName = "Other report detail" });
        db.Reports.AddRange(first, second);
        await db.SaveChangesAsync();
        db.ReportAttachments.Add(NewReportSchemaAttachment(first.Id,
            missing ? int.MaxValue : second.Details.Single().Id, "wrong-detail.pdf"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Empty(await db.ReportAttachments.ToArrayAsync());
        Assert.Equal(2, await db.Reports.CountAsync());
    }

    [Fact]
    public async Task ReportSchema_DeletingAggregateCascadesWithoutMultipleCascadePaths()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var db = CreateReportSchemaContext(database.ConnectionString);
        await db.Database.MigrateAsync();
        var report = NewReportSchemaReport("DELETE");
        report.Details.Add(new ReportDetail { ActivityName = "Related detail" });
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        db.ReportAttachments.Add(NewReportSchemaAttachment(report.Id, report.Details.Single().Id, "linked.pdf"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(1, await db.Reports.Where(x => x.Id == report.Id).ExecuteDeleteAsync());
        Assert.Empty(await db.ReportDetails.ToArrayAsync());
        Assert.Empty(await db.ReportAttachments.ToArrayAsync());
    }

    private static ReportDbContext CreateReportSchemaContext(string connection) =>
        new(new DbContextOptionsBuilder<ReportDbContext>().UseSqlServer(connection).Options);

    private static Report NewReportSchemaReport(string period) => new()
    {
        ClubId = 1,
        ClubName = "Schema Club",
        Period = period,
        CreatedByUserId = 100,
        Status = ReportStatuses.Draft,
        DueDate = new DateOnly(2026, 10, 31)
    };

    private static ReportAttachment NewReportSchemaAttachment(int reportId, int? detailId, string name) => new()
    {
        ReportId = reportId,
        ReportDetailId = detailId,
        FileName = name,
        ContentType = "application/pdf",
        SizeBytes = 500,
        StoragePath = "evidence/" + name
    };

    private sealed class ReportSchemaFixture(WebApplication app, ManagerSqlDatabase database) : IAsyncDisposable
    {
        public WebApplication App { get; } = app;

        internal static async Task<ReportSchemaFixture> CreateAsync()
        {
            var database = await ManagerSqlDatabase.CreateAsync();
            WebApplication? app = null;
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
                builder.WebHost.UseTestServer();
                builder.Logging.ClearProviders();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Issuer"] = "ClubReportHub",
                    ["Jwt:Audience"] = "ClubReportHub.Client",
                    ["Jwt:SigningKey"] = SigningKey
                });
                builder.Services.AddDbContext<ReportDbContext>(options => options.UseSqlServer(database.ConnectionString));
                builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
                builder.Services.AddSingleton(new ClubAccessClient(CreateFakeHttpClient(_ => new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[] { new ClubAccessSnapshot(1, "Schema Club", true, false, true, [100], [], []) })
                }), new MemoryCache(new MemoryCacheOptions()), NullLogger<ClubAccessClient>.Instance));
                app = builder.Build();
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapGroup("/api/reports").RequireAuthorization(AuthPolicies.BusinessAccess).MapReportCrudEndpoints();
                await using (var scope = app.Services.CreateAsyncScope())
                {
                    await scope.ServiceProvider.GetRequiredService<ReportDbContext>().Database.MigrateAsync();
                }
                await app.StartAsync();
                return new ReportSchemaFixture(app, database);
            }
            catch
            {
                if (app is not null) await app.DisposeAsync();
                await database.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await database.DisposeAsync();
        }
    }
}
