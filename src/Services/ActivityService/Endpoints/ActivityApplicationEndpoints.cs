using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityActionHelpers;

namespace ActivityService.Endpoints;

public static class ActivityApplicationEndpoints
{
    public static void MapActivityApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = SafeGroup(app, "/api/activity-applications").WithTags("Activity applications");
        group.MapPost("", Create);
        group.MapGet("", List);
        group.MapGet("/{id:int}", Get);
        group.MapPatch("/{id:int}", Update);
        group.MapPost("/{id:int}/submit", Submit);
        group.MapPost("/{id:int}/review", Review);
        group.MapGet("/{id:int}/history", GetHistory);
    }
    private static async Task<IResult> Create(HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var input = await Body<ActivityApplicationInput>(http, ct);
        var scope = await Authorize(input.ClubId, http, ct); Manager(scope);
        Require(input.ExpectedVersion is null, 400, "INVALID_VERSION", "A new draft has no expectedVersion.");
        var content = Normalize(input); Validate(content, false, http);
        var semester = await Semester(content, http, ct);
        content = content with { Period = semester.SemesterCode };
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            var row = new ActivityApplication
            {
                ClubId = input.ClubId,
                CampusCode = scope.Club.CampusCode,
                SemesterId = semester.SemesterCode,
                ContentJson = JsonSerializer.Serialize(content, Json),
                CreatedBy = http.User.GetUserId(),
                CreatedAt = Now(http),
                UpdatedAt = Now(http)
            };
            db.Add(row); await db.SaveChangesAsync(ct); History(db, row, http);
            return Results.Json(await ApplicationResponse(db, row, scope, ct), statusCode: 201);
        }, ct);
    }
    private static async Task<IResult> Get(int id, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var row = await Find(id, db, ct); var scope = await ReadScope(row, http, ct);
        return Results.Ok(await ApplicationResponse(db, row, scope, ct));
    }
    private static async Task<IResult> List(int clubId, string? status, int? page, int? pageSize, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var scope = await Authorize(clubId, http, ct);
        Require(scope.Manager || scope.Reviewer, 403, "APPLICATION_ACCESS_DENIED", "Application access is required.");
        var index = page ?? 1; var size = pageSize ?? 20;
        Require(index is >= 1 and <= 100000 && size is >= 1 and <= 100, 400, "INVALID_PAGINATION", "Use page >= 1 and pageSize 1–100.");
        Require(status is null or "DRAFT" or "REVISION_REQUESTED" or "SUBMITTED" or "APPROVED" or "REJECTED", 400, "INVALID_STATUS", "Use a canonical status.");
        var query = db.Set<ActivityApplication>().AsNoTracking().Where(x => x.ClubId == clubId);
        if (status is not null) query = query.Where(x => x.Status == status);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Id).Skip((index - 1) * size).Take(size).ToArrayAsync(ct);
        var items = new List<object>(); foreach (var row in rows) items.Add(await ApplicationResponse(db, row, scope, ct));
        return Results.Ok(new { items, page = index, pageSize = size, totalItems = total, totalPages = (int)Math.Ceiling(total / (double)size) });
    }
    private static async Task<IResult> Update(int id, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var row = await Find(id, db, ct); var scope = await ReadScope(row, http, ct); Manager(scope);
        var input = await Body<ActivityApplicationInput>(http, ct);
        Require(input.ClubId == row.ClubId, 400, "IMMUTABLE_CLUB", "The application club cannot change.");
        var content = Normalize(input); Validate(content, false, http);
        var semester = await Semester(content, http, ct); content = content with { Period = semester.SemesterCode };
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            Expected(input.ExpectedVersion ?? 0, row.Version);
            Require(row.Status is "DRAFT" or "REVISION_REQUESTED", 409, "APPLICATION_LOCKED", "Only an editable draft can change.");
            row.ContentJson = JsonSerializer.Serialize(content, Json); row.SemesterId = semester.SemesterCode;
            row.Version++; row.UpdatedAt = Now(http); History(db, row, http);
            return Results.Ok(await ApplicationResponse(db, row, scope, ct));
        }, ct);
    }
    private static async Task<IResult> Submit(int id, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var row = await Find(id, db, ct); var scope = await ReadScope(row, http, ct); Manager(scope);
        var input = await Body<ActionVersionInput>(http, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            Expected(input.ExpectedVersion, row.Version);
            Require(row.Status is "DRAFT" or "REVISION_REQUESTED", 409, "APPLICATION_LOCKED", "Only an editable draft can be submitted.");
            var content = JsonSerializer.Deserialize<ActivityApplicationInput>(row.ContentJson, Json)!;
            Validate(content, true, http); await Semester(content, http, ct);
            if (row.Status == "REVISION_REQUESTED") row.Revision++;
            row.Status = "SUBMITTED"; row.Version++; row.UpdatedAt = Now(http); History(db, row, http);
            return Results.Ok(await ApplicationResponse(db, row, scope, ct));
        }, ct);
    }
    private static async Task<IResult> Review(int id, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var row = await Find(id, db, ct); var scope = await ReadScope(row, http, ct);
        Require(scope.Reviewer && scope.Account.Id != row.CreatedBy, 403, "INDEPENDENT_REVIEW_REQUIRED", "A scoped independent reviewer is required.");
        var input = await Body<ApplicationReviewInput>(http, ct);
        Require(input.Decision is "APPROVED" or "REJECTED" or "REVISION_REQUESTED" && (input.Reason?.Length ?? 0) <= 2000
            && (input.Decision == "APPROVED" || !string.IsNullOrWhiteSpace(input.Reason)),
            400, "INVALID_REVIEW", "Use a valid decision; rejection/revision requires a reason of at most 2000 characters.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            Expected(input.ExpectedVersion, row.Version);
            Require(row.Status == "SUBMITTED", 409, "APPLICATION_NOT_SUBMITTED", "Only a submitted revision can be reviewed.");
            if (input.Decision == "APPROVED")
            {
                var content = JsonSerializer.Deserialize<ActivityApplicationInput>(row.ContentJson, Json)!;
                Validate(content, true, http); await Semester(content, http, ct);
                var activity = new ClubActivity
                {
                    ClubId = row.ClubId,
                    ClubName = scope.Club.Name,
                    Title = content.Title!,
                    Description = content.Description!,
                    Location = content.Location!,
                    StartTimeUtc = content.StartTimeUtc!.Value,
                    EndTimeUtc = content.EndTimeUtc!.Value,
                    Scale = content.Scale!,
                    SemesterId = row.SemesterId,
                    PublicationStatus = "DRAFT",
                    DiscoveryVisibility = "CLUB_ONLY",
                    Eligibility = "CLUB_MEMBER",
                    CreatedByUserId = row.CreatedBy,
                    CreatedAtUtc = Now(http),
                    UpdatedAtUtc = Now(http)
                };
                db.Add(activity); await db.SaveChangesAsync(ct);
                var execution = new ActivityExecution { ActivityId = activity.Id, ApplicationId = id };
                db.Add(execution); await db.SaveChangesAsync(ct);
                var rules = content.Actions!.Select(x => new ActivityActionRule
                { ActivityId = activity.Id, ClientId = x.ClientId, DefinitionJson = JsonSerializer.Serialize(x, Json) }).ToArray();
                db.AddRange(rules);
                await db.SaveChangesAsync(ct);
                if (content.Recruitment is { } recruitment)
                {
                    var recruitmentRow = new ActivityRecruitment
                    {
                        ActivityId = activity.Id,
                        Title = content.Title!.Length <= 120 ? content.Title : content.Title[..120],
                        Description = content.Description!,
                        Visibility = recruitment.Visibility,
                        Eligibility = recruitment.Eligibility
                    };
                    db.Add(recruitmentRow);
                    db.AddRange(recruitment.Positions!.Select(x => new ActivityRecruitmentPosition
                    { RecruitmentId = recruitmentRow.Id, ActionRuleId = rules.Single(r => r.ClientId == x.ActionClientId).Id, Capacity = x.Capacity }));
                }
                Audit(db, execution, http, "APPROVED_WITHOUT_SCORING", new { applicationId = id, row.Revision, scoringStatus = ScoringStatus });
                await db.SaveChangesAsync(ct);
            }
            row.Status = input.Decision; row.Version++; row.UpdatedAt = Now(http); History(db, row, http, input.Reason?.Trim());
            return Results.Ok(await ApplicationResponse(db, row, scope, ct));
        }, ct);
    }
    private static async Task<IResult> GetHistory(int id, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var row = await Find(id, db, ct); await ReadScope(row, http, ct);
        return Results.Ok(new
        {
            applicationId = id,
            items = await db.Set<ActivityApplicationHistory>().AsNoTracking()
            .Where(x => x.ApplicationId == id).OrderBy(x => x.Version)
            .Select(x => new { x.Version, x.Revision, x.Status, x.ActorId, x.Reason, x.CreatedAt, content = x.ContentJson }).ToArrayAsync(ct)
        });
    }
    private static async Task<ActivityApplication> Find(int id, ActivityDbContext db, CancellationToken ct) =>
        await db.Set<ActivityApplication>().SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new WorkflowException(404, "APPLICATION_NOT_FOUND", "Application not found.");
    private static async Task<Scope> ReadScope(ActivityApplication row, HttpContext http, CancellationToken ct)
    {
        var scope = await Authorize(row.ClubId, http, ct);
        Require(scope.Manager || scope.Reviewer, 404, "APPLICATION_NOT_FOUND", "Application not found.");
        Require(row.CampusCode == scope.Club.CampusCode, 409, "CLUB_CAMPUS_CHANGED", "The application campus must be reconciled.");
        return scope;
    }
}
