using System.Net;
using ClubReportHub.Shared.Health;
using ClubReportHub.Shared.Security;
using DemoDataSeeder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Backend.StabilizationTests;

/// <summary>
/// Phase 18 regression tests: Docker, Configuration &amp; Operational Hardening
/// Covers: REL-F07 (health endpoints), SEC-F13 (security headers), SEC-F16 (Content-Disposition sanitizer),
///         POT-F14 (DemoDataSeeder safety guards), OPS-F06 (.dockerignore exclusions)
/// </summary>
public sealed class OperationalHardeningAndHealthCheckTests
{
    // =========================================================================
    // 1. REL-F07: Standardised Health Check Endpoints
    // =========================================================================

    [Fact]
    public async Task HealthLive_Returns200_WhenProcessIsAlive()
    {
        // /health/live predicate is (_ => false), so no checks run → Healthy
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHealthChecks();
        builder.Services.AddRouting();

        await using var app = builder.Build();
        app.UseRouting();
        app.MapStandardHealthChecks();
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_Returns200_WithNoChecks()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHealthChecks();
        builder.Services.AddRouting();

        await using var app = builder.Build();
        app.UseRouting();
        app.MapStandardHealthChecks();
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_ExecutesChecksWith_ReadyTag()
    {
        // /health/ready runs only "ready"-tagged checks
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHealthChecks()
            .AddCheck("always-healthy-ready", () => HealthCheckResult.Healthy(), tags: ["ready"])
            .AddCheck("untagged-check", () => HealthCheckResult.Unhealthy("Should not run"));
        builder.Services.AddRouting();

        await using var app = builder.Build();
        app.UseRouting();
        app.MapStandardHealthChecks();
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_Returns503_WhenReadyTaggedCheckFails()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHealthChecks()
            .AddCheck("failing-db", () => HealthCheckResult.Unhealthy("DB down"), tags: ["ready"]);
        builder.Services.AddRouting();

        await using var app = builder.Build();
        app.UseRouting();
        app.MapStandardHealthChecks();
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task HealthLive_Returns200_EvenWhenReadyCheckFails()
    {
        // Liveness probe is decoupled from readiness dependencies
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHealthChecks()
            .AddCheck("failing-db", () => HealthCheckResult.Unhealthy("DB down"), tags: ["ready"]);
        builder.Services.AddRouting();

        await using var app = builder.Build();
        app.UseRouting();
        app.MapStandardHealthChecks();
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AddRedisHealthCheck_RegistrationSucceeds_WhenMultiplexerNotRegistered()
    {
        // Without IConnectionMultiplexer in DI, Redis check returns Healthy("not registered")
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHealthChecks()
            .AddRedisHealthCheck("redis-test", ["ready"]);
        builder.Services.AddRouting();

        await using var app = builder.Build();
        app.UseRouting();
        app.MapStandardHealthChecks();
        await app.StartAsync();

        // /health/ready runs "ready"-tagged checks; Redis check returns Healthy since multiplexer is null
        var response = await app.GetTestClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =========================================================================
    // 2. SEC-F13: Security Headers Middleware
    // =========================================================================

    [Fact]
    public async Task SecurityHeadersMiddleware_SetsXContentTypeOptions()
    {
        await using var app = await BuildAppWithSecurityHeaders();
        var response = await app.GetTestClient().GetAsync("/ping");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());
    }

    [Fact]
    public async Task SecurityHeadersMiddleware_SetsXFrameOptions()
    {
        await using var app = await BuildAppWithSecurityHeaders();
        var response = await app.GetTestClient().GetAsync("/ping");
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").First());
    }

    [Fact]
    public async Task SecurityHeadersMiddleware_SetsReferrerPolicy()
    {
        await using var app = await BuildAppWithSecurityHeaders();
        var response = await app.GetTestClient().GetAsync("/ping");
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").First());
    }

    [Fact]
    public async Task SecurityHeadersMiddleware_SetsXXssProtectionToZero()
    {
        await using var app = await BuildAppWithSecurityHeaders();
        var response = await app.GetTestClient().GetAsync("/ping");
        Assert.Equal("0", response.Headers.GetValues("X-XSS-Protection").First());
    }

    [Fact]
    public async Task SecurityHeadersMiddleware_SetsPermissionsPolicy()
    {
        await using var app = await BuildAppWithSecurityHeaders();
        var response = await app.GetTestClient().GetAsync("/ping");
        var permPolicy = response.Headers.GetValues("Permissions-Policy").First();
        Assert.Contains("camera=()", permPolicy);
        Assert.Contains("microphone=()", permPolicy);
        Assert.Contains("geolocation=()", permPolicy);
    }

    [Fact]
    public async Task SecurityHeadersMiddleware_DoesNotOverwriteExistingXFrameOptions()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        await using var app = builder.Build();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.MapGet("/", (HttpContext ctx) =>
        {
            ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            return Results.Ok("ok");
        });
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/");
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").First());
    }

    private static async Task<WebApplication> BuildAppWithSecurityHeaders()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.MapGet("/ping", () => Results.Ok("pong"));
        await app.StartAsync();
        return app;
    }

    // =========================================================================
    // 3. SEC-F16: ContentDispositionSanitizer
    // =========================================================================

    [Theory]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("  report.pdf  ", "report.pdf")]
    [InlineData("../../../etc/passwd", "passwd")]
    [InlineData("C:\\Windows\\System32\\cmd.exe", "cmd.exe")]
    [InlineData("my\"file'.xlsx", "my_file_.xlsx")]
    [InlineData("", "download")]
    [InlineData("   ", "download")]
    [InlineData(null, "download")]
    public void ContentDispositionSanitizer_SanitizesFileNames(string? rawName, string expected)
    {
        var result = ContentDispositionSanitizer.SanitizeFileName(rawName, "download");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ContentDispositionSanitizer_StripsCrlfChars()
    {
        var input = "filename\r\nX-Header: injected.pdf";
        var result = ContentDispositionSanitizer.SanitizeFileName(input);
        Assert.DoesNotContain("\r", result);
        Assert.DoesNotContain("\n", result);
    }

    [Fact]
    public void ContentDispositionSanitizer_ReplacesInvalidChars_WithUnderscore()
    {
        // Colon, asterisk, question mark, pipe are definitely in InvalidFileNameChars
        var input = "bad:file*name?.pdf";
        var result = ContentDispositionSanitizer.SanitizeFileName(input);
        Assert.DoesNotContain(":", result);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("?", result);
        // Replaced characters become underscores
        Assert.Contains("_", result);
        Assert.Contains(".pdf", result);
    }

    [Fact]
    public void ContentDispositionSanitizer_TrimsDotFromEnd()
    {
        var result = ContentDispositionSanitizer.SanitizeFileName("filename.");
        Assert.False(result.EndsWith('.'));
    }

    [Fact]
    public void ContentDispositionSanitizer_UseFallback_ForAllControlInput()
    {
        // Input that becomes empty after stripping should use fallback
        var input = "\r\n\r\n";
        var result = ContentDispositionSanitizer.SanitizeFileName(input, "fallback");
        Assert.Equal("fallback", result);
    }

    // =========================================================================
    // 4. POT-F14: DemoDataSeeder Safety Guards
    // =========================================================================

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void DemoSeederOptions_ThrowsInProductionOrStaging_WhenResetAllIsTrue(string env)
    {
        var originalEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            SetEnv("ASPNETCORE_ENVIRONMENT", env);
            SetEnv("DemoData__Enabled", "true");
            SetEnv("DemoData__ResetAll", "true");
            SetEnv("DemoData__ConfirmDestructiveReset", "true");
            SetRequiredConnectionStrings();

            var ex = Assert.Throws<InvalidOperationException>(() => DemoSeederOptions.FromEnvironment());
            Assert.Contains("forbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            RestoreEnv("ASPNETCORE_ENVIRONMENT", originalEnv);
            CleanupDemoEnv();
        }
    }

    [Fact]
    public void DemoSeederOptions_ThrowsInDevelopment_WhenResetAllTrueButNoConfirmation()
    {
        var originalEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            SetEnv("ASPNETCORE_ENVIRONMENT", "Development");
            SetEnv("DemoData__Enabled", "true");
            SetEnv("DemoData__ResetAll", "true");
            SetEnv("DemoData__ConfirmDestructiveReset", "false");
            SetRequiredConnectionStrings();

            var ex = Assert.Throws<InvalidOperationException>(() => DemoSeederOptions.FromEnvironment());
            Assert.Contains("ConfirmDestructiveReset", ex.Message);
        }
        finally
        {
            RestoreEnv("ASPNETCORE_ENVIRONMENT", originalEnv);
            CleanupDemoEnv();
        }
    }

    [Fact]
    public void DemoSeederOptions_SucceedsInDevelopment_WithResetAllAndConfirmation()
    {
        var originalEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            SetEnv("ASPNETCORE_ENVIRONMENT", "Development");
            SetEnv("DemoData__Enabled", "true");
            SetEnv("DemoData__ResetAll", "true");
            SetEnv("DemoData__ConfirmDestructiveReset", "true");
            SetRequiredConnectionStrings();

            var options = DemoSeederOptions.FromEnvironment();
            Assert.True(options.ResetAll);
        }
        finally
        {
            RestoreEnv("ASPNETCORE_ENVIRONMENT", originalEnv);
            CleanupDemoEnv();
        }
    }

    [Fact]
    public void DemoSeederOptions_SucceedsInProduction_WhenResetAllIsFalse()
    {
        var originalEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            SetEnv("ASPNETCORE_ENVIRONMENT", "Production");
            SetEnv("DemoData__Enabled", "false");
            SetEnv("DemoData__ResetAll", "false");
            SetEnv("DemoData__ConfirmDestructiveReset", "false");
            SetRequiredConnectionStrings();

            var options = DemoSeederOptions.FromEnvironment();
            Assert.False(options.ResetAll);
        }
        finally
        {
            RestoreEnv("ASPNETCORE_ENVIRONMENT", originalEnv);
            CleanupDemoEnv();
        }
    }

    // =========================================================================
    // 5. OPS-F06: .dockerignore excludes test and dev configuration files
    // =========================================================================

    [Fact]
    public void DockerIgnore_ExcludesTestDirectory()
    {
        var dockerIgnorePath = Path.Combine(GetRepositoryRoot(), ".dockerignore");
        Assert.True(File.Exists(dockerIgnorePath), ".dockerignore should exist");

        var content = File.ReadAllText(dockerIgnorePath);
        Assert.True(
            content.Contains("tests/") || content.Contains("tests\\"),
            ".dockerignore should exclude the tests/ directory");
    }

    [Fact]
    public void DockerIgnore_ExcludesTestAppsettings()
    {
        var dockerIgnorePath = Path.Combine(GetRepositoryRoot(), ".dockerignore");
        var content = File.ReadAllText(dockerIgnorePath);
        Assert.True(
            content.Contains("appsettings.Test.json") || content.Contains("*.Test.json"),
            ".dockerignore should exclude appsettings.Test.json files");
    }

    [Fact]
    public void DockerIgnore_ExcludesDevelopmentAppsettings()
    {
        var dockerIgnorePath = Path.Combine(GetRepositoryRoot(), ".dockerignore");
        var content = File.ReadAllText(dockerIgnorePath);
        Assert.True(
            content.Contains("appsettings.Development.json") || content.Contains("*.Development.json"),
            ".dockerignore should exclude appsettings.Development.json files");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static void SetEnv(string key, string value) =>
        Environment.SetEnvironmentVariable(key, value);

    private static void RestoreEnv(string key, string? value) =>
        Environment.SetEnvironmentVariable(key, value);

    private static void SetRequiredConnectionStrings()
    {
        const string cs = "Server=localhost;Database=Test;User Id=sa;Password=Test@123;TrustServerCertificate=True;";
        SetEnv("ConnectionStrings__Auth", cs);
        SetEnv("ConnectionStrings__Club", cs);
        SetEnv("ConnectionStrings__Activity", cs);
        SetEnv("ConnectionStrings__Report", cs);
        SetEnv("ConnectionStrings__Finance", cs);
        SetEnv("ConnectionStrings__Notification", cs);
        SetEnv("ConnectionStrings__Export", cs);
        SetEnv("DemoData__AdminEmail", "admin@fpt.edu.vn");
        SetEnv("DemoData__ClubManagerEmail", "manager@fpt.edu.vn");
        SetEnv("DemoData__StudentEmail", "student@fpt.edu.vn");
    }

    private static void CleanupDemoEnv()
    {
        foreach (var key in new[]
        {
            "ConnectionStrings__Auth", "ConnectionStrings__Club", "ConnectionStrings__Activity",
            "ConnectionStrings__Report", "ConnectionStrings__Finance", "ConnectionStrings__Notification",
            "ConnectionStrings__Export", "DemoData__Enabled", "DemoData__ResetAll",
            "DemoData__ConfirmDestructiveReset", "DemoData__AdminEmail", "DemoData__ClubManagerEmail",
            "DemoData__StudentEmail"
        })
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    private static string GetRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ClubReportHub.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
