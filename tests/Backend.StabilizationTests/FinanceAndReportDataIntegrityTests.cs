using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Messaging;
using FinanceService.Contracts;
using FinanceService.Data;
using FinanceService.Endpoints;
using FinanceService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using ReportService.Clients;
using ReportService.Contracts;
using ReportService.Data;
using ReportService.Endpoints;
using ReportService.Models;
using Xunit;

namespace Backend.StabilizationTests;

public sealed class FinanceAndReportDataIntegrityTests
{
    private const string SigningKey = "StabilizationTestsSuperSecretKey1234567890!@#$";

    #region Metadata Tests (DATA-F02, DATA-F04)

    [Fact]
    public void FinanceDbContext_Metadata_ContainsActiveSettlementFilteredUniqueIndex()
    {
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=localhost;Database=Dummy;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        using var context = new FinanceDbContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var settlementEntity = model.FindEntityType(typeof(Settlement));
        Assert.NotNull(settlementEntity);

        var indexes = settlementEntity.GetIndexes().ToList();
        var proposalActiveIndex = indexes.FirstOrDefault(i =>
            i.Properties.Count == 1
            && i.Properties[0].Name == nameof(Settlement.BudgetProposalId)
            && i.IsUnique
            && i.GetFilter() != null);

        Assert.NotNull(proposalActiveIndex);
        var filter = proposalActiveIndex.GetFilter()!;
        Assert.Contains("Rejected", filter);
        Assert.Contains("<>", filter);
    }

    [Fact]
    public void ReportDbContext_Metadata_ContainsPeriodTagFilteredUniqueIndex()
    {
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseSqlServer("Server=localhost;Database=Dummy;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        using var context = new ReportDbContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var reportEntity = model.FindEntityType(typeof(Report));
        Assert.NotNull(reportEntity);

        var indexes = reportEntity.GetIndexes().ToList();
        var periodTagIndex = indexes.FirstOrDefault(i =>
            i.Properties.Count == 3
            && i.Properties.Any(p => p.Name == nameof(Report.ClubId))
            && i.Properties.Any(p => p.Name == nameof(Report.Period))
            && i.Properties.Any(p => p.Name == nameof(Report.Tag))
            && i.IsUnique
            && i.GetFilter() != null);

        Assert.NotNull(periodTagIndex);
        var filter = periodTagIndex.GetFilter()!;
        Assert.Contains("FUTURE_EVENT", filter);
    }

    #endregion

    #region DATA-F02: Finance Settlement Invariants & Concurrency Tests

    [Fact]
    public async Task CreateSettlement_WhenProposalAlreadyHasActiveSettlement_ReturnsConflict409()
    {
        await using var app = await CreateFinanceTestAppAsync();
        const int clubId = 10;
        const int managerUserId = 101;
        var proposalId = await SeedBudgetProposalAsync(app, clubId, managerUserId, 5_000_000m);

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(managerUserId, AuthRoles.ClubManager));

        // Submit first settlement -> 200 OK
        var firstRes = await client.PostAsJsonAsync($"/api/finance/proposals/{proposalId}/settlements",
            new CreateSettlementRequest(3_500_000m, "https://storage.example.com/receipt1.pdf"));
        Assert.Equal(HttpStatusCode.OK, firstRes.StatusCode);

        // Submit second settlement -> 409 Conflict
        var secondRes = await client.PostAsJsonAsync($"/api/finance/proposals/{proposalId}/settlements",
            new CreateSettlementRequest(1_000_000m, "https://storage.example.com/receipt2.pdf"));
        Assert.Equal(HttpStatusCode.Conflict, secondRes.StatusCode);

        var err = await secondRes.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Contains("already has an active settlement", err?.Message ?? string.Empty);
    }

    [Fact]
    public async Task CreateSettlement_ConcurrentRequests_OnlyOneSucceedsSecondReturnsConflict409()
    {
        const int clubId = 11;
        const int managerUserId = 102;
        var databasePath = Path.Combine(Path.GetTempPath(), $"clubhub-settlement-concurrency-{Guid.NewGuid():N}.db");
        var app = await CreateFinanceTestAppAsync(
            clubId,
            managerUserId,
            options => options.UseSqlite($"Data Source={databasePath};Default Timeout=30"));

        try
        {
            var proposalId = await SeedBudgetProposalAsync(app, clubId, managerUserId, 10_000_000m);

            using var clientA = app.GetTestClient();
            clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(managerUserId, AuthRoles.ClubManager));

            using var clientB = app.GetTestClient();
            clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(managerUserId, AuthRoles.ClubManager));

            var taskA = clientA.PostAsJsonAsync($"/api/finance/proposals/{proposalId}/settlements",
                new CreateSettlementRequest(4_000_000m, "https://storage.example.com/receiptA.pdf"));
            var taskB = clientB.PostAsJsonAsync($"/api/finance/proposals/{proposalId}/settlements",
                new CreateSettlementRequest(5_000_000m, "https://storage.example.com/receiptB.pdf"));

            var responses = await Task.WhenAll(taskA, taskB);

            var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
            var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

            Assert.Equal(1, okCount);
            Assert.Equal(1, conflictCount);

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var activeSettlements = await db.Settlements
                .Where(s => s.BudgetProposalId == proposalId && (s.Status == FinanceStatuses.Submitted || s.Status == FinanceStatuses.Approved))
                .ToListAsync();

            Assert.Single(activeSettlements);
        }
        finally
        {
            await app.DisposeAsync();
            if (File.Exists(databasePath))
            {
                try { File.Delete(databasePath); } catch { }
            }
        }
    }

    #endregion

    #region DATA-F04: Report Period & Tag Invariants Tests

    [Fact]
    public async Task CreateReport_WhenReportExistsForClubPeriodTag_ReturnsConflict409()
    {
        const int clubId = 20;
        const int managerUserId = 201;
        await using var app = await CreateReportTestAppAsync(clubId, managerUserId);

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(managerUserId, AuthRoles.ClubManager));

        var req1 = new CreateReportRequest(
            ClubId: clubId,
            Period: "SPRING2026",
            DueDate: null,
            ExecutiveSummary: "Summary A",
            Achievements: "Ach A",
            Challenges: "Chal A",
            Recommendations: "Rec A",
            NextPeriodPlan: "Plan A",
            Details: [],
            ReportType: "ACTIVITY_REPORT",
            Tag: "Activity report");

        // First report -> 201 Created
        var res1 = await client.PostAsJsonAsync("/api/reports", req1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // Duplicate report -> 409 Conflict
        var req2 = req1 with { ExecutiveSummary = "Summary Duplicate" };
        var res2 = await client.PostAsJsonAsync("/api/reports", req2);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);

        var err = await res2.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Contains("already exists for this club, period, and tag", err?.Message ?? string.Empty);
    }

    [Fact]
    public async Task CreateReport_ConcurrentRequests_OnlyOneSucceedsSecondReturnsConflict409()
    {
        const int clubId = 21;
        const int managerUserId = 202;
        var databasePath = Path.Combine(Path.GetTempPath(), $"clubhub-report-concurrency-{Guid.NewGuid():N}.db");
        var app = await CreateReportTestAppAsync(
            clubId,
            managerUserId,
            options => options.UseSqlite($"Data Source={databasePath};Default Timeout=30"));

        try
        {
            using var clientA = app.GetTestClient();
            clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(managerUserId, AuthRoles.ClubManager));

            using var clientB = app.GetTestClient();
            clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(managerUserId, AuthRoles.ClubManager));

            var reqA = new CreateReportRequest(
                ClubId: clubId,
                Period: "FALL2026",
                DueDate: null,
                ExecutiveSummary: "Summary A",
                Achievements: "Ach A",
                Challenges: "Chal A",
                Recommendations: "Rec A",
                NextPeriodPlan: "Plan A",
                Details: [],
                ReportType: "ACTIVITY_REPORT",
                Tag: "Activity report");

            var reqB = reqA with { ExecutiveSummary = "Summary B" };

            var taskA = clientA.PostAsJsonAsync("/api/reports", reqA);
            var taskB = clientB.PostAsJsonAsync("/api/reports", reqB);

            var responses = await Task.WhenAll(taskA, taskB);

            var createdCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
            var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

            Assert.Equal(1, createdCount);
            Assert.Equal(1, conflictCount);

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
            var count = await db.Reports.CountAsync(r => r.ClubId == clubId && r.Period == "FALL2026" && r.Tag == "Activity report");
            Assert.Equal(1, count);
        }
        finally
        {
            await app.DisposeAsync();
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task CreateReport_WhenAuditAndOutboxSaveFails_RollsBackReport()
    {
        const int clubId = 22;
        const int managerUserId = 203;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var interceptor = new FailOnSaveNumberInterceptor(failOnSaveNumber: 2);
        await using var app = await CreateRelationalReportTestAppAsync(clubId, managerUserId, connection, interceptor);

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(managerUserId, AuthRoles.ClubManager));

        var request = new CreateReportRequest(
            ClubId: clubId,
            Period: "SUMMER2026",
            DueDate: null,
            ExecutiveSummary: "Atomic report",
            Achievements: "Achievement",
            Challenges: "Challenge",
            Recommendations: "Recommendation",
            NextPeriodPlan: "Plan",
            Details: [],
            ReportType: "ACTIVITY_REPORT",
            Tag: "Activity report");

        var response = await client.PostAsJsonAsync("/api/reports", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        Assert.Equal(0, await db.Reports.CountAsync());
        Assert.Equal(0, await db.OutboxMessages.CountAsync());
        Assert.Equal(0, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task CreateReport_FutureEventReports_AllowsMultipleInSamePeriod()
    {
        const int clubId = 22;
        const int managerUserId = 203;
        await using var app = await CreateReportTestAppAsync(clubId, managerUserId);

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(managerUserId, AuthRoles.ClubManager));

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));

        var req1 = new CreateReportRequest(
            ClubId: clubId,
            Period: "SUMMER2026",
            DueDate: null,
            ExecutiveSummary: "Future Event 1",
            Achievements: null,
            Challenges: null,
            Recommendations: null,
            NextPeriodPlan: null,
            Details: [new UpsertReportDetailRequest(null, "Event 1", tomorrow, "Desc 1", 20, "Success", "FutureEvent", "Hall A", null, null, null, null, null, 1)],
            ReportType: "FUTURE_EVENT",
            Tag: "Future event");

        var req2 = new CreateReportRequest(
            ClubId: clubId,
            Period: "SUMMER2026",
            DueDate: null,
            ExecutiveSummary: "Future Event 2",
            Achievements: null,
            Challenges: null,
            Recommendations: null,
            NextPeriodPlan: null,
            Details: [new UpsertReportDetailRequest(null, "Event 2", tomorrow.AddDays(5), "Desc 2", 25, "Success", "FutureEvent", "Hall B", null, null, null, null, null, 1)],
            ReportType: "FUTURE_EVENT",
            Tag: "Future event");

        var res1 = await client.PostAsJsonAsync("/api/reports", req1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        var res2 = await client.PostAsJsonAsync("/api/reports", req2);
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var count = await db.Reports.CountAsync(r => r.ClubId == clubId && r.Period == "SUMMER2026" && r.ReportType == "FUTURE_EVENT");
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task UpdateReport_WhenDuplicateClubPeriodTagExists_ReturnsConflict409()
    {
        const int clubId = 23;
        const int managerUserId = 204;
        await using var app = await CreateReportTestAppAsync(clubId, managerUserId);

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(managerUserId, AuthRoles.ClubManager));

        // Create Report 1
        var res1 = await client.PostAsJsonAsync("/api/reports", new CreateReportRequest(
            ClubId: clubId, Period: "SPRING2026", DueDate: null,
            ExecutiveSummary: "Report 1", Achievements: null, Challenges: null, Recommendations: null, NextPeriodPlan: null,
            Details: [], ReportType: "ACTIVITY_REPORT", Tag: "TagA"));
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // Create Report 2
        var res2 = await client.PostAsJsonAsync("/api/reports", new CreateReportRequest(
            ClubId: clubId, Period: "SPRING2026", DueDate: null,
            ExecutiveSummary: "Report 2", Achievements: null, Challenges: null, Recommendations: null, NextPeriodPlan: null,
            Details: [], ReportType: "ACTIVITY_REPORT", Tag: "TagB"));
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
        var report2 = await res2.Content.ReadFromJsonAsync<ReportResponse>();
        Assert.NotNull(report2);

        // Update Report 2 to have TagA (same as Report 1) -> 409 Conflict
        var updateRes = await client.PutAsJsonAsync($"/api/reports/{report2.Id}", new UpdateReportRequest(
            Period: "SPRING2026", DueDate: null,
            ExecutiveSummary: "Report 2 Updated", Achievements: null, Challenges: null, Recommendations: null, NextPeriodPlan: null,
            Details: [], ReportType: "ACTIVITY_REPORT", Tag: "TagA"));

        Assert.Equal(HttpStatusCode.Conflict, updateRes.StatusCode);
        var err = await updateRes.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Contains("Another report already uses this club, period, and tag", err?.Message ?? string.Empty);
    }

    #endregion

    #region Helpers & Test Servers

    private static async Task<WebApplication> CreateFinanceTestAppAsync(
        int clubId = 10,
        int userId = 100,
        Action<DbContextOptionsBuilder>? configureDatabase = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        var dbName = $"finance-test-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<FinanceDbContext>(o =>
        {
            if (configureDatabase is null)
            {
                o.UseInMemoryDatabase(dbName);
            }
            else
            {
                configureDatabase(o);
            }
        });
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(CreateClubAccessClient(clubId, userId));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        var group = app.MapGroup("/api/finance").RequireAuthorization();
        group.MapSettlementEndpoints();

        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> CreateReportTestAppAsync(
        int clubId,
        int userId,
        Action<DbContextOptionsBuilder>? configureDatabase = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        var dbName = $"report-test-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ReportDbContext>(options =>
        {
            if (configureDatabase is null)
            {
                options.UseInMemoryDatabase(dbName);
            }
            else
            {
                configureDatabase(options);
            }
        });
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(Substitute.For<IEventBus>());
        builder.Services.AddSingleton(CreateClubAccessClient(clubId, userId));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        var group = app.MapGroup("/api/reports").RequireAuthorization();
        group.MapReportCrudEndpoints();

        await app.StartAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        await db.Database.EnsureCreatedAsync();
        return app;
    }

    private static async Task<WebApplication> CreateRelationalReportTestAppAsync(
        int clubId,
        int userId,
        SqliteConnection connection,
        SaveChangesInterceptor interceptor)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        builder.Services.AddDbContext<ReportDbContext>(o =>
            o.UseSqlite(connection).AddInterceptors(interceptor));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(Substitute.For<IEventBus>());
        builder.Services.AddSingleton(CreateClubAccessClient(clubId, userId));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        var group = app.MapGroup("/api/reports").RequireAuthorization();
        group.MapReportCrudEndpoints();

        await app.StartAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        await db.Database.EnsureCreatedAsync();
        return app;
    }

    private static async Task<int> SeedBudgetProposalAsync(WebApplication app, int clubId, int managerUserId, decimal approvedAmount)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        await db.Database.EnsureCreatedAsync();

        var proposal = new BudgetProposal
        {
            ClubId = clubId,
            ClubName = $"Club {clubId}",
            Title = "Test Budget Proposal",
            Description = "Testing Settlement Integrity",
            RequestedAmount = approvedAmount,
            ApprovedAmount = approvedAmount,
            Status = FinanceStatuses.Approved,
            ProposedByUserId = managerUserId,
            ProposedAtUtc = DateTimeOffset.UtcNow,
            ReviewedByUserId = 1,
            ReviewedAtUtc = DateTimeOffset.UtcNow
        };

        db.BudgetProposals.Add(proposal);
        await db.SaveChangesAsync();
        return proposal.Id;
    }

    private static ClubAccessClient CreateClubAccessClient(int clubId, int userId)
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var snapshot = new ClubAccessSnapshot(
            ClubId: clubId,
            ClubName: $"Club {clubId}",
            IsManager: true,
            IsTreasurer: true,
            IsApprovedMember: true,
            ManagerUserIds: [userId],
            MemberUserIds: [userId],
            TreasurerUserIds: [userId]);

        var client = CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[] { snapshot })
        }, "http://localhost:5102/");

        return new ClubAccessClient(client, memoryCache, NullLogger<ClubAccessClient>.Instance);
    }

    private static HttpClient CreateFakeHttpClient(Func<HttpRequestMessage, HttpResponseMessage> handler, string baseAddress) =>
        new(new FakeHttpMessageHandler(handler)) { BaseAddress = new Uri(baseAddress) };

    private static string CreateToken(int userId, string role)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "ClubReportHub",
            audience: "ClubReportHub.Client",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(ClaimTypes.Role, role)
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed class FailOnSaveNumberInterceptor(int failOnSaveNumber) : SaveChangesInterceptor
    {
        private int _saveCount;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _saveCount) == failOnSaveNumber)
            {
                throw new DbUpdateException("Forced failure while persisting audit and outbox records.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed record ErrorResponse(string? Message);

    #endregion
}
