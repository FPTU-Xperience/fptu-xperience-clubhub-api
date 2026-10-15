using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Endpoints;

public static class StudentActivityHistoryEndpoints
{
    public static void MapStudentActivityHistoryEndpoints(this IEndpointRouteBuilder app)
        => app.MapGet("/api/users/me/activity-history", ReadAsync)
            .RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Personal activity history");

    private static async Task<IResult> ReadAsync(int? page, int? pageSize, string? semesterId, string? verificationStatus,
        ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        if (page is < 1 || pageSize is < 1 or > 100 || semesterId?.Length > 40)
            return ApiOperations.Error(400, "INVALID_HISTORY_QUERY", "Check the semester and pagination.");
        var state = verificationStatus?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(state) && state is not ("VERIFIED" or "UNVERIFIED" or "REVOKED"))
            return ApiOperations.Error(400, "INVALID_VERIFICATION_STATUS", "Unknown verification status.");
        var userId = http.User.GetUserId();
        var attendance = db.ActivityAttendances.AsNoTracking().Where(x => x.UserId == userId);
        var sessions = db.Sessions.AsNoTracking().Where(s => db.ActivityParticipants.Any(p => p.UserId == userId && p.ActivityId == s.ActivityId)
            || attendance.Any(a => a.SessionId == s.Id && a.ActivityId == s.ActivityId));
        if (!string.IsNullOrWhiteSpace(semesterId)) sessions = sessions.Where(x => x.SemesterId == semesterId);
        var canonical = from session in sessions
                        join row in attendance on new { SessionId = (int?)session.Id, session.ActivityId }
                            equals new { row.SessionId, row.ActivityId } into matches
                        from row in matches.DefaultIfEmpty()
                        select new
                        {
                            session.ActivityId,
                            SessionId = (int?)session.Id,
                            AttendanceId = row == null ? (int?)null : row.Id,
                            SortAt = session.StartTimeUtc,
                            Kind = 0,
                            Verification = row == null ? "UNVERIFIED" : row.VerificationStatus
                        };
        var legacy = attendance.Where(x => x.SessionId == null).Select(x => new
        {
            x.ActivityId,
            SessionId = (int?)null,
            AttendanceId = (int?)x.Id,
            SortAt = x.CheckedInAtUtc ?? x.CreatedAtUtc,
            Kind = 1,
            Verification = x.VerificationStatus
        });
        var registration = db.ActivityParticipants.AsNoTracking().Where(p => p.UserId == userId
            && !db.Sessions.Any(s => s.ActivityId == p.ActivityId)
            && !attendance.Any(a => a.ActivityId == p.ActivityId))
            .Select(p => new
            {
                p.ActivityId,
                SessionId = (int?)null,
                AttendanceId = (int?)null,
                SortAt = p.RegisteredAtUtc,
                Kind = 2,
                Verification = "UNVERIFIED"
            });
        if (!string.IsNullOrWhiteSpace(semesterId)) { legacy = legacy.Where(_ => false); registration = registration.Where(_ => false); }
        var index = canonical.Concat(legacy).Concat(registration);
        if (!string.IsNullOrWhiteSpace(state)) index = index.Where(x => x.Verification == state);
        var current = page ?? 1; var size = pageSize ?? 20;
        var total = await index.CountAsync(ct);
        var selected = await index.OrderByDescending(x => x.SortAt).ThenByDescending(x => x.ActivityId)
            .ThenByDescending(x => x.SessionId).ThenByDescending(x => x.AttendanceId)
            .Skip((int)Math.Min((long)(current - 1) * size, int.MaxValue)).Take(size).ToListAsync(ct);
        var activityIds = selected.Select(x => x.ActivityId).Distinct().ToArray();
        var sessionIds = selected.Where(x => x.SessionId.HasValue).Select(x => x.SessionId!.Value).ToArray();
        var attendanceIds = selected.Where(x => x.AttendanceId.HasValue).Select(x => x.AttendanceId!.Value).ToArray();
        var activities = await db.Activities.AsNoTracking().Where(x => activityIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var occurrences = await db.Sessions.AsNoTracking().Where(x => sessionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var rows = await attendance.Where(x => attendanceIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var items = selected.Select(x =>
        {
            var activity = activities[x.ActivityId];
            var session = x.SessionId.HasValue ? occurrences[x.SessionId.Value] : null;
            var row = x.AttendanceId.HasValue ? rows[x.AttendanceId.Value] : null;
            return new
            {
                activityId = activity.Id,
                activity.Title,
                activity.ClubId,
                activity.ClubName,
                activityStatus = activity.Status,
                sessionId = session?.Id,
                sessionStatus = session?.Status,
                semesterId = session?.SemesterId,
                startTime = session?.StartTimeUtc ?? activity.StartTimeUtc,
                endTime = session?.EndTimeUtc ?? activity.EndTimeUtc,
                location = session?.Location ?? activity.Location,
                sourceNamespace = x.Kind == 0 ? "ACTIVITY_SESSION" : x.Kind == 1 ? "LEGACY_ATTENDANCE" : "LEGACY_REGISTRATION",
                isLegacy = x.Kind != 0,
                isAuthoritative = x.Kind == 0,
                attendanceId = row?.Id,
                attendanceDate = row?.AttendanceDate,
                attendanceStatus = row?.Status ?? AttendanceStatuses.Registered,
                verificationStatus = row?.VerificationStatus ?? "UNVERIFIED",
                recognitionStatus = row?.RecognitionStatus ?? "NOT_REQUESTED",
                contributionId = row?.ContributionId,
                checkedInAt = row?.CheckedInAtUtc,
                activityVersion = activity.Version,
                sessionVersion = session?.Version,
                attendanceVersion = row?.Version
            };
        }).ToArray();
        return Results.Ok(new
        {
            items,
            page = current,
            pageSize = size,
            totalItems = total,
            totalPages = (int)Math.Ceiling((decimal)total / size),
            asOf = DateTimeOffset.UtcNow
        });
    }
}

