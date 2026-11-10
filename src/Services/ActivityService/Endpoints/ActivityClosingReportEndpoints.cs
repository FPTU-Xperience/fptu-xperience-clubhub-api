using ActivityService.Data;
using ActivityService.Models;
using ActivityService.Services;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityActionHelpers;

namespace ActivityService.Endpoints;

internal static class ActivityClosingReportEndpoints
{
    private const int DefaultReportDays = 3;
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    internal static void MapActivityClosingReportEndpoints(this IEndpointRouteBuilder app)
    {
        var routes = SafeGroup(app, "/api/activities").WithTags("Activity closing reports");
        routes.MapGet("/closing-policy/{clubId:int}", GetPolicy);
        routes.MapPut("/closing-policy/{clubId:int}", SavePolicy);
        routes.MapGet("/{activityId:int}/closing-report", Get);
        routes.MapPost("/{activityId:int}/closing-report", Submit);
        routes.MapPost("/{activityId:int}/closing-report/approve", Approve);
    }

    private static async Task<IResult> GetPolicy(int clubId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var scope = await Authorize(clubId, http, ct);
        Require(scope.Reviewer, 403, "REPORT_POLICY_DENIED", "Current campus review authority is required.");
        var policy = await LatestPolicy(db, scope.Club.CampusCode, ct);
        return Results.Ok(new { scope.Club.CampusCode, reportDays = policy?.ReportDays ?? DefaultReportDays, version = policy?.Id ?? 0 });
    }

    private static async Task<IResult> SavePolicy(int clubId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var scope = await Authorize(clubId, http, ct);
        Require(scope.Reviewer, 403, "REPORT_POLICY_DENIED", "Current campus review authority is required.");
        var input = await Body<PolicyInput>(http, ct);
        Require(input.ReportDays is >= 1 and <= 365, 400, "INVALID_REPORT_DAYS", "Enter 1–365 days.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            var previous = await LatestPolicy(db, scope.Club.CampusCode, ct);
            Require(input.ExpectedVersion == (previous?.Id ?? 0), 409, "STALE_REPORT_POLICY", "Reload the campus report setting.");
            var policy = new ActivityClosingPolicy
            {
                CampusCode = scope.Club.CampusCode, ReportDays = input.ReportDays,
                UpdatedBy = scope.Account.Id, EffectiveAtUtc = Now(http)
            };
            db.Add(policy);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { policy.CampusCode, policy.ReportDays, version = policy.Id });
        }, ct);
    }

    private static async Task<IResult> Get(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var (activity, scope) = await Load(activityId, db, http, ct);
        if (activity.Status != ActivityStatuses.Completed) return Results.Ok(new { available = false });
        var completedAt = await CompletionTime(db, activity, ct);
        var deadline = await Deadline(db, scope.Club.CampusCode, completedAt, ct);
        var report = await db.Set<ActivityClosingReport>().AsNoTracking().SingleOrDefaultAsync(x => x.ActivityId == activityId, ct);
        var registrations = await db.ActivityParticipants.CountAsync(x => x.ActivityId == activityId, ct);
        var verifiedParticipants = await db.ActivityAttendances.Where(x => x.ActivityId == activityId && x.VerificationStatus == "VERIFIED")
            .Select(x => x.UserId).Distinct().CountAsync(ct);
        var verifiedCompletions = await db.Set<ActivityActionCompletion>().CountAsync(x => x.ActivityId == activityId && x.Status == "VERIFIED", ct);
        return Results.Ok(new
        {
            available = true, completedAtUtc = completedAt, deadlineUtc = deadline,
            isOverdue = (report?.SubmittedAtUtc ?? Now(http)) >= deadline,
            canSubmit = scope.Manager && report is null,
            canApprove = scope.Reviewer && report is { Status: "SUBMITTED" }
                && report.SubmittedBy != scope.Account.Id && report.SubmittedAtUtc < deadline,
            report = report is null ? null : Response(report),
            totals = new { registrations, verifiedParticipants, verifiedCompletions }
        });
    }

    private static async Task<IResult> Submit(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var (activity, scope) = await Load(activityId, db, http, ct);
        Manager(scope);
        var input = await Body<ReportInput>(http, ct);
        var summary = input.Summary?.Trim();
        var improvements = input.Improvements?.Trim() ?? "";
        Require(summary is { Length: > 0 and <= 4000 } && improvements.Length <= 4000,
            400, "INVALID_CLOSING_REPORT", "Provide a summary and use at most 4000 characters per field.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(activity).ReloadAsync(ct);
            Require(activity.Status == ActivityStatuses.Completed, 409, "ACTIVITY_NOT_COMPLETED", "Complete the activity before submitting a closing report.");
            Require(!await db.Set<ActivityClosingReport>().AnyAsync(x => x.ActivityId == activityId, ct),
                409, "CLOSING_REPORT_EXISTS", "A closing report already exists for this activity.");
            var report = new ActivityClosingReport
            {
                ActivityId = activityId, Version = Guid.NewGuid(), Summary = summary!, Improvements = improvements,
                SubmittedBy = scope.Account.Id, SubmittedAtUtc = Now(http)
            };
            db.Add(report);
            return Results.Ok(Response(report));
        }, ct);
    }

    private static async Task<IResult> Approve(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var (activity, scope) = await Load(activityId, db, http, ct);
        Require(scope.Reviewer, 403, "REPORT_REVIEW_DENIED", "Current campus review authority is required.");
        var input = await Body<ApproveInput>(http, ct);
        var note = input.Note?.Trim();
        Require((note?.Length ?? 0) <= 2000, 400, "INVALID_REVIEW_NOTE", "Use at most 2000 characters.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(activity).ReloadAsync(ct);
            Require(activity.Status == ActivityStatuses.Completed, 409, "ACTIVITY_NOT_COMPLETED", "The activity must remain completed.");
            var report = await db.Set<ActivityClosingReport>().SingleOrDefaultAsync(x => x.ActivityId == activityId, ct);
            if (report is null) return Results.NotFound();
            Require(report.SubmittedBy != scope.Account.Id, 403, "INDEPENDENT_REVIEW_REQUIRED", "The submitter cannot approve the report.");
            Require(report.Status == "SUBMITTED" && report.Version == input.ExpectedVersion,
                409, "CLOSING_REPORT_LOCKED", "The report was already approved or its version changed.");
            var completedAt = await CompletionTime(db, activity, ct);
            var deadline = await Deadline(db, scope.Club.CampusCode, completedAt, ct);
            Require(report.SubmittedAtUtc < deadline, 409, "CLOSING_REPORT_SUBMITTED_LATE",
                "Only a report submitted before the deadline can be approved.");
            report.Status = "APPROVED";
            report.Version = Guid.NewGuid();
            report.ReviewedBy = scope.Account.Id;
            report.ReviewedAtUtc = Now(http);
            report.ReviewNote = note;
            await ClosingReportXpGate.ReleaseAsync(activity,
                report.ReviewedAtUtc.Value < deadline ? report.ReviewedAtUtc.Value : deadline, db,
                http.RequestServices.GetRequiredService<ClubExperienceAuthorityClient>(), ct);
            await ActivityCertificateEndpoints.IssueAsync(activity, report.ReviewedAtUtc.Value, db, ct);
            return Results.Ok(Response(report));
        }, ct);
    }

    internal static async Task<DateTimeOffset> CompletionTime(ActivityDbContext db, ClubActivity activity, CancellationToken ct)
    {
        var completion = await db.SessionAudits.AsNoTracking()
            .Where(x => x.ActivityId == activity.Id && x.SessionId == null && (x.Action == "COMPLETE" || x.Action == "AUTO_COMPLETE"))
            .OrderByDescending(x => x.Id).Select(x => (DateTimeOffset?)x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        return completion ?? activity.UpdatedAtUtc;
    }

    internal static async Task<DateTimeOffset> Deadline(ActivityDbContext db, string campusCode, DateTimeOffset completedAt, CancellationToken ct)
    {
        var history = await db.Set<ActivityClosingPolicy>().AsNoTracking().Where(x => x.CampusCode == campusCode)
            .OrderByDescending(x => x.Id).ToListAsync(ct);
        var days = history.FirstOrDefault(x => x.EffectiveAtUtc <= completedAt)?.ReportDays ?? DefaultReportDays;
        var localMidnight = completedAt.ToOffset(VietnamOffset).Date.AddDays(1 + days);
        return new DateTimeOffset(localMidnight, VietnamOffset).ToUniversalTime();
    }

    private static Task<ActivityClosingPolicy?> LatestPolicy(ActivityDbContext db, string campusCode, CancellationToken ct) =>
        db.Set<ActivityClosingPolicy>().AsNoTracking().Where(x => x.CampusCode == campusCode)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);

    private static async Task<(ClubActivity Activity, Scope Scope)> Load(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var activity = await db.Activities.SingleOrDefaultAsync(x => x.Id == activityId, ct);
        Require(activity is not null, 404, "ACTIVITY_NOT_FOUND", "Activity does not exist.");
        var scope = await Authorize(activity!.ClubId, http, ct);
        Require(scope.Manager || scope.Reviewer, 403, "CLOSING_REPORT_ACCESS_DENIED", "Current club management or review authority is required.");
        return (activity, scope);
    }

    private static object Response(ActivityClosingReport report) =>
        new { report.Version, report.Status, report.Summary, report.Improvements, report.SubmittedAtUtc, report.ReviewedAtUtc, report.ReviewNote };

    private sealed record PolicyInput(int ReportDays, long ExpectedVersion);
    private sealed record ReportInput(string? Summary, string? Improvements);
    private sealed record ApproveInput(Guid ExpectedVersion, string? Note);
}
