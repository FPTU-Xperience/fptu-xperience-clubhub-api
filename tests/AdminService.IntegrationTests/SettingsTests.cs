using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdminService.Contracts;
using AdminService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdminService.IntegrationTests;

public sealed class SettingsTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task DiscoveryCaptureSettingsApplyToStudentInteractionsWithRoleAndVersionChecks()
    {
        const string settingsPath = "/api/v1/admin/settings";
        const string capturePath = "/api/v2/recommendations/interaction-settings";
        const string interactionsPath = "/api/v2/recommendations/interactions";
        using var admin = CreateClient("admin");
        using var affairs = CreateClient("student-affairs");
        using var student = CreateClient("student-1");
        using var other = CreateClient("student-2");
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(capturePath)).StatusCode);
        var initial = (await student.GetFromJsonAsync<DiscoveryCaptureSettings>(capturePath))!;
        Assert.Equal(DiscoveryCaptureSettings.Default, initial);
        object Update(bool click, bool view, int seconds, int percent, Guid expectedVersion) =>
            new { discoveryCapture = new { clickEnabled = click, viewEnabled = view, viewSeconds = seconds, visiblePercent = percent, expectedVersion } };
        object Event(Guid eventId, Guid version, string action) =>
            new { eventId, entityKind = "activity", entityId = 44, action, captureVersion = version };

        Assert.Equal(HttpStatusCode.Forbidden, (await affairs.PutAsJsonAsync(settingsPath, Update(true, false, 8, 75, initial.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(settingsPath, Update(true, false, 0, 75, initial.Version))).StatusCode);
        var saved = await admin.PutAsJsonAsync(settingsPath, Update(true, false, 8, 75, initial.Version));
        saved.EnsureSuccessStatusCode();
        var current = (await student.GetFromJsonAsync<DiscoveryCaptureSettings>(capturePath))!;
        Assert.NotEqual(initial.Version, current.Version);
        Assert.Equal(8, current.ViewSeconds);
        Assert.Equal(75, current.VisiblePercent);
        Assert.False(current.ViewEnabled);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(settingsPath, Update(true, true, 5, 50, initial.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync(interactionsPath, Event(Guid.NewGuid(), initial.Version, "click"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await student.PostAsJsonAsync(interactionsPath, Event(Guid.NewGuid(), initial.Version, "view"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync(interactionsPath, Event(Guid.NewGuid(), current.Version, "click"))).StatusCode);
        var eventId = Guid.NewGuid();
        var click = Event(eventId, current.Version, "click");
        Assert.Equal(HttpStatusCode.NoContent, (await student.PostAsJsonAsync(interactionsPath, click)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await student.PostAsJsonAsync(interactionsPath, click)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var captured = Assert.Single(await db.DiscoveryInteractions.ToListAsync());
        Assert.Equal(301, captured.UserId);
        Assert.Equal(eventId, captured.EventId);
        Assert.Equal("[\"experience:Research\"]", captured.TagIdsJson);
    }

    [Fact]
    public async Task GetSettings_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSettings_StudentForbidden_Returns403()
    {
        using var client = CreateClient("student-1");

        var response = await client.GetAsync("/api/v1/admin/settings");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanGetSettings_AndSeedsDefault()
    {
        using var client = CreateClient("admin");

        var response = await client.GetAsync("/api/v1/admin/settings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var settings = await response.Content.ReadFromJsonAsync<PlatformSettingsResponse>(JsonOptions);
        Assert.NotNull(settings);
        Assert.True(settings.GoogleDomain == "fpt.edu.vn" || settings.GoogleDomain == "fe.edu.vn");
        Assert.True(settings.RateLimit > 0);
        Assert.True(settings.Retention > 0);
    }

    [Fact]
    public async Task Admin_CanUpdateSettings_ValidatesAndPersists()
    {
        using var client = CreateClient("admin");

        var updateReq = new UpdatePlatformSettingsRequest(
            "fe.edu.vn",
            "https://fap.fpt.edu.vn/api/schedule",
            false,
            true,
            "daily",
            200,
            180);

        var updateResp = await client.PutAsJsonAsync("/api/v1/admin/settings", updateReq);
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

        var updated = await updateResp.Content.ReadFromJsonAsync<PlatformSettingsResponse>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("fe.edu.vn", updated.GoogleDomain);
        Assert.Equal("https://fap.fpt.edu.vn/api/schedule", updated.TimetableUrl);
        Assert.False(updated.InAppNotifications);
        Assert.True(updated.EmailNotifications);
        Assert.Equal("daily", updated.Digest);
        Assert.Equal(200, updated.RateLimit);
        Assert.Equal(180, updated.Retention);

        // Verify retrieval matches
        var getResp = await client.GetAsync("/api/v1/admin/settings");
        var fetched = await getResp.Content.ReadFromJsonAsync<PlatformSettingsResponse>(JsonOptions);
        Assert.NotNull(fetched);
        Assert.Equal("fe.edu.vn", fetched.GoogleDomain);
    }

    [Fact]
    public async Task Admin_CanGetHealthStats()
    {
        using var client = CreateClient("admin");

        var response = await client.GetAsync("/api/v1/admin/health/stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stats = await response.Content.ReadFromJsonAsync<SystemHealthStatsResponse>(JsonOptions);
        Assert.NotNull(stats);
        Assert.True(stats.Accounts > 0);
        Assert.True(stats.Clubs > 0);
        Assert.NotEmpty(stats.Services);
        Assert.Contains(stats.Services, s => s.Name == "API Gateway");
    }

    [Fact]
    public async Task Admin_CanFilterAuditEvents_AndVerifyEnhancedFields()
    {
        using var client = CreateClient("admin");

        // 1. Trigger an operation that generates an audit event
        var updateReq = new UpdatePlatformSettingsRequest(
            "fpt.edu.vn",
            null,
            true,
            true,
            "weekly",
            100,
            365);
        await client.PutAsJsonAsync("/api/v1/admin/settings", updateReq);

        // 2. Fetch audit events
        var response = await client.GetAsync("/api/v1/admin/audit-events?area=admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var paged = await response.Content.ReadFromJsonAsync<PagedResult<AuditEventResponse>>(JsonOptions);
        Assert.NotNull(paged);
        Assert.NotEmpty(paged.Items);

        var first = paged.Items.First();
        Assert.NotNull(first.Actor);
        Assert.NotNull(first.Detail);
        Assert.Equal("admin", first.Area);
        Assert.True(first.Timestamp > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task Anonymous_CanAccessApiHealth()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpClient CreateClient(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", role);
        return client;
    }
}
