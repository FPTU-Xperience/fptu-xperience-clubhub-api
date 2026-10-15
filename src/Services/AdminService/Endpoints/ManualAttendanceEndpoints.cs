using AdminService.Contracts;
using AdminService.Data;
using AdminService.Security;
using AdminService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using static AdminService.Services.VerifiedExperienceService;

namespace AdminService.Endpoints;

public static class ManualAttendanceEndpoints
{
    public static void MapManualAttendanceEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/activities/{activityId:int}/sessions/{sessionId:int}/manual-attendance-requests")
            .RequireAuthorization(AdminPolicies.AnyActor).WithTags("Manual attendance verification");
        api.MapPost("/", SubmitAsync);
        api.MapGet("/", ListAsync);
        api.MapPost("/{requestId:guid}/reviews", ReviewAsync);
        api.MapPost("/{requestId:guid}/review", ReviewAsync);
    }

    private static async Task<IResult> SubmitAsync(int activityId, int sessionId, SubmitManualAttendanceRequest request,
        AdminDbContext db, ActivitySourceClient sources, HttpContext http, CancellationToken ct)
    {
        var actor = http.User.GetUserId();
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000
            || !await OwnsEvidenceAsync(db, actor, request.EvidenceRefs ?? [], ct))
            return ApiOperations.Error(400, "INVALID_MANUAL_REQUEST", "Provide a reason and owned evidence.");
        ActivitySourceContext? context;
        try { context = await sources.GetAsync(activityId, sessionId, actor, ct); }
        catch (HttpRequestException) { return ApiOperations.Error(503, "ACTIVITY_SERVICE_UNAVAILABLE", "Activity source is unavailable."); }
        if (context is null) return Results.NotFound();
        if (!context.IsRegistered) return Results.Forbid();
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            if (!context.Source.ManualConfirmationEnabled || context.Source.Status == "CANCELLED"
                || context.Source.StartTimeUtc > DateTimeOffset.UtcNow || context.AlreadyVerified)
                return ApiOperations.Error(409, "MANUAL_CONFIRMATION_UNAVAILABLE", "Manual confirmation is disabled, not open, or attendance is already verified.");
            if (await db.ManualAttendanceRequests.AnyAsync(x => x.SessionId == sessionId && x.UserId == actor && x.Status == "PENDING", ct))
                return ApiOperations.Error(409, "MANUAL_REQUEST_PENDING", "A manual request is already pending.");
            var row = new ManualAttendanceRequest
            {
                UserId = actor,
                ClubId = context.Source.ClubId,
                ActivityId = activityId,
                SessionId = sessionId,
                Reason = request.Reason.Trim(),
                EvidenceRefsJson = Json(request.EvidenceRefs ?? []),
                SourceSnapshotJson = Json(context.Source),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.ManualAttendanceRequests.Add(row);
            ExperienceAuthorization.Audit(db, http.User, "MANUAL_ATTENDANCE_REQUESTED", "ManualAttendance", row.Id.ToString(), new { activityId, sessionId });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/activities/{activityId}/sessions/{sessionId}/manual-attendance-requests/{row.Id}", row);
        }, ct);
    }

    private static async Task<IResult> ListAsync(int activityId, int sessionId, int? page, int? pageSize, AdminDbContext db,
        ActivitySourceClient sources, HttpContext http, CancellationToken ct)
    {
        ActivitySourceContext? context;
        try { context = await sources.GetAsync(activityId, sessionId, http.User.GetUserId(), ct); }
        catch (HttpRequestException) { return ApiOperations.Error(503, "ACTIVITY_SERVICE_UNAVAILABLE", "Activity source is unavailable."); }
        if (context is null) return Results.NotFound();
        var assigned = await ExperienceAuthorization.FindAsync(db, http.User.GetUserId(), "CLUB", context.Source.ClubId, "attendance.verify", ct);
        var query = db.ManualAttendanceRequests.AsNoTracking().Where(x => x.ActivityId == activityId && x.SessionId == sessionId);
        if (assigned is null) query = query.Where(x => x.UserId == http.User.GetUserId());
        var current = Math.Max(page ?? 1, 1); var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((int)Math.Min((long)(current - 1) * size, int.MaxValue)).Take(size).ToListAsync(ct);
        return Results.Ok(new { items = rows, total, page = current, pageSize = size, totalPages = (int)Math.Ceiling((decimal)total / size) });
    }

    private static async Task<IResult> ReviewAsync(int activityId, int sessionId, Guid requestId, ReviewContributionRequest request,
        AdminDbContext db, ActivitySourceClient sources, HttpContext http, CancellationToken ct)
    {
        var row = await db.ManualAttendanceRequests.SingleOrDefaultAsync(x => x.Id == requestId && x.ActivityId == activityId && x.SessionId == sessionId, ct);
        if (row is null) return Results.NotFound();
        var actor = http.User.GetUserId();
        if (actor == row.UserId) return ApiOperations.Error(403, "SELF_REVIEW_FORBIDDEN", "You cannot confirm your own attendance.");
        if (await ExperienceAuthorization.FindAsync(db, actor, "CLUB", row.ClubId, "attendance.verify", ct) is null) return Results.Forbid();
        if (request.Decision is not ("APPROVE" or "REJECT" or "REQUEST_REVISION") || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > 1000 || request.VerifiedAttributes is not null)
            return ApiOperations.Error(400, "INVALID_MANUAL_REVIEW", "Provide APPROVE/REJECT and a reason; manual attendance awards the published participant attributes.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            var grant = await ExperienceAuthorization.FindAsync(db, actor, "CLUB", row.ClubId, "attendance.verify", ct);
            if (grant is null) return Results.Forbid();
            if (row.Version != request.ExpectedVersion || row.Status != "PENDING") return ApiOperations.Error(409, "STALE_MANUAL_REQUEST", "Reload the request.");
            if (request.Decision == "APPROVE")
            {
                ActivitySourceContext? live;
                try { live = await sources.GetAsync(activityId, sessionId, row.UserId, ct); }
                catch (HttpRequestException) { return ApiOperations.Error(503, "ACTIVITY_SERVICE_UNAVAILABLE", "Activity source is unavailable."); }
                var snapshot = Read<ExperienceSourceSnapshot>(row.SourceSnapshotJson);
                if (live is null || !live.IsRegistered || live.AlreadyVerified || live.Source.Status == "CANCELLED"
                    || !live.Source.ManualConfirmationEnabled || live.Source.SessionVersion != snapshot.SessionVersion)
                    return ApiOperations.Error(409, "STALE_ACTIVITY_SOURCE", "The session or eligibility changed. Submit a new request.");
                row.Status = "APPROVED_PENDING"; row.RecognitionStatus = "PENDING";
                db.AddOutboxMessage(new ManualAttendanceDecisionEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, row.Id,
                    row.UserId, actor, request.Reason.Trim(), live.Source), ExperienceRoutingKeys.ManualAttendanceDecision);
            }
            else row.Status = request.Decision == "REJECT" ? "REJECTED" : "REVISION_REQUESTED";
            grant.Version = Guid.NewGuid(); row.Version = Guid.NewGuid(); row.ReviewedBy = actor;
            row.ReviewReason = request.Reason.Trim(); row.ReviewedAt = DateTimeOffset.UtcNow;
            ExperienceAuthorization.Audit(db, http.User, "MANUAL_ATTENDANCE_REVIEWED", "ManualAttendance", row.Id.ToString(), new { request.Decision, assignmentId = grant.Id });
            await db.SaveChangesAsync(ct); return Results.Ok(row);
        }, ct);
    }
}
