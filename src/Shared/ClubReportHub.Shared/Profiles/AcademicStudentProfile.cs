using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ClubReportHub.Shared.Profiles;

public sealed record AcademicStudentProfile(string Availability, string? StudentCode = null, string? Source = null,
    Guid? SemesterKey = null, string? SemesterId = null, Guid? RosterRevisionId = null, Guid? SourceVersion = null)
{
    public string? Major => null;
    public int? Year => null;
    public string MajorAvailability => "NO_AUTHORITATIVE_SOURCE";
    public string YearAvailability => "NO_AUTHORITATIVE_SOURCE";
}
public sealed record AcademicProfileEnvelope(int UserId, AcademicStudentProfile Academic);
public sealed class CanonicalAcademicClient(HttpClient client, IConfiguration configuration)
{
    public async Task<AcademicStudentProfile> Read(HttpContext http, CancellationToken ct)
    {
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) return new("UNCONFIGURED");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/internal/academic-profiles/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", http.GetBearerToken());
            request.Headers.Add("X-Internal-Workflow-Token", secret);
            using var response = await client.SendAsync(request, ct); response.EnsureSuccessStatusCode();
            var value = await response.Content.ReadFromJsonAsync<AcademicProfileEnvelope>(ct);
            if (value?.UserId != http.User.GetUserId() || value.Academic is null
                || value.Academic.Availability == "AVAILABLE" && (string.IsNullOrWhiteSpace(value.Academic.StudentCode)
                    || value.Academic.RosterRevisionId is null || value.Academic.SourceVersion is null)) return new("AUTHORITY_UNAVAILABLE");
            return value.Academic;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException) { return new("AUTHORITY_UNAVAILABLE"); }
        catch (TaskCanceledException) when (!http.RequestAborted.IsCancellationRequested) { return new("AUTHORITY_UNAVAILABLE"); }
    }
}
