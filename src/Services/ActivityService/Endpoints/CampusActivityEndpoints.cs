using ActivityService.Data;
using ActivityService.Models;
using ActivityService.Services;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityActionHelpers;

namespace ActivityService.Endpoints;

internal static class CampusActivityEndpoints
{
    internal static void MapCampusActivityEndpoints(this IEndpointRouteBuilder app)
    {
        var routes = SafeGroup(app, "/api/activities").WithTags("Campus activities");
        routes.MapGet("/campus", List);
        routes.MapPost("/campus", Create);
        routes.MapPut("/campus/{id:int}", Update);
        routes.MapPost("/campus/{id:int}/publish", Publish);
        routes.MapPut("/campus/{id:int}/xp-policy", ConfigureXp);
        routes.MapPost("/campus/{id:int}/cancel-publication", CancelPublication);
        routes.MapPost("/campus/{id:int}/handover", Offer);
        routes.MapGet("/handover/club/{clubId:int}", Inbox);
        routes.MapPost("/handover/club/{clubId:int}/{id:int}/decision", Decide);
        routes.MapGet("/campus/{id:int}/report", GetReport);
        routes.MapPost("/campus/{id:int}/report", CreateReport);
        routes.MapPost("/campus/{id:int}/report/finalize", FinalizeReport);
        routes.MapGet("/campus/{id:int}/operations", Operations);
        routes.MapPost("/campus/{id:int}/sessions", AddSession);
        routes.MapPost("/campus/{id:int}/sessions/{sessionId:int}/attendance", VerifyAttendance);
        routes.MapPost("/campus/{id:int}/duties", AssignDuty);
        routes.MapPost("/campus/{id:int}/duties/{dutyId:guid}/confirm", ConfirmDuty);
    }

    internal static async Task<InternalUserProfile> CampusReviewer(HttpContext http, CancellationToken ct)
    {
        Require(http.User.IsInRole(AuthRoles.Admin) || http.User.IsInRole(AuthRoles.StudentAffairsAdmin),
            403, "CAMPUS_AUTHORITY_REQUIRED", "Campus activity management is restricted to student affairs.");
        var id = http.User.GetUserId();
        var account = (await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([id], ct)).GetValueOrDefault(id);
        Require(account is { IsActive: true, IsLocked: false } && !string.IsNullOrWhiteSpace(account.CampusCode),
            403, "CAMPUS_ACCOUNT_REQUIRED", "An active account with a campus is required.");
        return account!;
    }

    internal static async Task<Scope> OrganizerScope(ClubActivity activity, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        if (activity.ClubId != 0) return await Authorize(activity.ClubId, http, ct);
        var id = http.User.GetUserId();
        var account = (await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([id], ct)).GetValueOrDefault(id);
        Require(activity.OrganizerCampusCode is not null && account is { IsActive: true, IsLocked: false }
            && account.CampusCode == activity.OrganizerCampusCode, 403, "CAMPUS_SCOPE_DENIED", "This activity belongs to another campus.");
        var reviewer = http.User.IsInRole(AuthRoles.Admin) || http.User.IsInRole(AuthRoles.StudentAffairsAdmin);
        var participant = await db.ActivityParticipants.AnyAsync(p => p.ActivityId == activity.Id && p.UserId == id, ct);
        return new Scope(account!, new ClubExperienceAuthority(0, activity.ClubName, account!.CampusCode, true, participant, null),
            reviewer, reviewer);
    }

    private static async Task<IResult> List(ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var actor = await CampusReviewer(http, ct);
        var rows = await db.Activities.AsNoTracking().Where(a => a.OrganizerCampusCode == actor.CampusCode)
            .OrderByDescending(a => a.Id).Take(100).ToListAsync(ct);
        var ids = rows.Select(a => a.Id).ToArray();
        var offers = await db.Set<ActivityHandover>().AsNoTracking().Where(x => ids.Contains(x.ActivityId)).ToDictionaryAsync(x => x.ActivityId, ct);
        return Results.Ok(rows.Select(a => View(a, offers.GetValueOrDefault(a.Id))));
    }

    private static async Task<IResult> Create(ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var actor = await CampusReviewer(http, ct);
        var input = await Body<Plan>(http, ct);
        Require(DiscoveryTags.TryNormalize(input.TagSelection, true, out var tags), 400, "INVALID_TAGS", DiscoveryTags.InvalidMessage);
        await Validate(input, http, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            var row = new ClubActivity
            {
                ClubName = "Công tác sinh viên",
                OrganizerCampusCode = actor.CampusCode,
                IsInternal = false,
                DiscoveryVisibility = "CAMPUS_PUBLIC",
                Eligibility = "CAMPUS_PUBLIC",
                PublicationStatus = "DRAFT",
                CreatedByUserId = actor.Id,
                CreatedAtUtc = Now(http),
                TagSelectionJson = input.TagSelection is null ? null : DiscoveryTags.Store(tags)
            };
            Apply(row, input, Now(http));
            db.Add(row);
            await db.SaveChangesAsync(ct);
            return Results.Ok(View(row, null));
        }, ct);
    }

    private static async Task<IResult> Update(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var actor = await CampusReviewer(http, ct);
        var row = await Find(id, db, ct);
        Require(row.OrganizerCampusCode == actor.CampusCode && row.ClubId == 0, 403, "CAMPUS_SCOPE_DENIED", "This activity belongs to another organizer.");
        var input = await Body<Plan>(http, ct);
        Require(DiscoveryTags.TryNormalize(input.TagSelection, true, out var tags), 400, "INVALID_TAGS", DiscoveryTags.InvalidMessage);
        await Validate(input, http, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(row).ReloadAsync(ct);
            Require(row.ClubId == 0 && row.Version == input.ExpectedVersion && row.PublicationStatus == "DRAFT" && row.CampusPublishAtUtc is null,
                409, "CAMPUS_ACTIVITY_LOCKED", "Only the current campus draft can be edited.");
            var offer = await db.Set<ActivityHandover>().SingleOrDefaultAsync(x => x.ActivityId == id, ct);
            Require(offer?.Status != "PENDING", 409, "HANDOVER_PENDING", "Wait for the club decision before editing.");
            Require(!await db.Sessions.AnyAsync(s => s.ActivityId == id, ct)
                || row.StartTimeUtc == input.StartTimeUtc && row.EndTimeUtc == input.EndTimeUtc && row.SemesterId == input.SemesterId?.Trim(),
                409, "SESSION_SCHEDULE_LOCKED", "The schedule and semester are locked after sessions have been created.");
            if (row.StartTimeUtc != input.StartTimeUtc || row.EndTimeUtc != input.EndTimeUtc || row.SemesterId != input.SemesterId?.Trim())
            {
                row.PolicyVersion = null;
                row.ScoringSnapshotJson = null;
            }
            Apply(row, input, Now(http));
            if (input.TagSelection is not null) row.TagSelectionJson = DiscoveryTags.Store(tags);
            return Results.Ok(View(row, offer));
        }, ct);
    }

    private static async Task<IResult> Publish(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var row = await Find(id, db, ct);
        if (row.ClubId == 0)
        {
            var actor = await CampusReviewer(http, ct);
            Require(actor.CampusCode == row.OrganizerCampusCode, 403, "CAMPUS_SCOPE_DENIED", "This activity belongs to another campus.");
        }
        else Manager(await Authorize(row.ClubId, http, ct));
        var input = await Body<PublishInput>(http, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(row).ReloadAsync(ct);
            var now = Now(http);
            Require(row.Version == input.ExpectedVersion && row.Status == ActivityStatuses.Scheduled && row.PublicationStatus == "DRAFT",
                409, "CAMPUS_ACTIVITY_LOCKED", "Only the current draft can go public.");
            var offer = await db.Set<ActivityHandover>().SingleOrDefaultAsync(x => x.ActivityId == id, ct);
            Require(offer?.Status != "PENDING", 409, "HANDOVER_PENDING", "Wait for the club decision before publishing.");
            Require(input.PublishAtUtc is null || input.PublishAtUtc > now, 400, "INVALID_PUBLICATION_TIME", "Choose a future publication time.");
            Require((input.PublishAtUtc ?? now) <= input.RegistrationOpensAt && input.RegistrationOpensAt < input.RegistrationClosesAt
                && input.RegistrationClosesAt <= row.StartTimeUtc, 400, "INVALID_REGISTRATION_WINDOW", "Choose a registration window before the activity starts.");
            row.RegistrationOpensAt = input.RegistrationOpensAt;
            row.RegistrationClosesAt = input.RegistrationClosesAt;
            row.CampusPublishAtUtc = input.PublishAtUtc;
            row.PublicationStatus = input.PublishAtUtc.HasValue ? "DRAFT" : "PUBLISHED";
            row.UpdatedAtUtc = now;
            row.Version = Guid.NewGuid();
            return Results.Ok(View(row, offer));
        }, ct);
    }

    private static async Task<IResult> ConfigureXp(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var actor = await CampusReviewer(http, ct);
        var activity = await db.Activities.SingleOrDefaultAsync(x => x.Id == id, ct);
        Require(activity is not null, 404, "ACTIVITY_NOT_FOUND", "Activity does not exist.");
        var campus = activity!.OrganizerCampusCode;
        if (campus is null && activity.ClubId > 0)
            campus = (await Authorize(activity.ClubId, http, ct)).Club.CampusCode;
        Require(campus == actor.CampusCode, 403, "CAMPUS_SCOPE_DENIED", "This activity belongs to another campus.");
        var input = await Body<CampusXpInput>(http, ct);
        var attributes = new ContributionAttributes(input.Tier ?? "", ContributionRoles.Participant,
            activity.OrganizerCampusCode is null ? ActivityScales.Club : ActivityScales.Campus,
            BonusResults.None, input.PillarWeights ?? []);
        Require(VerifiedExperienceRules.Validate(attributes) is null && input.PolicyVersion != Guid.Empty,
            400, "INVALID_SCORING_ATTRIBUTES", "Choose a policy, effort tier and valid pillar weights.");
        var policy = await http.RequestServices.GetRequiredService<ExperienceAuthorityClient>()
            .ResolvePolicyAsync(activity.SemesterId!, input.PolicyVersion, http, ct);
        var environment = http.RequestServices.GetRequiredService<IConfiguration>().GetValue("Experience:Environment", "PROJECT");
        Require(policy is { ApprovalStatus: "APPROVED", StartDate: not null, EndDate: not null }
            && policy.Environment == environment, 400, "SCORE_POLICY_UNAPPROVED", "Choose an approved policy for this semester.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(activity).ReloadAsync(ct);
            Require(activity.Version == input.ExpectedVersion && activity.Status == ActivityStatuses.Scheduled
                && activity.PublicationStatus == "DRAFT" && activity.CampusPublishAtUtc is null,
                409, "CAMPUS_ACTIVITY_LOCKED", "Configure XP before scheduling publication.");
            Require(!await db.Set<ActivityExecution>().AnyAsync(x => x.ActivityId == id && (x.IsPublished || x.PublishAtUtc != null), ct)
                && !await db.ActivityAttendances.AnyAsync(x => x.ActivityId == id, ct),
                409, "SCORING_SOURCE_LOCKED", "The activity already has a publication or attendance history.");
            Require(activity.SemesterId == policy!.SemesterId && activity.StartTimeUtc >= policy.StartDate
                && activity.EndTimeUtc < policy.EndDate!.Value.AddDays(1),
                400, "ACTIVITY_OUTSIDE_SEMESTER", "The activity must fit the approved policy period.");
            activity.PolicyVersion = policy.PolicyVersion;
            activity.EffortTier = attributes.Tier;
            activity.Scale = attributes.Scale;
            activity.PillarWeightsJson = JsonSerializer.Serialize(attributes.PillarWeights, ActivitySessionEndpoints.JsonOptions);
            activity.Version = Guid.NewGuid();
            activity.UpdatedAtUtc = Now(http);
            var source = new ExperienceSourceSnapshot(activity.Id, activity.ClubId, 0, policy.SemesterId, activity.Title,
                activity.StartTimeUtc, activity.EndTimeUtc, activity.Version, Guid.Empty, "SCHEDULED", false, attributes, policy);
            activity.ScoringSnapshotJson = JsonSerializer.Serialize(source, ActivitySessionEndpoints.JsonOptions);
            return Results.Ok(View(activity, await db.Set<ActivityHandover>().AsNoTracking().SingleOrDefaultAsync(x => x.ActivityId == id, ct)));
        }, ct);
    }

    private static async Task<IResult> CancelPublication(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var row = await Find(id, db, ct);
        Manager(await OrganizerScope(row, db, http, ct));
        var input = await Body<VersionInput>(http, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(row).ReloadAsync(ct);
            Require(row.Version == input.ExpectedVersion && row.PublicationStatus == "DRAFT" && row.CampusPublishAtUtc.HasValue,
                409, "PUBLICATION_NOT_SCHEDULED", "Reload the scheduled draft.");
            row.CampusPublishAtUtc = null;
            row.Version = Guid.NewGuid();
            row.UpdatedAtUtc = Now(http);
            var offer = await db.Set<ActivityHandover>().AsNoTracking().SingleOrDefaultAsync(x => x.ActivityId == id, ct);
            return Results.Ok(View(row, offer));
        }, ct);
    }

    private static async Task<IResult> Offer(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var actor = await CampusReviewer(http, ct);
        var row = await Find(id, db, ct);
        Require(row.OrganizerCampusCode == actor.CampusCode && row.ClubId == 0, 403, "CAMPUS_SCOPE_DENIED", "This activity belongs to another organizer.");
        var input = await Body<OfferInput>(http, ct);
        Require(input.ClubId > 0, 400, "INVALID_HOST_CLUB", "Choose an active club.");
        var scope = await Authorize(input.ClubId, http, ct);
        Require(scope.Club.CampusCode == actor.CampusCode, 403, "CAMPUS_SCOPE_DENIED", "The host club must belong to the activity campus.");
        var managers = await http.RequestServices.GetRequiredService<ClubExperienceAuthorityClient>().ManagerIdsAsync(input.ClubId, ct);
        Require(managers.Length > 0, 409, "HOST_MANAGEMENT_REQUIRED", "The selected club must have an active manager.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(row).ReloadAsync(ct);
            var now = Now(http);
            Require(row.Version == input.ExpectedVersion && row.ClubId == 0 && row.PublicationStatus == "DRAFT" && row.CampusPublishAtUtc is null
                && row.Status == ActivityStatuses.Scheduled && row.StartTimeUtc > now,
                409, "HANDOVER_LOCKED", "Only an upcoming unpublished campus activity can be offered.");
            var offer = await db.Set<ActivityHandover>().SingleOrDefaultAsync(x => x.ActivityId == id, ct);
            Require(offer is null, 409, "HANDOVER_EXISTS", "This activity already has a handover decision or a pending offer.");
            offer = new ActivityHandover
            {
                ActivityId = id,
                ClubId = input.ClubId,
                ClubName = scope.Club.Name,
                Version = Guid.NewGuid(),
                OfferedBy = actor.Id,
                OfferedAtUtc = now
            };
            db.Add(offer);
            db.AddOutboxMessage(new ExperienceNotificationEvent(Guid.NewGuid(), now, input.ClubId, managers,
                "ACTIVITY_HANDOVER_OFFERED", $"Đề nghị bàn giao hoạt động: {row.Title}", "/v2/my-clubs"),
                ExperienceRoutingKeys.Notification);
            return Results.Ok(View(row, offer));
        }, ct);
    }

    private static async Task<IResult> Inbox(int clubId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var scope = await Authorize(clubId, http, ct);
        Manager(scope);
        var now = Now(http);
        var items = await (from offer in db.Set<ActivityHandover>().AsNoTracking()
                           join activity in db.Activities.AsNoTracking() on offer.ActivityId equals activity.Id
                           where offer.ClubId == clubId && (offer.Status == "PENDING" && activity.ClubId == 0
                               || offer.Status == "ACCEPTED" && activity.ClubId == clubId)
                               && activity.OrganizerCampusCode == scope.Club.CampusCode && activity.PublicationStatus == "DRAFT"
                               && activity.Status == ActivityStatuses.Scheduled && activity.StartTimeUtc > now
                           orderby activity.Id descending
                           select new { activity, offer }).Take(100).ToListAsync(ct);
        return Results.Ok(items.Select(x => View(x.activity, x.offer)));
    }

    private static async Task<IResult> Decide(int clubId, int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var scope = await Authorize(clubId, http, ct);
        Manager(scope);
        var input = await Body<DecisionInput>(http, ct);
        Require(input.Decision is "ACCEPTED" or "REJECTED", 400, "INVALID_HANDOVER_DECISION", "Accept or reject the offer.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            var offer = await db.Set<ActivityHandover>().SingleOrDefaultAsync(x => x.ActivityId == id && x.ClubId == clubId, ct);
            if (offer is null) return Results.NotFound();
            var row = await Find(id, db, ct);
            var now = Now(http);
            Require(offer.Version == input.ExpectedVersion && offer.Status == "PENDING" && row.ClubId == 0
                && row.OrganizerCampusCode == scope.Club.CampusCode && row.PublicationStatus == "DRAFT"
                && row.Status == ActivityStatuses.Scheduled && row.StartTimeUtc > now,
                409, "HANDOVER_LOCKED", "This offer is no longer open.");
            offer.Status = input.Decision!;
            offer.Version = Guid.NewGuid();
            offer.DecidedBy = scope.Account.Id;
            offer.DecidedAtUtc = now;
            if (offer.Status == "ACCEPTED")
            {
                row.ClubId = clubId;
                row.ClubName = scope.Club.Name;
                row.Version = Guid.NewGuid();
                row.UpdatedAtUtc = now;
            }
            return Results.Ok(View(row, offer));
        }, ct);
    }

    private static async Task<IResult> GetReport(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var (_, row) = await Own(id, db, http, ct);
        var report = await db.Set<ActivityClosingReport>().AsNoTracking().SingleOrDefaultAsync(x => x.ActivityId == id, ct);
        return Results.Ok(new { available = row.Status == ActivityStatuses.Completed, report = report is null ? null : Report(report) });
    }

    private static async Task<IResult> Operations(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var row = await Find(id, db, ct);
        Manager(await OrganizerScope(row, db, http, ct));
        var sessions = await db.Sessions.AsNoTracking().Where(x => x.ActivityId == id).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.StartTimeUtc, x.EndTimeUtc, x.OpensAt, x.ClosesAt, x.Location, x.Status }).ToListAsync(ct);
        var participants = await db.ActivityParticipants.AsNoTracking().Where(x => x.ActivityId == id).OrderBy(x => x.UserId)
            .Select(x => new { x.UserId, x.FullName }).ToListAsync(ct);
        var verified = await db.ActivityAttendances.AsNoTracking().Where(x => x.ActivityId == id && x.VerificationStatus == "VERIFIED")
            .Select(x => new { x.SessionId, x.UserId }).ToListAsync(ct);
        var duties = await db.Set<CampusActivityDuty>().AsNoTracking().Where(x => x.ActivityId == id).OrderBy(x => x.UserId)
            .Select(x => new { x.Id, x.UserId, x.DisplayName, x.Role, x.Responsibility, x.Version, x.ConfirmedAtUtc }).ToListAsync(ct);
        return Results.Ok(new { row.Version, row.Status, row.PublicationStatus, sessions, participants, verified, duties });
    }

    private static async Task<IResult> AssignDuty(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var activity = await Find(id, db, ct);
        var scope = await OrganizerScope(activity, db, http, ct);
        Manager(scope);
        var input = await Body<DutyInput>(http, ct);
        var responsibility = input.Responsibility?.Trim();
        Require(input.UserId > 0 && input.Role is "STAFF" or "HOST" && responsibility is { Length: > 0 and <= 500 },
            400, "INVALID_DUTY", "Choose a student, role and responsibility.");
        var user = (await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([input.UserId], ct)).GetValueOrDefault(input.UserId);
        Require(user is { IsActive: true, IsLocked: false } && user.CampusCode == activity.OrganizerCampusCode
            && user.DisplayName.Length is > 0 and <= 200, 400, "DUTY_STUDENT_UNAVAILABLE", "Choose an active student from the activity campus.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(activity).ReloadAsync(ct);
            Require(activity.ClubId == scope.Club.ClubId && activity.Status == ActivityStatuses.Scheduled && Now(http) < activity.EndTimeUtc,
                409, "ACTIVITY_DUTIES_LOCKED", "Only the current organizer can assign work before the activity ends.");
            Require(!await db.Set<CampusActivityDuty>().AnyAsync(x => x.ActivityId == id && x.UserId == input.UserId && x.Role == input.Role, ct),
                409, "DUTY_ALREADY_ASSIGNED", "This student already has this role.");
            var duty = new CampusActivityDuty
            {
                Id = Guid.NewGuid(),
                Version = Guid.NewGuid(),
                ActivityId = id,
                UserId = input.UserId,
                Role = input.Role!,
                DisplayName = user!.DisplayName,
                Responsibility = responsibility!,
                AssignedBy = scope.Account.Id,
                AssignedAtUtc = Now(http)
            };
            db.Add(duty);
            return Results.Ok(new { duty.Id, duty.Version });
        }, ct);
    }

    private static async Task<IResult> ConfirmDuty(int id, Guid dutyId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var activity = await Find(id, db, ct);
        var scope = await OrganizerScope(activity, db, http, ct);
        Manager(scope);
        var input = await Body<VersionInput>(http, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(activity).ReloadAsync(ct);
            var duty = await db.Set<CampusActivityDuty>().SingleOrDefaultAsync(x => x.ActivityId == id && x.Id == dutyId, ct);
            if (duty is null) return Results.NotFound();
            var now = Now(http);
            Require(activity.ClubId == scope.Club.ClubId && activity.Status != ActivityStatuses.Cancelled && now >= activity.StartTimeUtc
                && activity.PublicationStatus is "PUBLISHED" or "CLOSED" or "COMPLETED",
                409, "ACTIVITY_DUTIES_LOCKED", "Only the organizer can confirm work after the published activity starts.");
            if (activity.Status == ActivityStatuses.Completed)
            {
                var completed = await ActivityClosingReportEndpoints.CompletionTime(db, activity, ct);
                var deadline = await ActivityClosingReportEndpoints.Deadline(db, scope.Club.CampusCode, completed, ct);
                Require(now < deadline && !await db.Set<ActivityClosingReport>().AnyAsync(x => x.ActivityId == id
                    && (x.Status == "APPROVED" || x.Status == "FINALIZED"), ct),
                    409, "ACTIVITY_RESULTS_LOCKED", "The activity results are already locked.");
            }
            Require(duty.UserId != scope.Account.Id, 403, "INDEPENDENT_DUTY_CONFIRMATION_REQUIRED", "A student cannot confirm their own duty.");
            Require(duty.Version == input.ExpectedVersion && duty.ConfirmedAtUtc is null,
                409, "DUTY_ALREADY_DECIDED", "Reload this assignment.");
            duty.ConfirmedAtUtc = now;
            duty.ConfirmedBy = scope.Account.Id;
            duty.Version = Guid.NewGuid();
            return Results.Ok(new { duty.Id, duty.Version, duty.ConfirmedAtUtc });
        }, ct);
    }

    private static async Task<IResult> AddSession(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var row = await Find(id, db, ct);
        var scope = await OrganizerScope(row, db, http, ct);
        Manager(scope);
        var input = await Body<CampusSessionInput>(http, ct);
        var location = input.Location?.Trim();
        Require(location is { Length: > 0 and <= 200 }, 400, "INVALID_CAMPUS_SESSION", "Provide a session location.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(row).ReloadAsync(ct);
            var now = Now(http);
            Require(row.Version == input.ExpectedVersion && row.Status == ActivityStatuses.Scheduled
                && row.PublicationStatus is "DRAFT" or "PUBLISHED", 409, "CAMPUS_ACTIVITY_LOCKED", "Reload the current activity.");
            Require(now < input.StartTimeUtc && input.StartTimeUtc >= row.StartTimeUtc && input.EndTimeUtc <= row.EndTimeUtc
                && input.StartTimeUtc <= input.OpensAt && input.OpensAt < input.ClosesAt && input.ClosesAt <= input.EndTimeUtc,
                400, "INVALID_CAMPUS_SESSION", "The session and attendance window must fit the activity schedule.");
            var session = new ActivitySession
            {
                ActivityId = id,
                SeriesId = Guid.NewGuid(),
                SemesterId = row.SemesterId!,
                Location = location!,
                StartTimeUtc = input.StartTimeUtc,
                EndTimeUtc = input.EndTimeUtc,
                OpensAt = input.OpensAt,
                ClosesAt = input.ClosesAt,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedByUserId = scope.Account.Id
            };
            row.Version = Guid.NewGuid();
            row.UpdatedAtUtc = now;
            db.Add(session);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { session.Id, activityVersion = row.Version });
        }, ct);
    }

    private static async Task<IResult> VerifyAttendance(int id, int sessionId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var activity = await Find(id, db, ct);
        var scope = await OrganizerScope(activity, db, http, ct);
        Manager(scope);
        var input = await Body<AttendanceInput>(http, ct);
        Require(input.UserId > 0 && input.UserId != scope.Account.Id, 400, "INDEPENDENT_ATTENDANCE_REQUIRED", "Select another registered participant.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(activity).ReloadAsync(ct);
            var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId && x.ActivityId == id, ct);
            if (session is null) return Results.NotFound();
            var now = Now(http);
            Require(activity.Status == ActivityStatuses.Scheduled && activity.PublicationStatus == "PUBLISHED"
                && session.Status == "SCHEDULED" && now >= session.OpensAt && now < session.ClosesAt,
                409, "ATTENDANCE_CLOSED", "Attendance can only be recorded during the open session window.");
            var participant = await db.ActivityParticipants.AsNoTracking().SingleOrDefaultAsync(x => x.ActivityId == id && x.UserId == input.UserId, ct);
            Require(participant is not null, 409, "REGISTRATION_REQUIRED", "The student must have an active registration.");
            Require(!await db.ActivityAttendances.AnyAsync(x => x.SessionId == sessionId && x.UserId == input.UserId, ct),
                409, "ATTENDANCE_EXISTS", "Attendance has already been recorded.");
            var attendance = new ActivityAttendance
            {
                ActivityId = id,
                SessionId = sessionId,
                UserId = input.UserId,
                FullName = participant!.FullName,
                AttendanceDate = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).Date),
                Status = AttendanceStatuses.Present,
                VerificationStatus = "VERIFIED",
                VerificationMethod = "MANUAL_REVIEW",
                CheckedInAtUtc = now,
                CheckedInByUserId = scope.Account.Id,
                CreatedAtUtc = now
            };
            db.Add(attendance);
            if (activity.ScoringSnapshotJson is { } blueprint)
            {
                var saved = JsonSerializer.Deserialize<ExperienceSourceSnapshot>(blueprint, ActivitySessionEndpoints.JsonOptions)!;
                var source = saved with
                {
                    ClubId = activity.ClubId,
                    ActivityVersion = activity.Version,
                    SessionId = session.Id,
                    Title = activity.Title,
                    SessionVersion = session.Version,
                    StartTimeUtc = session.StartTimeUtc,
                    EndTimeUtc = session.EndTimeUtc
                };
                attendance.RecognitionStatus = "PENDING";
                await db.SaveChangesAsync(ct);
                db.AddOutboxMessage(new AttendanceVerifiedEvent(Guid.NewGuid(), now, input.UserId, attendance.Id,
                    "CAMPUS_ORGANIZER", scope.Account.Id, source), ExperienceRoutingKeys.AttendanceVerified);
            }
            return Results.Ok(new { sessionId, input.UserId, status = "VERIFIED" });
        }, ct);
    }

    private static async Task<IResult> CreateReport(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var (actor, row) = await Own(id, db, http, ct);
        var input = await Body<ReportInput>(http, ct);
        var summary = input.Summary?.Trim();
        Require(summary is { Length: > 0 and <= 4000 }, 400, "REPORT_SUMMARY_REQUIRED", "Provide a report summary.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(row).ReloadAsync(ct);
            Require(row.ClubId == 0 && row.Status == ActivityStatuses.Completed, 409, "CAMPUS_REPORT_UNAVAILABLE", "Complete the campus activity first.");
            Require(!await db.Set<ActivityClosingReport>().AnyAsync(x => x.ActivityId == id, ct),
                409, "CLOSING_REPORT_EXISTS", "A report already exists.");
            var report = new ActivityClosingReport
            {
                ActivityId = id,
                Status = "DRAFT",
                Version = Guid.NewGuid(),
                SubmittedBy = actor.Id,
                SubmittedAtUtc = Now(http),
                Summary = summary!
            };
            db.Add(report);
            return Results.Ok(Report(report));
        }, ct);
    }

    private static async Task<IResult> FinalizeReport(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var (actor, row) = await Own(id, db, http, ct);
        var input = await Body<VersionInput>(http, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(row).ReloadAsync(ct);
            Require(row.ClubId == 0 && row.Status == ActivityStatuses.Completed, 409, "CAMPUS_REPORT_UNAVAILABLE", "Complete the campus activity first.");
            var report = await db.Set<ActivityClosingReport>().SingleOrDefaultAsync(x => x.ActivityId == id, ct);
            if (report is null) return Results.NotFound();
            Require(report.Status == "DRAFT" && report.Version == input.ExpectedVersion, 409, "CAMPUS_REPORT_LOCKED", "Only the current draft can be finalized.");
            report.Status = "FINALIZED";
            report.ReviewedBy = actor.Id;
            report.ReviewedAtUtc = Now(http);
            report.Version = Guid.NewGuid();
            var completedAt = await ActivityClosingReportEndpoints.CompletionTime(db, row, ct);
            var deadline = await ActivityClosingReportEndpoints.Deadline(db, actor.CampusCode, completedAt, ct);
            await ClosingReportXpGate.ReleaseAsync(row,
                report.ReviewedAtUtc.Value < deadline ? report.ReviewedAtUtc.Value : deadline, db,
                http.RequestServices.GetRequiredService<ClubExperienceAuthorityClient>(), ct);
            await ActivityCertificateEndpoints.IssueAsync(row, report.ReviewedAtUtc.Value, db, ct);
            return Results.Ok(Report(report));
        }, ct);
    }

    private static async Task<(InternalUserProfile Actor, ClubActivity Row)> Own(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var actor = await CampusReviewer(http, ct);
        var row = await Find(id, db, ct);
        Require(row.OrganizerCampusCode == actor.CampusCode && row.ClubId == 0, 403, "CAMPUS_SCOPE_DENIED", "This activity belongs to another organizer.");
        return (actor, row);
    }

    private static async Task<ClubActivity> Find(int id, ActivityDbContext db, CancellationToken ct)
    {
        var row = await db.Activities.SingleOrDefaultAsync(a => a.Id == id && a.OrganizerCampusCode != null, ct);
        Require(row is not null, 404, "CAMPUS_ACTIVITY_NOT_FOUND", "Campus activity does not exist.");
        return row!;
    }

    private static async Task Validate(Plan input, HttpContext http, CancellationToken ct)
    {
        Require(input.Title?.Trim().Length is > 0 and <= 200 && input.Description?.Trim().Length is > 0 and <= 2000
            && input.Location?.Trim().Length is > 0 and <= 200 && input.SemesterId?.Trim().Length is > 0 and <= 40
            && input.StartTimeUtc > Now(http) && input.EndTimeUtc > input.StartTimeUtc && input.Capacity is null or (> 0 and <= 100000),
            400, "INVALID_CAMPUS_ACTIVITY", "Check the title, description, location, capacity and dates.");
        var semester = await http.RequestServices.GetRequiredService<SemesterCatalogClient>().ResolveAsync(input.SemesterId!.Trim(), http, ct);
        Require(semester is { IsActive: true, StartDate: not null, EndDate: not null }
            && input.StartTimeUtc >= semester.StartDate && input.EndTimeUtc < semester.EndDate.Value.AddDays(1),
            400, "INVALID_CAMPUS_SEMESTER", "The activity must fit the active semester.");
    }

    private static void Apply(ClubActivity row, Plan plan, DateTimeOffset now)
    {
        row.Title = plan.Title!.Trim();
        row.Description = plan.Description!.Trim();
        row.Location = plan.Location!.Trim();
        row.SemesterId = plan.SemesterId!.Trim();
        row.StartTimeUtc = plan.StartTimeUtc;
        row.EndTimeUtc = plan.EndTimeUtc;
        row.Capacity = plan.Capacity;
        row.Version = Guid.NewGuid();
        row.UpdatedAtUtc = now;
    }

    private static object View(ClubActivity a, ActivityHandover? h) => new
    {
        a.Id,
        a.Version,
        a.Title,
        a.Description,
        a.Location,
        a.SemesterId,
        a.StartTimeUtc,
        a.EndTimeUtc,
        a.ClubId,
        a.ClubName,
        a.Capacity,
        a.Status,
        a.PublicationStatus,
        a.RegistrationOpensAt,
        a.RegistrationClosesAt,
        publishAtUtc = a.CampusPublishAtUtc,
        a.PolicyVersion,
        a.EffortTier,
        TagSelection = DiscoveryTags.Read(a.TagSelectionJson),
        handover = h is null ? null : new { h.ClubId, h.ClubName, h.Version, h.Status, h.OfferedAtUtc, h.DecidedAtUtc }
    };

    private static object Report(ActivityClosingReport r) => new { r.Version, r.Status, r.Summary, r.ReviewedAtUtc };
    private sealed record Plan(string? Title, string? Description, string? Location, string? SemesterId,
        DateTimeOffset StartTimeUtc, DateTimeOffset EndTimeUtc, int? Capacity, Guid? ExpectedVersion,
        DiscoveryTagSelection? TagSelection = null);
    private sealed record PublishInput(Guid ExpectedVersion, DateTimeOffset RegistrationOpensAt, DateTimeOffset RegistrationClosesAt,
        DateTimeOffset? PublishAtUtc);
    private sealed record OfferInput(Guid ExpectedVersion, int ClubId);
    private sealed record DecisionInput(Guid ExpectedVersion, string? Decision);
    private sealed record ReportInput(string? Summary);
    private sealed record VersionInput(Guid ExpectedVersion);
    private sealed record CampusSessionInput(Guid ExpectedVersion, DateTimeOffset StartTimeUtc, DateTimeOffset EndTimeUtc,
        DateTimeOffset OpensAt, DateTimeOffset ClosesAt, string? Location);
    private sealed record AttendanceInput(int UserId);
    private sealed record CampusXpInput(Guid ExpectedVersion, Guid PolicyVersion, string? Tier, IReadOnlyList<PillarWeight>? PillarWeights);
    private sealed record DutyInput(int UserId, string? Role, string? Responsibility);
}
