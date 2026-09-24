using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReportService.Data;
using ReportService.Endpoints;
using ReportService.Models;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Theory]
    [InlineData(AuthRoles.Admin, HttpStatusCode.OK)]
    [InlineData(AuthRoles.StudentAffairsAdmin, HttpStatusCode.OK)]
    [InlineData(AuthRoles.SystemAdmin, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.ClubManager, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Treasurer, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.ClubMember, HttpStatusCode.Forbidden)]
    public async Task ReportWriteContract_DeadlineDetailAndUpdateKeepReviewerPolicy(string role, HttpStatusCode expected)
    {
        await using var app = await CreateAdminContractAppAsync(CreateAccessSnapshot(1, 100));
        using var client = CreateAdminContractClient(app, 100, role);
        Assert.Equal(expected, (await client.GetAsync("/api/deadlines/2026-09")).StatusCode);
        var update = await client.PutAsJsonAsync("/api/deadlines/2026-09", new { dueDate = "2026-10-02" });
        Assert.Equal(expected, update.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var deadline = await db.ReportingDeadlines.SingleAsync();
        Assert.Equal(expected == HttpStatusCode.OK ? new DateOnly(2026, 10, 2) : new DateOnly(2026, 9, 30), deadline.DueDate);
        Assert.True(deadline.IsActive);
    }

    [Fact]
    public async Task ReportWriteContract_DeadlineReadTrimsPeriodAndDoesNotAliasSemesterCodes()
    {
        await using var app = await CreateAdminContractAppAsync();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
            db.ReportingDeadlines.Add(new ReportingDeadline { Period = "FA26", DueDate = new DateOnly(2026, 12, 1), IsActive = false });
            await db.SaveChangesAsync();
        }
        using var client = CreateAdminContractClient(app, 100, AuthRoles.Admin);
        var response = await client.GetAsync("/api/deadlines/%20FA26%20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FA26", body.GetProperty("period").GetString());
        Assert.False(body.GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/deadlines/FALL2026")).StatusCode);
    }

    [Fact]
    public async Task ReportWriteContract_DeadlineUpdateKeepsKeyAndPreservesOmittedActiveFlag()
    {
        await using var app = await CreateAdminContractAppAsync(CreateAccessSnapshot(1, 100));
        using var client = CreateAdminContractClient(app, 100, AuthRoles.Admin);
        var response = await client.PutAsJsonAsync("/api/deadlines/%202026-09%20",
            new { period = " 2026-09 ", dueDate = "2026-10-01", isActive = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-09", body.GetProperty("period").GetString());
        Assert.False(body.GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/deadlines/2026-09", new { dueDate = "2026-10-02" })).StatusCode);
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        Assert.Empty((await manager.GetFromJsonAsync<JsonElement[]>("/api/deadlines/me"))!);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var deadline = await db.ReportingDeadlines.SingleAsync();
        Assert.False(deadline.IsActive);
        Assert.Equal("2026-09", deadline.Period);
        Assert.Equal(new DateOnly(2026, 10, 2), deadline.DueDate);
        Assert.Single(await db.Reports.ToListAsync());
    }

    [Theory]
    [InlineData("{\"period\":\"2026-10\",\"dueDate\":\"2026-10-02\"}")]
    [InlineData("{\"period\":\"   \",\"dueDate\":\"2026-10-02\"}")]
    [InlineData("{}")]
    [InlineData("{\"dueDate\":null}")]
    [InlineData("{\"dueDate\":\"0001-01-01\"}")]
    [InlineData("{\"dueDate\":\"not-a-date\"}")]
    public async Task ReportWriteContract_DeadlineInvalidInputCannotMutate(string json)
    {
        await using var app = await CreateAdminContractAppAsync();
        using var client = CreateAdminContractClient(app, 100, AuthRoles.Admin);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/api/deadlines/2026-09", content)).StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var deadline = await db.ReportingDeadlines.SingleAsync();
        Assert.Equal(new DateOnly(2026, 9, 30), deadline.DueDate);
        Assert.True(deadline.IsActive);
    }

    [Fact]
    public async Task ReportWriteContract_DeadlineMissingUpdateDoesNotUpsertAndAnonymousCannotWrite()
    {
        await using var app = await CreateAdminContractAppAsync();
        using var anonymous = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/deadlines/2026-09")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsJsonAsync("/api/deadlines/2026-09", new { dueDate = "2026-10-02" })).StatusCode);
        using var client = CreateAdminContractClient(app, 100, AuthRoles.Admin);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync("/api/deadlines/missing", new { dueDate = "2026-10-02" })).StatusCode);
        // The existing POST contract remains an upsert.
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/deadlines", new { period = "new", dueDate = "2026-10-02", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/deadlines/new")).StatusCode);
    }

    [Theory]
    [InlineData(AuthRoles.Admin, HttpStatusCode.OK)]
    [InlineData(AuthRoles.StudentAffairsAdmin, HttpStatusCode.OK)]
    [InlineData(AuthRoles.SystemAdmin, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.ClubManager, HttpStatusCode.Forbidden)]
    public async Task ReportWriteContract_DeadlineDeleteSoftDisablesWithoutRemovingHistory(string role, HttpStatusCode expected)
    {
        await using var app = await CreateAdminContractAppAsync(CreateAccessSnapshot(1, 100));
        using var client = CreateAdminContractClient(app, 100, role);
        var response = await client.DeleteAsync("/api/deadlines/2026-09");
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
            Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/deadlines/2026-09")).StatusCode);
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var deadline = await db.ReportingDeadlines.SingleAsync();
        Assert.Equal("2026-09", deadline.Period);
        Assert.Equal(expected != HttpStatusCode.OK, deadline.IsActive);
        Assert.Single(await db.Reports.ToListAsync());
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        Assert.Equal(expected == HttpStatusCode.OK ? 0 : 1,
            (await manager.GetFromJsonAsync<JsonElement[]>("/api/deadlines/me"))!.Length);
    }

    [Fact]
    public async Task ReportWriteContract_DeadlineDeleteRejectsMissingPeriodAndAnonymousActor()
    {
        await using var app = await CreateAdminContractAppAsync();
        using var anonymous = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync("/api/deadlines/2026-09")).StatusCode);
        using var reviewer = CreateAdminContractClient(app, 100, AuthRoles.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.DeleteAsync("/api/deadlines/missing")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await reviewer.DeleteAsync("/api/deadlines/%20%20%20")).StatusCode);
    }

    [Fact]
    public async Task ReportWriteContract_DeleteArchivesDraftAndPreservesFileAndAudit()
    {
        await using var app = await CreateAdminContractAppAsync(CreateAccessSnapshot(1, 100));
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        var response = await client.DeleteAsync("/api/reports/1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/reports/1")).StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var report = await db.Reports.Include(x => x.UploadedFile).SingleAsync();
        Assert.Equal(ReportStatuses.Archived, report.Status);
        Assert.NotNull(report.UploadedFile);
        Assert.Single(await db.AuditLogs.Where(x => x.ReportId == 1 && x.Action == "Archive").ToListAsync());
        var list = await client.GetFromJsonAsync<JsonElement>("/api/reports?page=1&pageSize=20");
        Assert.Equal(0, list.GetProperty("total").GetInt32());
        var history = await client.GetFromJsonAsync<JsonElement>("/api/reports?status=Archived&page=1&pageSize=20");
        Assert.Equal(1, history.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/reports/1")).StatusCode);
    }

    [Theory]
    [InlineData(ReportStatuses.Submitted)]
    [InlineData(ReportStatuses.UnderReview)]
    [InlineData(ReportStatuses.Approved)]
    public async Task ReportWriteContract_DeleteRejectsCommittedReport(string status)
    {
        await using var app = await CreateAdminContractAppAsync(CreateAccessSnapshot(1, 100));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
            (await db.Reports.SingleAsync()).Status = status;
            await db.SaveChangesAsync();
        }
        using var client = CreateAdminContractClient(app, 100, AuthRoles.Admin);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync("/api/reports/1")).StatusCode);
        await using var verificationScope = app.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ReportDbContext>();
        Assert.Equal(status, (await verificationDb.Reports.SingleAsync()).Status);
        Assert.Single(await verificationDb.ReportUploadedFiles.ToListAsync());
    }

    [Theory]
    [InlineData(100, 1, true, ReportStatuses.Draft, HttpStatusCode.OK)]
    [InlineData(100, 1, true, ReportStatuses.Rejected, HttpStatusCode.OK)]
    [InlineData(101, 1, true, ReportStatuses.Draft, HttpStatusCode.Forbidden)]
    [InlineData(100, 2, true, ReportStatuses.Draft, HttpStatusCode.Forbidden)]
    [InlineData(100, 1, false, ReportStatuses.Draft, HttpStatusCode.Forbidden)]
    [InlineData(100, 1, true, ReportStatuses.Submitted, HttpStatusCode.BadRequest)]
    [InlineData(100, 1, true, ReportStatuses.UnderReview, HttpStatusCode.BadRequest)]
    [InlineData(100, 1, true, ReportStatuses.Approved, HttpStatusCode.BadRequest)]
    public async Task ReportWriteContract_AttachmentDeleteRequiresAuthorScopeAndEditableState(
        int actorId, int clubId, bool manager, string status, HttpStatusCode expected)
    {
        var access = new ClubAccessSnapshot(clubId, "Club", manager, false, true, manager ? [actorId] : [], [actorId]);
        await using var app = await CreateAdminContractAppAsync(access);
        await SeedContractAttachmentsAsync(app, status);
        using var client = CreateAdminContractClient(app, actorId, AuthRoles.ClubManager);
        var response = await client.DeleteAsync("/api/reports/1/attachments/10?reportId=2&userId=100&storagePath=C:/ignored");
        Assert.Equal(expected, response.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 2, await db.ReportAttachments.CountAsync());
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, await db.AuditLogs.CountAsync());
        var report = await db.Reports.SingleAsync(row => row.Id == 1);
        Assert.Equal(status, report.Status);
        if (expected == HttpStatusCode.OK)
        {
            Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
            var audit = await db.AuditLogs.SingleAsync();
            Assert.Equal(1, audit.ReportId);
            Assert.Equal(actorId, audit.ActorUserId);
            Assert.Equal("DeleteAttachment", audit.Action);
            Assert.DoesNotContain("private-storage", audit.Description);
            Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/reports/1/attachments/10")).StatusCode);
            Assert.Equal(1, await db.AuditLogs.CountAsync());
        }
    }

    [Theory]
    [InlineData("/api/reports/1/attachments/20")]
    [InlineData("/api/reports/1/attachments/999")]
    [InlineData("/api/reports/999/attachments/10")]
    public async Task ReportWriteContract_AttachmentIdsMustBelongToReport(string path)
    {
        await using var app = await CreateAdminContractAppAsync(CreateAccessSnapshot(1, 100));
        await SeedContractAttachmentsAsync(app, ReportStatuses.Draft);
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(path)).StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        Assert.Equal(2, await db.ReportAttachments.CountAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task ReportWriteContract_AttachmentAnonymousIsRejected()
    {
        await using var app = await CreateAdminContractAppAsync();
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync("/api/reports/1/attachments/10")).StatusCode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ReportWriteContract_AttachmentDatabaseFailureRollsBackAndNeverDeletesPhysicalFile(bool failAudit, bool concurrentReview)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var failure = new AttachmentAuditFailureInterceptor();
        var competingChange = new ConcurrentReportChangeInterceptor(connection);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });
        builder.Services.AddDbContext<ReportDbContext>(options => options.UseSqlite(connection).AddInterceptors(failure, competingChange));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(CreateClubAccessClient(CreateAccessSnapshot(1, 100)));
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            try { await next(); }
            catch (DbUpdateException) { context.Response.StatusCode = StatusCodes.Status500InternalServerError; }
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("/api/reports").RequireAuthorization(AuthPolicies.BusinessAccess).MapReportFileEndpoints();
        var canary = Path.GetTempFileName();
        try
        {
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
                await db.Database.EnsureCreatedAsync();
                db.Reports.Add(new Report { Id = 1, ClubId = 1, CreatedByUserId = 100, Status = ReportStatuses.Draft, Period = "2026-09", Tag = "Monthly" });
                await db.SaveChangesAsync();
            }
            await SeedContractAttachmentsAsync(app, ReportStatuses.Draft, canary);
            failure.Enabled = failAudit;
            competingChange.Enabled = concurrentReview;
            await app.StartAsync();
            using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
            var response = await client.DeleteAsync("/api/reports/1/attachments/10");
            Assert.Equal(failAudit ? HttpStatusCode.InternalServerError : concurrentReview ? HttpStatusCode.Conflict : HttpStatusCode.OK, response.StatusCode);
            Assert.True(File.Exists(canary));
            await using var verify = app.Services.CreateAsyncScope();
            var verifyDb = verify.ServiceProvider.GetRequiredService<ReportDbContext>();
            Assert.Equal(failAudit || concurrentReview ? 2 : 1, await verifyDb.ReportAttachments.CountAsync());
            Assert.Equal(failAudit || concurrentReview ? 0 : 1, await verifyDb.AuditLogs.CountAsync());
            Assert.Equal(failAudit ? 1 : 2, (await verifyDb.Reports.SingleAsync(row => row.Id == 1)).Version);
            if (failAudit) Assert.True(failure.AuditAttempted);
            if (concurrentReview)
            {
                Assert.True(competingChange.Changed);
                Assert.Equal(ReportStatuses.UnderReview, (await verifyDb.Reports.SingleAsync(row => row.Id == 1)).Status);
            }
        }
        finally
        {
            File.Delete(canary);
        }
    }

    private static async Task SeedContractAttachmentsAsync(WebApplication app, string status, string? storagePath = null)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var report = await db.Reports.SingleAsync(row => row.Id == 1);
        report.Status = status;
        db.ReportAttachments.Add(new ReportAttachment
        {
            Id = 10,
            ReportId = 1,
            FileName = "evidence.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1,
            StoragePath = storagePath ?? "private-storage/../outside/evidence.pdf"
        });
        db.Reports.Add(new Report
        {
            Id = 2,
            ClubId = 2,
            CreatedByUserId = 101,
            Status = ReportStatuses.Draft,
            Period = "2026-09",
            Tag = "Monthly",
            Attachments = [new ReportAttachment { Id = 20, FileName = "other.pdf", ContentType = "application/pdf", SizeBytes = 1, StoragePath = "private-storage/other.pdf" }]
        });
        await db.SaveChangesAsync();
    }

    private sealed class AttachmentAuditFailureInterceptor : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public bool AuditAttempted { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("INSERT INTO \"AuditLogs\"", StringComparison.Ordinal))
            {
                AuditAttempted = true;
                throw new InvalidOperationException("Injected audit persistence failure.");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class ConcurrentReportChangeInterceptor(SqliteConnection connection) : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public bool Changed { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                Enabled = false;
                await using var competingDb = new ReportDbContext(new DbContextOptionsBuilder<ReportDbContext>().UseSqlite(connection).Options);
                await competingDb.Reports.Where(report => report.Id == 1).ExecuteUpdateAsync(
                    setters => setters.SetProperty(report => report.Status, ReportStatuses.UnderReview)
                        .SetProperty(report => report.Version, report => report.Version + 1), cancellationToken);
                Changed = true;
            }
            return result;
        }
    }
}
