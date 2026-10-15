using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ClubReportHub.Shared.Experience;

public sealed record PillarWeight(string Pillar, decimal Weight);
public sealed record ContributionAttributes(string Tier, string Role, string Scale, string Result, IReadOnlyList<PillarWeight> PillarWeights);
public sealed record ExperiencePolicyReference(Guid PolicyVersion, string FormulaVersion, string SemesterId,
    DateTimeOffset? StartDate, DateTimeOffset? EndDate, string ApprovalStatus, string Environment, int Precision,
    string CancellationRule);
public sealed record ExperienceSourceSnapshot(int ActivityId, int ClubId, int SessionId, string SemesterId,
    string Title, DateTimeOffset StartTimeUtc, DateTimeOffset EndTimeUtc, Guid ActivityVersion, Guid SessionVersion,
    string Status, bool ManualConfirmationEnabled, ContributionAttributes Attributes, ExperiencePolicyReference Policy);

public static class VerifiedExperienceRules
{
    public static string? Validate(ContributionAttributes? attributes)
    {
        if (attributes is null || attributes.PillarWeights is null || attributes.PillarWeights.Count is < 1 or > 3)
            return "Provide attributes and one to three pillar weights.";
        if (!EffortTiers.All.Contains(attributes.Tier) || !ContributionRoles.All.Contains(attributes.Role)
            || !ActivityScales.All.Contains(attributes.Scale) || !BonusResults.All.Contains(attributes.Result))
            return "Use canonical tier, role, scale and result codes.";
        try { _ = ExperienceScoringCalculator.CalculateRawPoints(attributes.Tier, attributes.Role, attributes.Scale, attributes.Result); }
        catch (ArgumentException exception) { return exception.Message; }
        if (attributes.PillarWeights.Any(x => x is null || !ExperienceCategories.All.Contains(x.Pillar)
                || x.Weight <= 0 || x.Weight > 1 || decimal.Round(x.Weight, 4) != x.Weight)
            || attributes.PillarWeights.Select(x => x.Pillar).Distinct(StringComparer.Ordinal).Count() != attributes.PillarWeights.Count
            || attributes.PillarWeights.Sum(x => x.Weight) != 1m)
            return "Use distinct canonical pillars, positive weights with up to four decimals, and an exact sum of one.";
        return null;
    }

    public static IReadOnlyDictionary<string, decimal> Allocate(decimal points, IReadOnlyList<PillarWeight> weights, int precision = 4)
    {
        var result = weights.ToDictionary(x => x.Pillar, x => decimal.Round(points * x.Weight, precision, MidpointRounding.AwayFromZero));
        var remainderPillar = weights.OrderByDescending(x => x.Weight).ThenBy(x => x.Pillar, StringComparer.Ordinal).First().Pillar;
        result[remainderPillar] += points - result.Values.Sum();
        return result;
    }

    public static bool InternalRequest(HttpContext http, IConfiguration configuration)
    {
        var configured = configuration["Security:InternalWorkflowToken"];
        var provided = http.Request.Headers["X-Internal-Workflow-Token"].ToString();
        return !string.IsNullOrWhiteSpace(configured) && provided.Length == configured.Length
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(configured), Encoding.UTF8.GetBytes(provided));
    }
}

public sealed class ExperienceAuthorityClient(HttpClient client, IConfiguration configuration)
{
    public async Task<ExperiencePolicyReference?> ResolvePolicyAsync(string semesterId, Guid policyVersion,
        HttpContext http, CancellationToken cancellationToken) =>
        await GetAsync<ExperiencePolicyReference>($"api/internal/experience/policies/{policyVersion}?semesterId={Uri.EscapeDataString(semesterId)}", http, cancellationToken);

    public async Task<bool> CanVerifyAsync(int clubId, string capability, HttpContext http, CancellationToken cancellationToken)
    {
        var response = await GetAsync<VerificationCapabilityResponse>(
            $"api/internal/experience/capabilities?clubId={clubId}&capability={Uri.EscapeDataString(capability)}", http, cancellationToken);
        return response?.Allowed == true;
    }

    private async Task<T?> GetAsync<T>(string path, HttpContext http, CancellationToken cancellationToken)
    {
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) throw new HttpRequestException("Experience authority is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Internal-Workflow-Token", secret);
        if (AuthenticationHeaderValue.TryParse(http.Request.Headers.Authorization, out var authorization)) request.Headers.Authorization = authorization;
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }
}

public sealed record VerificationCapabilityResponse(bool Allowed);
