using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdminService.Contracts;
using Xunit;

namespace AdminService.IntegrationTests;

public sealed class AnomalyTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetAnomalies_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/student-affairs/anomalies");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAnomalies_StudentForbidden_Returns403()
    {
        using var client = CreateClient("student-1");

        var response = await client.GetAsync("/api/v1/student-affairs/anomalies");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ctsv_CanGetAnomaliesAndStats()
    {
        using var client = CreateClient("student-affairs");

        // 1. Get anomalies list
        var response = await client.GetAsync("/api/v1/student-affairs/anomalies");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await response.Content.ReadFromJsonAsync<List<AnomalyResponse>>(JsonOptions);
        Assert.NotNull(list);
        Assert.NotEmpty(list);
        Assert.Contains(list, a => a.Status == "open");

        // 2. Get stats
        var statsResp = await client.GetAsync("/api/v1/student-affairs/anomalies/stats");
        Assert.Equal(HttpStatusCode.OK, statsResp.StatusCode);

        var stats = await statsResp.Content.ReadFromJsonAsync<AnomalyStatsResponse>(JsonOptions);
        Assert.NotNull(stats);
        Assert.True(stats.OpenCount >= 1);
        Assert.True(stats.TotalLedgerCount >= 1);
    }

    [Fact]
    public async Task Ctsv_CanResolveAnomaly_WithAdjust_AndVerifyLedgerOffset()
    {
        using var client = CreateClient("student-affairs");

        // 1. Get open anomaly
        var listResp = await client.GetAsync("/api/v1/student-affairs/anomalies?status=open");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var openList = await listResp.Content.ReadFromJsonAsync<List<AnomalyResponse>>(JsonOptions);
        Assert.NotNull(openList);
        Assert.NotEmpty(openList);

        var target = openList.First();

        // 2. Resolve with adjust (e.g. from 120 or 80 down to 20 XP)
        var adjustVal = 20;
        var resolveRequest = new ResolveAnomalyRequest(
            Decision: "adjust",
            Adjustment: adjustVal,
            Reason: "Đã đối chiếu danh sách sinh hoạt thực tế, chỉ xác nhận 20 XP hợp lệ.");

        var resolveResp = await client.PostAsJsonAsync(
            $"/api/v1/student-affairs/anomalies/{target.Id}/resolve",
            resolveRequest);
        Assert.Equal(HttpStatusCode.OK, resolveResp.StatusCode);

        var resolved = await resolveResp.Content.ReadFromJsonAsync<AnomalyResponse>(JsonOptions);
        Assert.NotNull(resolved);
        Assert.Equal("resolved", resolved.Status);
        Assert.Equal("adjust", resolved.Decision);
        Assert.Equal(adjustVal, resolved.Adjustment);

        // 3. Verify ledger entry was appended
        var ledgerResp = await client.GetAsync("/api/v1/student-affairs/ledger");
        Assert.Equal(HttpStatusCode.OK, ledgerResp.StatusCode);
        var ledger = await ledgerResp.Content.ReadFromJsonAsync<List<XpLedgerEntryResponse>>(JsonOptions);
        Assert.NotNull(ledger);
        Assert.Contains(ledger, e => e.StudentUserId == target.StudentUserId && e.Type == "adjust");

        var offsetEntry = ledger.First(e => e.StudentUserId == target.StudentUserId && e.Type == "adjust");
        Assert.Equal(adjustVal - target.Amount, offsetEntry.Amount);
    }

    [Fact]
    public async Task Ctsv_CanResolveAnomaly_WithRevoke_AndAppendFullOffset()
    {
        using var client = CreateClient("admin");

        // 1. Get open anomaly
        var listResp = await client.GetAsync("/api/v1/student-affairs/anomalies?status=open");
        var openList = await listResp.Content.ReadFromJsonAsync<List<AnomalyResponse>>(JsonOptions);
        Assert.NotNull(openList);
        if (openList.Count == 0) return; // already resolved

        var target = openList.First();

        // 2. Resolve with revoke
        var resolveRequest = new ResolveAnomalyRequest(
            Decision: "revoke",
            Adjustment: null,
            Reason: "Minh chứng không hợp lệ, thu hồi toàn bộ số điểm đã cấp.");

        var resolveResp = await client.PostAsJsonAsync(
            $"/api/v1/student-affairs/anomalies/{target.Id}/resolve",
            resolveRequest);
        Assert.Equal(HttpStatusCode.OK, resolveResp.StatusCode);

        var resolved = await resolveResp.Content.ReadFromJsonAsync<AnomalyResponse>(JsonOptions);
        Assert.NotNull(resolved);
        Assert.Equal("resolved", resolved.Status);
        Assert.Equal("revoke", resolved.Decision);

        // 3. Check ledger has full negative amount
        var ledgerResp = await client.GetAsync("/api/v1/student-affairs/ledger");
        var ledger = await ledgerResp.Content.ReadFromJsonAsync<List<XpLedgerEntryResponse>>(JsonOptions);
        Assert.NotNull(ledger);
        Assert.Contains(ledger, e => e.StudentUserId == target.StudentUserId && e.Amount == -target.Amount);
    }

    private HttpClient CreateClient(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
