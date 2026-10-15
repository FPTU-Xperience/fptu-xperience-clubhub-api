using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Infrastructure;
using ActivityService.Models;
using ActivityService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Endpoints;

internal static class SessionAttendanceManagement
{
    internal static async Task<IResult> GetAsync(int activityId, int? sessionId, string? search, int? page, int? pageSize,
        ActivityDbContext db, ClubMemberRosterClient roster, HttpContext http, CancellationToken ct)
    {
        var session = await db.Sessions.Include(x => x.Activity).AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId && x.ActivityId == activityId, ct);
        if (session is null) return ApiOperations.Error(400, "SESSION_REQUIRED", "Select a session for attendance.");
        ClubMemberRosterPage members;
        try { members = await roster.GetAsync(session.Activity.ClubId, session.StartTimeUtc, search, Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? 20, 1, 100), http.GetBearerToken(), ct); }
        catch (HttpRequestException) { return ApiOperations.Error(503, "MEMBERSHIP_SERVICE_UNAVAILABLE", "Roster is unavailable."); }
        var ids = members.Items.Select(x => x.UserId).ToArray();
        var rows = await db.ActivityAttendances.AsNoTracking().Where(x => x.SessionId == session.Id && ids.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, ct);
        var count = await db.ActivityAttendances.CountAsync(x => x.SessionId == session.Id && x.VerificationStatus == "VERIFIED", ct);
        return Results.Ok(new
        {
            activityId,
            sessionId,
            sessionVersion = session.Version,
            items = members.Items.Select(m => new
            { memberId = m.Id, m.UserId, m.FullName, m.Role, attendance = rows.TryGetValue(m.UserId, out var attendance) ? SessionAttendanceEndpoints.Response(attendance) : null }),
            members.Page,
            members.PageSize,
            members.TotalItems,
            members.TotalPages,
            verifiedCount = count
        });
    }

    internal static async Task<IResult> WriteAsync(int activityId, BulkUpdateAttendanceRequest request, ActivityDbContext db,
        ClubMemberRosterClient roster, HttpContext http, CancellationToken ct)
    {
        var session = await db.Sessions.Include(x => x.Activity).SingleOrDefaultAsync(x => x.Id == request.SessionId && x.ActivityId == activityId, ct);
        if (session is null) return ApiOperations.Error(400, "SESSION_REQUIRED", "Select a session for attendance.");
        var authority = http.RequestServices.GetRequiredService<ExperienceAuthorityClient>();
        try { if (!await authority.CanVerifyAsync(session.Activity.ClubId, "attendance.verify", http, ct)) return Results.Forbid(); }
        catch (HttpRequestException) { return ApiOperations.Error(503, "VERIFICATION_SERVICE_UNAVAILABLE", "Verification assignments are unavailable."); }
        if (request.Items is null || request.Items.Count is 0 or > 100 || request.Items.Select(x => x.MemberId).Distinct().Count() != request.Items.Count
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000
            || request.Items.Any(x => x.Status is not ("Present" or "Absent" or "Excused" or "Late") || x.Note?.Length > 1000))
            return ApiOperations.Error(400, "INVALID_ATTENDANCE", "Provide unique members, valid statuses, versions and a reason.");
        IReadOnlyCollection<ClubMemberRosterItem> members;
        try { members = await roster.ResolveAsync(session.Activity.ClubId, request.Items.Select(x => x.MemberId).ToArray(), session.StartTimeUtc, http.GetBearerToken(), ct); }
        catch (HttpRequestException) { return ApiOperations.Error(503, "MEMBERSHIP_SERVICE_UNAVAILABLE", "Roster is unavailable."); }
        if (members.Count != request.Items.Count) return ApiOperations.Error(400, "INVALID_ROSTER", "All members must be eligible at the session time.");
        if (members.Any(x => x.UserId == http.User.GetUserId())) return ApiOperations.Error(403, "SELF_REVIEW_FORBIDDEN", "You cannot verify your own attendance.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            await db.Entry(session).ReloadAsync(ct); await db.Entry(session.Activity).ReloadAsync(ct);
            if (session.Version != request.ExpectedSessionVersion || session.Status == "CANCELLED" || session.Activity.Status == ActivityStatuses.Cancelled
                || session.StartTimeUtc > ActivitySessionEndpoints.Now(http)) return ApiOperations.Error(409, "STALE_SESSION", "Reload an eligible session that has started.");
            var userIds = members.Select(x => x.UserId).ToArray();
            if (await db.ActivityParticipants.CountAsync(x => x.ActivityId == activityId && userIds.Contains(x.UserId), ct) != userIds.Length)
                return ApiOperations.Error(409, "REGISTRATION_REQUIRED", "All members must be registered.");
            var existing = await db.ActivityAttendances.Where(x => x.SessionId == session.Id && userIds.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, ct);
            foreach (var item in request.Items)
            {
                var member = members.Single(x => x.Id == item.MemberId); existing.TryGetValue(member.UserId, out var row);
                if (row?.Version != item.ExpectedVersion) return ApiOperations.Error(409, "STALE_ATTENDANCE", "Reload all attendance versions.");
                if (row?.VerificationStatus == "VERIFIED") return ApiOperations.Error(409, "VERIFIED_ATTENDANCE_PROTECTED", "Use the contribution adjustment workflow for verified attendance.");
                if (item.Status is "Present" or "Late" && !session.ManualConfirmationEnabled) return ApiOperations.Error(409, "MANUAL_CONFIRMATION_DISABLED", "This session does not allow manual verification.");
                if (item.Status is "Absent" or "Excused" && session.EndTimeUtc > ActivitySessionEndpoints.Now(http)) return ApiOperations.Error(409, "SESSION_NOT_FINISHED", "Mark absence after the session ends.");
            }
            foreach (var item in request.Items)
            {
                var member = members.Single(x => x.Id == item.MemberId); existing.TryGetValue(member.UserId, out var row);
                row ??= new ActivityAttendance
                {
                    ActivityId = activityId,
                    SessionId = session.Id,
                    UserId = member.UserId,
                    FullName = member.FullName,
                    AttendanceDate = WeeklyAttendanceRules.GetVietnamDate(session.StartTimeUtc),
                    CreatedAtUtc = ActivitySessionEndpoints.Now(http)
                };
                if (row.Id == 0) db.ActivityAttendances.Add(row);
                row.Status = item.Status; row.Note = item.Note?.Trim(); row.Version = Guid.NewGuid(); row.UpdatedAtUtc = ActivitySessionEndpoints.Now(http);
                if (item.Status is "Present" or "Late")
                {
                    row.VerificationStatus = "VERIFIED"; row.VerificationMethod = "MANUAL_REVIEW"; row.RecognitionStatus = "PENDING";
                    row.CheckedInAtUtc = ActivitySessionEndpoints.Now(http); row.CheckedInByUserId = http.User.GetUserId();
                }
                await db.SaveChangesAsync(ct);
                if (row.VerificationStatus == "VERIFIED") db.AddOutboxMessage(new AttendanceVerifiedEvent(Guid.NewGuid(), ActivitySessionEndpoints.Now(http),
                    row.UserId, row.Id, "MANUAL_REVIEW", http.User.GetUserId(), ActivitySessionEndpoints.Source(session)), ExperienceRoutingKeys.AttendanceVerified);
                ActivitySessionEndpoints.Audit(db, session, http, "ATTENDANCE_MANUAL", request.Reason.Trim());
                existing[row.UserId] = row;
            }
            await db.SaveChangesAsync(ct); return Results.Ok(new { items = existing.Values.Select(SessionAttendanceEndpoints.Response) });
        }, ct);
    }
}
