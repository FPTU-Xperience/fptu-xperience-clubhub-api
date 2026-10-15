using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace ClubReportHub.Shared.Experience;

public sealed record InternalUserProfile(int Id, string FullName, string Email, string CampusCode, bool IsActive, bool IsLocked, JsonElement? Profile)
{
    public string DisplayName => StringAt("profile", "displayName") ?? FullName;
    public string? PhoneNumber => StringAt("personal", "phoneNumber");
    public string? StringAt(string section, string field) => Profile is { ValueKind: JsonValueKind.Object } root
        && root.TryGetProperty(section, out var nested) && nested.ValueKind == JsonValueKind.Object
        && nested.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
public sealed class CurrentUserProfileClient(HttpClient client, IConfiguration configuration)
{
    public async Task<IReadOnlyDictionary<int, InternalUserProfile>> GetAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var users = ids.Distinct().ToArray(); if (users.Length == 0) return new Dictionary<int, InternalUserProfile>();
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) throw new HttpRequestException("Profile authority is unconfigured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/internal/experience/user-profiles") { Content = JsonContent.Create(users) };
        request.Headers.Add("X-Internal-Workflow-Token", secret);
        using var response = await client.SendAsync(request, ct); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<InternalUserProfile>>(ct) ?? []).ToDictionary(x => x.Id);
    }
}
