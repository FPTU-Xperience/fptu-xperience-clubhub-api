using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdminService.Contracts;
using Xunit;

namespace AdminService.IntegrationTests;

public sealed class SettingsTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
