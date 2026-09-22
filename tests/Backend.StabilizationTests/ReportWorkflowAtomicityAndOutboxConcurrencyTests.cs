using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using ReportService.Clients;
using ReportService.Contracts;
using ReportService.Data;
using ReportService.Endpoints;
using ReportService.Models;
using Xunit;

namespace Backend.StabilizationTests;

public sealed class ReportWorkflowAtomicityAndOutboxConcurrencyTests
{
    private const string SigningKey = "Stabilization-Test-Jwt-Key-That-Is-Long-Enough-For-Sha256!";

    #region REL-F02: Outbox Publisher Lease / Claim Concurrency Tests

    [Fact]
    public async Task OutboxPublisher_ConcurrentWorkers_ClaimMessagesWithoutDuplicatePublishing()
    {
        var dbName = $"outbox-concurrency-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<ReportDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();

        // Seed 10 pending outbox messages
        await using (var seedScope = sp.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ReportDbContext>();
            for (var i = 1; i <= 10; i++)
            {
                var evt = new ReportSubmittedEvent(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    ReportId: i,
                    ClubId: 10,
                    ClubName: "Test Club",
                    Period: "FA26",
                    SubmittedByUserId: 100,
                    Status: ReportStatuses.Submitted,
                    RecipientUserId: 101,
                    WorkflowStage: "Standard",
                    RecipientUserIds: null);

                db.AddOutboxMessage(evt, EventRoutingKeys.ReportSubmitted, $"corr-{i}");
            }
            await db.SaveChangesAsync();
        }

        // Setup mock event bus to track all published event IDs thread-safely
        var publishedEventIds = new ConcurrentBag<Guid>();
        var eventBus = Substitute.For<IEventBus>();
        eventBus.PublishAsync(Arg.Any<IntegrationEvent>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ev = callInfo.Arg<IntegrationEvent>();
                publishedEventIds.Add(ev.EventId);
                return Task.CompletedTask;
            });

        var outboxOptions = Options.Create(new OutboxOptions
        {
            BatchSize = 10,
            ClaimDuration = TimeSpan.FromSeconds(30),
            MaxRetries = 3
        });

        // Spin up 3 separate publisher instances representing independent replica workers
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var worker1 = new OutboxPublisherBackgroundService<ReportDbContext>(
            scopeFactory, eventBus, outboxOptions, NullLogger<OutboxPublisherBackgroundService<ReportDbContext>>.Instance);
        var worker2 = new OutboxPublisherBackgroundService<ReportDbContext>(
            scopeFactory, eventBus, outboxOptions, NullLogger<OutboxPublisherBackgroundService<ReportDbContext>>.Instance);
        var worker3 = new OutboxPublisherBackgroundService<ReportDbContext>(
            scopeFactory, eventBus, outboxOptions, NullLogger<OutboxPublisherBackgroundService<ReportDbContext>>.Instance);

        // Run process loop concurrently across all 3 workers
        var task1 = worker1.ProcessPendingMessagesAsync(CancellationToken.None);
        var task2 = worker2.ProcessPendingMessagesAsync(CancellationToken.None);
        var task3 = worker3.ProcessPendingMessagesAsync(CancellationToken.None);

        var results = await Task.WhenAll(task1, task2, task3);
        var totalProcessed = results.Sum();

        // Exactly 10 messages should have been claimed and published across the 3 workers
        Assert.Equal(10, totalProcessed);
        Assert.Equal(10, publishedEventIds.Count);
        Assert.Equal(10, publishedEventIds.Distinct().Count()); // Zero duplicates!

        // Verify all 10 messages in database are now Published and have expired claim cleared
        await using (var verifyScope = sp.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ReportDbContext>();
            var messages = await db.OutboxMessages.ToListAsync();

            Assert.Equal(10, messages.Count);
            Assert.All(messages, m =>
            {
                Assert.Equal(OutboxMessageStatus.Published, m.Status);
                Assert.NotNull(m.ProcessedAtUtc);
                Assert.Null(m.ClaimExpiresAtUtc);
                Assert.NotNull(m.ClaimedByInstanceId);
            });
        }
    }

    [Fact]
    public async Task OutboxPublisher_ExpiredClaim_IsReclaimedByActiveWorker()
    {
        var dbName = $"outbox-reclaim-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<ReportDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();

        // Seed 1 message in Processing state with an EXPIRED claim (e.g. 5 minutes ago)
        var messageId = Guid.NewGuid();
        await using (var seedScope = sp.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ReportDbContext>();
            var evt = new ReportApprovedEvent(
                messageId,
                DateTimeOffset.UtcNow,
                ReportId: 42,
                ClubId: 5,
                ClubName: "F-Code",
                Period: "FA26",
                ApprovedByUserId: 1,
                RecipientUserId: 100);

            var msg = OutboxMessage.FromEvent(evt, EventRoutingKeys.ReportApproved);
            msg.Status = OutboxMessageStatus.Processing;
            msg.ClaimedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
            msg.ClaimExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5); // Expired!
            msg.ClaimedByInstanceId = "dead-worker-instance";

            db.OutboxMessages.Add(msg);
            await db.SaveChangesAsync();
        }

        var published = false;
        var eventBus = Substitute.For<IEventBus>();
        eventBus.PublishAsync(Arg.Any<IntegrationEvent>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                published = true;
                return Task.CompletedTask;
            });

        var outboxOptions = Options.Create(new OutboxOptions
        {
            BatchSize = 10,
            ClaimDuration = TimeSpan.FromSeconds(30),
            MaxRetries = 3
        });

        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var activeWorker = new OutboxPublisherBackgroundService<ReportDbContext>(
            scopeFactory, eventBus, outboxOptions, NullLogger<OutboxPublisherBackgroundService<ReportDbContext>>.Instance);

        var processed = await activeWorker.ProcessPendingMessagesAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.True(published);

        await using (var verifyScope = sp.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ReportDbContext>();
            var msg = await db.OutboxMessages.FirstAsync(m => m.Id == messageId);

            Assert.Equal(OutboxMessageStatus.Published, msg.Status);
            Assert.NotNull(msg.ProcessedAtUtc);
            Assert.Null(msg.ClaimExpiresAtUtc);
            Assert.NotEqual("dead-worker-instance", msg.ClaimedByInstanceId);
        }
    }

    [Fact]
    public async Task OutboxPublisher_UnexpiredClaim_IsNotClaimedByOtherWorkers()
    {
        var dbName = $"outbox-unexpired-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<ReportDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();

        // Seed 1 message in Processing state with an UNEXPIRED lease
        await using (var seedScope = sp.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ReportDbContext>();
            var evt = new ReportApprovedEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                ReportId: 50,
                ClubId: 5,
                ClubName: "F-Code",
                Period: "FA26",
                ApprovedByUserId: 1,
                RecipientUserId: 100);

            var msg = OutboxMessage.FromEvent(evt, EventRoutingKeys.ReportApproved);
            msg.Status = OutboxMessageStatus.Processing;
            msg.ClaimedAtUtc = DateTimeOffset.UtcNow;
            msg.ClaimExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5); // Still valid!
            msg.ClaimedByInstanceId = "active-worker-holder";

            db.OutboxMessages.Add(msg);
            await db.SaveChangesAsync();
        }

        var eventBus = Substitute.For<IEventBus>();
        var outboxOptions = Options.Create(new OutboxOptions());
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var anotherWorker = new OutboxPublisherBackgroundService<ReportDbContext>(
            scopeFactory, eventBus, outboxOptions, NullLogger<OutboxPublisherBackgroundService<ReportDbContext>>.Instance);

        var processed = await anotherWorker.ProcessPendingMessagesAsync(CancellationToken.None);

        Assert.Equal(0, processed);
        await eventBus.DidNotReceiveWithAnyArgs().PublishAsync<IntegrationEvent>(default!, default!, default);
    }

    [Fact]
    public async Task OutboxPublisher_PostPublishConcurrencyConflict_DoesNotPoisonRemainingBatch()
    {
        var dbName = $"outbox-post-publish-conflict-{Guid.NewGuid():N}";
        var interceptor = new PostPublishConcurrencyConflictInterceptor();
        var services = new ServiceCollection();
        services.AddDbContext<ReportDbContext>(o =>
            o.UseInMemoryDatabase(dbName).AddInterceptors(interceptor));
        await using var serviceProvider = services.BuildServiceProvider();

        await using (var seedScope = serviceProvider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ReportDbContext>();
            for (var index = 0; index < 2; index++)
            {
                var evt = new ReportApprovedEvent(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow.AddMilliseconds(index),
                    ReportId: 100 + index,
                    ClubId: 5,
                    ClubName: "F-Code",
                    Period: "FA26",
                    ApprovedByUserId: 1,
                    RecipientUserId: 100);
                db.OutboxMessages.Add(OutboxMessage.FromEvent(evt, EventRoutingKeys.ReportApproved));
            }

            await db.SaveChangesAsync();
        }

        var publishedEventIds = new ConcurrentBag<Guid>();
        var eventBus = Substitute.For<IEventBus>();
        eventBus.PublishAsync(Arg.Any<IntegrationEvent>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var integrationEvent = callInfo.Arg<IntegrationEvent>();
                publishedEventIds.Add(integrationEvent.EventId);
                return Task.CompletedTask;
            });

        var publisher = new OutboxPublisherBackgroundService<ReportDbContext>(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            eventBus,
            Options.Create(new OutboxOptions
            {
                BatchSize = 2,
                ClaimDuration = TimeSpan.FromMinutes(5),
                MaxRetries = 3
            }),
            NullLogger<OutboxPublisherBackgroundService<ReportDbContext>>.Instance);

        var processed = await publisher.ProcessPendingMessagesAsync(CancellationToken.None);

        Assert.Equal(2, processed);
        Assert.Equal(2, publishedEventIds.Count);
        await using var verifyScope = serviceProvider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var statuses = await verifyDb.OutboxMessages.Select(message => message.Status).ToListAsync();
        Assert.Equal(1, statuses.Count(status => status == OutboxMessageStatus.Published));
        Assert.Equal(1, statuses.Count(status => status == OutboxMessageStatus.Processing));
    }

    [Fact]
    public void OutboxMessage_Metadata_ContainsClaimAndConcurrencyProperties()
    {
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseSqlServer("Server=localhost;Database=Dummy;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        using var context = new ReportDbContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var outboxEntity = model.FindEntityType(typeof(OutboxMessage));
        Assert.NotNull(outboxEntity);

        var claimedByProp = outboxEntity.FindProperty(nameof(OutboxMessage.ClaimedByInstanceId));
        Assert.NotNull(claimedByProp);
        Assert.Equal(100, claimedByProp.GetMaxLength());

        var tokenProp = outboxEntity.FindProperty(nameof(OutboxMessage.ConcurrencyToken));
        Assert.NotNull(tokenProp);
        Assert.True(tokenProp.IsConcurrencyToken);

        var indexes = outboxEntity.GetIndexes().ToList();
        var claimIndex = indexes.FirstOrDefault(i =>
            i.Properties.Any(p => p.Name == nameof(OutboxMessage.Status))
            && i.Properties.Any(p => p.Name == nameof(OutboxMessage.ClaimExpiresAtUtc))
            && i.Properties.Any(p => p.Name == nameof(OutboxMessage.OccurredAtUtc)));

        Assert.NotNull(claimIndex);
    }

    #endregion

    #region DATA-F03: Report Workflow State & Outbox & Audit Atomicity Tests

    [Fact]
    public async Task SubmitReport_ReportStateOutboxAndAudit_SavedAtomically()
    {
        const int clubId = 5;
        const int authorUserId = 101;
        await using var app = await CreateReportTestAppAsync(clubId, authorUserId, isManager: true);

        var reportId = await SeedDraftReportAsync(app, clubId, authorUserId);

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(authorUserId, AuthRoles.ClubManager));

        var response = await client.PostAsync($"/api/reports/{reportId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify in database: report state, outbox event, and audit log all committed atomically
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();

        var report = await db.Reports.FindAsync(reportId);
        Assert.NotNull(report);
        Assert.Equal(ReportStatuses.UnderReview, report.Status);
        Assert.NotNull(report.SubmittedAtUtc);
        Assert.Equal(2, report.Version);

        var outboxEvent = await db.OutboxMessages.FirstOrDefaultAsync(m => m.EventType == EventRoutingKeys.ReportSubmitted);
        Assert.NotNull(outboxEvent);
        Assert.Contains($"\"reportId\":{reportId}", outboxEvent.Payload);

        var auditLog = await db.AuditLogs.FirstOrDefaultAsync(a => a.ReportId == reportId && a.Action == "Submit");
        Assert.NotNull(auditLog);
        Assert.Equal(authorUserId, auditLog.ActorUserId);
    }

    [Fact]
    public async Task ReviewReport_ReportStateOutboxAndAudit_SavedAtomically()
    {
        const int clubId = 5;
        const int managerUserId = 102;
        await using var app = await CreateReportTestAppAsync(clubId, managerUserId);

        var reportId = await SeedSubmittedReportAsync(app, clubId, createdByUserId: 101);

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(managerUserId, AuthRoles.ClubManager));

        var response = await client.PostAsync($"/api/reports/{reportId}/review", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();

        var report = await db.Reports.FindAsync(reportId);
        Assert.NotNull(report);
        Assert.Equal(ReportStatuses.UnderReview, report.Status);
        Assert.Equal(managerUserId, report.ReviewedByUserId);
        Assert.NotNull(report.ReviewedAtUtc);

        var outboxEvent = await db.OutboxMessages.FirstOrDefaultAsync(m => m.EventType == EventRoutingKeys.ReportSubmitted);
        Assert.NotNull(outboxEvent);

        var auditLog = await db.AuditLogs.FirstOrDefaultAsync(a => a.ReportId == reportId && a.Action == "ManagerReview");
        Assert.NotNull(auditLog);
        Assert.Equal(managerUserId, auditLog.ActorUserId);
    }

    [Fact]
    public async Task ApproveReport_ReportStateOutboxFeedbackAndAudit_SavedAtomically()
    {
        const int clubId = 5;
        const int reviewerUserId = 501;
        await using var app = await CreateReportTestAppAsync(clubId, reviewerUserId);

        var reportId = await SeedUnderReviewReportAsync(app, clubId);

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(reviewerUserId, AuthRoles.StudentAffairsAdmin));

        var response = await client.PostAsJsonAsync($"/api/reports/{reportId}/approve", new ReviewRequest("Báo cáo xuất sắc, duyệt."));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();

        var report = await db.Reports.Include(r => r.Feedback).FirstOrDefaultAsync(r => r.Id == reportId);
        Assert.NotNull(report);
        Assert.Equal(ReportStatuses.Approved, report.Status);
        Assert.Equal(reviewerUserId, report.ReviewedByUserId);
        Assert.Single(report.Feedback);
        Assert.Equal("Báo cáo xuất sắc, duyệt.", report.Feedback.First().Message);

        var outboxEvent = await db.OutboxMessages.FirstOrDefaultAsync(m => m.EventType == EventRoutingKeys.ReportApproved);
        Assert.NotNull(outboxEvent);

        var auditLog = await db.AuditLogs.FirstOrDefaultAsync(a => a.ReportId == reportId && a.Action == "Approve");
        Assert.NotNull(auditLog);
        Assert.Equal(reviewerUserId, auditLog.ActorUserId);
    }

    [Fact]
    public async Task RejectReport_ReportStateOutboxFeedbackAndAudit_SavedAtomically()
    {
        const int clubId = 5;
        const int reviewerUserId = 502;
        await using var app = await CreateReportTestAppAsync(clubId, reviewerUserId);

        var reportId = await SeedUnderReviewReportAsync(app, clubId);

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(reviewerUserId, AuthRoles.StudentAffairsAdmin));

        var response = await client.PostAsJsonAsync($"/api/reports/{reportId}/reject", new ReviewRequest("Cần bổ sung minh chứng chi tiết."));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();

        var report = await db.Reports.Include(r => r.Feedback).FirstOrDefaultAsync(r => r.Id == reportId);
        Assert.NotNull(report);
        Assert.Equal(ReportStatuses.Rejected, report.Status);
        Assert.Equal(reviewerUserId, report.ReviewedByUserId);
        Assert.Single(report.Feedback);
        Assert.Equal("Cần bổ sung minh chứng chi tiết.", report.Feedback.First().Message);

        var outboxEvent = await db.OutboxMessages.FirstOrDefaultAsync(m => m.EventType == EventRoutingKeys.ReportRejected);
        Assert.NotNull(outboxEvent);

        var auditLog = await db.AuditLogs.FirstOrDefaultAsync(a => a.ReportId == reportId && a.Action == "Reject");
        Assert.NotNull(auditLog);
        Assert.Equal(reviewerUserId, auditLog.ActorUserId);
    }

    #endregion

    #region Helpers & Test Server Setup

    private static async Task<WebApplication> CreateReportTestAppAsync(int clubId, int userId, bool isManager = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        var dbName = $"report-atomicity-test-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ReportDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(Substitute.For<IEventBus>());
        builder.Services.AddSingleton(CreateClubAccessClient(clubId, userId, isManager));

        // Stub external clients for finance & activity publishing
        builder.Services.AddSingleton(new FinanceWorkflowClient(
            CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { Id = 1, Status = "Approved", ApprovedAmount = 5_000_000m })
            }, "http://localhost:5107/"),
            NullLogger<FinanceWorkflowClient>.Instance));

        builder.Services.AddSingleton(new ActivityPublishingClient(
            CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { Id = 99, Title = "Published Activity" })
            }, "http://localhost:5106/"),
            NullLogger<ActivityPublishingClient>.Instance));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        var group = app.MapGroup("/api/reports").RequireAuthorization();
        group.MapReportWorkflowEndpoints();

        await app.StartAsync();
        return app;
    }

    private static async Task<int> SeedDraftReportAsync(WebApplication app, int clubId, int authorUserId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        await db.Database.EnsureCreatedAsync();

        var report = new Report
        {
            ClubId = clubId,
            ClubName = $"Club {clubId}",
            Period = "FA26",
            Tag = "Activity report",
            ReportType = "Activity report",
            Status = ReportStatuses.Draft,
            CreatedByUserId = authorUserId,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            ExecutiveSummary = "Executive Summary",
            Achievements = "Achievements",
            Challenges = "Challenges",
            Recommendations = "Recommendations",
            NextPeriodPlan = "Next Period Plan",
            Version = 1,
            Details =
            [
                new ReportDetail
                {
                    ActivityName = "Kickoff Meeting",
                    Description = "Club gathering",
                    ActivityDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    Location = "Hall A",
                    ParticipantCount = 25,
                    BudgetSpent = 450_000m
                }
            ]
        };

        db.Reports.Add(report);
        await db.SaveChangesAsync();
        return report.Id;
    }

    private static async Task<int> SeedSubmittedReportAsync(WebApplication app, int clubId, int createdByUserId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        await db.Database.EnsureCreatedAsync();

        var report = new Report
        {
            ClubId = clubId,
            ClubName = $"Club {clubId}",
            Period = "FA26",
            Tag = "Activity report",
            ReportType = "Activity report",
            Status = ReportStatuses.Submitted,
            CreatedByUserId = createdByUserId,
            SubmittedAtUtc = DateTimeOffset.UtcNow,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            ExecutiveSummary = "Summary",
            Achievements = "Achievements",
            Challenges = "Challenges",
            Recommendations = "Recommendations",
            NextPeriodPlan = "Next",
            Version = 1,
            Details =
            [
                new ReportDetail
                {
                    ActivityName = "General Meeting",
                    Description = "Monthly meeting",
                    ActivityDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    ParticipantCount = 30
                }
            ]
        };

        db.Reports.Add(report);
        await db.SaveChangesAsync();
        return report.Id;
    }

    private static async Task<int> SeedUnderReviewReportAsync(WebApplication app, int clubId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        await db.Database.EnsureCreatedAsync();

        var report = new Report
        {
            ClubId = clubId,
            ClubName = $"Club {clubId}",
            Period = "FA26",
            Tag = "Activity report",
            ReportType = "Activity report",
            Status = ReportStatuses.UnderReview,
            CreatedByUserId = 101,
            SubmittedAtUtc = DateTimeOffset.UtcNow,
            ReviewedByUserId = 102,
            ReviewedAtUtc = DateTimeOffset.UtcNow,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            ExecutiveSummary = "Summary",
            Achievements = "Achievements",
            Challenges = "Challenges",
            Recommendations = "Recommendations",
            NextPeriodPlan = "Next",
            Version = 2,
            Details =
            [
                new ReportDetail
                {
                    ActivityName = "Tech Workshop",
                    Description = "Workshop description",
                    ActivityDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    ParticipantCount = 50
                }
            ]
        };

        db.Reports.Add(report);
        await db.SaveChangesAsync();
        return report.Id;
    }

    private static ClubAccessClient CreateClubAccessClient(int clubId, int userId, bool isManager = true)
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var snapshot = new ClubAccessSnapshot(
            ClubId: clubId,
            ClubName: $"Club {clubId}",
            IsManager: isManager,
            IsTreasurer: true,
            IsApprovedMember: true,
            ManagerUserIds: isManager ? [userId, 102] : [102],
            MemberUserIds: [userId, 101, 102],
            TreasurerUserIds: [userId, 103]);

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

    private sealed class PostPublishConcurrencyConflictInterceptor : SaveChangesInterceptor
    {
        private bool _firstConflictThrown;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var modifiedPublishedCount = eventData.Context?.ChangeTracker
                .Entries<OutboxMessage>()
                .Count(entry => entry.State == EntityState.Modified
                    && entry.Entity.Status == OutboxMessageStatus.Published) ?? 0;

            if (!_firstConflictThrown && modifiedPublishedCount == 1)
            {
                _firstConflictThrown = true;
                throw new DbUpdateConcurrencyException("Forced post-publish concurrency conflict.");
            }

            if (_firstConflictThrown && modifiedPublishedCount > 1)
            {
                throw new DbUpdateConcurrencyException("A stale failed entry poisoned the next outbox save.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    #endregion
}
