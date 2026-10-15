using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace ClubReportHub.Shared.Experience;

public sealed record ActivitySourceContext(ExperienceSourceSnapshot Source, bool IsRegistered, bool AlreadyVerified);
public sealed record ActivityExperienceScope(int ActivityId, int ClubId);

public sealed class ActivitySourceClient(HttpClient client, IConfiguration configuration)
{
    public async Task<ActivityExperienceScope?> GetScopeAsync(int activityId, CancellationToken ct)
    {
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) throw new HttpRequestException("Activity source service is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/internal/experience/activities/{activityId}/scope");
        request.Headers.Add("X-Internal-Workflow-Token", secret);
        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ActivityExperienceScope>(ct);
    }
    public async Task<ActivitySourceContext?> GetAsync(int activityId, int sessionId, int userId, CancellationToken cancellationToken)
    {
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) throw new HttpRequestException("Activity source service is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"api/internal/experience/activities/{activityId}/sessions/{sessionId}?userId={userId}");
        request.Headers.Add("X-Internal-Workflow-Token", secret);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ActivitySourceContext>(cancellationToken);
    }
}
