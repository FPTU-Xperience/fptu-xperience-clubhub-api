using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubService.Data;
using ClubService.Endpoints;
using ClubService.Models;
using FinanceService.Data;
using FinanceService.Endpoints;
using FinanceService.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NotificationService.Data;
using NotificationService.Endpoints;
using NotificationService.Models;
using ReportService.Data;
using ReportService.Endpoints;
using ReportService.Models;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Theory]
    [InlineData("/api/clubs/applications/1")]
    [InlineData("/api/finance/settlements/1")]
    [InlineData("/api/notifications/1")]
    [InlineData("/api/reporting-deadlines")]
    [InlineData("/api/reports/1/file")]
    [InlineData("/api/reports/1/file/preview")]
    [InlineData("/api/reports/1/file/download")]
    public async Task AdminReadContract_AnonymousIsRejected(string path)
    {
        await using var app = await CreateAdminContractAppAsync();
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData(100, AuthRoles.ClubMember, HttpStatusCode.OK)]
    [InlineData(101, AuthRoles.ClubMember, HttpStatusCode.Forbidden)]
    [InlineData(101, AuthRoles.ClubManager, HttpStatusCode.Forbidden)]
    [InlineData(101, AuthRoles.Admin, HttpStatusCode.OK)]
    [InlineData(101, AuthRoles.StudentAffairsAdmin, HttpStatusCode.OK)]
    [InlineData(100, AuthRoles.SystemAdmin, HttpStatusCode.Forbidden)]
    public async Task AdminReadContract_ApplicationRequiresOwnerOrReviewer(int actorId, string role, HttpStatusCode expected)
    {
        await using var app = await CreateAdminContractAppAsync();
        using var client = CreateAdminContractClient(app, actorId, role);
        var response = await client.GetAsync("/api/clubs/applications/1?requesterUserId=100");
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, body.GetProperty("id").GetInt32());
            Assert.Equal(100, body.GetProperty("requesterUserId").GetInt32());
            Assert.Equal("Application one", body.GetProperty("name").GetString());
        }
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData("", 2)]
    [InlineData("Submitted", 1)]
    [InlineData("Rejected", 1)]
    [InlineData("NeedsRevision", 0)]
    [InlineData("Approved", 0)]
    public async Task AdminReadContract_ApplicationStatusFilterPreservesArray(string? status, int expectedCount)
    {
        await using var app = await CreateAdminContractAppAsync();
        using var client = CreateAdminContractClient(app, 101, AuthRoles.StudentAffairsAdmin);
        var path = status is null ? "/api/clubs/applications" : $"/api/clubs/applications?status={status}";
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(rows);
        Assert.Equal(expectedCount, rows.Length);
        if (!string.IsNullOrEmpty(status))
        {
            Assert.All(rows, row => Assert.Equal(status, row.GetProperty("status").GetString()));
        }
    }

    [Fact]
    public async Task AdminReadContract_ApplicationInvalidStatusIsRejectedAndMeRemainsScoped()
    {
        await using var app = await CreateAdminContractAppAsync();
        using var reviewer = CreateAdminContractClient(app, 101, AuthRoles.Admin);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await reviewer.GetAsync("/api/clubs/applications?status=not-a-state")).StatusCode);
        using var member = CreateAdminContractClient(app, 100, AuthRoles.ClubMember);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/clubs/applications?status=Submitted")).StatusCode);
        var mine = await member.GetFromJsonAsync<JsonElement[]>("/api/clubs/applications/me?requesterUserId=101");
        Assert.NotNull(mine);
        Assert.Equal(1, Assert.Single(mine).GetProperty("id").GetInt32());
    }

    [Theory]
    [InlineData(AuthRoles.ClubManager, 1, true, false, HttpStatusCode.OK)]
    [InlineData(AuthRoles.Treasurer, 1, false, true, HttpStatusCode.OK)]
    [InlineData(AuthRoles.ClubMember, 1, false, false, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.ClubManager, 2, true, false, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Admin, 2, false, false, HttpStatusCode.OK)]
    [InlineData(AuthRoles.StudentAffairsAdmin, 2, false, false, HttpStatusCode.OK)]
    [InlineData(AuthRoles.SystemAdmin, 1, true, true, HttpStatusCode.Forbidden)]
    public async Task AdminReadContract_SettlementRequiresFinanceClubAccess(
        string role, int clubId, bool manager, bool treasurer, HttpStatusCode expected)
    {
        var access = new ClubAccessSnapshot(clubId, "Test club", manager, treasurer, true, [100], [100], treasurer ? [100] : []);
        await using var app = await CreateAdminContractAppAsync(access);
        using var client = CreateAdminContractClient(app, 100, role);
        var response = await client.GetAsync("/api/finance/settlements/1?clubId=1");
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
            var list = await client.GetFromJsonAsync<JsonElement>("/api/finance/settlements?page=1&pageSize=20");
            Assert.Equal(list.GetProperty("items")[0].ToString(), detail.ToString());
            Assert.False(detail.TryGetProperty("budgetProposal", out _));
        }
    }

    [Theory]
    [InlineData(1, 100, AuthRoles.ClubMember, false, HttpStatusCode.OK)]
    [InlineData(1, 101, AuthRoles.ClubMember, false, HttpStatusCode.Forbidden)]
    [InlineData(1, 101, AuthRoles.StudentAffairsAdmin, false, HttpStatusCode.Forbidden)]
    [InlineData(1, 101, AuthRoles.SystemAdmin, false, HttpStatusCode.Forbidden)]
    [InlineData(1, 101, AuthRoles.Admin, false, HttpStatusCode.OK)]
    [InlineData(2, 100, AuthRoles.ClubMember, true, HttpStatusCode.OK)]
    [InlineData(2, 100, AuthRoles.ClubMember, false, HttpStatusCode.Forbidden)]
    public async Task AdminReadContract_NotificationUsesRecipientScopeWithoutMarkingRead(
        int notificationId, int actorId, string role, bool derivedTreasurer, HttpStatusCode expected)
    {
        var access = derivedTreasurer
            ? new[] { new ClubAccessSnapshot(1, "Test club", false, true, true, [], [actorId], [actorId]) }
            : [];
        await using var app = await CreateAdminContractAppAsync(access);
        using var client = CreateAdminContractClient(app, actorId, role);
        var response = await client.GetAsync($"/api/notifications/{notificationId}?recipientUserId=100&recipientRole=TREASURER");
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(notificationId, detail.GetProperty("id").GetInt32());
            Assert.False(detail.GetProperty("isRead").GetBoolean());
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.False((await db.Notifications.SingleAsync(x => x.Id == notificationId)).IsRead);
    }

    [Theory]
    [InlineData("/api/clubs/applications/999999")]
    [InlineData("/api/finance/settlements/999999")]
    [InlineData("/api/notifications/999999")]
    public async Task AdminReadContract_MissingResourceReturns404(string path)
    {
        await using var app = await CreateAdminContractAppAsync();
        using var client = CreateAdminContractClient(app, 100, AuthRoles.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("/api/clubs/applications/1")]
    [InlineData("/api/finance/settlements/1")]
    [InlineData("/api/notifications/1")]
    public async Task AdminReadContract_InvalidActorIdIsRejected(string path)
    {
        await using var app = await CreateAdminContractAppAsync();
        using var client = CreateAdminContractClient(app, 0, AuthRoles.Admin);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData(AuthRoles.Admin, HttpStatusCode.OK)]
    [InlineData(AuthRoles.StudentAffairsAdmin, HttpStatusCode.OK)]
    [InlineData(AuthRoles.SystemAdmin, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.ClubManager, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Treasurer, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.ClubMember, HttpStatusCode.Forbidden)]
    public async Task AdminReadContract_DeadlineAliasKeepsReviewerPolicy(string role, HttpStatusCode expected)
    {
        await using var app = await CreateAdminContractAppAsync(CreateAccessSnapshot(1, 100));
        using var client = CreateAdminContractClient(app, 100, role);
        var original = await client.GetAsync("/api/deadlines");
        var alias = await client.GetAsync("/api/reporting-deadlines");
        Assert.Equal(expected, original.StatusCode);
        Assert.Equal(expected, alias.StatusCode);
        Assert.Equal(await original.Content.ReadAsStringAsync(), await alias.Content.ReadAsStringAsync());
        if (role == AuthRoles.ClubManager)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/deadlines/me")).StatusCode);
        }
    }

    [Theory]
    [InlineData("GET", "GET", "")]
    [InlineData("GET", "GET", "/preview")]
    [InlineData("GET", "GET", "/download")]
    [InlineData("PUT", "POST", "")]
    [InlineData("DELETE", "DELETE", "")]
    public async Task AdminReadContract_FileAliasesShareHandlerAndSecurityMetadata(string oldMethod, string newMethod, string suffix)
    {
        await using var app = await CreateAdminContractAppAsync();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        var original = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == $"/api/reports/{{reportId:int}}/uploaded-file{suffix}"
            && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(oldMethod));
        var alias = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == $"/api/reports/{{reportId:int}}/file{suffix}"
            && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(newMethod));
        Assert.NotNull(original.Metadata.GetMetadata<MethodInfo>());
        Assert.Equal(original.Metadata.GetMetadata<MethodInfo>(), alias.Metadata.GetMetadata<MethodInfo>());
        Assert.Equal(original.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy),
            alias.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy));
        Assert.Null(alias.Metadata.GetMetadata<IAllowAnonymous>());
        if (newMethod == "POST")
        {
            Assert.False(alias.Metadata.GetMetadata<IAntiforgeryMetadata>()!.RequiresValidation);
        }

        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(new HttpMethod(newMethod), $"/api/reports/1/file{suffix}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Theory]
    [InlineData("GET", "GET", "")]
    [InlineData("GET", "GET", "/preview")]
    [InlineData("GET", "GET", "/download")]
    [InlineData("PUT", "POST", "")]
    [InlineData("DELETE", "DELETE", "")]
    public async Task AdminReadContract_FileAliasesRejectCrossClubAccess(string oldMethod, string newMethod, string suffix)
    {
        await using var app = await CreateAdminContractAppAsync(CreateAccessSnapshot(2, 101));
        using var client = CreateAdminContractClient(app, 101, AuthRoles.ClubManager);
        using var original = new HttpRequestMessage(new HttpMethod(oldMethod), $"/api/reports/1/uploaded-file{suffix}");
        using var alias = new HttpRequestMessage(new HttpMethod(newMethod), $"/api/reports/1/file{suffix}");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(original)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(alias)).StatusCode);
    }

    [Fact]
    public async Task AdminReadContract_FileMetadataAliasReturnsSameSafeDto()
    {
        await using var app = await CreateAdminContractAppAsync(CreateAccessSnapshot(1, 100));
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        var original = await client.GetAsync("/api/reports/1/uploaded-file");
        var alias = await client.GetAsync("/api/reports/1/file");
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alias.StatusCode);
        var text = await alias.Content.ReadAsStringAsync();
        Assert.Equal(await original.Content.ReadAsStringAsync(), text);
        var body = JsonSerializer.Deserialize<JsonElement>(text);
        Assert.Equal("report.pdf", body.GetProperty("originalFileName").GetString());
        Assert.False(body.TryGetProperty("storagePath", out _));
        Assert.False(body.TryGetProperty("checksum", out _));
    }

    [Theory]
    [InlineData("uploaded-file", "PUT")]
    [InlineData("file", "POST")]
    public async Task AdminReadContract_FileReplacementKeepsValidation(string segment, string method)
    {
        await using var app = await CreateAdminContractAppAsync(CreateAccessSnapshot(1, 100));
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/reports/1/{segment}")
        {
            Content = JsonContent.Create(new { file = "not a multipart upload" })
        };
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("multipart/form-data", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("uploaded-file")]
    [InlineData("file")]
    public async Task AdminReadContract_FileDeleteKeepsSoftDeleteAndAudit(string segment)
    {
        await using var app = await CreateAdminContractAppAsync(CreateAccessSnapshot(1, 100));
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/reports/1/{segment}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/reports/1/{segment}")).StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var report = await db.Reports.Include(row => row.UploadedFile).SingleAsync();
        Assert.False(report.UploadedFile!.IsActive);
        Assert.Equal(ReportStatuses.Draft, report.Status);
        Assert.Single(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public void AdminReadContract_GatewayAliasesKeepClustersAuthAndUploadLimiter()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Gateway/ApiGateway/yarp.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var routes = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");
        var alias = Assert.Single(routes.EnumerateObject(), route =>
            route.Value.GetProperty("Match").GetProperty("Path").GetString() == "/api/reports/{reportId}/file").Value;
        var original = routes.GetProperty("report-file-replace");
        foreach (var property in new[] { "ClusterId", "AuthorizationPolicy", "RateLimiterPolicy", "Order" })
        {
            Assert.Equal(original.GetProperty(property).ToString(), alias.GetProperty(property).ToString());
        }
        Assert.Equal("upload", alias.GetProperty("RateLimiterPolicy").GetString());
        Assert.True(alias.GetProperty("Order").GetInt32() < routes.GetProperty("reports").GetProperty("Order").GetInt32());
        Assert.False(alias.GetProperty("Match").TryGetProperty("Methods", out _));
        var deadlines = Assert.Single(routes.EnumerateObject(), route =>
            route.Value.GetProperty("Match").GetProperty("Path").GetString() == "/api/reporting-deadlines").Value;
        Assert.Equal("report-service", deadlines.GetProperty("ClusterId").GetString());
        Assert.Equal("Default", deadlines.GetProperty("AuthorizationPolicy").GetString());
        Assert.Equal("general-api", deadlines.GetProperty("RateLimiterPolicy").GetString());
    }

    private static HttpClient CreateAdminContractClient(WebApplication app, int userId, string role)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(userId, role));
        return client;
    }

    private static async Task<WebApplication> CreateAdminContractAppAsync(params ClubAccessSnapshot[] access)
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
        var dbName = $"admin-contract-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ClubDbContext>(options => options.UseInMemoryDatabase(dbName + "-club"));
        builder.Services.AddDbContext<FinanceDbContext>(options => options.UseInMemoryDatabase(dbName + "-finance"));
        builder.Services.AddDbContext<NotificationDbContext>(options => options.UseInMemoryDatabase(dbName + "-notification"));
        builder.Services.AddDbContext<ReportDbContext>(options => options.UseInMemoryDatabase(dbName + "-report"));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(CreateClubAccessClient(access));
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapApplicationEndpoints();
        app.MapNotificationEndpoints();
        app.MapDeadlineEndpoints();
        app.MapGroup("/api/finance").RequireAuthorization(AuthPolicies.BusinessAccess).MapSettlementEndpoints();
        app.MapGroup("/api/reports").RequireAuthorization(AuthPolicies.BusinessAccess).MapReportFileEndpoints();
        app.MapGroup("/api/reports").RequireAuthorization(AuthPolicies.BusinessAccess).MapReportCrudEndpoints();
        app.MapGroup("/api/reports").RequireAuthorization(AuthPolicies.BusinessAccess).MapReportQueryEndpoints();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var clubDb = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            clubDb.ClubCreationApplications.AddRange(
                new ClubCreationApplication { Id = 1, RequesterUserId = 100, Name = "Application one", Code = "ONE" },
                new ClubCreationApplication { Id = 2, RequesterUserId = 101, Name = "Application two", Code = "TWO", Status = ClubApplicationStatuses.Rejected });
            await clubDb.SaveChangesAsync();
            var financeDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            financeDb.Settlements.Add(new Settlement
            {
                Id = 1,
                BudgetProposal = new BudgetProposal { Id = 1, ClubId = 1, ClubName = "Club one", Title = "Budget one", Status = FinanceStatuses.Approved },
                TotalSpent = 100m,
                ReceiptUrl = "https://example.invalid/receipt.pdf"
            });
            await financeDb.SaveChangesAsync();
            var notificationDb = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            notificationDb.Notifications.AddRange(
                new Notification { Id = 1, RecipientUserId = 100, Title = "Private message", Message = "Owner only" },
                new Notification { Id = 2, RecipientRole = AuthRoles.Treasurer, Title = "Treasurer message", Message = "Role recipient" });
            await notificationDb.SaveChangesAsync();
            var reportDb = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
            reportDb.ReportingDeadlines.Add(new ReportingDeadline { Period = "2026-09", DueDate = new DateOnly(2026, 9, 30), IsActive = true });
            reportDb.Reports.Add(new Report
            {
                Id = 1,
                ClubId = 1,
                ClubName = "Club one",
                CreatedByUserId = 100,
                Period = "2026-09",
                ReportType = "MONTHLY",
                Tag = "Monthly",
                Status = ReportStatuses.Draft,
                DueDate = new DateOnly(2026, 9, 30),
                UploadedFile = new ReportUploadedFile { OriginalFileName = "report.pdf", ContentType = "application/pdf", FileExtension = ".pdf" }
            });
            await reportDb.SaveChangesAsync();
        }
        await app.StartAsync();
        return app;
    }
}
