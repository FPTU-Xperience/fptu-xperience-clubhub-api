using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Endpoints;
using ActivityService.Infrastructure;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Messaging;
using FinanceService.Clients;
using FinanceService.Contracts;
using FinanceService.Data;
using FinanceService.Endpoints;
using FinanceService.Extensions;
using FinanceService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using ReportService.Clients;
using ReportService.Contracts;
using ReportService.Data;
using ReportService.Endpoints;
using ReportService.Models;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    private const string SigningKey = "cross-service-workflow-auth-test-key-32chars!";
    private const string TestInternalWorkflowToken = "test-internal-workflow-token-secret-12345";

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private static HttpClient CreateFakeHttpClient(Func<HttpRequestMessage, HttpResponseMessage> handler, string baseUrl = "http://localhost:5000/") =>
        new(new FakeHttpMessageHandler(handler))
        {
            BaseAddress = new Uri(baseUrl)
        };

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
                new Claim(ClaimTypes.Role, role),
                new Claim("club_id", "1")
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static ClubAccessSnapshot CreateAccessSnapshot(int clubId, int userId) =>
        new(
            ClubId: clubId,
            ClubName: $"Club {clubId}",
            IsManager: true,
            IsTreasurer: true,
            IsApprovedMember: true,
            ManagerUserIds: [userId],
            MemberUserIds: [userId],
            TreasurerUserIds: [userId]);

    private static ClubAccessClient CreateClubAccessClient(params ClubAccessSnapshot[] snapshots)
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var client = CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(snapshots)
        }, "http://localhost:5102/");
        return new ClubAccessClient(client, memoryCache, NullLogger<ClubAccessClient>.Instance);
    }

    private static ActivityCatalogClient CreateActivityCatalogClient() =>
        new(CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound), "http://localhost:5106/"),
            NullLogger<ActivityCatalogClient>.Instance);

    private static ActivityPublishingClient CreateActivityPublishingClient() =>
        new(CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound), "http://localhost:5106/"),
            NullLogger<ActivityPublishingClient>.Instance);

    #region SEC-F04: FinanceService Combined Report Workflow Token Protection

    [Fact]
    public void IsCombinedReportWorkflow_WithoutInternalToken_ReturnsFalse_WhenTokenConfigured()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:InternalWorkflowToken"] = TestInternalWorkflowToken
            })
            .Build();

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Combined-Report-Workflow"] = "true";

        Assert.False(context.IsCombinedReportWorkflow(config));
    }

    [Fact]
    public void IsCombinedReportWorkflow_WithMismatchedInternalToken_ReturnsFalse()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:InternalWorkflowToken"] = TestInternalWorkflowToken
            })
            .Build();

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Combined-Report-Workflow"] = "true";
        context.Request.Headers["X-Internal-Workflow-Token"] = "wrong-token";

        Assert.False(context.IsCombinedReportWorkflow(config));
    }

    [Fact]
    public void IsCombinedReportWorkflow_WithValidInternalToken_ReturnsTrue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:InternalWorkflowToken"] = TestInternalWorkflowToken
            })
            .Build();

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Combined-Report-Workflow"] = "true";
        context.Request.Headers["X-Internal-Workflow-Token"] = TestInternalWorkflowToken;

        Assert.True(context.IsCombinedReportWorkflow(config));
    }

    [Fact]
    public async Task Finance_DirectClientReview_OnReportLinkedProposal_WithoutInternalToken_IsRejected()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey,
            ["Security:InternalWorkflowToken"] = TestInternalWorkflowToken
        });

        var dbName = $"finance-test-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<FinanceDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(new FutureEventReportClient(
            CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound), "http://localhost:5103/"),
            NullLogger<FutureEventReportClient>.Instance));
        builder.Services.AddSingleton(CreateActivityCatalogClient());
        builder.Services.AddSingleton(CreateClubAccessClient(CreateAccessSnapshot(1, 100)));

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        var group = app.MapGroup("/api/finance").WithTags("Finance").RequireAuthorization();
        group.MapProposalEndpoints();

        int proposalId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var proposal = new BudgetProposal
            {
                ClubId = 1,
                ClubName = "Test Club",
                Title = "Report-linked Proposal",
                Description = "Description",
                RequestedAmount = 5000000m,
                Status = FinanceStatuses.Submitted,
                ProposedByUserId = 101,
                SourceReportId = 999 // Linked to a future event report
            };
            db.BudgetProposals.Add(proposal);
            await db.SaveChangesAsync();
            proposalId = proposal.Id;
        }

        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(100, AuthRoles.ClubManager));

        // Attempt direct review without X-Internal-Workflow-Token
        var response = await client.PostAsJsonAsync($"/api/finance/proposals/{proposalId}/manager-approve", new
        {
            note = "Direct manager approval attempt"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Review this budget together with its future event report", body);
    }

    [Fact]
    public async Task Finance_CombinedWorkflowReview_WithMismatchedReportClub_IsRejected()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey,
            ["Security:InternalWorkflowToken"] = TestInternalWorkflowToken
        });

        var dbName = $"finance-mismatch-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<FinanceDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);

        // Report returns ClubId = 2, while Proposal has ClubId = 1
        var reportSnapshot = new FutureEventReportSnapshot(
            Id: 999,
            ClubId: 2, // Different club!
            ClubName: "Other Club",
            ReportType: "FUTURE_EVENT",
            Status: "UnderReview",
            BudgetProposalId: 1,
            Details: []);

        var clientReport = CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(reportSnapshot)
        }, "http://localhost:5103/");
        builder.Services.AddSingleton(new FutureEventReportClient(clientReport, NullLogger<FutureEventReportClient>.Instance));
        builder.Services.AddSingleton(CreateActivityCatalogClient());
        builder.Services.AddSingleton(CreateClubAccessClient(CreateAccessSnapshot(1, 100)));

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        var group = app.MapGroup("/api/finance").WithTags("Finance").RequireAuthorization();
        group.MapProposalEndpoints();

        int proposalId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var proposal = new BudgetProposal
            {
                ClubId = 1,
                ClubName = "Test Club",
                Title = "Report-linked Proposal",
                Description = "Description",
                RequestedAmount = 5000000m,
                Status = FinanceStatuses.Submitted,
                ProposedByUserId = 101,
                SourceReportId = 999
            };
            db.BudgetProposals.Add(proposal);
            await db.SaveChangesAsync();
            proposalId = proposal.Id;
        }

        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(100, AuthRoles.ClubManager));
        client.DefaultRequestHeaders.Add("X-Combined-Report-Workflow", "true");
        client.DefaultRequestHeaders.Add("X-Internal-Workflow-Token", TestInternalWorkflowToken);

        var response = await client.PostAsJsonAsync($"/api/finance/proposals/{proposalId}/manager-approve", new
        {
            note = "Manager approve"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region SEC-F05: ReportService Budget Linking Verification

    [Fact]
    public async Task Report_LinkBudget_CrossClubProposal_IsRejected()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        var dbName = $"report-link-cross-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ReportDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);

        // Proposal belongs to ClubId = 99, but report is ClubId = 1
        var proposalSnapshot = new BudgetProposalDetailSnapshot(
            Id: 55,
            ClubId: 99, // Cross-club!
            SourceReportId: null,
            Status: "Draft",
            RequestedAmount: 1000000m,
            ApprovedAmount: null);

        var fakeFinanceClient = CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(proposalSnapshot)
        }, "http://localhost:5105/");
        builder.Services.AddSingleton(new FinanceWorkflowClient(
            fakeFinanceClient,
            NullLogger<FinanceWorkflowClient>.Instance));
        builder.Services.AddSingleton(CreateActivityPublishingClient());
        builder.Services.AddSingleton(CreateClubAccessClient(CreateAccessSnapshot(1, 100)));

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        var group = app.MapGroup("/api/reports").WithTags("Reports").RequireAuthorization();
        group.MapReportWorkflowEndpoints();

        int reportId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
            var report = new Report
            {
                ClubId = 1,
                ClubName = "Club 1",
                Period = "2026-09",
                ReportType = "FUTURE_EVENT",
                Tag = "Event",
                Status = ReportStatuses.AwaitingFinance,
                CreatedByUserId = 100,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7))
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;
        }

        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(100, AuthRoles.Treasurer));

        var response = await client.PostAsJsonAsync($"/api/reports/{reportId}/link-budget", new
        {
            budgetProposalId = 55,
            requestedAmount = 1000000m,
            description = "Attempt linking cross-club budget"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("The budget proposal belongs to another club", body);
    }

    [Fact]
    public async Task Report_LinkBudget_MismatchedSourceReportId_IsRejected()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        var dbName = $"report-link-mismatch-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ReportDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);

        // Proposal already tied to Report 888, but we are linking to Report 10
        var proposalSnapshot = new BudgetProposalDetailSnapshot(
            Id: 55,
            ClubId: 1,
            SourceReportId: 888, // Different report
            Status: "Draft",
            RequestedAmount: 1000000m,
            ApprovedAmount: null);

        var fakeFinanceClient = CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(proposalSnapshot)
        }, "http://localhost:5105/");
        builder.Services.AddSingleton(new FinanceWorkflowClient(
            fakeFinanceClient,
            NullLogger<FinanceWorkflowClient>.Instance));
        builder.Services.AddSingleton(CreateActivityPublishingClient());
        builder.Services.AddSingleton(CreateClubAccessClient(CreateAccessSnapshot(1, 100)));

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        var group = app.MapGroup("/api/reports").WithTags("Reports").RequireAuthorization();
        group.MapReportWorkflowEndpoints();

        int reportId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
            var report = new Report
            {
                ClubId = 1,
                ClubName = "Club 1",
                Period = "2026-09",
                ReportType = "FUTURE_EVENT",
                Tag = "Event",
                Status = ReportStatuses.AwaitingFinance,
                CreatedByUserId = 100,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7))
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;
        }

        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(100, AuthRoles.Treasurer));

        var response = await client.PostAsJsonAsync($"/api/reports/{reportId}/link-budget", new
        {
            budgetProposalId = 55,
            requestedAmount = 1000000m,
            description = "Attempt linking budget tied to another report"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("The budget proposal is not associated with this report", body);
    }

    [Fact]
    public async Task Report_LinkBudget_ValidProposal_Succeeds()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        var dbName = $"report-link-valid-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ReportDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);

        var proposalSnapshot = new BudgetProposalDetailSnapshot(
            Id: 55,
            ClubId: 1,
            SourceReportId: null, // Unlinked or matching
            Status: "Draft",
            RequestedAmount: 1000000m,
            ApprovedAmount: null);

        var fakeFinanceClient = CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(proposalSnapshot)
        }, "http://localhost:5105/");
        builder.Services.AddSingleton(new FinanceWorkflowClient(
            fakeFinanceClient,
            NullLogger<FinanceWorkflowClient>.Instance));
        builder.Services.AddSingleton(CreateActivityPublishingClient());
        builder.Services.AddSingleton(CreateClubAccessClient(CreateAccessSnapshot(1, 100)));

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        var group = app.MapGroup("/api/reports").WithTags("Reports").RequireAuthorization();
        group.MapReportWorkflowEndpoints();

        int reportId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
            var report = new Report
            {
                ClubId = 1,
                ClubName = "Club 1",
                Period = "2026-09",
                ReportType = "FUTURE_EVENT",
                Tag = "Event",
                Status = ReportStatuses.AwaitingFinance,
                CreatedByUserId = 100,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7))
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;
        }

        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(100, AuthRoles.Treasurer));

        var response = await client.PostAsJsonAsync($"/api/reports/{reportId}/link-budget", new
        {
            budgetProposalId = 55,
            requestedAmount = 1000000m,
            description = "Valid linked event budget"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verifyScope = app.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var updated = await verifyDb.Reports.FindAsync(reportId);
        Assert.NotNull(updated);
        Assert.Equal(55, updated.BudgetProposalId);
        Assert.Equal(ReportStatuses.Submitted, updated.Status);
    }

    #endregion

    #region POT-F04: ActivityService Report Verification Before Publishing

    [Fact]
    public async Task Activity_CreateFromApprovedReport_ReportNotFound_IsRejected()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        var dbName = $"activity-notfound-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ActivityDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(Substitute.For<IEventBus>());
        builder.Services.AddSingleton(CreateClubAccessClient(CreateAccessSnapshot(1, 500)));

        var fakeReportClient = CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound), "http://localhost:5103/");
        builder.Services.AddSingleton(new ReportVerificationClient(
            fakeReportClient,
            NullLogger<ReportVerificationClient>.Instance));

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapActivityEndpoints();

        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken(500, AuthRoles.StudentAffairsAdmin));

        var response = await client.PostAsJsonAsync("/api/activities/from-approved-report", new
        {
            reportId = 123,
            reportDetailId = 1,
            clubId = 1,
            clubName = "Club 1",
            title = "Test Activity",
            description = "Test Desc",
            location = "Hall A",
            activityDate = "2026-10-01"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Report not found", body);
    }

    [Fact]
    public async Task Activity_CreateFromApprovedReport_MismatchedClub_IsRejected()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        var dbName = $"activity-mismatch-club-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ActivityDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(Substitute.For<IEventBus>());
        builder.Services.AddSingleton(CreateClubAccessClient(CreateAccessSnapshot(1, 500)));

        // Report in ReportService belongs to Club 2
        var snapshot = new ReportVerificationSnapshot(
            Id: 123,
            ClubId: 2,
            Status: "Approved",
            Details: [new ReportVerificationDetail(1, "Activity 1", new DateOnly(2026, 10, 1))]);

        var fakeReportClient = CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(snapshot)
        }, "http://localhost:5103/");
        builder.Services.AddSingleton(new ReportVerificationClient(
            fakeReportClient,
            NullLogger<ReportVerificationClient>.Instance));

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapActivityEndpoints();

        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken(500, AuthRoles.StudentAffairsAdmin));

        // Request specifies Club 1
        var response = await client.PostAsJsonAsync("/api/activities/from-approved-report", new
        {
            reportId = 123,
            reportDetailId = 1,
            clubId = 1,
            clubName = "Club 1",
            title = "Test Activity",
            description = "Test Desc",
            location = "Hall A",
            activityDate = "2026-10-01"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("The report belongs to another club", body);
    }

    [Fact]
    public async Task Activity_CreateFromApprovedReport_UnapprovedReport_IsRejected()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        var dbName = $"activity-unapproved-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ActivityDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(Substitute.For<IEventBus>());
        builder.Services.AddSingleton(CreateClubAccessClient(CreateAccessSnapshot(1, 500)));

        // Report is UnderReview, not Approved
        var snapshot = new ReportVerificationSnapshot(
            Id: 123,
            ClubId: 1,
            Status: "UnderReview",
            Details: [new ReportVerificationDetail(1, "Activity 1", new DateOnly(2026, 10, 1))]);

        var fakeReportClient = CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(snapshot)
        }, "http://localhost:5103/");
        builder.Services.AddSingleton(new ReportVerificationClient(
            fakeReportClient,
            NullLogger<ReportVerificationClient>.Instance));

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapActivityEndpoints();

        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken(500, AuthRoles.StudentAffairsAdmin));

        var response = await client.PostAsJsonAsync("/api/activities/from-approved-report", new
        {
            reportId = 123,
            reportDetailId = 1,
            clubId = 1,
            clubName = "Club 1",
            title = "Test Activity",
            description = "Test Desc",
            location = "Hall A",
            activityDate = "2026-10-01"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Only approved reports can be published as activities", body);
    }

    [Fact]
    public async Task Activity_CreateFromApprovedReport_DetailNotFound_IsRejected()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        var dbName = $"activity-detail-notfound-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ActivityDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(Substitute.For<IEventBus>());
        builder.Services.AddSingleton(CreateClubAccessClient(CreateAccessSnapshot(1, 500)));

        var snapshot = new ReportVerificationSnapshot(
            Id: 123,
            ClubId: 1,
            Status: "Approved",
            Details: [new ReportVerificationDetail(1, "Activity 1", new DateOnly(2026, 10, 1))]);

        var fakeReportClient = CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(snapshot)
        }, "http://localhost:5103/");
        builder.Services.AddSingleton(new ReportVerificationClient(
            fakeReportClient,
            NullLogger<ReportVerificationClient>.Instance));

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapActivityEndpoints();

        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken(500, AuthRoles.StudentAffairsAdmin));

        // Detail 999 does not exist in report
        var response = await client.PostAsJsonAsync("/api/activities/from-approved-report", new
        {
            reportId = 123,
            reportDetailId = 999,
            clubId = 1,
            clubName = "Club 1",
            title = "Test Activity",
            description = "Test Desc",
            location = "Hall A",
            activityDate = "2026-10-01"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("The report detail does not belong to the specified report", body);
    }

    [Fact]
    public async Task Activity_CreateFromApprovedReport_Valid_CreatesActivitySuccessfully()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        var dbName = $"activity-valid-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ActivityDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(Substitute.For<IEventBus>());
        builder.Services.AddSingleton(CreateClubAccessClient(CreateAccessSnapshot(1, 500)));

        var snapshot = new ReportVerificationSnapshot(
            Id: 123,
            ClubId: 1,
            Status: "Approved",
            Details: [new ReportVerificationDetail(1, "Activity 1", new DateOnly(2026, 10, 1))]);

        var fakeReportClient = CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(snapshot)
        }, "http://localhost:5103/");
        builder.Services.AddSingleton(new ReportVerificationClient(
            fakeReportClient,
            NullLogger<ReportVerificationClient>.Instance));

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapActivityEndpoints();

        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken(500, AuthRoles.StudentAffairsAdmin));

        var response = await client.PostAsJsonAsync("/api/activities/from-approved-report", new
        {
            reportId = 123,
            reportDetailId = 1,
            clubId = 1,
            clubName = "Club 1",
            title = "Test Activity",
            description = "Test Desc",
            location = "Hall A",
            activityDate = "2026-10-01"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verifyScope = app.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ActivityDbContext>();
        var created = await verifyDb.Activities.SingleOrDefaultAsync(a => a.SourceReportId == 123);
        Assert.NotNull(created);
        Assert.Equal("Test Activity", created.Title);
        Assert.Equal(1, created.ClubId);
        Assert.Equal(1, created.SourceReportDetailId);
    }

    #endregion
}
