using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace ClubReportHub.Shared.Experience;

public sealed record StudentClubScopeRequest(int UserId, string CampusCode);
public sealed record StudentClubMetadata(int ClubId, string ClubCode, string ClubName, string Category, string CampusCode,
    bool IsApprovedMember, bool CanViewPrivate, DateTimeOffset? JoinedAt, Guid Version, Guid? MembershipVersion)
{
    public bool IsRecruiting { get; init; }
}
public sealed record StudentClubScope(IReadOnlyList<StudentClubMetadata> Clubs, DateTimeOffset AsOf, string Version);
public sealed record StudentCandidateQuery(int UserId, int Page = 1, int PageSize = 100,
    string? SemesterId = null, IReadOnlyList<int>? ActivityIds = null, bool IncludeRegistered = false,
    IReadOnlyList<int>? SessionIds = null);
public sealed record StudentSourceReference(int ActivityId, int SessionId);
public sealed record StudentSourceStatusQuery(IReadOnlyList<StudentSourceReference> Sources);
public sealed record StudentSourceStatus(IReadOnlyList<StudentSourceReference> ActiveSources, DateTimeOffset AsOf);
public sealed record StudentActivityItem(int Id, int ClubId, string ClubCode, string ClubName,
    string Title, string Description, string? CoverImageUrl, DateTimeOffset StartTime, DateTimeOffset EndTime,
    string Location, string Status, string NextAction, bool CanRegister, bool IsEligible,
    string? SemesterId, int? NextSessionId, Guid Version, Guid? SessionVersion, Guid? PolicyVersion,
    IReadOnlyList<PillarWeight> PillarWeights)
{
    public RecommendationActivitySource? RecommendationSource { get; init; }
}
public sealed record StudentActivityPage(IReadOnlyList<StudentActivityItem> Items, int Page, int PageSize,
    int TotalItems, int TotalPages, DateTimeOffset AsOf, string ScopeVersion);
public sealed record StudentCounterQuery(int UserId, string Scope, string? SemesterId, DateTimeOffset? AsOf = null);
public sealed record StudentCounters(int UserId, int? VerifiedAttendanceCount, int? ApprovedClubCount,
    DateTimeOffset AsOf, string Version, bool HistoricalSnapshotSupported = false);

public sealed class StudentExperienceSourceClient(IHttpClientFactory clients, IConfiguration configuration)
{
    public async Task<StudentActivityPage> CandidatesAsync(StudentCandidateQuery query, CancellationToken ct)
        => await PostAsync<StudentActivityPage>("Activity", "api/internal/experience/student/candidates", query, ct);

    public async Task<StudentCounters> CountersAsync(string service, StudentCounterQuery query, CancellationToken ct)
        => await PostAsync<StudentCounters>(service, "api/internal/experience/student/counters", query, ct);

    public async Task<StudentSourceStatus> SourceStatusAsync(StudentSourceStatusQuery query, CancellationToken ct)
        => await PostAsync<StudentSourceStatus>("Activity", "api/internal/experience/student/source-status", query, ct);

    private async Task<T> PostAsync<T>(string service, string path, object data, CancellationToken ct)
    {
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) throw new HttpRequestException("Student source authority is unconfigured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(data) };
        request.Headers.Add("X-Internal-Workflow-Token", secret);
        using var response = await clients.CreateClient("Experience" + service).SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new HttpRequestException("Invalid student source response.");
    }
}

