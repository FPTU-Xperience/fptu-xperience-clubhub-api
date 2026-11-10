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

public static class ActivityStaffingEndpoints
{
    public static void MapActivityStaffingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = SafeGroup(app, "/api").WithTags("Activity staffing (scoring deferred)");
        group.MapGet("/activities/{activityId:int}/staffing", Get);
        group.MapPatch("/activities/{activityId:int}/publication", Publish);
        group.MapPost("/activities/{activityId:int}/assignments", Assign);
        group.MapPost("/activities/{activityId:int}/assignments/{assignmentId:guid}/cancel", Cancel);
        group.MapPost("/activities/{activityId:int}/assignments/{assignmentId:guid}/completions", Complete);
        group.MapPost("/activities/{activityId:int}/assignments/{assignmentId:guid}/attendance-claims", Complete)
            .WithMetadata(new AttendanceClaimMode(true));
        group.MapPost("/activities/{activityId:int}/action-completions/{completionId:guid}/verify", Verify);
        group.MapPost("/activities/{activityId:int}/recruitment", SaveRecruitment);
        group.MapPatch("/activities/{activityId:int}/recruitment", SaveRecruitment);
        group.MapPatch("/activities/{activityId:int}/recruitment/status", RecruitmentStatus);
        group.MapPost("/activities/{activityId:int}/recruitment/positions/{positionId:guid}/join", Join);
        group.MapPost("/activities/{activityId:int}/recruitment/applications/{applicationId:guid}/review", ReviewApplication);
        group.MapGet("/clubs/{clubId:int}/activity-recruitments", Recruitments);
    }
    private sealed record Source(ActivityExecution Execution, ClubActivity Activity, Scope Scope, bool SemesterAvailable);
    private sealed record AttendanceClaimMode(bool Enabled);
    private static bool Operational(Source source, HttpContext http) => source.SemesterAvailable && Mutable(source.Activity, http);
    private static async Task<Source> Load(int id, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var execution = await db.Set<ActivityExecution>().SingleOrDefaultAsync(x => x.ActivityId == id, ct);
        Require(execution is not null, 404, "ACTION_ACTIVITY_NOT_FOUND", "An approved action activity is required.");
        var activity = await db.Activities.SingleAsync(x => x.Id == id, ct);
        var scope = await Authorize(activity.ClubId, http, ct);
        var application = await db.Set<ActivityApplication>().AsNoTracking().SingleAsync(x => x.Id == execution!.ApplicationId, ct);
        Require(application.Status == "APPROVED" && application.CampusCode == scope.Club.CampusCode,
            409, "SOURCE_NOT_APPROVED", "The source application is no longer valid for this club.");
        var semester = await http.RequestServices.GetRequiredService<SemesterCatalogClient>().ResolveAsync(application.SemesterId, http, ct);
        var semesterAvailable = semester is { IsActive: true, StartDate: not null, EndDate: not null }
            && activity.StartTimeUtc >= semester.StartDate && activity.EndTimeUtc < semester.EndDate.Value.AddDays(1);
        return new(execution!, activity, scope, semesterAvailable);
    }
    private static async Task<(bool Attendance, bool Work)> Verification(Source source, HttpContext http, CancellationToken ct)
    {
        var authority = http.RequestServices.GetRequiredService<ExperienceAuthorityClient>();
        return (await authority.CanVerifyAsync(source.Activity.ClubId, "attendance.verify", http, ct),
            await authority.CanVerifyAsync(source.Activity.ClubId, "contribution.review", http, ct));
    }
    private static ActivityActionInput Definition(ActivityActionRule rule) => JsonSerializer.Deserialize<ActivityActionInput>(rule.DefinitionJson, Json)!;
    private static bool Student(HttpContext http) => http.User.IsInRole(AuthRoles.ClubMember)
        || http.User.IsInRole(AuthRoles.ClubManager) || http.User.IsInRole(AuthRoles.Treasurer);
    private static async Task<IResult> Publish(int activityId, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var source = await Load(activityId, http, db, ct); Manager(source.Scope, "event.content.write");
        var input = await Body<ActivityPublicationInput>(http, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            Expected(input.ExpectedVersion, source.Execution.PublicationVersion);
            Require(Operational(source, http), 409, "ACTIVITY_LOCKED", "Only a scheduled, unexpired activity in an active semester can change publication.");
            var now = Now(http);
            var publishAt = input.PublishAtUtc;
            Require(!publishAt.HasValue || input.IsPublished && !source.Execution.IsPublished && publishAt > now,
                400, "INVALID_PUBLICATION_TIME", "Scheduled publication must be in the future for an unpublished activity.");
            if (publishAt.HasValue || input.RegistrationOpensAt.HasValue || input.RegistrationClosesAt.HasValue)
            {
                Require(input.IsPublished && !source.Execution.IsPublished
                    && input.RegistrationOpensAt is { } opens && input.RegistrationClosesAt is { } closes
                    && (publishAt ?? now) <= opens && opens < closes && closes <= source.Activity.StartTimeUtc,
                    400, "INVALID_REGISTRATION_WINDOW", "Registration must open at or after publication and close by the activity start.");
                source.Activity.RegistrationOpensAt = input.RegistrationOpensAt;
                source.Activity.RegistrationClosesAt = input.RegistrationClosesAt;
            }
            if (!input.IsPublished && source.Execution.IsPublished)
            {
                Require(!await db.ActivityParticipants.AnyAsync(x => x.ActivityId == activityId, ct)
                    && !await db.Set<ActivityStaffAssignment>().AnyAsync(x => x.ActivityId == activityId && x.Status != "REMOVED", ct),
                    409, "PUBLICATION_IN_USE", "An activity with participants or active assignments cannot be hidden.");
                var recruitment = await db.Set<ActivityRecruitment>().SingleOrDefaultAsync(x => x.ActivityId == activityId, ct);
                if (recruitment is not null) recruitment.Status = "CLOSED";
            }
            source.Execution.IsPublished = input.IsPublished && !publishAt.HasValue;
            source.Execution.PublishAtUtc = publishAt;
            source.Execution.PublicationVersion++; source.Execution.Version++;
            source.Activity.PublicationStatus = source.Execution.IsPublished ? "PUBLISHED" : "DRAFT";
            source.Activity.Version = Guid.NewGuid(); source.Activity.UpdatedAtUtc = now;
            // No policy/snapshot/effect is fabricated for the deferred scoring contract.
            Audit(db, source.Execution, http, "PUBLICATION", input);
            return Results.Ok(new
            {
                id = activityId,
                source.Execution.IsPublished,
                source.Execution.PublishAtUtc,
                publicationVersion = source.Execution.PublicationVersion,
                version = source.Execution.Version,
                scoringStatus = ScoringStatus
            });
        }, ct);
    }
    private static async Task<IResult> Get(int activityId, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var source = await Load(activityId, http, db, ct);
        var verify = await Verification(source, http, ct);
        var recruitment = await db.Set<ActivityRecruitment>().AsNoTracking().SingleOrDefaultAsync(x => x.ActivityId == activityId, ct);
        var privileged = source.Scope.Manager || source.Scope.Reviewer;
        var publicAccess = source.Execution.IsPublished && Student(http) && (!source.Activity.IsInternal || recruitment?.Visibility == "PUBLIC");
        Require(privileged || source.Scope.Can("event.staff.assign") || verify.Attendance || verify.Work || source.Execution.IsPublished && source.Scope.Club.IsApprovedMember || publicAccess,
            404, "ACTION_ACTIVITY_NOT_FOUND", "Activity staffing is unavailable.");
        var rules = await db.Set<ActivityActionRule>().AsNoTracking().Where(x => x.ActivityId == activityId).OrderBy(x => x.Id).ToArrayAsync(ct);
        var assignments = await db.Set<ActivityStaffAssignment>().AsNoTracking().Where(x => x.ActivityId == activityId).ToArrayAsync(ct);
        var canReview = await CanReviewApplications(source, http, db, ct);
        var applications = await db.Set<ActivityRecruitmentApplication>().AsNoTracking()
            .Where(x => x.ActivityId == activityId && (canReview || x.UserId == source.Scope.Account.Id)).ToArrayAsync(ct);
        var visible = assignments.Where(x => privileged || source.Scope.Can("event.staff.assign") || verify.Attendance || verify.Work || x.UserId == source.Scope.Account.Id).ToArray();
        var completions = await db.Set<ActivityActionCompletion>().AsNoTracking().Where(x => x.ActivityId == activityId).ToArrayAsync(ct);
        var resources = await db.Set<ActivityResource>().AsNoTracking().Where(x => x.ActivityId == activityId && x.DetachedAtUtc == null
            && (x.Visibility == "PUBLIC" || source.Scope.Club.IsApprovedMember || privileged || verify.Attendance || verify.Work)).ToArrayAsync(ct);
        var completionIds = completions.Select(x => x.Id).ToArray();
        var resourceLinks = await db.Set<ActivityCompletionResource>().AsNoTracking().Where(x => completionIds.Contains(x.CompletionId)).ToArrayAsync(ct);
        var eligible = new List<object>();
        if (source.Scope.Can("event.staff.assign") && Operational(source, http))
        {
            var rosterClient = http.RequestServices.GetRequiredService<ClubMemberRosterClient>();
            var ids = source.Scope.Manager
                ? (await rosterClient.GetAsync(source.Activity.ClubId, Now(http), null, 1, 100, http.GetBearerToken(), ct)).Items.Where(x => x.Status == "Approved").Select(x => x.UserId).ToArray()
                : (await rosterClient.GetAssignmentCandidatesAsync(source.Activity.ClubId, Now(http), http.GetBearerToken(), ct)).Select(x => x.UserId).ToArray();
            foreach (var batch in ids.Chunk(100))
                foreach (var profile in (await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync(batch, ct)).Values)
                    if (profile.IsActive && !profile.IsLocked && profile.CampusCode == source.Scope.Club.CampusCode)
                        eligible.Add(new { userId = profile.Id, displayName = profile.DisplayName });
        }
        var positions = recruitment is null ? [] : await db.Set<ActivityRecruitmentPosition>().AsNoTracking()
            .Where(x => x.RecruitmentId == recruitment.Id && !x.IsArchived).OrderBy(x => x.Id).ToArrayAsync(ct);
        var recruitmentVisible = recruitment is not null && (privileged || verify.Attendance || verify.Work
            || source.Scope.Club.IsApprovedMember || recruitment.Visibility == "PUBLIC");
        return Results.Ok(new
        {
            activityId,
            version = source.Execution.Version,
            publicationVersion = source.Execution.PublicationVersion,
            source.Execution.IsPublished,
            applicationStatus = "APPROVED",
            scoringStatus = ScoringStatus,
            canAwardPoints = false,
            capabilities = new { canManageAssignments = source.Scope.Can("event.staff.assign") && Operational(source, http), canManageRecruitment = source.Scope.Can("event.staff.assign") && Operational(source, http), canReviewApplications = canReview && Operational(source, http), canVerify = verify.Attendance || verify.Work },
            publicAssignments = source.Execution.IsPublished ? assignments.Where(x => x.Status != "REMOVED").Select(x => new
            { x.Id, x.DisplayName, x.ActionRuleId, actionName = Definition(rules.Single(r => r.Id == x.ActionRuleId)).Name, x.AssignmentKind }) : [],
            applications = applications.Select(x => new
            {
                x.Id,
                x.PositionId,
                x.UserId,
                x.DisplayName,
                x.Status,
                x.Version,
                x.SubmittedAt,
                x.ReviewedAt,
                x.Reason,
                x.AssignmentId,
                canReview = canReview && Operational(source, http) && x.Status == "PENDING" && x.UserId != source.Scope.Account.Id
            }),
            availableEvidenceResources = resources.Select(x => new { x.Id, x.Title, x.Kind }),
            actions = rules.Select(x =>
            {
                var d = Definition(x); return new
                {
                    x.Id,
                    d.ClientId,
                    d.Name,
                    d.ExpectedPeople,
                    d.VerificationMethod,
                    d.ContributionRole,
                    d.MaxAwardsPerPerson,
                    d.MaxAwardCount,
                    rawXpPerOccurrence = (decimal?)null
                };
            }),
            assignments = visible.Select(x =>
            {
                var rule = Definition(rules.Single(r => r.Id == x.ActionRuleId));
                var mayVerify = rule.VerificationMethod == "ATTENDANCE" ? verify.Attendance : verify.Work;
                return new
                {
                    x.Id,
                    x.UserId,
                    x.DisplayName,
                    x.ActionRuleId,
                    actionName = rule.Name,
                    x.AssignmentKind,
                    x.Status,
                    canRemove = source.Scope.Can("event.staff.assign") && x.Status != "REMOVED" && Operational(source, http),
                    canSubmit = x.UserId == source.Scope.Account.Id && source.Execution.IsPublished && x.Status != "REMOVED"
                        && source.Activity.Status != ActivityStatuses.Cancelled && source.Activity.StartTimeUtc <= Now(http),
                    verifiedOccurrences = completions.Count(c => c.AssignmentId == x.Id && c.Status == "VERIFIED"),
                    pendingRawXp = (decimal?)null,
                    completions = completions.Where(c => c.AssignmentId == x.Id).Select(c => new
                    {
                        c.Id,
                        c.Version,
                        c.OccurrenceLabel,
                        c.Status,
                        verificationMethod = rule.VerificationMethod,
                        completionKind = rule.VerificationMethod == "ATTENDANCE" ? "MANUAL_ATTENDANCE_CLAIM" : "WORK_EVIDENCE",
                        evidence = mayVerify || x.UserId == source.Scope.Account.Id ? c.Evidence : null,
                        resources = mayVerify || x.UserId == source.Scope.Account.Id
                            ? resources.Where(r => resourceLinks.Any(l => l.CompletionId == c.Id && l.ResourceId == r.Id)).Select(r => new { r.Id, r.Title, r.Url }) : [],
                        canVerify = mayVerify && c.UserId != source.Scope.Account.Id && c.Status == "PENDING" && x.Status != "REMOVED"
                            && source.Activity.Status != ActivityStatuses.Cancelled
                    })
                };
            }),
            eligibleAssignees = eligible,
            recruitment = !recruitmentVisible ? null : new
            {
                recruitment!.Id,
                applicationWorkflow = "APPROVAL_REQUIRED_V1",
                recruitment.Title,
                recruitment.Description,
                recruitment.Visibility,
                recruitment.Eligibility,
                recruitment.Status,
                positions = positions.Select(p => new
                {
                    p.Id,
                    p.ActionRuleId,
                    actionName = Definition(rules.Single(r => r.Id == p.ActionRuleId)).Name,
                    p.Capacity,
                    p.AssignmentKind,
                    filledCount = assignments.Count(a => a.PositionId == p.Id && a.Status != "REMOVED"),
                    myAssignmentId = assignments.Where(a => a.PositionId == p.Id && a.UserId == source.Scope.Account.Id && a.Status != "REMOVED").Select(a => (Guid?)a.Id).SingleOrDefault(),
                    myApplication = applications.Where(a => a.PositionId == p.Id && a.UserId == source.Scope.Account.Id)
                        .Select(a => new { a.Id, a.Status, a.Version, a.Reason }).SingleOrDefault(),
                    canJoin = CanJoin(source, recruitment, http) && assignments.Count(a => a.PositionId == p.Id && a.Status != "REMOVED") < p.Capacity
                        && assignments.Count(a => a.ActionRuleId == p.ActionRuleId && a.Status != "REMOVED") < Definition(rules.Single(r => r.Id == p.ActionRuleId)).ExpectedPeople
                        && !assignments.Any(a => a.ActionRuleId == p.ActionRuleId && a.UserId == source.Scope.Account.Id && a.Status != "REMOVED")
                        && !applications.Any(a => a.PositionId == p.Id && a.UserId == source.Scope.Account.Id && a.Status == "PENDING"),
                    rawXpPerOccurrence = (decimal?)null
                })
            }
        });
    }
    private static async Task<IResult> Assign(int activityId, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var source = await Load(activityId, http, db, ct); Manager(source.Scope, "event.staff.assign");
        var input = await Body<StaffAssignmentInput>(http, ct);
        Require(input.UserId > 0 && input.AssignmentKind is "HOST" or "STAFF", 400, "INVALID_ASSIGNMENT", "Use an eligible user and HOST/STAFF.");
        var approvedMember = source.Scope.Manager
            ? (await http.RequestServices.GetRequiredService<ClubMemberRosterClient>().ResolveByUserIdsAsync(source.Activity.ClubId, [input.UserId], http.GetBearerToken(), ct))
                .Any(x => x.UserId == input.UserId && x.Status == "Approved")
            : (await http.RequestServices.GetRequiredService<ClubExperienceAuthorityClient>().GetAsync(source.Activity.ClubId, input.UserId, ct)) is { IsActive: true, IsApprovedMember: true };
        var account = (await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([input.UserId], ct)).GetValueOrDefault(input.UserId);
        Require(approvedMember && account is { IsActive: true, IsLocked: false }
            && account.CampusCode == source.Scope.Club.CampusCode, 409, "ASSIGNEE_INELIGIBLE", "Choose a current eligible club member.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            Expected(input.ExpectedVersion, source.Execution.Version);
            Require(Operational(source, http), 409, "ACTIVITY_LOCKED", "Assignments are closed.");
            var rule = await Rule(activityId, input.ActionRuleId, db, ct);
            await AssignmentAvailable(source, rule, input.UserId, db, ct);
            var row = new ActivityStaffAssignment
            {
                ActivityId = activityId,
                ActionRuleId = rule.Id,
                UserId = input.UserId,
                DisplayName = account!.DisplayName,
                AssignmentKind = input.AssignmentKind,
                CreatedBy = source.Scope.Account.Id,
                CreatedAt = Now(http)
            };
            db.Add(row); source.Execution.Version++; Audit(db, source.Execution, http, "ASSIGN", new { row.Id, input });
            return Results.Ok(new { row.Id, version = source.Execution.Version, row.Status });
        }, ct);
    }
    private static async Task<IResult> Cancel(int activityId, Guid assignmentId, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var source = await Load(activityId, http, db, ct); Manager(source.Scope, "event.staff.assign");
        var input = await Body<ActionVersionInput>(http, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            Expected(input.ExpectedVersion, source.Execution.Version);
            Require(Operational(source, http), 409, "ACTIVITY_LOCKED", "Assignments are closed.");
            var row = await db.Set<ActivityStaffAssignment>().SingleOrDefaultAsync(x => x.Id == assignmentId && x.ActivityId == activityId, ct);
            Require(row is not null, 404, "ASSIGNMENT_NOT_FOUND", "Assignment not found.");
            Require(row!.Status != "REMOVED", 409, "ASSIGNMENT_REMOVED", "The assignment was already removed.");
            row.Status = "REMOVED"; row.CancelledBy = source.Scope.Account.Id; row.CancelledAt = Now(http);
            source.Execution.Version++; Audit(db, source.Execution, http, "REMOVE_ASSIGNMENT", new { assignmentId });
            return Results.Ok(new { row.Id, version = source.Execution.Version, row.Status });
        }, ct);
    }
    private static async Task<IResult> SaveRecruitment(int activityId, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var source = await Load(activityId, http, db, ct); Manager(source.Scope, "event.staff.assign");
        var input = await Body<RecruitmentInput>(http, ct);
        Require(!string.IsNullOrWhiteSpace(input.Title) && input.Title.Trim().Length <= 120 && (input.Description?.Length ?? 0) <= 2000
            && input.Visibility is "PUBLIC" or "PRIVATE" && input.Eligibility is "SCHOOL_STUDENTS" or "CLUB_MEMBERS"
            && (input.Visibility != "PRIVATE" || input.Eligibility == "CLUB_MEMBERS") && input.Positions is { Count: > 0 and <= 100 },
            400, "INVALID_RECRUITMENT", "Use a bounded title, valid scope and one to 100 distinct positions.");
        var rules = await db.Set<ActivityActionRule>().AsNoTracking().Where(x => x.ActivityId == activityId).ToArrayAsync(ct);
        var ids = new HashSet<int>();
        foreach (var position in input.Positions!)
            Require(position is not null && ids.Add(position.ActionRuleId) && position.AssignmentKind is "HOST" or "STAFF"
                && rules.Any(r => r.Id == position.ActionRuleId && position.Capacity > 0 && position.Capacity <= Definition(r).ExpectedPeople),
                400, "INVALID_POSITION", "Use distinct approved actions, HOST/STAFF and capacity within the plan.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            Expected(input.ExpectedVersion, source.Execution.Version);
            Require(Operational(source, http), 409, "ACTIVITY_LOCKED", "Recruitment is closed.");
            var row = await db.Set<ActivityRecruitment>().SingleOrDefaultAsync(x => x.ActivityId == activityId, ct);
            Require(http.Request.Method == "POST" ? row is null : row is not null, 409, "RECRUITMENT_STATE_CONFLICT", "Create once; PATCH an existing recruitment.");
            var positions = row is null ? [] : await db.Set<ActivityRecruitmentPosition>().Where(x => x.RecruitmentId == row.Id && !x.IsArchived).ToArrayAsync(ct);
            var assignments = await db.Set<ActivityStaffAssignment>().Where(x => x.ActivityId == activityId && x.Status != "REMOVED").ToArrayAsync(ct);
            var pending = await db.Set<ActivityRecruitmentApplication>().Where(x => x.ActivityId == activityId && x.Status == "PENDING").ToArrayAsync(ct);
            if (row is not null && (row.Visibility != input.Visibility || row.Eligibility != input.Eligibility))
                Require(assignments.Length == 0 && pending.Length == 0, 409, "RECRUITMENT_IN_USE", "Scope cannot change while assignments or applications are active.");
            foreach (var old in positions)
            {
                var replacement = input.Positions.SingleOrDefault(x => x.ActionRuleId == old.ActionRuleId);
                var filled = assignments.Count(x => x.PositionId == old.Id);
                Require(!pending.Any(x => x.PositionId == old.Id) || replacement is not null && replacement.AssignmentKind == old.AssignmentKind,
                    409, "POSITION_IN_USE", "Resolve pending applications before removing or changing the position kind.");
                Require(filled == 0 || replacement is not null && replacement.Capacity >= filled && replacement.AssignmentKind == old.AssignmentKind,
                    409, "POSITION_IN_USE", "Filled positions cannot be removed, changed in kind or reduced below occupancy.");
                if (replacement is null) old.IsArchived = true;
                else { old.Capacity = replacement.Capacity; old.AssignmentKind = replacement.AssignmentKind; }
            }
            if (row is null) { row = new ActivityRecruitment { ActivityId = activityId }; db.Add(row); }
            row.Title = input.Title.Trim(); row.Description = input.Description?.Trim() ?? "";
            row.Visibility = input.Visibility; row.Eligibility = input.Eligibility;
            source.Activity.DiscoveryVisibility = row.Visibility == "PUBLIC" ? "CAMPUS_PUBLIC" : "CLUB_ONLY";
            source.Activity.Eligibility = row.Eligibility == "SCHOOL_STUDENTS" ? "CAMPUS_PUBLIC" : "CLUB_MEMBER";
            source.Activity.Version = Guid.NewGuid(); source.Activity.UpdatedAtUtc = Now(http);
            foreach (var position in input.Positions.Where(p => !positions.Any(x => x.ActionRuleId == p.ActionRuleId)))
                db.Add(new ActivityRecruitmentPosition
                {
                    RecruitmentId = row.Id,
                    ActionRuleId = position.ActionRuleId,
                    Capacity = position.Capacity,
                    AssignmentKind = position.AssignmentKind
                });
            source.Execution.Version++; Audit(db, source.Execution, http, "SAVE_RECRUITMENT", input);
            return Results.Ok(new { row.Id, version = source.Execution.Version, row.Status });
        }, ct);
    }
    private static async Task<IResult> RecruitmentStatus(int activityId, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var source = await Load(activityId, http, db, ct); Manager(source.Scope, "event.staff.assign");
        var input = await Body<RecruitmentStatusInput>(http, ct);
        Require(input.Status is "OPEN" or "CLOSED", 400, "INVALID_STATUS", "Use OPEN or CLOSED.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            Expected(input.ExpectedVersion, source.Execution.Version);
            Require(Operational(source, http) && (input.Status != "OPEN" || source.Execution.IsPublished),
                409, "RECRUITMENT_UNAVAILABLE", "Open recruitment requires a published, scheduled activity.");
            var row = await db.Set<ActivityRecruitment>().SingleOrDefaultAsync(x => x.ActivityId == activityId, ct);
            Require(row is not null, 404, "RECRUITMENT_NOT_FOUND", "Recruitment not found.");
            row!.Status = input.Status; source.Execution.Version++; Audit(db, source.Execution, http, "RECRUITMENT_STATUS", input);
            return Results.Ok(new { row.Id, version = source.Execution.Version, row.Status });
        }, ct);
    }
    private static bool CanJoin(Source source, ActivityRecruitment recruitment, HttpContext http) =>
        Student(http) && source.Execution.IsPublished && Operational(source, http) && recruitment.Status == "OPEN"
        && (recruitment.Visibility == "PUBLIC" || source.Scope.Club.IsApprovedMember)
        && (recruitment.Eligibility == "SCHOOL_STUDENTS" || source.Scope.Club.IsApprovedMember);
    private static async Task<IResult> Join(int activityId, Guid positionId, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var source = await Load(activityId, http, db, ct);
        var input = await Body<ActionVersionInput>(http, ct);
        // Revalidate eligibility before receipt replay, as well as in the transaction.
        var recruitment = await db.Set<ActivityRecruitment>().SingleOrDefaultAsync(x => x.ActivityId == activityId, ct);
        Require(recruitment is not null && Student(http)
            && (recruitment.Visibility == "PUBLIC" || source.Scope.Club.IsApprovedMember)
            && (recruitment.Eligibility == "SCHOOL_STUDENTS" || source.Scope.Club.IsApprovedMember), 403, "JOIN_INELIGIBLE", "You are not eligible for this recruitment.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            Expected(input.ExpectedVersion, source.Execution.Version);
            Require(CanJoin(source, recruitment!, http), 409, "RECRUITMENT_CLOSED", "Recruitment is unavailable.");
            var position = await db.Set<ActivityRecruitmentPosition>().SingleOrDefaultAsync(x => x.Id == positionId && x.RecruitmentId == recruitment!.Id && !x.IsArchived, ct);
            Require(position is not null, 404, "POSITION_NOT_FOUND", "Position not found.");
            var rule = await Rule(activityId, position!.ActionRuleId, db, ct);
            await AssignmentAvailable(source, rule, source.Scope.Account.Id, db, ct);
            Require(await db.Set<ActivityStaffAssignment>().CountAsync(x => x.PositionId == positionId && x.Status != "REMOVED", ct) < position.Capacity,
                409, "POSITION_FULL", "The last position was already filled.");
            var row = await db.Set<ActivityRecruitmentApplication>().SingleOrDefaultAsync(x => x.PositionId == positionId && x.UserId == source.Scope.Account.Id, ct);
            Require(row is null || row.Status != "PENDING", 409, "ALREADY_APPLIED", "Your application is already pending.");
            if (row is null)
            {
                row = new ActivityRecruitmentApplication { ActivityId = activityId, PositionId = positionId, UserId = source.Scope.Account.Id };
                db.Add(row);
            }
            else row.Version++;
            row.DisplayName = source.Scope.Account.DisplayName; row.Status = "PENDING"; row.SubmittedAt = Now(http);
            row.ReviewedBy = null; row.ReviewedAt = null; row.Reason = null; row.AssignmentId = null;
            source.Execution.Version++; Audit(db, source.Execution, http, "RECRUITMENT_APPLIED", new { positionId, row.Id, row.Version });
            return Results.Ok(new { row.Id, applicationId = row.Id, applicationVersion = row.Version, version = source.Execution.Version, row.Status });
        }, ct);
    }
    private static async Task<bool> CanReviewApplications(Source source, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        if (source.Scope.Can("event.staff.assign")) return true;
        var host = await db.Set<ActivityStaffAssignment>().AsNoTracking().FirstOrDefaultAsync(x => x.ActivityId == source.Activity.Id
            && x.UserId == source.Scope.Account.Id && x.AssignmentKind == "HOST" && x.Status != "REMOVED", ct);
        if (host is null) return false;
        await AssigneeAuthority(source, host, http, ct);
        return true;
    }
    private static async Task<IResult> ReviewApplication(int activityId, Guid applicationId, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var source = await Load(activityId, http, db, ct);
        Require(await CanReviewApplications(source, http, db, ct), 403, "RECRUITMENT_REVIEW_REQUIRED", "Current event management or host assignment is required.");
        var input = await Body<RecruitmentReviewInput>(http, ct);
        Require(input.Decision is "APPROVED" or "REJECTED" && (input.Reason?.Length ?? 0) <= 2000
            && (input.Decision != "REJECTED" || !string.IsNullOrWhiteSpace(input.Reason)), 400, "INVALID_REVIEW", "Use APPROVED or REJECTED and provide a rejection reason.");
        var row = await db.Set<ActivityRecruitmentApplication>().SingleOrDefaultAsync(x => x.Id == applicationId && x.ActivityId == activityId, ct);
        Require(row is not null, 404, "RECRUITMENT_APPLICATION_NOT_FOUND", "Application not found.");
        Require(row!.UserId != source.Scope.Account.Id, 403, "SELF_REVIEW_DENIED", "Another authorized reviewer must review your application.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            Expected(input.ExpectedVersion, source.Execution.Version); Expected(input.ApplicationVersion, row.Version);
            Require(row.Status == "PENDING" && Operational(source, http), 409, "APPLICATION_LOCKED", "Only pending applications on an operational activity can be reviewed.");
            if (input.Decision == "APPROVED")
            {
                var recruitment = await db.Set<ActivityRecruitment>().SingleAsync(x => x.ActivityId == activityId, ct);
                Require(recruitment.Status == "OPEN" && source.Execution.IsPublished, 409, "RECRUITMENT_CLOSED", "Recruitment is unavailable.");
                var position = await db.Set<ActivityRecruitmentPosition>().SingleOrDefaultAsync(x => x.Id == row.PositionId && x.RecruitmentId == recruitment.Id && !x.IsArchived, ct);
                Require(position is not null, 409, "POSITION_UNAVAILABLE", "The position is no longer available.");
                var assignment = new ActivityStaffAssignment
                {
                    ActivityId = activityId,
                    PositionId = row.PositionId,
                    ActionRuleId = position!.ActionRuleId,
                    UserId = row.UserId,
                    DisplayName = row.DisplayName,
                    AssignmentKind = position.AssignmentKind,
                    CreatedBy = source.Scope.Account.Id,
                    CreatedAt = Now(http)
                };
                await AssigneeAuthority(source, assignment, http, ct);
                var rule = await Rule(activityId, position.ActionRuleId, db, ct);
                await AssignmentAvailable(source, rule, row.UserId, db, ct);
                Require(await db.Set<ActivityStaffAssignment>().CountAsync(x => x.PositionId == row.PositionId && x.Status != "REMOVED", ct) < position.Capacity,
                    409, "POSITION_FULL", "The last position was already filled.");
                db.Add(assignment); row.AssignmentId = assignment.Id;
            }
            row.Status = input.Decision; row.Version++; row.ReviewedBy = source.Scope.Account.Id; row.ReviewedAt = Now(http); row.Reason = input.Reason?.Trim();
            source.Execution.Version++; Audit(db, source.Execution, http, "RECRUITMENT_REVIEWED", new { row.Id, row.Status, row.Version, row.AssignmentId, row.Reason });
            return Results.Ok(new { row.Id, row.Status, row.AssignmentId, applicationVersion = row.Version, version = source.Execution.Version });
        }, ct);
    }
    private static async Task<ActivityActionRule> Rule(int id, int ruleId, ActivityDbContext db, CancellationToken ct) =>
        await db.Set<ActivityActionRule>().SingleOrDefaultAsync(x => x.ActivityId == id && x.Id == ruleId, ct)
        ?? throw new WorkflowException(404, "ACTION_NOT_FOUND", "Approved action not found.");
    private static async Task AssignmentAvailable(Source source, ActivityActionRule rule, int userId, ActivityDbContext db, CancellationToken ct)
    {
        Require(!await db.Set<ActivityStaffAssignment>().AnyAsync(x => x.ActivityId == source.Activity.Id && x.ActionRuleId == rule.Id && x.UserId == userId && x.Status != "REMOVED", ct),
            409, "ALREADY_ASSIGNED", "There is already an active assignment for this action.");
        Require(await db.Set<ActivityStaffAssignment>().CountAsync(x => x.ActionRuleId == rule.Id && x.Status != "REMOVED", ct) < Definition(rule).ExpectedPeople,
            409, "ACTION_FULL", "The planned number of people has been reached.");
    }
    private static async Task<IResult> Complete(int activityId, Guid assignmentId, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var source = await Load(activityId, http, db, ct);
        var input = await Body<ActionCompletionInput>(http, ct);
        var attendanceClaim = http.GetEndpoint()?.Metadata.GetMetadata<AttendanceClaimMode>()?.Enabled == true;
        Require(!string.IsNullOrWhiteSpace(input.OccurrenceKey) && input.OccurrenceKey.Length <= 80
            && !string.IsNullOrWhiteSpace(input.OccurrenceLabel) && input.OccurrenceLabel.Length <= 200
            && !string.IsNullOrWhiteSpace(input.Evidence) && input.Evidence.Length <= 4000,
            400, "INVALID_COMPLETION", "Provide an occurrence identity, label and bounded evidence.");
        var assignment = await db.Set<ActivityStaffAssignment>().SingleOrDefaultAsync(x => x.Id == assignmentId && x.ActivityId == activityId && x.UserId == source.Scope.Account.Id, ct);
        Require(assignment is not null, 404, "ASSIGNMENT_NOT_FOUND", "Your assignment was not found.");
        await AssigneeAuthority(source, assignment!, http, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            Expected(input.ExpectedVersion, source.Execution.Version);
            Require(source.Execution.IsPublished && source.Activity.Status != ActivityStatuses.Cancelled
                && source.Activity.StartTimeUtc <= Now(http) && assignment!.Status != "REMOVED",
                409, "COMPLETION_UNAVAILABLE", "Use an active assignment after the activity starts.");
            var rule = Definition(await Rule(activityId, assignment!.ActionRuleId, db, ct));
            Require(rule.VerificationMethod == (attendanceClaim ? "ATTENDANCE" : "WORK_REVIEW"), 409, "COMPLETION_SOURCE_MISMATCH",
                "Use work evidence for WORK_REVIEW or an explicit manual attendance claim for ATTENDANCE. A claim alone is not verified attendance.");
            Require(await db.Set<ActivityActionCompletion>().CountAsync(x => x.ActionRuleId == assignment.ActionRuleId && x.UserId == assignment.UserId, ct) < rule.MaxAwardsPerPerson
                && await db.Set<ActivityActionCompletion>().CountAsync(x => x.ActionRuleId == assignment.ActionRuleId, ct) < rule.MaxAwardCount,
                409, "OCCURRENCE_LIMIT", "The approved occurrence limit has been reached.");
            var row = new ActivityActionCompletion
            {
                ActivityId = activityId,
                AssignmentId = assignmentId,
                ActionRuleId = assignment.ActionRuleId,
                UserId = assignment.UserId,
                OccurrenceKey = input.OccurrenceKey.Trim(),
                OccurrenceLabel = input.OccurrenceLabel.Trim(),
                Evidence = input.Evidence.Trim(),
                SubmittedAt = Now(http)
            };
            var resourceIds = input.ResourceIds ?? [];
            Require(resourceIds.Count <= 10 && resourceIds.All(x => x != Guid.Empty) && resourceIds.Distinct().Count() == resourceIds.Count,
                400, "INVALID_EVIDENCE_RESOURCES", "Select up to ten distinct resources.");
            var resourceCount = await db.Set<ActivityResource>().CountAsync(x => resourceIds.Contains(x.Id) && x.ActivityId == activityId
                && x.DetachedAtUtc == null && (x.Visibility == "PUBLIC" || source.Scope.Club.IsApprovedMember), ct);
            Require(resourceCount == resourceIds.Count, 403, "EVIDENCE_RESOURCE_UNAVAILABLE", "Resources must belong to this activity and be visible to you.");
            foreach (var resourceId in resourceIds) db.Add(new ActivityCompletionResource { CompletionId = row.Id, ResourceId = resourceId });
            db.Add(row); source.Execution.Version++; Audit(db, source.Execution, http, attendanceClaim ? "MANUAL_ATTENDANCE_CLAIM" : "COMPLETION_SUBMITTED", new { row.Id, assignmentId });
            return Results.Ok(new
            {
                row.Id,
                version = source.Execution.Version,
                completionVersion = row.Version,
                row.Status,
                completionKind = attendanceClaim ? "MANUAL_ATTENDANCE_CLAIM" : "WORK_EVIDENCE",
                scoringStatus = ScoringStatus
            });
        }, ct);
    }
    private static async Task<IResult> Verify(int activityId, Guid completionId, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var source = await Load(activityId, http, db, ct);
        var input = await Body<ActionVerificationInput>(http, ct);
        var row = await db.Set<ActivityActionCompletion>().SingleOrDefaultAsync(x => x.Id == completionId && x.ActivityId == activityId, ct);
        Require(row is not null, 404, "COMPLETION_NOT_FOUND", "Completion not found.");
        Require(row!.UserId != source.Scope.Account.Id, 403, "SELF_VERIFICATION_DENIED", "Independent verification is required.");
        var assignment = await db.Set<ActivityStaffAssignment>().SingleAsync(x => x.Id == row.AssignmentId, ct);
        await AssigneeAuthority(source, assignment, http, ct);
        var rule = Definition(await Rule(activityId, row.ActionRuleId, db, ct));
        var capability = rule.VerificationMethod == "ATTENDANCE" ? "attendance.verify" : "contribution.review";
        Require(await http.RequestServices.GetRequiredService<ExperienceAuthorityClient>().CanVerifyAsync(source.Activity.ClubId, capability, http, ct),
            403, "VERIFICATION_REQUIRED", "A current scoped verification capability is required.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            Expected(input.ExpectedVersion, source.Execution.Version); Expected(input.CompletionVersion, row.Version);
            Require(row.Status == "PENDING" && source.Activity.Status != ActivityStatuses.Cancelled
                && await db.Set<ActivityStaffAssignment>().AnyAsync(x => x.Id == row.AssignmentId && x.Status != "REMOVED", ct),
                409, "COMPLETION_LOCKED", "Only a pending completion on an active source can be verified.");
            row.Status = "VERIFIED"; row.Version++; row.VerifiedBy = source.Scope.Account.Id; row.VerifiedAt = Now(http);
            source.Execution.Version++; Audit(db, source.Execution, http, "COMPLETION_VERIFIED_WITHOUT_POINTS", new { row.Id, row.Version });
            return Results.Ok(new
            {
                row.Id,
                version = source.Execution.Version,
                completionVersion = row.Version,
                row.Status,
                scoringStatus = ScoringStatus,
                awardedRawXp = (decimal?)null,
                canAwardPoints = false
            });
        }, ct);
    }
    private static async Task AssigneeAuthority(Source source, ActivityStaffAssignment assignment, HttpContext http, CancellationToken ct)
    {
        var profile = (await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([assignment.UserId], ct))
            .GetValueOrDefault(assignment.UserId);
        var club = await http.RequestServices.GetRequiredService<ClubExperienceAuthorityClient>()
            .GetAsync(source.Activity.ClubId, assignment.UserId, ct);
        var requiresMembership = assignment.PositionId is null;
        if (assignment.PositionId.HasValue)
        {
            var db = http.RequestServices.GetRequiredService<ActivityDbContext>();
            var recruitment = await db.Set<ActivityRecruitment>().AsNoTracking().SingleAsync(x => x.ActivityId == source.Activity.Id, ct);
            requiresMembership = recruitment.Eligibility == "CLUB_MEMBERS" || recruitment.Visibility == "PRIVATE";
        }
        Require(profile is { IsActive: true, IsLocked: false } && profile.CampusCode == source.Scope.Club.CampusCode
            && club is { IsActive: true } && (!requiresMembership || club.IsApprovedMember && club.JoinedAt <= assignment.CreatedAt),
            409, "ASSIGNEE_NO_LONGER_ELIGIBLE", "The assignee's current account or membership no longer permits this completion.");
    }
    private static async Task<IResult> Recruitments(int clubId, int? page, int? pageSize, string? status,
        DateTimeOffset? from, DateTimeOffset? to, HttpContext http, ActivityDbContext db, CancellationToken ct)
    {
        var scope = await Authorize(clubId, http, ct);
        Require(scope.Manager || scope.Reviewer || Student(http), 403, "RECRUITMENT_ACCESS_DENIED", "Recruitment access is required.");
        var index = page ?? 1; var size = pageSize ?? 20;
        Require(index is >= 1 and <= 100000 && size is >= 1 and <= 100 && (from is null || to is null || from <= to),
            400, "INVALID_FILTERS", "Check pagination and date bounds.");
        Require(status is null or "DRAFT" or "OPEN" or "CLOSED", 400, "INVALID_STATUS", "Use a canonical status.");
        var query = from r in db.Set<ActivityRecruitment>().AsNoTracking()
                    join e in db.Set<ActivityExecution>().AsNoTracking() on r.ActivityId equals e.ActivityId
                    join a in db.Activities.AsNoTracking() on r.ActivityId equals a.Id
                    where a.ClubId == clubId && (scope.Manager || scope.Reviewer || e.IsPublished
                        && (r.Visibility == "PUBLIC" || scope.Club.IsApprovedMember))
                    select new
                    {
                        r.Id,
                        r.ActivityId,
                        a.ClubId,
                        activityTitle = a.Title,
                        r.Title,
                        r.Description,
                        r.Status,
                        r.Visibility,
                        r.Eligibility,
                        a.StartTimeUtc,
                        a.EndTimeUtc,
                        version = e.Version,
                        canView = true
                    };
        if (status is not null) query = query.Where(x => x.Status == status);
        if (from.HasValue) query = query.Where(x => x.StartTimeUtc >= from);
        if (to.HasValue) query = query.Where(x => x.StartTimeUtc <= to);
        var total = await query.CountAsync(ct);
        return Results.Ok(new
        {
            items = await query.OrderBy(x => x.ActivityId).Skip((index - 1) * size).Take(size).ToArrayAsync(ct),
            page = index,
            pageSize = size,
            totalItems = total,
            totalPages = (int)Math.Ceiling(total / (double)size),
            scoringStatus = ScoringStatus
        });
    }
}
