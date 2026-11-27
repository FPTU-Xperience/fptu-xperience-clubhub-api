using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using static ActivityService.Endpoints.ActivityActionHelpers;

namespace ActivityService.Services;

internal sealed class RawFundingClient(HttpClient client, IConfiguration configuration)
{
    internal async Task<RawFundingReceipt> Reserve(string sourceKey, string version, HttpContext http, CancellationToken ct)
    {
        var secret = configuration["Security:InternalWorkflowToken"];
        Require(!string.IsNullOrWhiteSpace(secret), 503, "RAW_AUTHORITY_UNAVAILABLE", "Raw funding authority is unconfigured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/internal/experience/raw-funding/reserve")
        { Content = JsonContent.Create(new ReserveRawFundingRequest(sourceKey, version)) };
        request.Headers.Add("X-Internal-Workflow-Token", secret);
        request.Headers.Add("Idempotency-Key", "raw-reserve:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceKey + ":" + version))));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", http.GetBearerToken());
        using var response = await client.SendAsync(request, ct);
        Require(response.IsSuccessStatusCode, (int)response.StatusCode, "RAW_RESERVATION_DENIED", "Raw funding could not be reserved. Check the current grant, budget, source revision and approved policy.");
        var receipt = await response.Content.ReadFromJsonAsync<RawFundingReceipt>(ct);
        Require(receipt is { Status: "RESERVED" } && receipt.Id != Guid.Empty && receipt.SourceKey == sourceKey && receipt.SourceVersion == version,
            502, "INVALID_RAW_RECEIPT", "The raw funding authority did not confirm this source revision.");
        return receipt!;
    }
}
