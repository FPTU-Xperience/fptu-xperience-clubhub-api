using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Endpoints;
using ClubReportHub.Shared.Errors;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Messaging;
using ClubReportHub.Shared.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ReportService.Data;
using ReportService.Jobs;
using ReportService.Models;
using Xunit;

namespace Backend.StabilizationTests;

public sealed class ObservabilityAndLoggingStandardsTests
{
    // =========================================================================
    // 1. OBS-F01: Correlation ID Propagation & Middleware
    // =========================================================================

    [Fact]
    public async Task CorrelationIdMiddleware_WhenHeaderMissing_GeneratesGuidAndAttachesToRequestAndItems()
    {
        var context = new DefaultHttpContext();
        var logger = Substitute.For<ILogger<CorrelationIdMiddleware>>();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context);

        var requestHeader = context.Request.Headers[CorrelationIdConstants.HeaderName].ToString();
        var itemValue = context.Items[CorrelationIdConstants.ItemKey]?.ToString();

        Assert.False(string.IsNullOrWhiteSpace(requestHeader));
        Assert.Equal(requestHeader, itemValue);
        Assert.True(Guid.TryParse(requestHeader, out _));
    }

    [Fact]
    public async Task CorrelationIdMiddleware_WhenHeaderPresent_PreservesIncomingHeaderAndSetsItems()
    {
        var incomingCorrelationId = "client-trace-" + Guid.NewGuid().ToString("N");
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdConstants.HeaderName] = incomingCorrelationId;

        var logger = Substitute.For<ILogger<CorrelationIdMiddleware>>();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context);

        var requestHeader = context.Request.Headers[CorrelationIdConstants.HeaderName].ToString();
        var itemValue = context.Items[CorrelationIdConstants.ItemKey]?.ToString();

        Assert.Equal(incomingCorrelationId, requestHeader);
        Assert.Equal(incomingCorrelationId, itemValue);
    }

    [Fact]
    public async Task CorrelationIdDelegatingHandler_AppendsCorrelationIdHeaderToOutgoingRequests()
    {
        var correlationId = "outbox-trace-" + Guid.NewGuid().ToString("N");
        var httpContext = new DefaultHttpContext();
        httpContext.Items[CorrelationIdConstants.ItemKey] = correlationId;

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(httpContext);

        string? capturedHeader = null;
        var innerHandler = new TestHttpMessageHandler((req, _) =>
        {
            if (req.Headers.TryGetValues(CorrelationIdConstants.HeaderName, out var values))
            {
                capturedHeader = string.Join(",", values);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var handler = new CorrelationIdDelegatingHandler(httpContextAccessor)
        {
            InnerHandler = innerHandler
        };

        using var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/test");
        await client.SendAsync(request);

        Assert.Equal(correlationId, capturedHeader);
    }

    // =========================================================================
    // 2. OBS-F02: Structured Audit Logging & Secret Masking
    // =========================================================================

    [Fact]
    public void StructuredAuditLogging_NeverExposesPlaintextTokensOrSecretsInLogTemplates()
    {
        // Audit auth endpoints to ensure sensitive tokens and passwords are not placed into log strings
        var authEndpointsPath = Path.Combine(FindSolutionRoot(), "src/Services/AuthService/Endpoints/AuthEndpoints.cs");
        var authEndpointsCode = File.ReadAllText(authEndpointsPath);

        // Ensure raw token values are never interpolated into log parameters
        Assert.DoesNotContain("{request.RefreshToken}", authEndpointsCode);
        Assert.DoesNotContain("{refreshToken}", authEndpointsCode);
        Assert.DoesNotContain("{accessToken}", authEndpointsCode);
        Assert.DoesNotContain("{Token}", authEndpointsCode);

        // Ensure structured log fields (Event, UserId, ClientIp, CorrelationId) are present
        Assert.Contains("{ClientIp}", authEndpointsCode);
        Assert.Contains("{CorrelationId}", authEndpointsCode);
        Assert.Contains("{UserId}", authEndpointsCode);
    }

    // =========================================================================
    // 3. OBS-F03: EntityFrameworkCore SQL Logging Leak Prevention
    // =========================================================================

    [Theory]
    [InlineData("src/Services/ReportService/appsettings.json")]
    [InlineData("src/Services/ClubService/appsettings.json")]
    [InlineData("src/Services/ActivityService/appsettings.json")]
    [InlineData("src/Services/FinanceService/appsettings.json")]
    [InlineData("src/Services/AuthService/appsettings.json")]
    [InlineData("src/Services/NotificationService/appsettings.json")]
    [InlineData("src/Services/ExportService/appsettings.json")]
    [InlineData("src/Services/AdminService/appsettings.json")]
    public void AppsettingsConfiguration_SetsEntityFrameworkCoreLogLevelToWarning_AcrossAllDataServices(string relativePath)
    {
        var rootDir = FindSolutionRoot();
        var fullPath = Path.Combine(rootDir, relativePath);

        Assert.True(File.Exists(fullPath), $"Config file not found: {fullPath}");

        var config = new ConfigurationBuilder()
            .AddJsonFile(fullPath, optional: false)
            .Build();

        var efLogLevel = config["Logging:LogLevel:Microsoft.EntityFrameworkCore"];
        Assert.Equal("Warning", efLogLevel);
    }

    // =========================================================================
    // 4. DATA-F07: Report Deadline Missing Clubs Calculation
    // =========================================================================

    [Fact]
    public async Task ReportDeadlineJobs_PublishDailyReminderAsync_AccuratelyIdentifiesMissingClubsAndExcludesSubmittedClubs()
    {
        var dbName = "report_deadline_jobs_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        const string targetPeriod = "2026-03";

        using (var db = new ReportDbContext(options))
        {
            // Set up active reporting deadline within 3 days
            db.ReportingDeadlines.Add(new ReportingDeadline
            {
                Period = targetPeriod,
                DueDate = today.AddDays(1),
                IsActive = true
            });

            // Club 1: Has Approved report for target period -> NOT missing
            db.Reports.Add(new Report
            {
                ClubId = 1,
                ClubName = "Club One",
                Period = targetPeriod,
                Status = ReportStatuses.Approved,
                DueDate = today.AddDays(1)
            });

            // Club 2: Has Submitted report for target period -> NOT missing
            db.Reports.Add(new Report
            {
                ClubId = 2,
                ClubName = "Club Two",
                Period = targetPeriod,
                Status = ReportStatuses.Submitted,
                DueDate = today.AddDays(1)
            });

            // Club 3: Has Draft report for target period -> IS missing!
            db.Reports.Add(new Report
            {
                ClubId = 3,
                ClubName = "Club Three",
                Period = targetPeriod,
                Status = ReportStatuses.Draft,
                DueDate = today.AddDays(1)
            });

            // Club 4: Has report from previous month only ("2026-02") -> IS missing!
            db.Reports.Add(new Report
            {
                ClubId = 4,
                ClubName = "Club Four",
                Period = "2026-02",
                Status = ReportStatuses.Approved,
                DueDate = today.AddMonths(-1)
            });

            await db.SaveChangesAsync();
        }

        ReportDeadlineReminderEvent? publishedEvent = null;
        var eventBus = Substitute.For<IEventBus>();
        eventBus.PublishAsync(
            Arg.Do<ReportDeadlineReminderEvent>(e => publishedEvent = e),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var logger = Substitute.For<ILogger<ReportDeadlineJobs>>();

        using (var db = new ReportDbContext(options))
        {
            var job = new ReportDeadlineJobs(db, eventBus, logger);
            await job.PublishDailyReminderAsync();
        }

        Assert.NotNull(publishedEvent);
        Assert.Equal(targetPeriod, publishedEvent.Period);

        var missingClubIds = publishedEvent.MissingClubIds.ToList();
        // Clubs 1 and 2 submitted non-draft reports, so they must NOT be in missing list
        Assert.DoesNotContain(1, missingClubIds);
        Assert.DoesNotContain(2, missingClubIds);

        // Clubs 3 and 4 have NOT submitted a non-draft report for 2026-03, so they MUST be in missing list
        Assert.Contains(3, missingClubIds);
        Assert.Contains(4, missingClubIds);
    }

    // =========================================================================
    // 5. REL-F06: Global Error Endpoint Handles All HTTP Methods (RFC 7807)
    // =========================================================================

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task MapGlobalErrorEndpoint_HandlesAnyHttpMethod_WithRfc7807ProblemDetails(string httpMethod)
    {
        var builder = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddClubReportTracing();
            })
            .Configure(app =>
            {
                app.UseCorrelationId();
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGlobalErrorEndpoint();
                });
            });

        using var server = new TestServer(builder);
        var client = server.CreateClient();

        var request = new HttpRequestMessage(new HttpMethod(httpMethod), "/error");
        request.Headers.Add(CorrelationIdConstants.HeaderName, "test-error-trace-456");

        if (httpMethod is "POST" or "PUT" or "PATCH")
        {
            request.Content = JsonContent.Create(new { dummy = "payload" });
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", root.GetProperty("title").GetString());
        Assert.True(root.TryGetProperty("correlationId", out var corrElem));
        Assert.Equal("test-error-trace-456", corrElem.GetString());
        Assert.True(root.TryGetProperty("traceId", out _));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static string FindSolutionRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "ClubReportHub.sln")))
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        throw new InvalidOperationException("Could not find ClubReportHub.sln root");
    }

    private sealed class TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsyncFunc)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => sendAsyncFunc(request, cancellationToken);
    }
}
