using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace ClubReportHub.Shared.Rosters;

public sealed class SchoolAccountClient(HttpClient client, IConfiguration configuration)
{
    public async Task<IReadOnlyList<SchoolAccount>> EmailsAsync(IEnumerable<string> emails, CancellationToken ct)
    {
        var result = new List<SchoolAccount>();
        foreach (var batch in emails.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(500))
            result.AddRange(await Resolve(new SchoolAccountQuery(Emails: batch), ct));
        return result;
    }
    public async Task<IReadOnlyList<SchoolAccount>> UsersAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var result = new List<SchoolAccount>();
        foreach (var batch in ids.Distinct().Chunk(500)) result.AddRange(await Resolve(new SchoolAccountQuery(UserIds: batch), ct));
        return result;
    }
    private async Task<IReadOnlyList<SchoolAccount>> Resolve(SchoolAccountQuery query, CancellationToken ct)
    {
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) throw new HttpRequestException("School account authority is unconfigured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/internal/semester-roster/accounts/resolve") { Content = JsonContent.Create(query) };
        request.Headers.Add("X-Internal-Workflow-Token", secret);
        using var response = await client.SendAsync(request, ct); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SchoolAccount[]>(ct) ?? throw new HttpRequestException("Invalid account response.");
    }
}
