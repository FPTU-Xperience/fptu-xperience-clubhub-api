using System.Security.Claims;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Infrastructure;
using ActivityService.Models;
using ActivityService.Services;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityEndpointHelpers;

namespace ActivityService.Endpoints;

internal static class ActivityWriteEndpoints
{
    internal static void MapActivityWriteEndpoints(this RouteGroupBuilder activities)
    {
        activities.MapPut("/{id:int}", UpdateActivity);
        activities.MapDelete("/{id:int}", CancelActivity);
        activities.MapPost("/{id:int}/participants", RegisterParticipant);
        activities.MapPost("/{id:int}/check-in", CheckIn);
        activities.MapGet("/{id:int}/my-attendance", GetMyAttendance);
        activities.MapMethods("/{id:int}/complete", ["PATCH"], CompleteActivity);
    }

    private static async Task<IResult> CancelActivity(
        int id,
        ActivityDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var activity = await db.Activities.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (activity is null) return Results.NotFound();
        if (!await CanManageClubOrReviewAllAsync(activity.ClubId, user, clubAccess,
                httpContext, cancellationToken, bypassCache: true))
            return Results.Forbid();
        if (activity.Status == ActivityStatuses.Cancelled)
            return Results.Ok(new { success = true });
        if (activity.Status != ActivityStatuses.Scheduled)
            return Results.Conflict(new { message = "Only a scheduled activity can be cancelled." });

        var now = httpContext.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var changed = await db.Activities
            .Where(x => x.Id == id && x.Status == ActivityStatuses.Scheduled
                && x.UpdatedAtUtc == activity.UpdatedAtUtc)
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.Status, ActivityStatuses.Cancelled)
                .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);
        if (changed == 0)
            return Results.Conflict(new { message = "Activity changed while it was being cancelled." });

        loggerFactory.CreateLogger("ActivityService.ActivityManagement")
            .LogInformation("Activity {ActivityId} cancelled by user {ActorId}", id, user.GetUserId());
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> UpdateActivity(
        int id,
        UpdateActivityDetailsRequest request,
        ActivityDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        CancellationToken cancellationToken)
    {
        var activity = await db.Activities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (activity is null) return Results.NotFound();
        if (!await CanManageClubOrReviewAllAsync(activity.ClubId, user, clubAccess, httpContext, cancellationToken, bypassCache: true))
            return Results.Forbid();
        if (activity.Status != ActivityStatuses.Scheduled || activity.SourceReportId.HasValue)
            return Results.Conflict(new { message = "Only scheduled standalone activities can be edited here." });

        if (request.Title is not null && request.Name is not null
            && !string.Equals(request.Title.Trim(), request.Name.Trim(), StringComparison.Ordinal))
            return Results.BadRequest(new { message = "Title and name must agree when both are provided." });
        if (request.StartTimeUtc.HasValue && request.StartAt.HasValue && request.StartTimeUtc != request.StartAt)
            return Results.BadRequest(new { message = "StartTimeUtc and startAt must agree when both are provided." });
        if (request.EndTimeUtc.HasValue && request.EndAt.HasValue && request.EndTimeUtc != request.EndAt)
            return Results.BadRequest(new { message = "EndTimeUtc and endAt must agree when both are provided." });

        var title = (request.Title ?? request.Name)?.Trim() ?? activity.Title;
        var description = request.Description?.Trim() ?? activity.Description;
        var location = request.Location?.Trim() ?? activity.Location;
        var start = request.StartTimeUtc ?? request.StartAt ?? activity.StartTimeUtc;
        var end = request.EndTimeUtc ?? request.EndAt ?? activity.EndTimeUtc;
        if (request.Title is null && request.Name is null && request.Description is null
            && request.Location is null && request.StartTimeUtc is null && request.StartAt is null
            && request.EndTimeUtc is null && request.EndAt is null && request.MeetingDays is null)
            return Results.BadRequest(new { message = "Provide at least one activity field to update." });
        if (title.Length is 0 or > 200 || description.Length is 0 or > 2000
            || location.Length is 0 or > 200 || end <= start
            || (request.MeetingDays is not null && request.MeetingDays.Any(day => day is < 1 or > 7)))
            return Results.BadRequest(new { message = "Activity content, schedule or meeting days are invalid." });

        var meetingDays = request.MeetingDays is null
            ? activity.MeetingDaysCsv
            : string.Join(',', NormalizeMeetingDays(request.MeetingDays));
        var now = httpContext.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var changed = await db.Activities
            .Where(x => x.Id == id && x.Status == ActivityStatuses.Scheduled
                && x.SourceReportId == null && x.UpdatedAtUtc == activity.UpdatedAtUtc)
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.Title, title)
                .SetProperty(x => x.Description, description)
                .SetProperty(x => x.Location, location)
                .SetProperty(x => x.StartTimeUtc, start)
                .SetProperty(x => x.EndTimeUtc, end)
                .SetProperty(x => x.MeetingDaysCsv, meetingDays)
                .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);
        if (changed == 0) return Results.Conflict(new { message = "Activity changed while it was being edited." });

        var updated = await db.Activities.AsNoTracking()
            .Include(x => x.Participants).Include(x => x.Attendances).AsSplitQuery()
            .SingleAsync(x => x.Id == id, cancellationToken);
        return Results.Ok(ToResponse(updated));
    }

    private static async Task<IResult> RegisterParticipant(
        int id,
        RegisterActivityParticipantRequest request,
        ActivityDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        [FromServices] ClubMemberRosterClient roster,
        CancellationToken cancellationToken)
    {
        var actorId = user.GetUserId();
        if (request.FullName is not null || request.UserId is <= 0)
            return Results.BadRequest(new { message = "Participant name comes from the club roster; userId must be positive." });
        var targetUserId = request.UserId ?? actorId;
        var activity = await db.Activities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (activity is null) return Results.NotFound();
        if (activity.Status != ActivityStatuses.Scheduled)
            return Results.Conflict(new { message = "Registration is closed for this activity." });

        var bearerToken = httpContext.GetBearerToken();
        string fullName;
        DateTimeOffset joinedAt;
        try
        {
            if (targetUserId == actorId)
            {
                var member = await roster.GetMyApprovedMembershipAsync(activity.ClubId, actorId, bearerToken, cancellationToken);
                if (member is null) return Results.Forbid();
                fullName = member.FullName;
                joinedAt = member.ReviewedAtUtc ?? member.RequestedAtUtc;
            }
            else
            {
                if (!await CanManageClubAsync(activity.ClubId, clubAccess, httpContext, cancellationToken, bypassCache: true))
                    return Results.Forbid();
                var members = await roster.ResolveByUserIdsAsync(activity.ClubId, [targetUserId], bearerToken, cancellationToken);
                var member = members.SingleOrDefault(x => x.UserId == targetUserId && x.Status == "Approved");
                if (member is null) return Results.Forbid();
                fullName = member.FullName;
                joinedAt = member.JoinedAtUtc;
            }
        }
        catch (HttpRequestException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 200 || joinedAt > activity.StartTimeUtc)
            return Results.Forbid();
        var existing = await db.ActivityParticipants.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ActivityId == id && x.UserId == targetUserId, cancellationToken);
        if (existing is not null) return Results.Ok(new { success = true, participant = ToParticipantResponse(existing) });

        var row = new ActivityParticipant { ActivityId = id, UserId = targetUserId, FullName = fullName };
        db.ActivityParticipants.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            existing = await db.ActivityParticipants.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ActivityId == id && x.UserId == targetUserId, cancellationToken);
            if (existing is null) throw;
            return Results.Ok(new { success = true, participant = ToParticipantResponse(existing) });
        }
        return Results.Ok(new { success = true, participant = ToParticipantResponse(row) });
    }

    private static async Task<IResult> CheckIn(
        int id,
        ActivityDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        [FromServices] ClubMemberRosterClient roster,
        CancellationToken cancellationToken)
    {
        if (httpContext.Request.ContentLength is > 0 || httpContext.Request.Headers.ContainsKey("Transfer-Encoding"))
            return Results.BadRequest(new { message = "Check-in does not accept client-supplied identity, time or date." });
        var actorId = user.GetUserId();
        var activity = await db.Activities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (activity is null) return Results.NotFound();
        var now = httpContext.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        if (activity.Status != ActivityStatuses.Scheduled || now < activity.StartTimeUtc || now > activity.EndTimeUtc)
            return Results.Conflict(new { message = "Check-in is not open for this activity." });
        MyClubMembership? member;
        try
        {
            member = await roster.GetMyApprovedMembershipAsync(activity.ClubId, actorId, httpContext.GetBearerToken(), cancellationToken);
        }
        catch (HttpRequestException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        if (member is null || string.IsNullOrWhiteSpace(member.FullName)
            || (member.ReviewedAtUtc ?? member.RequestedAtUtc) > activity.StartTimeUtc)
            return Results.Forbid();

        var vietnamDate = WeeklyAttendanceRules.GetVietnamDate(now);
        var meetingDays = GetMeetingDays(activity.MeetingDaysCsv);
        if (meetingDays.Count > 0 && !WeeklyAttendanceRules.IsScheduledDay(meetingDays, vietnamDate))
            return Results.Conflict(new { message = "This is not a scheduled check-in day in Vietnam." });
        var existing = await db.ActivityAttendances.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ActivityId == id && x.UserId == actorId && x.AttendanceDate == vietnamDate, cancellationToken);
        if (existing is not null) return Results.Ok(new { success = true, attendance = ToAttendanceResponse(existing) });

        var row = new ActivityAttendance
        {
            ActivityId = id,
            UserId = actorId,
            FullName = member.FullName,
            AttendanceDate = vietnamDate,
            Status = AttendanceStatuses.Present,
            CheckedInAtUtc = now,
            CheckedInByUserId = actorId
        };
        db.ActivityAttendances.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            existing = await db.ActivityAttendances.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ActivityId == id && x.UserId == actorId && x.AttendanceDate == vietnamDate, cancellationToken);
            if (existing is null) throw;
            return Results.Ok(new { success = true, attendance = ToAttendanceResponse(existing) });
        }
        return Results.Ok(new { success = true, attendance = ToAttendanceResponse(row) });
    }

    private static async Task<IResult> GetMyAttendance(
        int id,
        int? page,
        int? pageSize,
        ActivityDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await db.Activities.AnyAsync(x => x.Id == id, cancellationToken)) return Results.NotFound();
        var actorId = user.GetUserId();
        var actualPage = Math.Max(page ?? 1, 1);
        var actualPageSize = Math.Clamp(pageSize ?? 20, 1, 100);
        var skip = (int)Math.Min((long)(actualPage - 1) * actualPageSize, int.MaxValue);
        var query = db.ActivityAttendances.AsNoTracking().Where(x => x.ActivityId == id && x.UserId == actorId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.AttendanceDate).ThenByDescending(x => x.Id)
            .Skip(skip).Take(actualPageSize).ToListAsync(cancellationToken);
        return Results.Ok(new
        {
            activityId = id,
            userId = actorId,
            total,
            page = actualPage,
            pageSize = actualPageSize,
            items = rows.Select(ToAttendanceResponse).ToArray()
        });
    }

    private static async Task<IResult> CompleteActivity(
        int id,
        ActivityDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        CancellationToken cancellationToken)
    {
        var activity = await db.Activities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (activity is null) return Results.NotFound();
        if (!await CanManageClubOrReviewAllAsync(activity.ClubId, user, clubAccess, httpContext, cancellationToken, bypassCache: true))
            return Results.Forbid();
        if (activity.Status == ActivityStatuses.Completed)
            return Results.Ok(new { success = true, status = ActivityStatuses.Completed });
        var now = httpContext.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        if (activity.Status != ActivityStatuses.Scheduled || now < activity.EndTimeUtc)
            return Results.Conflict(new { message = "Only a finished scheduled activity can be completed." });
        var changed = await db.Activities.Where(x => x.Id == id && x.Status == ActivityStatuses.Scheduled)
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.Status, ActivityStatuses.Completed)
                .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);
        if (changed == 0) return Results.Conflict(new { message = "Activity state changed during completion." });
        return Results.Ok(new { success = true, status = ActivityStatuses.Completed });
    }

    private static ActivityParticipantResponse ToParticipantResponse(ActivityParticipant row) =>
        new(row.Id, row.UserId, row.FullName, row.AttendanceStatus, row.RegisteredAtUtc);

    private static ActivityAttendanceResponse ToAttendanceResponse(ActivityAttendance row) =>
        new(row.Id, row.UserId, row.FullName, row.AttendanceDate, row.Status, row.Note,
            row.CheckedInAtUtc, row.CheckedInByUserId);
}
