using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace ClubReportHub.Shared.Rosters;

public sealed class SemesterRosterClient(HttpClient client, IConfiguration configuration)
{
    public async Task<RosterLookupResult> LookupAsync(RosterLookupQuery query, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, "api/internal/semester-roster/lookup");
        request.Content = JsonContent.Create(query);
        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new RosterException(404, "SEMESTER_NOT_FOUND", "The selected semester does not exist.");
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            throw new RosterException(400, "INVALID_ROSTER_QUERY", "The roster query is invalid.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RosterLookupResult>(ct) ?? throw new HttpRequestException("Invalid roster response.");
    }
    public async Task<IReadOnlyList<RosterSummary>> SummariesAsync(string campusCode, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, "api/internal/semester-roster/summaries?campusCode=" + Uri.EscapeDataString(campusCode));
        using var response = await client.SendAsync(request, ct); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RosterSummary[]>(ct) ?? throw new HttpRequestException("Invalid roster summary.");
    }
    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) throw new HttpRequestException("Semester roster is unconfigured.");
        var request = new HttpRequestMessage(method, path); request.Headers.Add("X-Internal-Workflow-Token", secret); return request;
    }
}
