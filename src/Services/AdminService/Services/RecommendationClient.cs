using System.Net.Http.Json;
using System.Text.Json;

namespace AdminService.Services;

public sealed class RecommendationClient(HttpClient client, IConfiguration configuration)
{
    public bool Enabled => configuration.GetValue<bool>("Recommendation:Enabled");

    public async Task<JsonElement> ReadAsync(int userId, string semester, string kind, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"internal/recommendations/users/{userId}?semesterId={Uri.EscapeDataString(semester)}&type={kind}", null, ct);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    public async Task FeedAsync(object snapshot, DateTimeOffset readStartedAt, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, "internal/feeds", new { snapshot, readStartedAt }, ct);
    }

    public async Task InvalidateAsync(int[]? users, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, "internal/invalidations", new { userIds = users }, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var token = configuration["Recommendation:Token"];
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32) throw new HttpRequestException("Recommendation authority is unconfigured.");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Recommendation-Token", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) { response.Dispose(); throw new HttpRequestException("Recommendation request failed."); }
        return response;
    }
}
