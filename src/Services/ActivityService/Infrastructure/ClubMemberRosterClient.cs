using System.Net.Http.Headers;
using System.Net.Http.Json;
using ActivityService.Contracts;

namespace ActivityService.Infrastructure;

public sealed record ClubMemberRosterItem(int Id, int UserId, string FullName, string Email, string PhoneNumber, string Role, string Status, DateTimeOffset JoinedAtUtc);
public sealed record ClubMemberRosterPage(IReadOnlyCollection<ClubMemberRosterItem> Items, int Page, int PageSize, int TotalItems, int TotalPages);
public sealed record MyClubMembership(int Id, int ClubId, int UserId, string FullName, string Status, DateTimeOffset RequestedAtUtc, DateTimeOffset? ReviewedAtUtc);
public sealed record ClubAssignmentCandidate(int MemberId, int UserId, string DisplayName, DateTimeOffset JoinedAtUtc);
public sealed record ResolveRosterRequest(
    IReadOnlyCollection<int>? MemberIds = null,
    DateTimeOffset? JoinedOnOrBefore = null,
    IReadOnlyCollection<int>? UserIds = null);

public sealed class ClubMemberRosterClient(HttpClient httpClient)
{
    private sealed record CandidatePage(Guid Version, int Page, int TotalItems, int TotalPages, IReadOnlyList<ClubAssignmentCandidate> Items);
    public async Task<IReadOnlyList<ClubAssignmentCandidate>> GetAssignmentCandidatesAsync(int clubId, DateTimeOffset cutoff, string? bearerToken, CancellationToken ct)
    {
        var result = new List<ClubAssignmentCandidate>(); Guid? version = null; int? total = null;
        for (var page = 1; page <= 100; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/clubs/{clubId}/roles?page={page}&pageSize=100");
            SetBearer(request, bearerToken); using var response = await httpClient.SendAsync(request, ct); await EnsureSuccessAsync(response, ct);
            var data = await response.Content.ReadFromJsonAsync<CandidatePage>(ct);
            if (data is null || data.Items is null || data.Version == Guid.Empty || data.Page != page || data.TotalItems < 0 || data.TotalPages < 0
                || version.HasValue && version != data.Version || total.HasValue && total != data.TotalItems)
                throw new HttpRequestException("The current minimal member directory changed or is unavailable.");
            version = data.Version; total = data.TotalItems; result.AddRange(data.Items);
            if (page >= data.TotalPages)
            {
                if (result.Count != total || result.Any(x => x.MemberId < 1 || x.UserId < 1 || x.JoinedAtUtc == default)
                    || result.Select(x => x.MemberId).Distinct().Count() != result.Count || result.Select(x => x.UserId).Distinct().Count() != result.Count)
                    throw new HttpRequestException("The minimal member directory is incomplete.");
                return result.Where(x => x.JoinedAtUtc <= cutoff).ToArray();
            }
        }
        throw new HttpRequestException("The minimal member directory requires a narrower server query.");
    }
    public async Task<MyClubMembership?> GetMyApprovedMembershipAsync(
        int clubId,
        int userId,
        string? bearerToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/clubs/me/memberships");
        SetBearer(request, bearerToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var memberships = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<MyClubMembership>>(cancellationToken: cancellationToken) ?? [];
        return memberships.FirstOrDefault(x => x.ClubId == clubId && x.UserId == userId
            && x.Id > 0 && string.Equals(x.Status, "Approved", StringComparison.Ordinal));
    }

    public async Task<ClubMemberRosterPage> GetAsync(
        int clubId,
        DateTimeOffset joinedOnOrBefore,
        string? search,
        int page,
        int pageSize,
        string? bearerToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"api/clubs/{clubId}/member-roster?joinedOnOrBefore={Uri.EscapeDataString(joinedOnOrBefore.ToString("O"))}&search={Uri.EscapeDataString(search ?? string.Empty)}&page={page}&pageSize={pageSize}");
        SetBearer(request, bearerToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ClubMemberRosterPage>(cancellationToken: cancellationToken)
            ?? new ClubMemberRosterPage([], page, pageSize, 0, 0);
    }

    public async Task<IReadOnlyCollection<ClubMemberRosterItem>> ResolveAsync(
        int clubId,
        IReadOnlyCollection<int> memberIds,
        DateTimeOffset joinedOnOrBefore,
        string? bearerToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/clubs/{clubId}/member-roster/resolve")
        {
            Content = JsonContent.Create(new ResolveRosterRequest(memberIds, joinedOnOrBefore))
        };
        SetBearer(request, bearerToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<IReadOnlyCollection<ClubMemberRosterItem>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task<IReadOnlyCollection<ClubMemberRosterItem>> ResolveByUserIdsAsync(
        int clubId,
        IReadOnlyCollection<int> userIds,
        string? bearerToken,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0) return [];

        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/clubs/{clubId}/member-roster/resolve")
        {
            Content = JsonContent.Create(new ResolveRosterRequest(null, null, userIds))
        };
        SetBearer(request, bearerToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<IReadOnlyCollection<ClubMemberRosterItem>>(cancellationToken: cancellationToken) ?? [];
    }

    private static void SetBearer(HttpRequestMessage request, string? bearerToken)
    {
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"Club roster request failed ({(int)response.StatusCode}): {detail}", null, response.StatusCode);
    }
}
