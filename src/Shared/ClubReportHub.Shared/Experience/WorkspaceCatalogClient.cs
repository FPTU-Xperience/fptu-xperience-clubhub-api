using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ClubReportHub.Shared.Experience;

public sealed record WorkspaceSemesterMetadata(Guid Id, string Code, string Label, bool IsCurrent,
    bool Editable, DateTimeOffset? StartUtc, DateTimeOffset? EndUtc, string? ReportPeriod,
    int? Year, string? Term, string EditAvailability, string MappingAvailability);
public sealed record WorkspacePolicyMetadata(ExperiencePolicyReference Policy, DateTimeOffset? EffectiveAt);
public sealed record WorkspaceCatalog(IReadOnlyList<WorkspaceSemesterMetadata> Semesters,
    IReadOnlyList<WorkspacePolicyMetadata> Policies, string Environment, string CurrentAvailability,
    DateTimeOffset AsOf, string Timezone = "UTC", bool StartInclusive = true, bool EndInclusive = true);

public sealed class WorkspaceCatalogClient(HttpClient client, IConfiguration configuration)
{
    public async Task<WorkspaceCatalog> ReadAsync(HttpContext http, CancellationToken ct)
    {
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) throw new HttpRequestException("Workspace catalog is unconfigured.");
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/internal/experience/workspace-catalog");
        request.Headers.Add("X-Internal-Workflow-Token", secret);
        if (AuthenticationHeaderValue.TryParse(http.Request.Headers.Authorization, out var auth)) request.Headers.Authorization = auth;
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WorkspaceCatalog>(ct)
            ?? throw new HttpRequestException("Invalid workspace catalog.");
    }
}
