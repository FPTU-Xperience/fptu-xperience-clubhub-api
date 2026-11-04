using System.Text.Json.Serialization;
using ActivityService.Data;
using ActivityService.Models;
using ActivityService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityEndpointHelpers;

namespace ActivityService.Endpoints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisteredAttendanceItem(int UserId, string Status, string? Note, Guid? ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisteredAttendanceInput(Guid ExpectedSessionVersion, string Reason, IReadOnlyList<RegisteredAttendanceItem> Items);

public static class RegisteredSessionAttendance
{
    public static void MapRegisteredSessionAttendance(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/clubs/{clubId:int}/activities/{activityId:int}/sessions/{sessionId:int}/registered-attendance")
            .RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Registered session attendance");
        group.MapGet("/", Read);
        group.MapPut("/", Write);
    }

    private static async Task<ActivitySession?> Load(int clubId, int activityId, int sessionId, ActivityDbContext db, CancellationToken ct) =>
        await db.Sessions.Include(x => x.Activity).SingleOrDefaultAsync(x => x.Id == sessionId && x.ActivityId == activityId && x.Activity.ClubId == clubId, ct);

    private static Task<bool> Manage(int clubId, HttpContext http, CancellationToken ct) =>
        CanManageClubOrReviewAllAsync(clubId, http.User, http.RequestServices.GetRequiredService<ClubAccessClient>(), http, ct, true);

    private static Task<bool> Verify(int clubId, HttpContext http, CancellationToken ct) =>
        http.RequestServices.GetRequiredService<ExperienceAuthorityClient>().CanVerifyAsync(clubId, "attendance.verify", http, ct);

    private static bool Eligible(InternalUserProfile? user, ClubExperienceAuthority club) =>
        user is { IsActive: true, IsLocked: false } && user.CampusCode == club.CampusCode;

    private static bool Open(ActivitySession session, DateTimeOffset now) => session.Status != "CANCELLED"
        && session.Activity.Status == ActivityStatuses.Scheduled && session.Activity.PublicationStatus is "PUBLISHED" or "CLOSED"
        && session.StartTimeUtc <= now;

    private static async Task<IResult> Read(int clubId, int activityId, int sessionId, string? search, int? page, int? pageSize,
        ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await Manage(clubId, http, ct)) return Results.Forbid();
        var session = await Load(clubId, activityId, sessionId, db, ct);
        if (session is null) return Results.NotFound();
        if (session.Activity.IsInternal) return ApiOperations.Error(400, "CLUB_ROSTER_REQUIRED", "Use the club member roster for internal activities.");
        var query = db.ActivityParticipants.AsNoTracking().Where(x => x.ActivityId == activityId);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.FullName.Contains(search.Trim()));
        var total = await query.CountAsync(ct); var actualPage = Math.Clamp(page ?? 1, 1, 1_000_000); var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var participants = await query.OrderBy(x => x.UserId).Skip((actualPage - 1) * size).Take(size).ToListAsync(ct);
        var ids = participants.Select(x => x.UserId).ToArray();
        try
        {
            var club = await http.RequestServices.GetRequiredService<ClubExperienceAuthorityClient>().GetAsync(clubId, null, ct);
            if (club is not { IsActive: true }) return Results.Forbid();
            var profiles = await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync(ids, ct);
            var canVerify = await Verify(clubId, http, ct); var now = ActivitySessionEndpoints.Now(http);
            var rows = await db.ActivityAttendances.AsNoTracking().Where(x => x.SessionId == sessionId && ids.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, ct);
            return Results.Ok(new
            {
                activityId,
                sessionId,
                sessionVersion = session.Version,
                identityKind = "REGISTERED_USER",
                capabilities = new { canVerify },
                items = participants.Select(p =>
                {
                    rows.TryGetValue(p.UserId, out var row); profiles.TryGetValue(p.UserId, out var profile);
                    var allowed = canVerify && p.UserId != http.User.GetUserId() && Eligible(profile, club) && Open(session, now)
                        && row?.VerificationStatus is not ("VERIFIED" or "REVOKED");
                    return new
                    {
                        p.UserId,
                        fullName = profile?.DisplayName ?? p.FullName,
                        isRegistered = true,
                        attendance = row is null ? null : SessionAttendanceEndpoints.Response(row),
                        canMarkPresent = allowed && session.ManualConfirmationEnabled,
                        canMarkAbsent = allowed && session.EndTimeUtc <= now
                    };
                }),
                page = actualPage,
                pageSize = size,
                totalItems = total,
                totalPages = (int)Math.Ceiling(total / (double)size),
                verifiedCount = await db.ActivityAttendances.CountAsync(x => x.SessionId == sessionId && x.VerificationStatus == "VERIFIED", ct)
            });
        }
        catch (HttpRequestException) { return ApiOperations.Error(503, "ATTENDANCE_AUTHORITY_UNAVAILABLE", "Current eligibility or verification authority is unavailable."); }
    }

    private static async Task<IResult> Write(int clubId, int activityId, int sessionId, RegisteredAttendanceInput input,
        ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!await Manage(clubId, http, ct)) return Results.Forbid();
        var session = await Load(clubId, activityId, sessionId, db, ct);
        if (session is null) return Results.NotFound();
        if (session.Activity.IsInternal) return ApiOperations.Error(400, "CLUB_ROSTER_REQUIRED", "Use the club member roster for internal activities.");
        if (input.ExpectedSessionVersion == Guid.Empty || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 1000
            || input.Items is null || input.Items.Count is < 1 or > 100 || input.Items.Select(x => x.UserId).Distinct().Count() != input.Items.Count
            || input.Items.Any(x => x.UserId < 1 || x.Status is not ("Present" or "Late" or "Absent" or "Excused") || x.Note?.Length > 1000))
            return ApiOperations.Error(400, "INVALID_ATTENDANCE", "Provide unique registered user IDs, statuses, versions and a reason.");
        if (input.Items.Any(x => x.UserId == http.User.GetUserId())) return ApiOperations.Error(403, "SELF_REVIEW_FORBIDDEN", "You cannot verify your own attendance.");
        try
        {
            if (!await Verify(clubId, http, ct)) return Results.Forbid();
            return await ApiOperations.ExecuteAsync(http, db, input, async () =>
            {
                await db.Entry(session).ReloadAsync(ct); await db.Entry(session.Activity).ReloadAsync(ct);
                if (session.Version != input.ExpectedSessionVersion || !Open(session, ActivitySessionEndpoints.Now(http)))
                    return ApiOperations.Error(409, "STALE_SESSION", "Reload an eligible session that has started.");
                if (!await Manage(clubId, http, ct) || !await Verify(clubId, http, ct))
                    return ApiOperations.Error(403, "ATTENDANCE_AUTHORITY_REQUIRED", "Current manual verification authority is required.");
                var ids = input.Items.Select(x => x.UserId).ToArray();
                if (await db.ActivityParticipants.CountAsync(x => x.ActivityId == activityId && ids.Contains(x.UserId), ct) != ids.Length)
                    return ApiOperations.Error(409, "REGISTRATION_REQUIRED", "Every student must still be registered.");
                var club = await http.RequestServices.GetRequiredService<ClubExperienceAuthorityClient>().GetAsync(clubId, null, ct);
                var users = await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync(ids, ct);
                if (club is not { IsActive: true } || ids.Any(id => !users.TryGetValue(id, out var user) || !Eligible(user, club)))
                    return ApiOperations.Error(403, "ATTENDANCE_ELIGIBILITY_REQUIRED", "All students must have an active eligible campus account.");
                var rows = await db.ActivityAttendances.Where(x => x.SessionId == sessionId && ids.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, ct);
                foreach (var item in input.Items)
                {
                    rows.TryGetValue(item.UserId, out var row);
                    if (row?.Version != item.ExpectedVersion) return ApiOperations.Error(409, "STALE_ATTENDANCE", "Reload attendance versions.");
                    if (row?.VerificationStatus is "VERIFIED" or "REVOKED") return ApiOperations.Error(409, "VERIFIED_ATTENDANCE_PROTECTED", "Use the adjustment workflow for verified attendance.");
                    if (item.Status is "Present" or "Late" && !session.ManualConfirmationEnabled) return ApiOperations.Error(409, "MANUAL_CONFIRMATION_DISABLED", "Manual verification is disabled.");
                    if (item.Status is "Absent" or "Excused" && session.EndTimeUtc > ActivitySessionEndpoints.Now(http)) return ApiOperations.Error(409, "SESSION_NOT_FINISHED", "Mark absence after the session ends.");
                }
                foreach (var item in input.Items)
                {
                    rows.TryGetValue(item.UserId, out var row); var now = ActivitySessionEndpoints.Now(http);
                    row ??= new ActivityAttendance
                    {
                        ActivityId = activityId,
                        SessionId = sessionId,
                        UserId = item.UserId,
                        FullName = users[item.UserId].DisplayName,
                        AttendanceDate = WeeklyAttendanceRules.GetVietnamDate(session.StartTimeUtc),
                        CreatedAtUtc = now
                    };
                    if (row.Id == 0) db.ActivityAttendances.Add(row);
                    row.Status = item.Status; row.Note = item.Note?.Trim(); row.Version = Guid.NewGuid(); row.UpdatedAtUtc = now;
                    if (item.Status is "Present" or "Late")
                    {
                        row.VerificationStatus = "VERIFIED"; row.VerificationMethod = "MANUAL_REVIEW"; row.RecognitionStatus = "PENDING";
                        row.CheckedInAtUtc = now; row.CheckedInByUserId = http.User.GetUserId();
                    }
                    await db.SaveChangesAsync(ct);
                    if (row.VerificationStatus == "VERIFIED") db.AddOutboxMessage(new AttendanceVerifiedEvent(Guid.NewGuid(), now, row.UserId,
                        row.Id, "MANUAL_REVIEW", http.User.GetUserId(), ActivitySessionEndpoints.Source(session)), ExperienceRoutingKeys.AttendanceVerified);
                    ActivitySessionEndpoints.Audit(db, session, http, "REGISTERED_ATTENDANCE_MANUAL", input.Reason.Trim());
                    rows[row.UserId] = row;
                }
                await db.SaveChangesAsync(ct); return Results.Ok(new { items = rows.Values.Select(SessionAttendanceEndpoints.Response) });
            }, ct);
        }
        catch (HttpRequestException) { return ApiOperations.Error(503, "ATTENDANCE_AUTHORITY_UNAVAILABLE", "Current eligibility or verification authority is unavailable."); }
    }
}
