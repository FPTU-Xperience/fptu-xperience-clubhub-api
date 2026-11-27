using System.Net.Http.Headers;
using AdminService.Contracts;
using AdminService.Data;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;

namespace AdminService.Services;

internal static class ClubDeclarationWorkflow
{
    internal const string Permission = "club.declarations.process";
    internal static bool IsReferral(VerifiedContribution row) => row.ClubReviewStatus is not null
        && row.ClubId.HasValue && row.SourceType is "EXTERNAL" or "REAL_WORLD";

    // Read live authority on every action. Browser workspace flags and cached grants cannot authorize a referral.
    internal static async Task<ClubAccessSnapshot?> AccessAsync(int clubId, HttpContext http, CancellationToken ct)
    {
        var actor = http.User.GetUserId();
        var profiles = await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([actor], ct);
        var club = await http.RequestServices.GetRequiredService<ClubExperienceAuthorityClient>().GetAsync(clubId, actor, ct);
        if (club is not { IsActive: true } || !profiles.TryGetValue(actor, out var profile)
            || profile is not { IsActive: true, IsLocked: false }
            || club.CampusCode != CampusCodes.Global && profile.CampusCode != club.CampusCode) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/clubs/me/access");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", http.GetBearerToken());
        using var response = await http.RequestServices.GetRequiredService<IHttpClientFactory>()
            .CreateClient("ExperienceClub").SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var access = await response.Content.ReadFromJsonAsync<ClubAccessSnapshot[]>(ct)
            ?? throw new HttpRequestException("Invalid club access response.");
        return access.SingleOrDefault(x => x.ClubId == clubId && x.HasCapability(Permission));
    }

    internal static async Task<bool> CanProcessAsync(VerifiedContribution row, HttpContext http, CancellationToken ct) =>
        IsReferral(row) && row.UserId != http.User.GetUserId() && await AccessAsync(row.ClubId!.Value, http, ct) is not null;

    internal static DeclarationCapabilities Capabilities(VerifiedContribution row, bool process, bool final) => new(
        process && row.Status == "SUBMITTED" && row.CtsvSubmissionStatus == "NOT_SUBMITTED" && row.ClubReviewStatus == "PENDING",
        process && row.Status == "SUBMITTED" && row.CtsvSubmissionStatus == "NOT_SUBMITTED" && row.ClubReviewStatus == "APPROVED",
        final && row.Status == "SUBMITTED");

    internal static void Reset(VerifiedContribution row)
    {
        if (!IsReferral(row)) return;
        row.ClubReviewStatus = "PENDING";
        row.CtsvSubmissionStatus = "NOT_SUBMITTED";
        row.ClubReviewReason = null;
        row.ClubReviewedBy = null;
        row.CtsvSubmittedBy = null;
    }
}
