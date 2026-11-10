using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Data;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityActionHelpers;

namespace ActivityService.Endpoints;

internal static class ActivityFinalRoleEndpoints
{
    private const int PageSize = 20;

    internal static void MapActivityFinalRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var routes = SafeGroup(app, "/api/activities").WithTags("Activity results");
        routes.MapGet("/{activityId:int}/final-roles", Get);
        routes.MapPut("/{activityId:int}/final-roles/{userId:int}", Save);
    }

    private static async Task<IResult> Get(int activityId, int? page, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var (activity, scope) = await Load(activityId, db, http, ct);
        var currentPage = page ?? 1;
        Require(currentPage is >= 1 and <= 10000, 400, "INVALID_RESULT_PAGE", "Choose a valid results page.");
        var available = activity.Status == ActivityStatuses.Completed;
        if (!available) return Results.Ok(new { available, items = Array.Empty<object>(), hasMore = false });
        var locked = await IsLocked(activity, scope, db, http, ct);
        var selected = db.Set<ActivityFinalRole>().AsNoTracking().Where(x => x.ActivityId == activityId);
        var confirmedUsers = selected.Select(x => x.UserId);
        var ids = locked || !scope.Manager ? confirmedUsers : confirmedUsers.Union(
            db.ActivityParticipants.Where(x => x.ActivityId == activityId).Select(x => x.UserId))
            .Union(db.Set<ActivityStaffAssignment>().Where(x => x.ActivityId == activityId && x.Status != "REMOVED").Select(x => x.UserId))
            .Union(db.Set<CampusActivityDuty>().Where(x => x.ActivityId == activityId).Select(x => x.UserId));
        var users = await ids.OrderBy(x => x).Skip((currentPage - 1) * PageSize).Take(PageSize + 1).ToListAsync(ct);
        var currentIds = users.Take(PageSize).ToArray();
        var saved = await selected.Where(x => currentIds.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, ct);
        var candidates = locked || !scope.Manager
            ? new Dictionary<int, Candidate>() : await Candidates(activityId, currentIds, db, ct);
        return Results.Ok(new
        {
            available, locked, canConfirm = !locked && scope.Manager, hasMore = users.Count > PageSize,
            items = currentIds.Select(id =>
            {
                var row = saved.GetValueOrDefault(id);
                var candidate = candidates.GetValueOrDefault(id);
                return new
                {
                    userId = id, displayName = row?.DisplayName ?? candidate?.Name,
                    role = row?.Role, version = row?.Version, confirmedAtUtc = row?.ConfirmedAtUtc,
                    eligibleRoles = candidate?.Roles ?? []
                };
            })
        });
    }

    private static async Task<IResult> Save(int activityId, int userId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var (activity, scope) = await Load(activityId, db, http, ct);
        Manager(scope);
        var input = await Body<RoleInput>(http, ct);
        Require(userId > 0 && input.Role is "PARTICIPANT" or "STAFF" or "HOST",
            400, "INVALID_FINAL_ROLE", "Choose a supported role for a valid student.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(activity).ReloadAsync(ct);
            Require(activity.Status == ActivityStatuses.Completed && !await IsLocked(activity, scope, db, http, ct),
                409, "ACTIVITY_RESULTS_LOCKED", "Results can be confirmed only after completion and before approval or the deadline.");
            var candidates = await Candidates(activityId, [userId], db, ct);
            Require(candidates.TryGetValue(userId, out var candidate) && candidate.Roles.Contains(input.Role!),
                409, "FINAL_ROLE_NOT_VERIFIED", "Verify participation or assignment completion for this role first.");
            var selected = await db.Set<ActivityFinalRole>().SingleOrDefaultAsync(x => x.ActivityId == activityId && x.UserId == userId, ct);
            Require(selected?.Version == input.ExpectedVersion, 409, "STALE_FINAL_ROLE", "Reload the student's result.");
            if (selected is null)
            {
                selected = new ActivityFinalRole { ActivityId = activityId, UserId = userId };
                db.Add(selected);
            }
            selected.Role = input.Role!;
            selected.DisplayName = candidate!.Name;
            selected.Version = Guid.NewGuid();
            selected.ConfirmedBy = scope.Account.Id;
            selected.ConfirmedAtUtc = Now(http);
            return Results.Ok(new { selected.UserId, selected.Role, selected.Version, selected.ConfirmedAtUtc });
        }, ct);
    }

    private static async Task<Dictionary<int, Candidate>> Candidates(int activityId, int[] users, ActivityDbContext db, CancellationToken ct)
    {
        if (users.Length == 0) return [];
        var participants = await db.ActivityParticipants.AsNoTracking().Where(x => x.ActivityId == activityId && users.Contains(x.UserId))
            .Select(x => new { x.UserId, x.FullName }).ToListAsync(ct);
        var attended = await db.ActivityAttendances.AsNoTracking()
            .Where(x => x.ActivityId == activityId && x.VerificationStatus == "VERIFIED" && users.Contains(x.UserId))
            .Select(x => x.UserId).Distinct().ToListAsync(ct);
        var assignments = await db.Set<ActivityStaffAssignment>().AsNoTracking()
            .Where(x => x.ActivityId == activityId && x.Status != "REMOVED" && users.Contains(x.UserId))
            .Select(x => new
            {
                x.UserId, x.DisplayName, x.AssignmentKind,
                Verified = db.Set<ActivityActionCompletion>().Any(c => c.AssignmentId == x.Id && c.Status == "VERIFIED")
            }).ToListAsync(ct);
        var duties = await db.Set<CampusActivityDuty>().AsNoTracking().Where(x => x.ActivityId == activityId && users.Contains(x.UserId))
            .Select(x => new { x.UserId, x.DisplayName, x.Role, x.ConfirmedAtUtc }).ToListAsync(ct);
        var names = participants.Select(x => (x.UserId, Name: x.FullName)).Concat(assignments.Select(x => (x.UserId, Name: x.DisplayName)))
            .Concat(duties.Select(x => (x.UserId, Name: x.DisplayName)))
            .GroupBy(x => x.UserId).ToDictionary(x => x.Key, x => x.First().Name);
        var roles = participants.Where(x => attended.Contains(x.UserId)).Select(x => (x.UserId, Role: "PARTICIPANT"))
            .Concat(assignments.Where(x => x.Verified && x.AssignmentKind is "STAFF" or "HOST").Select(x => (x.UserId, Role: x.AssignmentKind)))
            .Concat(duties.Where(x => x.ConfirmedAtUtc.HasValue).Select(x => (x.UserId, x.Role)))
            .ToLookup(x => x.UserId, x => x.Role);
        return names.ToDictionary(x => x.Key, x => new Candidate(x.Value, roles[x.Key].Distinct().ToArray()));
    }

    private static async Task<bool> IsLocked(ClubActivity activity, Scope scope, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var completed = await ActivityClosingReportEndpoints.CompletionTime(db, activity, ct);
        var deadline = await ActivityClosingReportEndpoints.Deadline(db, scope.Club.CampusCode, completed, ct);
        return Now(http) >= deadline || await db.Set<ActivityClosingReport>()
            .AnyAsync(x => x.ActivityId == activity.Id && (x.Status == "APPROVED" || x.Status == "FINALIZED"), ct);
    }

    private static async Task<(ClubActivity Activity, Scope Scope)> Load(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var activity = await db.Activities.SingleOrDefaultAsync(x => x.Id == id, ct);
        Require(activity is not null, 404, "ACTIVITY_NOT_FOUND", "Activity does not exist.");
        var scope = await CampusActivityEndpoints.OrganizerScope(activity!, db, http, ct);
        Require(scope.Manager || scope.Reviewer, 403, "RESULT_ACCESS_DENIED", "Current club management or review authority is required.");
        return (activity!, scope);
    }

    private sealed record Candidate(string Name, string[] Roles);
    private sealed record RoleInput(Guid? ExpectedVersion, string? Role);
}
