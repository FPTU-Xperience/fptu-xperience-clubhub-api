using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Theory]
    [InlineData("GET", "/api/clubs/1/eligible-members", "club-service")]
    [InlineData("POST", "/api/v1/semesters/FALL2026/roster-imports/preview", "admin-service")]
    [InlineData("POST", "/api/v1/semesters/FALL2026/roster-imports/11111111-1111-1111-1111-111111111111/commit", "admin-service")]
    [InlineData("POST", "/api/activity-applications", "activity-service")]
    [InlineData("GET", "/api/activity-applications/1", "activity-service")]
    [InlineData("POST", "/api/activity-applications/1/review", "activity-service")]
    [InlineData("GET", "/api/clubs/1/activity-recruitments", "activity-service")]
    [InlineData("GET", "/api/activities/1/staffing", "activity-service")]
    [InlineData("PATCH", "/api/activities/1/publication", "activity-service")]
    [InlineData("POST", "/api/activities/1/recruitment/positions/11111111-1111-1111-1111-111111111111/join", "activity-service")]
    [InlineData("POST", "/api/media/upload-intents", "auth-service")]
    [InlineData("GET", "/api/contributions/11111111-1111-1111-1111-111111111111", "admin-service")]
    [InlineData("POST", "/api/activities/1/contributions", "admin-service")]
    [InlineData("POST", "/api/activities/1/sessions/1/manual-attendance-requests", "admin-service")]
    [InlineData("POST", "/api/activities/1/sessions/1/manual-attendance-requests/11111111-1111-1111-1111-111111111111/review", "admin-service")]
    [InlineData("GET", "/api/users/me/contributions", "admin-service")]
    [InlineData("GET", "/api/users/me/experience-ledger", "admin-service")]
    [InlineData("GET", "/api/verifications/me/queue", "admin-service")]
    [InlineData("POST", "/api/verification-assignments", "admin-service")]
    [InlineData("GET", "/api/clubs/1/experience-statistics", "admin-service")]
    [InlineData("POST", "/api/clubs/1/experience-reports", "export-service")]
    [InlineData("GET", "/api/clubs/1/experience-reports/11111111-1111-1111-1111-111111111111/download", "export-service")]
    [InlineData("GET", "/api/club-experience-reports/11111111-1111-1111-1111-111111111111", "export-service")]
    [InlineData("POST", "/api/activities/1/sessions", "activity-service")]
    [InlineData("PATCH", "/api/clubs/1/public-profile", "club-service")]
    [InlineData("PUT", "/api/clubs/1/semester-plans/FALL2026", "club-service")]
    public async Task ClubManager_GatewayRoutesNewApisToOwningServiceBeforeLegacyCatchAll(string method, string path, string expectedCluster)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "src", "Gateway", "ApiGateway", "yarp.json"))) folder = folder.Parent;
        Assert.NotNull(folder);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.AddJsonFile(Path.Combine(folder!.FullName, "src", "Gateway", "ApiGateway", "yarp.json"));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Issuer"] = "ClubReportHub", ["Jwt:Audience"] = "ClubReportHub.Client", ["Jwt:SigningKey"] = SigningKey });
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddRateLimiter(options =>
        {
            foreach (var policy in builder.Configuration.GetSection("ReverseProxy:Routes").GetChildren()
                .Select(route => route["RateLimiterPolicy"]).Where(policy => policy is not null).Distinct())
                options.AddPolicy(policy!, _ => RateLimitPartition.GetNoLimiter("test-route-selection"));
        });
        builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
        // Exercise ASP.NET/YARP route selection, replacing only the remote transport.
        app.MapReverseProxy(proxy => proxy.Use(async (HttpContext http, RequestDelegate _) => await http.Response.WriteAsJsonAsync(new
        {
            cluster = http.GetReverseProxyFeature().Route.Config.ClusterId
        })));
        await app.StartAsync(); using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        var response = await manager.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedCluster, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cluster").GetString());
    }
}
