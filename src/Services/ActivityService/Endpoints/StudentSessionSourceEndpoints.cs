using System.Text.Json;
using ActivityService.Data;
using ActivityService.Models;
using ActivityService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Endpoints;

public static class StudentSessionSourceEndpoints
{
    public static void MapStudentSessionSourceEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/v2/activities/{activityId:int}/sessions", ReadAsync)
            .RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Student session sources");

    private static async Task<IResult> ReadAsync(int activityId, int? page, int? pageSize, ActivityDbContext db,
        HttpContext http, CurrentUserProfileClient users, ClubExperienceAuthorityClient clubs, IConfiguration configuration, CancellationToken ct)
    {
        var current = page ?? 1; var size = pageSize ?? 20;
        if (current < 1 || size is < 1 or > 100) return ApiOperations.Error(400, "INVALID_PAGING", "Use page >= 1 and pageSize 1..100.");
        var actor = http.User.GetUserId();
        InternalUserProfile? user; StudentClubScope authority;
        try
        {
            user = (await users.GetAsync([actor], ct)).GetValueOrDefault(actor);
            if (user is null || !user.IsActive || user.IsLocked) return Results.Forbid();
            authority = await clubs.StudentScopeAsync(actor, user.CampusCode, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        { return ApiOperations.Error(503, "SOURCE_AUTHORITY_UNAVAILABLE", "Current account or club authority is unavailable."); }
        var activity = await db.Activities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == activityId, ct);
        if (activity is null || activity.PublicationStatus is not ("PUBLISHED" or "CLOSED" or "CANCELLED")) return Results.NotFound();
        var club = authority.Clubs.SingleOrDefault(x => x.ClubId == activity.ClubId);
        if (club is null || !club.CanViewPrivate && !(activity.DiscoveryVisibility == "CAMPUS_PUBLIC" && club.CampusCode == user.CampusCode))
            return Results.NotFound();
        if (!await db.ActivityParticipants.AsNoTracking().AnyAsync(x => x.ActivityId == activityId && x.UserId == actor, ct)) return Results.Forbid();
        if (activity.Eligibility == "CAMPUS_PUBLIC" ? club.CampusCode != user.CampusCode : !club.IsApprovedMember || !club.JoinedAt.HasValue)
            return Results.Forbid();
        var query = db.Sessions.AsNoTracking().Include(x => x.Activity).Where(x => x.ActivityId == activityId);
        if (activity.Eligibility != "CAMPUS_PUBLIC") query = query.Where(x => x.StartTimeUtc >= club.JoinedAt!.Value);
        var total = await query.CountAsync(ct);
        var sessions = await query.OrderBy(x => x.StartTimeUtc).ThenBy(x => x.Id)
            .Skip((int)Math.Min((long)(current - 1) * size, int.MaxValue)).Take(size).ToListAsync(ct);
        var ids = sessions.Select(x => x.Id).ToArray();
        var verified = await db.ActivityAttendances.AsNoTracking().Where(x => x.UserId == actor && x.SessionId != null
            && ids.Contains(x.SessionId.Value) && x.VerificationStatus == "VERIFIED").Select(x => x.SessionId!.Value).ToListAsync(ct);
        var now = http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var items = new List<object>();
        foreach (var session in sessions)
        {
            ExperienceSourceSnapshot source;
            try { source = ActivitySessionEndpoints.Source(session); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            { return ApiOperations.Error(503, "SOURCE_SNAPSHOT_UNAVAILABLE", "A published session source is unavailable."); }
            if (source.ActivityId != activityId || source.ClubId != activity.ClubId || source.SemesterId != session.SemesterId
                || source.Policy is null || source.Policy.SemesterId != source.SemesterId || source.Policy.ApprovalStatus != "APPROVED"
                || VerifiedExperienceRules.Validate(source.Attributes) is not null)
                return ApiOperations.Error(503, "SOURCE_SNAPSHOT_UNAVAILABLE", "The published session source is inconsistent.");
            var cancelled = activity.Status == ActivityStatuses.Cancelled || activity.PublicationStatus == "CANCELLED" || session.Status == "CANCELLED";
            if (cancelled) source = source with { Status = "CANCELLED" };
            var occurred = now >= session.StartTimeUtc && !cancelled;
            var alreadyVerified = verified.Contains(session.Id);
            items.Add(new
            {
                session.Id, session.ActivityId, session.SemesterId, session.StartTimeUtc, session.EndTimeUtc, session.Timezone,
                session.Location, status = source.Status, version = session.Version, activityVersion = activity.Version, source,
                alreadyVerified,
                sourceActions = new
                {
                    canCheckIn = AttendanceChallenge.Configured(configuration) && !cancelled && !alreadyVerified && session.Status == "SCHEDULED" && activity.Status == ActivityStatuses.Scheduled
                        && now >= session.OpensAt && now < session.ClosesAt,
                    contribution = occurred ? "REQUIRES_SUBMISSION_VALIDATION" : "UNAVAILABLE",
                    manualAttendance = occurred && session.ManualConfirmationEnabled && !alreadyVerified ? "REQUIRES_SUBMISSION_VALIDATION" : "UNAVAILABLE"
                }
            });
        }
        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { activityId, items, page = current, pageSize = size, totalItems = total,
            totalPages = (int)Math.Ceiling((decimal)total / size), asOf = now, scopeVersion = authority.Version });
    }
}
