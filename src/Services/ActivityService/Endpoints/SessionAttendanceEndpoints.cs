using System.Text.Json;
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

internal static class SessionAttendanceEndpoints
{
    internal static object Response(ActivityAttendance row) => new
    {
        attendanceId = row.Id,
        activityId = row.ActivityId,
        sessionId = row.SessionId,
        status = row.VerificationStatus,
        attendanceStatus = row.Status,
        verificationMethod = row.VerificationMethod,
        verifiedAt = row.CheckedInAtUtc,
        contributionId = row.ContributionId,
        recognitionStatus = row.RecognitionStatus,
        version = row.Version
    };

    internal static async Task<IResult> CheckInAsync(int activityId, SessionCheckInRequest request, ActivityDbContext db,
        HttpContext http, ClubMemberRosterClient roster, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (request.SessionId <= 0 || string.IsNullOrWhiteSpace(request.Challenge))
            return ApiOperations.Error(400, "QR_REQUIRED", "Provide sessionId and its attendance challenge.");
        var session = await db.Sessions.Include(x => x.Activity).SingleOrDefaultAsync(
            x => x.Id == request.SessionId && x.ActivityId == activityId, cancellationToken);
        if (session is null) return Results.NotFound();
        var actor = http.User.GetUserId();
        MyClubMembership? member;
        try
        {
            if (session.Activity.Eligibility == "CAMPUS_PUBLIC")
            {
                var account = await ActivityEligibility.ResolveAsync(session.Activity, actor, session.StartTimeUtc,
                    http.RequestServices.GetRequiredService<CurrentUserProfileClient>(), http.RequestServices.GetRequiredService<ClubExperienceAuthorityClient>(), cancellationToken);
                member = account is null ? null : new MyClubMembership(0, session.Activity.ClubId, actor, account.DisplayName, "Approved", DateTimeOffset.MinValue, DateTimeOffset.MinValue);
            }
            else member = await roster.GetMyApprovedMembershipAsync(session.Activity.ClubId, actor, http.GetBearerToken(), cancellationToken);
        }
        catch (HttpRequestException) { return ApiOperations.Error(503, "MEMBERSHIP_SERVICE_UNAVAILABLE", "Membership service is unavailable."); }
        if (member is null || (member.ReviewedAtUtc ?? member.RequestedAtUtc) > session.StartTimeUtc) return Results.Forbid();
        if (!await db.ActivityParticipants.AnyAsync(x => x.ActivityId == activityId && x.UserId == actor, cancellationToken))
            return ApiOperations.Error(409, "REGISTRATION_REQUIRED", "Register before attending this activity.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            // Serializable source reads prevent cancellation/closure from racing
            // the verified attendance transaction without rotating a shared QR.
            await db.Entry(session).ReloadAsync(cancellationToken);
            await db.Entry(session.Activity).ReloadAsync(cancellationToken);
            var now = ActivitySessionEndpoints.Now(http);
            if (session.Status != "SCHEDULED" || session.Activity.Status != ActivityStatuses.Scheduled
                || session.Activity.PublicationStatus is not ("PUBLISHED" or "CLOSED") || now < session.OpensAt || now >= session.ClosesAt)
                return ApiOperations.Error(409, "ATTENDANCE_CLOSED", "The attendance window is closed.");
            if (!AttendanceChallenge.Configured(configuration)) return ApiOperations.Error(503, "ATTENDANCE_UNCONFIGURED", "Attendance signing key is unavailable.");
            if (!AttendanceChallenge.Validate(request.Challenge, session, now, configuration))
                return ApiOperations.Error(400, "INVALID_ATTENDANCE_CHALLENGE", "The QR is invalid, expired or belongs to another session.");
            if (session.Mode == "ONSITE" && !AttendanceChallenge.InsideGeofence(session, request.Location, configuration))
                return ApiOperations.Error(403, "LOCATION_OUTSIDE_POLICY", "Provide an accurate location inside the geofence or request manual confirmation.");
            var existing = await db.ActivityAttendances.SingleOrDefaultAsync(x => x.SessionId == session.Id && x.UserId == actor, cancellationToken);
            if (existing is not null && existing.VerificationStatus == "VERIFIED") return Results.Ok(Response(existing));
            if (existing is not null && existing.VerificationStatus == "REVOKED") return ApiOperations.Error(409, "ATTENDANCE_REVOKED", "A revoked attendance cannot be restored by check-in.");
            var row = existing ?? new ActivityAttendance
            {
                ActivityId = activityId,
                SessionId = session.Id,
                UserId = actor,
                FullName = member.FullName,
                AttendanceDate = WeeklyAttendanceRules.GetVietnamDate(session.StartTimeUtc),
                CreatedAtUtc = now
            };
            row.Status = AttendanceStatuses.Present; row.VerificationStatus = "VERIFIED"; row.VerificationMethod = "ROTATING_QR";
            row.RecognitionStatus = "PENDING"; row.CheckedInAtUtc = now; row.CheckedInByUserId = actor; row.Version = Guid.NewGuid();
            row.Note = null;
            if (existing is null) db.ActivityAttendances.Add(row);
            await db.SaveChangesAsync(cancellationToken);
            db.AddOutboxMessage(new AttendanceVerifiedEvent(Guid.NewGuid(), now, actor, row.Id,
                row.VerificationMethod, null, ActivitySessionEndpoints.Source(session)), ExperienceRoutingKeys.AttendanceVerified);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(Response(row));
        }, cancellationToken);
    }
}
