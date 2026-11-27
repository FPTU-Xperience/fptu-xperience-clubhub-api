using System.Net;
using System.Net.Http.Json;
using ClubReportHub.Shared.Experience;

namespace AdminService.Services;

public static class RawFundingServiceCollectionExtensions
{
    public static IServiceCollection AddRawFunding(this IServiceCollection services)
    { services.AddScoped<RawFundingActivityClient>(); return services; }
}

internal sealed class RawFundingActivityClient(IHttpClientFactory clients, IConfiguration configuration)
{
    internal async Task<RawFundingSource?> Read(string sourceKey, CancellationToken ct)
    {
        var token = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(token)) throw new HttpRequestException("Activity funding authority is unconfigured.");
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/internal/experience/funding-sources/" + Uri.EscapeDataString(sourceKey));
        request.Headers.Add("X-Internal-Workflow-Token", token);
        using var response = await clients.CreateClient("ExperienceActivity").SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RawFundingSource>(ct) ?? throw new HttpRequestException("Invalid activity funding proof.");
    }
}
