using System.Net;
using System.Net.Http.Json;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Rosters;

namespace Backend.StabilizationTests;

// A known cohort for existing membership unit scenarios. The cross-service roster
// tests exercise real Auth/Admin/Club authorities and import validation separately.
internal sealed class CanonicalSemesterRosterHandler(int userId, string fullName) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var query = (await request.Content!.ReadFromJsonAsync<RosterLookupQuery>(ct))!;
        var eligible = query.SemesterId == "FALL2026" && query.CampusCode == CampusCodes.Hanoi && query.UserId == userId;
        return new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new RosterLookupResult(Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "FALL2026", CampusCodes.Hanoi, "AVAILABLE", Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333"), eligible ? [new(userId, fullName, "SE" + userId, true, false, true)] : [],
                1, 20, eligible ? 1 : 0, eligible ? 1 : 0))
        };
    }
}
