using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ActivityService.Infrastructure;

public sealed record ReportVerificationDetail(int Id, string ActivityName, DateOnly ActivityDate);

public sealed record ReportVerificationSnapshot(
    int Id,
    int ClubId,
    string Status,
    IReadOnlyCollection<ReportVerificationDetail> Details);

public sealed class ReportVerificationClient(
    HttpClient httpClient,
    ILogger<ReportVerificationClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ReportVerificationSnapshot?> GetReportAsync(
        int reportId,
        string? bearerToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/reports/{reportId}");
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Getting report {ReportId} failed with {StatusCode}.", reportId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ReportVerificationSnapshot>(JsonOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Failed to get report {ReportId} for verification.", reportId);
            return null;
        }
    }
}
