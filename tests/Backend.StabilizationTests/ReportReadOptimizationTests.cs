using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
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
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReportFileMetadata_AliasesAndDetailAgreeOnOriginalAndPreviewAvailability(
        bool originalExists, bool previewExists)
    {
        await using var fixture = await ReportReadFixture.CreateAsync(originalExists, previewExists);
        using var student = CreateAdminContractClient(fixture.App, 104, AuthRoles.ClubMember);
        using var manager = CreateAdminContractClient(fixture.App, 100, AuthRoles.ClubManager);

        foreach (var client in new[] { student, manager })
        {
            var canonical = await client.GetFromJsonAsync<ReportUploadedFileResponse>("/api/reports/1/uploaded-file");
            var alias = await client.GetFromJsonAsync<ReportUploadedFileResponse>("/api/reports/1/file");
            var detail = await client.GetFromJsonAsync<ReportResponse>("/api/reports/1");

            Assert.NotNull(canonical);
            Assert.Equal(originalExists, canonical.IsDownloadAvailable);
            Assert.Equal(previewExists, canonical.IsPreviewAvailable);
            Assert.Equal(canonical, alias);
            Assert.Equal(canonical, detail!.UploadedFile);
        }
    }

    [Fact]
    public async Task ReportRead_HugePageIsEmptyAndKeepsTotalAndRequestedPage()
    {
        await using var fixture = await ReportReadFixture.CreateAsync();
        using var manager = CreateAdminContractClient(fixture.App, 100, AuthRoles.ClubManager);

        var response = await manager.GetAsync($"/api/reports?page={int.MaxValue}&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(int.MaxValue, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task ReportFileRead_UnrelatedActorIsDeniedAndInactiveFileRemainsHidden()
    {
        await using var fixture = await ReportReadFixture.CreateAsync();
        using var outsider = CreateAdminContractClient(fixture.App, 999, AuthRoles.ClubMember);
        using var manager = CreateAdminContractClient(fixture.App, 100, AuthRoles.ClubManager);

        foreach (var path in new[] { "/api/reports/1/uploaded-file", "/api/reports/1/file", "/api/reports/1" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(path)).StatusCode);
        }

        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
            (await db.Reports.Include(x => x.UploadedFile).SingleAsync()).UploadedFile!.IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync("/api/reports/1/uploaded-file")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync("/api/reports/1/file")).StatusCode);
        Assert.Null((await manager.GetFromJsonAsync<ReportResponse>("/api/reports/1"))!.UploadedFile);
    }

    [Fact]
    public async Task ReportPreview_PrefixCollisionSiblingDirectoryIsRejected()
    {
        await using var fixture = await ReportReadFixture.CreateAsync(previewExists: true, siblingPreview: true);
        using var manager = CreateAdminContractClient(fixture.App, 100, AuthRoles.ClubManager);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await manager.GetAsync("/api/reports/1/uploaded-file/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await manager.GetAsync("/api/reports/1/file/preview")).StatusCode);
    }

    private sealed class ReportReadFixture(WebApplication app, string root) : IAsyncDisposable
    {
        public WebApplication App { get; } = app;

        public static async Task<ReportReadFixture> CreateAsync(
            bool originalExists = false, bool previewExists = false, bool siblingPreview = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "clubhub-report-read-" + Guid.NewGuid().ToString("N"));
            var uploads = Path.Combine(root, "uploads");
            var previews = Path.Combine(root, "previews");
            var actualPreviews = siblingPreview ? previews + "-sibling" : previews;
            Directory.CreateDirectory(uploads);
            Directory.CreateDirectory(actualPreviews);
            var original = Path.Combine(uploads, "report.pdf");
            var preview = Path.Combine(actualPreviews, "preview.pdf");
            if (originalExists) await File.WriteAllTextAsync(original, "%PDF-original");
            if (previewExists) await File.WriteAllTextAsync(preview, "%PDF-preview");

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "ClubReportHub",
                ["Jwt:Audience"] = "ClubReportHub.Client",
                ["Jwt:SigningKey"] = SigningKey,
                ["Uploads:StoragePath"] = uploads,
                ["Uploads:PreviewStoragePath"] = previews
            });
            var database = "report-read-" + Guid.NewGuid().ToString("N");
            builder.Services.AddDbContext<ReportDbContext>(options => options.UseInMemoryDatabase(database));
            builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
            builder.Services.AddSingleton(new ClubAccessClient(CreateFakeHttpClient(request =>
            {
                var user = new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter).Subject;
                var access = user is "100" or "104"
                    ? new[] { new ClubAccessSnapshot(1, "Club 1", user == "100", false, true, [100], [], []) }
                    : [];
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(access) };
            }), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                NullLogger<ClubAccessClient>.Instance));

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            var reports = app.MapGroup("/api/reports").RequireAuthorization(AuthPolicies.BusinessAccess);
            reports.MapReportQueryEndpoints();
            reports.MapReportFileEndpoints();

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
                db.Reports.Add(new Report
                {
                    Id = 1,
                    ClubId = 1,
                    ClubName = "Club 1",
                    Period = "FALL2026",
                    Status = ReportStatuses.Approved,
                    CreatedByUserId = 100,
                    UploadedFile = new ReportUploadedFile
                    {
                        OriginalFileName = "report.pdf",
                        StoragePath = original,
                        PreviewStoragePath = preview,
                        ContentType = "application/pdf",
                        FileExtension = ".pdf",
                        PreviewStatus = "Available"
                    }
                });
                await db.SaveChangesAsync();
            }
            await app.StartAsync();
            return new ReportReadFixture(app, root);
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            var fullRoot = Path.GetFullPath(root);
            var expectedPrefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "clubhub-report-read-");
            if (fullRoot.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }
    }
}
