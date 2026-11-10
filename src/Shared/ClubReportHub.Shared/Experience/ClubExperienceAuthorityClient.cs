using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace ClubReportHub.Shared.Experience;

public sealed record ClubExperienceAuthority(int ClubId, string Name, string CampusCode, bool IsActive,
    bool IsApprovedMember, DateTimeOffset? JoinedAt);
public sealed record ClubMembersAtRequest(DateTimeOffset AtUtc, int[] UserIds);
public sealed class ClubExperienceAuthorityClient(HttpClient client, IConfiguration configuration)
{
    public async Task<int[]> MembersAtAsync(int clubId, DateTimeOffset at, int[] userIds, CancellationToken ct)
    {
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) throw new HttpRequestException("Club authority is unconfigured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/internal/experience/clubs/{clubId}/members-at")
        { Content = JsonContent.Create(new ClubMembersAtRequest(at, userIds)) };
        request.Headers.Add("X-Internal-Workflow-Token", secret);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<int[]>(ct) ?? throw new HttpRequestException("Invalid membership history.");
    }

    public async Task<int[]> ManagerIdsAsync(int clubId, CancellationToken ct)
    {
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) throw new HttpRequestException("Club authority is unconfigured.");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/internal/experience/clubs/{clubId}/manager-ids");
        request.Headers.Add("X-Internal-Workflow-Token", secret);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<int[]>(ct) ?? throw new HttpRequestException("Invalid manager list.");
    }

    public async Task<StudentClubScope> StudentScopeAsync(int userId, string campusCode, CancellationToken ct)
    {
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) throw new HttpRequestException("Club authority is unconfigured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/internal/experience/student/club-scope")
        { Content = JsonContent.Create(new StudentClubScopeRequest(userId, campusCode)) };
        request.Headers.Add("X-Internal-Workflow-Token", secret);
        using var response = await client.SendAsync(request, ct); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<StudentClubScope>(ct) ?? throw new HttpRequestException("Invalid club scope.");
    }
    public async Task<ClubExperienceAuthority?> GetAsync(int clubId, int? userId, CancellationToken ct)
    {
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) throw new HttpRequestException("Club authority is unconfigured.");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/internal/experience/clubs/{clubId}?userId={userId}");
        request.Headers.Add("X-Internal-Workflow-Token", secret); using var response = await client.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<ClubExperienceAuthority>(ct);
    }
}
