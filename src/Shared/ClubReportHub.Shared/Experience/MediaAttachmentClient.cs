using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ClubReportHub.Shared.Experience;

public sealed record MediaAttachmentResult(string? PublicUrl, int StatusCode, string? Code);
public sealed class MediaAttachmentClient(HttpClient client, IConfiguration configuration)
{
    public async Task<MediaAttachmentResult> VerifyAsync(string? url, string? current, string kind, int owner,
        HttpContext http, CancellationToken ct)
    {
        if (url is null || url == current) return new(url, 200, null);
        if (url.Length > 1000 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return new(null, 400, "INVALID_IMAGE_URL");
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) return new(null, 503, "MEDIA_SERVICE_UNAVAILABLE");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/internal/media/verify")
        { Content = JsonContent.Create(new { kind, ownerId = owner, publicUrl = url }) };
        request.Headers.Add("X-Internal-Workflow-Token", secret);
        if (AuthenticationHeaderValue.TryParse(http.Request.Headers.Authorization, out var auth)) request.Headers.Authorization = auth;
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new(null, (int)response.StatusCode is 400 or 403 or 409 ? (int)response.StatusCode : 503, "MEDIA_ATTACHMENT_REJECTED");
            var result = await response.Content.ReadFromJsonAsync<MediaUrl>(ct); return new(result?.PublicUrl, result is null ? 503 : 200, null);
        }
        catch (HttpRequestException) { return new(null, 503, "MEDIA_SERVICE_UNAVAILABLE"); }
    }
    private sealed record MediaUrl(string PublicUrl);
}
