using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Infrastructure;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
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
        group.MapGet("/review-queue", ReviewQueue);
        group.MapGet("/staffing-candidates", StaffingCandidates);
        group.MapGet("/venue-catalog", VenueCatalog);
        group.MapGet("/{id:int}", Get);
        group.MapPatch("/{id:int}", Update);
        group.MapPost("/{id:int}/submit", Submit);
        group.MapPost("/{id:int}/review", Review);
        group.MapGet("/{id:int}/history", GetHistory);
    }
    private static async Task<IResult> Create(HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var input = await Body<ActivityApplicationInput>(http, ct);
        var scope = await Authorize(input.ClubId, http, ct); Manager(scope, "event.registrations.manage");
        Require(input.ExpectedVersion is null, 400, "INVALID_VERSION", "A new draft has no expectedVersion.");
        var content = Normalize(input); Validate(content, false, http);
        content = ApplyActivityType(content);
        var semester = await Semester(content, http, ct);
        content = content with { Period = semester.SemesterCode };
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            content = await ResolveVenues(content, scope.Club.CampusCode, db, ct);
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
        Require(scope.Can("event.registrations.manage") || scope.Reviewer, 403, "APPLICATION_ACCESS_DENIED", "Application access is required.");
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
    private static async Task<IResult> ReviewQueue(int? page, int? pageSize, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var admin = http.User.IsInRole(AuthRoles.Admin);
        Require(admin || http.User.IsInRole(AuthRoles.StudentAffairsAdmin), 403, "REVIEW_ACCESS_DENIED", "A CTSV reviewer is required.");
        var actor = http.User.GetUserId();
        var account = (await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([actor], ct)).GetValueOrDefault(actor);
        Require(account is { IsActive: true, IsLocked: false }, 403, "ACCOUNT_UNAVAILABLE", "An active account is required.");
        var index = page ?? 1;
        var size = pageSize ?? 20;
        Require(index is >= 1 and <= 100000 && size is >= 1 and <= 100, 400, "INVALID_PAGINATION", "Use page >= 1 and pageSize 1–100.");
        var query = db.Set<ActivityApplication>().AsNoTracking().Where(row => row.Status == "SUBMITTED");
        if (!admin) query = query.Where(row => row.CampusCode == account!.CampusCode);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(row => row.Id).Skip((index - 1) * size).Take(size).ToArrayAsync(ct);
        var items = rows.Select(row => new
        {
            row.Id, row.ClubId, row.Status,
            Title = JsonSerializer.Deserialize<ActivityApplicationInput>(row.ContentJson, Json)?.Title
        });
        return Results.Ok(new { items, page = index, pageSize = size, totalItems = total, totalPages = (int)Math.Ceiling(total / (double)size) });
    }
    private static async Task<IResult> StaffingCandidates(int clubId, HttpContext http, CancellationToken ct)
    {
        var scope = await Authorize(clubId, http, ct);
        Manager(scope, "event.registrations.manage");
        var candidates = await ReadStaffingCandidates(clubId, http, ct);
        return Results.Ok(new { items = candidates.Select(row => new { row.UserId, row.DisplayName, AssignmentKind = row.StaffingKind }) });
    }

    private static async Task<IResult> VenueCatalog(int clubId, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var scope = await Authorize(clubId, http, ct);
        Manager(scope, "event.registrations.manage");
        var campus = scope.Club.CampusCode;
        var venues = await db.Set<CampusVenue>().AsNoTracking().Where(x => x.CampusCode == campus && x.IsBookable)
            .OrderBy(x => x.Code).ToArrayAsync(ct);
        var layouts = await db.Set<CampusBookingLayout>().AsNoTracking().Where(x => x.CampusCode == campus)
            .OrderBy(x => x.Building).ThenBy(x => x.Floor).ToArrayAsync(ct);
        return Results.Ok(new { venues, layouts });
    }

    private static async Task<ActivityApplicationInput> ResolveVenues(ActivityApplicationInput content, string campus,
        ActivityDbContext db, CancellationToken ct)
    {
        if (content.VenueIds is not { Length: > 0 } ids) return content;
        Require(ids.Length <= 200 && ids.All(id => id > 0) && ids.Distinct().Count() == ids.Length
            && CampusCodes.FiveCampuses.Contains(campus),
            400, "INVALID_VENUE", "Select distinct bookable rooms in the club's campus.");
        var names = await db.Set<CampusVenue>().AsNoTracking().Where(x => ids.Contains(x.Id) && x.CampusCode == campus && x.IsBookable)
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        Require(names.Count == ids.Length, 400, "INVALID_VENUE", "Select rooms still available for booking in the club's campus.");
        return content with { Location = ActivityBookingEndpoints.RoomLocation(ids.Select(id => names[id]).ToArray()) };
    }

    private static ActivityApplicationInput ApplyActivityType(ActivityApplicationInput content)
    {
        Require(RadarPillars.CoreSix.Contains(content.ActivityType), 400, "ACTIVITY_TYPE_REQUIRED",
            "Choose one of the six activity types.");
        Require(DiscoveryTags.TryNormalize(new(Custom: content.TagSelection?.Custom), true, out var tags),
            400, "INVALID_TAGS", DiscoveryTags.InvalidMessage);
        return content with { TagSelection = new(Custom: tags.Custom) { ActivityType = content.ActivityType } };
    }
    private static async Task<ClubAssignmentCandidate[]> ReadStaffingCandidates(int clubId, HttpContext http, CancellationToken ct) =>
        (await http.RequestServices.GetRequiredService<ClubMemberRosterClient>()
            .GetAssignmentCandidatesAsync(clubId, Now(http), http.GetBearerToken(), ct, includeStaffing: true))
        .Where(row => row.StaffingKind is "HOST" or "STAFF").ToArray();

    private static async Task<ApplicationStaffingInput[]> ResolveStaffing(ActivityApplicationInput content, HttpContext http, CancellationToken ct)
    {
        var staffing = content.Staffing ?? [];
        Require(staffing.Any(row => row.AssignmentKind == "HOST") && staffing.Any(row => row.AssignmentKind == "STAFF"),
            400, "STAFFING_REQUIRED", "Assign at least one Event group member as Host and one regular member as Staff.");
        Require(staffing.Select(row => row.UserId).Distinct().Count() == staffing.Count, 400, "INVALID_STAFFING", "Assign each club member only once.");
        var actions = content.Actions!.ToDictionary(row => row.ClientId, StringComparer.Ordinal);
        var capacity = content.Recruitment?.Positions?.ToDictionary(row => row.ActionClientId, row => row.Capacity, StringComparer.Ordinal);
        foreach (var group in staffing.GroupBy(row => row.ActionClientId, StringComparer.Ordinal))
        {
            Require(!string.IsNullOrWhiteSpace(group.Key) && actions.ContainsKey(group.Key), 400, "INVALID_STAFFING", "Each assignment must reference an action in the plan.");
            Require(group.Count() + (capacity?.GetValueOrDefault(group.Key) ?? 0) <= actions[group.Key].ExpectedPeople,
                400, "INVALID_STAFFING", "Assignments and recruitment must fit within planned action capacity.");
        }
        var candidates = (await ReadStaffingCandidates(content.ClubId, http, ct)).ToDictionary(row => row.UserId);
        var result = new List<ApplicationStaffingInput>();
        foreach (var assignment in staffing)
        {
            Require(candidates.TryGetValue(assignment.UserId, out var candidate) && candidate.StaffingKind == assignment.AssignmentKind,
                409, "STAFFING_MEMBER_INELIGIBLE", "A Host or Staff assignment no longer matches the current eligible club roster.");
            result.Add(assignment with { DisplayName = candidate!.DisplayName });
        }
        return result.ToArray();
    }
    private static async Task<IResult> Update(int id, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var row = await Find(id, db, ct); var scope = await ReadScope(row, http, ct); Manager(scope, "event.registrations.manage");
        var input = await Body<ActivityApplicationInput>(http, ct);
        Require(input.ClubId == row.ClubId, 400, "IMMUTABLE_CLUB", "The application club cannot change.");
        var content = Normalize(input); Validate(content, false, http);
        var semester = await Semester(content, http, ct); content = content with { Period = semester.SemesterCode };
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            Expected(input.ExpectedVersion ?? 0, row.Version);
            Require(row.Status is "DRAFT" or "REVISION_REQUESTED", 409, "APPLICATION_LOCKED", "Only an editable draft can change.");
            content = await ResolveVenues(content, row.CampusCode, db, ct);
            var selection = input.TagSelection ?? JsonSerializer.Deserialize<ActivityApplicationInput>(row.ContentJson, Json)?.TagSelection;
            row.ContentJson = JsonSerializer.Serialize(ApplyActivityType(content with { TagSelection = selection }), Json);
            row.SemesterId = semester.SemesterCode;
            row.Version++; row.UpdatedAt = Now(http); History(db, row, http);
            return Results.Ok(await ApplicationResponse(db, row, scope, ct));
        }, ct);
    }
    private static async Task<IResult> Submit(int id, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var row = await Find(id, db, ct); var scope = await ReadScope(row, http, ct); Manager(scope, "event.registrations.manage");
        var input = await Body<ActionVersionInput>(http, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            Expected(input.ExpectedVersion, row.Version);
            Require(row.Status is "DRAFT" or "REVISION_REQUESTED", 409, "APPLICATION_LOCKED", "Only an editable draft can be submitted.");
            var content = JsonSerializer.Deserialize<ActivityApplicationInput>(row.ContentJson, Json)!;
            content = ApplyActivityType(content);
            content = await ResolveVenues(content, row.CampusCode, db, ct);
            Validate(content, true, http); await Semester(content, http, ct);
            var staffing = await ResolveStaffing(content, http, ct);
            row.ContentJson = JsonSerializer.Serialize(content with { Staffing = staffing }, Json);
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
                // Plans already in the review queue before internal staffing was introduced remain reviewable.
                var staffing = content.Staffing is null ? [] : await ResolveStaffing(content, http, ct);
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
                    TagSelectionJson = content.TagSelection is null ? null : DiscoveryTags.Store(content.TagSelection),
                    PillarWeightsJson = JsonSerializer.Serialize(
                        content.ActivityType is { } type ? new[] { new PillarWeight(type, 1m) } : [],
                        ActivitySessionEndpoints.JsonOptions),
                    SemesterId = row.SemesterId,
                    PublicationStatus = "DRAFT",
                    IsInternal = content.IsInternal,
                    DiscoveryVisibility = content.IsInternal ? "CLUB_ONLY" : "CAMPUS_PUBLIC",
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
                var rulesByClientId = rules.ToDictionary(rule => rule.ClientId, StringComparer.Ordinal);
                db.AddRange(staffing.Select(assignment => new ActivityStaffAssignment
                {
                    ActivityId = activity.Id,
                    ActionRuleId = rulesByClientId[assignment.ActionClientId].Id,
                    UserId = assignment.UserId,
                    DisplayName = assignment.DisplayName!,
                    AssignmentKind = assignment.AssignmentKind,
                    CreatedBy = row.CreatedBy,
                    CreatedAt = Now(http)
                }));
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
        Require(scope.Can("event.registrations.manage") || scope.Reviewer, 404, "APPLICATION_NOT_FOUND", "Application not found.");
        Require(row.CampusCode == scope.Club.CampusCode, 409, "CLUB_CAMPUS_CHANGED", "The application campus must be reconciled.");
        return scope;
    }
}
