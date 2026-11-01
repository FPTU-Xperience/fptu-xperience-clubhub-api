using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdminService.Services;

public sealed class RecommendationClient(HttpClient client, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new UtcDateTimeOffsetConverter() }
    };

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
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: RequestJson);
            // The recommendation service reads Content-Length rather than
            // chunked bodies. Buffer the same JSON bytes before sending.
            await request.Content.LoadIntoBufferAsync();
        }
        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) { response.Dispose(); throw new HttpRequestException("Recommendation request failed."); }
        return response;
    }

    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetDateTimeOffset();

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToUniversalTime());
    }
}
