using System.Security.Claims;
using System.Text.Json;
using ActivityService.Contracts;
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

public static class ActivitySessionEndpoints
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapActivitySessionEndpoints(this IEndpointRouteBuilder app)
    {
        var sessions = app.MapGroup("/api/activities/{activityId:int}/sessions")
            .RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Activity sessions");
        sessions.MapGet("/", ListAsync);
        sessions.MapPost("/", CreateAsync);
        sessions.MapPatch("/{sessionId:int}", UpdateAsync);
        sessions.MapPost("/{sessionId:int}/close", CloseAsync);
        sessions.MapPost("/{sessionId:int}/cancel", CancelAsync);
        sessions.MapGet("/{sessionId:int}/attendance-challenge", ChallengeAsync);
    }

    internal static ActivitySessionResponse Response(ActivitySession session) => new(session.Id, session.ActivityId,
        session.SeriesId, session.SemesterId, session.StartTimeUtc, session.EndTimeUtc, session.Timezone,
        session.Location, session.Mode, session.Status, session.Version,
        new
        {
            mode = session.Mode,
            qrRotationSeconds = 30,
            opensAt = session.OpensAt,
            closesAt = session.ClosesAt,
            geofence = session.Latitude.HasValue ? new AttendanceGeofence(session.Latitude.Value, session.Longitude!.Value, session.RadiusMeters!.Value) : null,
            manualConfirmationEnabled = session.ManualConfirmationEnabled,
            version = session.Version
        }, Source(session));

    internal static ExperienceSourceSnapshot Source(ActivitySession session)
    {
        var snapshot = JsonSerializer.Deserialize<ExperienceSourceSnapshot>(session.ScoringSnapshotJson, JsonOptions)
            ?? throw new InvalidOperationException("A scoring snapshot is required.");
        return snapshot with
        {
            SessionId = session.Id,
            SessionVersion = session.Version,
            Status = session.Status,
            ActivityVersion = session.Activity is null ? snapshot.ActivityVersion : session.Activity.Version,
            StartTimeUtc = session.StartTimeUtc,
            EndTimeUtc = session.EndTimeUtc,
            ManualConfirmationEnabled = session.ManualConfirmationEnabled
        };
    }

    private static async Task<IResult> ListAsync(int activityId, ActivityDbContext db, HttpContext http,
        ClubAccessClient access, CancellationToken cancellationToken)
    {
        var activity = await db.Activities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == activityId, cancellationToken);
        if (activity is null) return Results.NotFound();
        var scopes = await access.GetMyAccessAsync(http.GetBearerToken(), cancellationToken, bypassCache: true);
        if (!CanReviewAllActivities(http.User) && !scopes.Any(x => x.ClubId == activity.ClubId && x.CanView)) return Results.Forbid();
        var rows = await db.Sessions.AsNoTracking().Include(x => x.Activity).Where(x => x.ActivityId == activityId)
            .OrderBy(x => x.StartTimeUtc).ToListAsync(cancellationToken);
        return Results.Ok(new { activityId, items = rows.Select(Response), total = rows.Count });
    }

    private static async Task<IResult> CreateAsync(int activityId, CreateActivitySessionsRequest request,
        ActivityDbContext db, HttpContext http, ClubAccessClient access, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var activity = await db.Activities.SingleOrDefaultAsync(x => x.Id == activityId, cancellationToken);
        if (activity is null) return Results.NotFound();
        if (!await CanManageClubOrReviewAllAsync(activity.ClubId, http.User, access, http, cancellationToken, true)) return Results.Forbid();
        if (request.Timezone != "Asia/Ho_Chi_Minh" || request.AttendancePolicy is null)
            return ApiOperations.Error(400, "INVALID_SESSION", "Use Asia/Ho_Chi_Minh and an attendance policy.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            if (request.ExpectedVersion != activity.Version) return ApiOperations.Error(409, "STALE_VERSION", "Reload the activity.");
            if (activity.PublicationStatus != "PUBLISHED" || activity.ScoringSnapshotJson is null)
                return ApiOperations.Error(409, "ACTIVITY_NOT_PUBLISHED", "Publish with an approved scoring policy first.");
            List<(DateTimeOffset Start, DateTimeOffset End)> occurrences = [];
            if (request.StartAt.HasValue && request.EndAt.HasValue)
            {
                if (request.Weekdays is not null || request.RangeStartDate.HasValue || request.RangeEndDate.HasValue)
                    return ApiOperations.Error(400, "AMBIGUOUS_RECURRENCE", "Choose a single session or a recurrence.");
                occurrences.Add((request.StartAt.Value, request.EndAt.Value));
            }
            else
            {
                if (request.Weekdays is null || request.Weekdays.Count == 0 || request.Weekdays.Any(x => x is < 1 or > 7)
                    || request.Weekdays.Distinct().Count() != request.Weekdays.Count || !request.StartTime.HasValue || !request.EndTime.HasValue
                    || request.EndTime <= request.StartTime || !request.RangeStartDate.HasValue || !request.RangeEndDate.HasValue
                    || request.RangeEndDate < request.RangeStartDate || request.RangeEndDate.Value.DayNumber - request.RangeStartDate.Value.DayNumber > 366)
                    return ApiOperations.Error(400, "INVALID_RECURRENCE", "Provide valid days, times and a range of at most one year.");
                for (var date = request.RangeStartDate.Value; date <= request.RangeEndDate.Value; date = date.AddDays(1))
                {
                    var weekday = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
                    if (!request.Weekdays.Contains(weekday) || request.ExcludedDates?.Contains(date) == true) continue;
                    occurrences.Add((new DateTimeOffset(date.ToDateTime(request.StartTime.Value), TimeSpan.FromHours(7)).ToUniversalTime(),
                        new DateTimeOffset(date.ToDateTime(request.EndTime.Value), TimeSpan.FromHours(7)).ToUniversalTime()));
                }
            }
            if (occurrences.Count == 0 || occurrences.Count > configuration.GetValue("Activities:MaxSessionsPerRequest", 100))
                return ApiOperations.Error(400, "SESSION_LIMIT", "The request must produce a permitted number of sessions.");
            var series = Guid.NewGuid();
            var blueprint = JsonSerializer.Deserialize<ExperienceSourceSnapshot>(activity.ScoringSnapshotJson, JsonOptions)!;
            var rows = new List<ActivitySession>();
            var firstStart = occurrences[0].Start;
            foreach (var occurrence in occurrences)
            {
                var shift = occurrence.Start - firstStart;
                var policy = request.AttendancePolicy with
                { OpensAt = request.AttendancePolicy.OpensAt + shift, ClosesAt = request.AttendancePolicy.ClosesAt + shift };
                var error = ValidatePolicy(policy, occurrence.Start, occurrence.End, configuration);
                if (error is not null || occurrence.Start < activity.StartTimeUtc || occurrence.End > activity.EndTimeUtc)
                    return ApiOperations.Error(400, "INVALID_SESSION", error ?? "Sessions must fit the activity time range.");
                if (await db.Sessions.AnyAsync(x => x.ActivityId == activityId && x.StartTimeUtc == occurrence.Start && x.EndTimeUtc == occurrence.End, cancellationToken))
                    return ApiOperations.Error(409, "DUPLICATE_OCCURRENCE", "This occurrence already exists.");
                var row = new ActivitySession
                {
                    ActivityId = activityId,
                    SeriesId = series,
                    SemesterId = blueprint.SemesterId,
                    StartTimeUtc = occurrence.Start,
                    EndTimeUtc = occurrence.End,
                    Location = request.Location?.Trim() ?? activity.Location,
                    CreatedAtUtc = Now(http),
                    UpdatedAtUtc = Now(http),
                    CreatedByUserId = http.User.GetUserId(),
                    ScoringSnapshotJson = activity.ScoringSnapshotJson
                };
                if (row.Location.Length > 200) return ApiOperations.Error(400, "INVALID_LOCATION", "Location is too long.");
                ApplyPolicy(row, policy);
                db.Sessions.Add(row);
                rows.Add(row);
            }
            await db.SaveChangesAsync(cancellationToken);
            foreach (var row in rows) Audit(db, row, http, "CREATE", null);
            activity.Version = Guid.NewGuid();
            activity.UpdatedAtUtc = Now(http);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { activityId, activityVersion = activity.Version, items = rows.Select(Response).ToArray(), total = rows.Count });
        }, cancellationToken);
    }

    private static async Task<IResult> UpdateAsync(int activityId, int sessionId, UpdateActivitySessionRequest request,
        ActivityDbContext db, HttpContext http, ClubAccessClient access, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var session = await db.Sessions.Include(x => x.Activity).SingleOrDefaultAsync(x => x.Id == sessionId && x.ActivityId == activityId, cancellationToken);
        if (session is null) return Results.NotFound();
        if (!await CanManageClubOrReviewAllAsync(session.Activity.ClubId, http.User, access, http, cancellationToken, true)) return Results.Forbid();
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            if (request.ExpectedVersion != session.Version) return ApiOperations.Error(409, "STALE_VERSION", "Reload the session.");
            if (session.Status != "SCHEDULED" || Now(http) >= session.OpensAt
                || await db.ActivityAttendances.AnyAsync(x => x.SessionId == sessionId, cancellationToken))
                return ApiOperations.Error(409, "SESSION_HISTORY_PROTECTED", "An opened or recorded session cannot be edited.");
            var start = request.StartAt ?? session.StartTimeUtc;
            var end = request.EndAt ?? session.EndTimeUtc;
            var policy = request.AttendancePolicy ?? new AttendancePolicyRequest(session.Mode, session.OpensAt, session.ClosesAt,
                session.Latitude.HasValue ? new AttendanceGeofence(session.Latitude.Value, session.Longitude!.Value, session.RadiusMeters!.Value) : null, session.ManualConfirmationEnabled);
            var error = ValidatePolicy(policy, start, end, configuration);
            if (error is not null || start < session.Activity.StartTimeUtc || end > session.Activity.EndTimeUtc || request.Location?.Length > 200)
                return ApiOperations.Error(400, "INVALID_SESSION", error ?? "The session must fit the activity range.");
            session.StartTimeUtc = start; session.EndTimeUtc = end;
            session.Location = request.Location?.Trim() ?? session.Location;
            ApplyPolicy(session, policy);
            session.Version = Guid.NewGuid(); session.UpdatedAtUtc = Now(http);
            Audit(db, session, http, "UPDATE", null);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(Response(session));
        }, cancellationToken);
    }

    private static Task<IResult> CloseAsync(int activityId, int sessionId, SessionDecisionRequest request,
        ActivityDbContext db, HttpContext http, ClubAccessClient access, CancellationToken cancellationToken) =>
        DecideAsync(activityId, sessionId, request, db, http, access, false, cancellationToken);
    private static Task<IResult> CancelAsync(int activityId, int sessionId, SessionDecisionRequest request,
        ActivityDbContext db, HttpContext http, ClubAccessClient access, CancellationToken cancellationToken) =>
        DecideAsync(activityId, sessionId, request, db, http, access, true, cancellationToken);

    private static async Task<IResult> DecideAsync(int activityId, int sessionId, SessionDecisionRequest request,
        ActivityDbContext db, HttpContext http, ClubAccessClient access, bool cancel, CancellationToken cancellationToken)
    {
        var session = await db.Sessions.Include(x => x.Activity).SingleOrDefaultAsync(x => x.Id == sessionId && x.ActivityId == activityId, cancellationToken);
        if (session is null) return Results.NotFound();
        if (!await CanManageClubOrReviewAllAsync(session.Activity.ClubId, http.User, access, http, cancellationToken, true)) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            return ApiOperations.Error(400, "REASON_REQUIRED", "Provide a reason of up to 1,000 characters.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            if (request.ExpectedVersion != session.Version) return ApiOperations.Error(409, "STALE_VERSION", "Reload the session.");
            if (session.Status == "CANCELLED" || (!cancel && session.Status != "SCHEDULED"))
                return ApiOperations.Error(409, "INVALID_SESSION_STATE", "This transition is not allowed.");
            session.Status = cancel ? "CANCELLED" : "CLOSED";
            session.Version = Guid.NewGuid(); session.UpdatedAtUtc = Now(http);
            Audit(db, session, http, cancel ? "CANCEL" : "CLOSE", request.Reason.Trim());
            if (cancel)
            {
                foreach (var attendance in await db.ActivityAttendances.Where(x => x.SessionId == sessionId && x.VerificationStatus == "VERIFIED").ToListAsync(cancellationToken))
                { attendance.VerificationStatus = "REVOKED"; attendance.RecognitionStatus = "REVOCATION_PENDING"; attendance.Version = Guid.NewGuid(); }
                db.AddOutboxMessage(new ExperienceSourceCancelledEvent(Guid.NewGuid(), Now(http), activityId,
                    sessionId, session.Activity.ClubId, http.User.GetUserId(), request.Reason.Trim()), ExperienceRoutingKeys.SourceCancelled);
            }
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(Response(session));
        }, cancellationToken);
    }

    private static async Task<IResult> ChallengeAsync(int activityId, int sessionId, ActivityDbContext db,
        HttpContext http, ClubAccessClient access, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var session = await db.Sessions.Include(x => x.Activity).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == sessionId && x.ActivityId == activityId, cancellationToken);
        if (session is null) return Results.NotFound();
        if (!await CanManageClubOrReviewAllAsync(session.Activity.ClubId, http.User, access, http, cancellationToken, true)) return Results.Forbid();
        var now = Now(http);
        if (session.Status != "SCHEDULED" || session.Activity.PublicationStatus is not ("PUBLISHED" or "CLOSED")
            || now < session.OpensAt || now >= session.ClosesAt)
            return ApiOperations.Error(409, "ATTENDANCE_CLOSED", "The session attendance window is closed.");
        if (!AttendanceChallenge.Configured(configuration)) return ApiOperations.Error(503, "ATTENDANCE_UNCONFIGURED", "Attendance signing key is unavailable.");
        var issued = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() / 30 * 30);
        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new
        {
            challenge = AttendanceChallenge.Issue(session, now, configuration),
            issuedAt = issued,
            expiresAt = issued.AddSeconds(30),
            serverTime = now,
            sessionId
        });
    }

    internal static DateTimeOffset Now(HttpContext http) => http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
    internal static void Audit(ActivityDbContext db, ActivitySession session, HttpContext http, string action, string? reason) =>
        db.SessionAudits.Add(new ActivitySessionAudit
        {
            ActivityId = session.ActivityId,
            SessionId = session.Id,
            Action = action,
            Reason = reason,
            ActorId = http.User.GetUserId(),
            Version = session.Version,
            CreatedAtUtc = Now(http),
            SnapshotJson = JsonSerializer.Serialize(Response(session), JsonOptions)
        });

    internal static string? ValidatePolicy(AttendancePolicyRequest policy, DateTimeOffset start, DateTimeOffset end, IConfiguration configuration)
    {
        if (end <= start || policy.OpensAt >= policy.ClosesAt || policy.OpensAt < start || policy.ClosesAt > end
            || policy.QrRotationSeconds != 30 || policy.Mode is not ("ONSITE" or "ONLINE")) return "Check session times, attendance window, mode and the 30-second rotation.";
        var geo = policy.Geofence;
        if (policy.Mode == "ONLINE") return geo is null ? null : "Online attendance does not use a geofence.";
        return geo is null || !double.IsFinite(geo.Latitude) || !double.IsFinite(geo.Longitude) || !double.IsFinite(geo.RadiusMeters)
            || geo.Latitude is < -90 or > 90 || geo.Longitude is < -180 or > 180 || geo.RadiusMeters <= 0
            || geo.RadiusMeters > configuration.GetValue("Attendance:MaxRadiusMeters", 5000d) ? "Provide a valid onsite geofence." : null;
    }

    private static void ApplyPolicy(ActivitySession row, AttendancePolicyRequest policy)
    {
        row.Mode = policy.Mode; row.OpensAt = policy.OpensAt; row.ClosesAt = policy.ClosesAt;
        row.Latitude = policy.Geofence?.Latitude; row.Longitude = policy.Geofence?.Longitude;
        row.RadiusMeters = policy.Geofence?.RadiusMeters; row.ManualConfirmationEnabled = policy.ManualConfirmationEnabled;
    }
}
