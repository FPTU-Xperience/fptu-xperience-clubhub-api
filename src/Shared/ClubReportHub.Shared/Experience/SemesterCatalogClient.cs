using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ClubReportHub.Shared.Experience;

public sealed record SemesterReference(Guid Id, string SemesterCode, DateTimeOffset? StartDate, DateTimeOffset? EndDate, bool IsActive);

public sealed class SemesterCatalogClient(HttpClient client, IConfiguration configuration)
{
    public async Task<SemesterReference?> ResolveAsync(string identifier, HttpContext http, CancellationToken cancellationToken)
    {
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) throw new HttpRequestException("Semester service is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "api/internal/experience/semesters/" + Uri.EscapeDataString(identifier));
        request.Headers.Add("X-Internal-Workflow-Token", secret);
        if (AuthenticationHeaderValue.TryParse(http.Request.Headers.Authorization, out var authorization))
            request.Headers.Authorization = authorization;
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SemesterReference>(cancellationToken);
    }
}
