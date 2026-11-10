using AdminService.Data;
using AdminService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using static AdminService.Services.VerifiedExperienceService;

namespace AdminService.Endpoints;

internal static class ClosingDutyXpEndpoints
{
    internal static void MapClosingDutyXpEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/closing-duties", List);
        group.MapPost("/closing-duties/{id:guid}/review", Review);
    }

    private static async Task<IResult> List(int activityId, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        var scope = await Scope(activityId, http, ct);
        if (scope is null) return Results.Forbid();
        var rows = await db.Contributions.AsNoTracking().Where(x => x.ActivityId == activityId && x.SourceType == "CLOSING_DUTY")
            .OrderBy(x => x.UserId).ToListAsync(ct);
        return Results.Ok(rows.Select(x => new { x.Id, x.UserId, role = x.SourceId, x.Status, x.Version, x.RawPoints }));
    }

    private static async Task<IResult> Review(int activityId, Guid id, DutyReview input, AdminDbContext db,
        HttpContext http, IConfiguration config, CancellationToken ct)
    {
        var scope = await Scope(activityId, http, ct);
        if (scope is null) return Results.Forbid();
        if (!scope.ClosingReportReady)
            return ApiOperations.Error(409, "CLOSING_REPORT_REQUIRED", "Approve or finalize the report before recording XP.");
        var reason = input.Reason?.Trim();
        if (input.Decision is not ("APPROVE" or "REJECT") || reason is not { Length: > 0 and <= 1000 }
            || input.Decision == "APPROVE" && !ContributionRoles.All.Contains(input.Role))
            return ApiOperations.Error(400, "INVALID_DUTY_REVIEW", "Choose a decision, valid XP role, and reason.");

        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            var row = await db.Contributions.SingleOrDefaultAsync(x => x.Id == id && x.ActivityId == activityId && x.SourceType == "CLOSING_DUTY", ct);
            if (row is null) return Results.NotFound();
            var actor = http.User.GetUserId();
            if (row.UserId == actor) return Results.Forbid();
            if (row.Status != "SUBMITTED" || row.Version != input.ExpectedVersion)
                return ApiOperations.Error(409, "STALE_DUTY_XP", "Reload this XP review.");
            if (await db.InvalidatedExperienceSources.AnyAsync(x => x.ActivityId == activityId && x.SessionId == null, ct))
                return ApiOperations.Error(409, "SOURCE_INVALIDATED", "This activity is no longer eligible.");
            var before = row.CurrentRevisionId;
            var source = Read<ExperienceSourceSnapshot>(row.SourceSnapshotJson);
            var approve = input.Decision == "APPROVE";
            var attributes = approve ? source.Attributes with { Role = input.Role! } : source.Attributes;
            var policy = approve ? await PolicyAsync(db, source.Policy.PolicyVersion, source.SemesterId,
                config["Experience:Environment"] ?? "PROJECT", ct) : null;
            if (approve && policy is null)
                return ApiOperations.Error(409, "SCORE_POLICY_UNAPPROVED", "The activity policy is no longer approved.");
            row.Status = approve ? "APPROVED" : "REJECTED";
            var revision = Revision(db, row, row.Status, attributes, actor, reason);
            var decision = new ContributionDecision
            {
                ContributionId = row.Id, TargetRevisionId = before, RevisionId = revision.Id,
                ReviewerUserId = actor, Decision = input.Decision, Reason = reason, CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.ContributionDecisions.Add(decision);
            if (approve)
                await AppendEffectAsync(db, row, revision, null, attributes, policy!, "AWARD", decision.Id, actor,
                    "CLOSING_DUTY_REVIEW", reason, null, ct);
            Notify(db, row, "REVIEW_RESULT", "Kết quả xét XP nhiệm vụ hoạt động");
            ExperienceAuthorization.Audit(db, http.User, "CLOSING_DUTY_XP_REVIEWED", "Contribution", row.Id.ToString(),
                new { activityId, input.Decision, decision.Id });
            return Results.Ok(new { row.Id, row.Status, row.Version, row.RawPoints });
        }, ct);
    }

    private static async Task<ActivityExperienceScope?> Scope(int activityId, HttpContext http, CancellationToken ct)
    {
        if (!http.User.IsInRole(AuthRoles.StudentAffairsAdmin) && !http.User.IsInRole(AuthRoles.Admin)) return null;
        var actor = http.User.GetUserId();
        var services = http.RequestServices;
        var user = (await services.GetRequiredService<CurrentUserProfileClient>().GetAsync([actor], ct)).GetValueOrDefault(actor);
        if (user is not { IsActive: true, IsLocked: false } || string.IsNullOrWhiteSpace(user.CampusCode)) return null;
        var scope = await services.GetRequiredService<ActivitySourceClient>().GetScopeAsync(activityId, ct);
        if (scope is null) return null;
        var campus = scope.OrganizerCampusCode;
        if (campus is null && scope.ClubId > 0)
            campus = (await services.GetRequiredService<ClubExperienceAuthorityClient>().GetAsync(scope.ClubId, actor, ct))?.CampusCode;
        return campus == user.CampusCode ? scope : null;
    }

    private sealed record DutyReview(Guid ExpectedVersion, string Decision, string? Role, string? Reason);
}
