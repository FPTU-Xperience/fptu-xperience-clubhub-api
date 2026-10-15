using ActivityService.Models;
using ClubReportHub.Shared.Experience;

namespace ActivityService.Services;

public static class ActivityEligibility
{
    public static async Task<InternalUserProfile?> ResolveAsync(ClubActivity activity, int userId, DateTimeOffset cutoff,
        CurrentUserProfileClient users, ClubExperienceAuthorityClient clubs, CancellationToken ct)
    {
        var user = (await users.GetAsync([userId], ct)).GetValueOrDefault(userId);
        var club = await clubs.GetAsync(activity.ClubId, userId, ct);
        if (user is null || !user.IsActive || user.IsLocked || club is null || !club.IsActive) return null;
        return activity.Eligibility == "CAMPUS_PUBLIC" ? user.CampusCode == club.CampusCode ? user : null
            : club.IsApprovedMember && club.JoinedAt <= cutoff ? user : null;
    }
}
