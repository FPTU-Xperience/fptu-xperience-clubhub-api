using System.Linq.Expressions;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Endpoints;

public static class StudentActivityEndpoints
{
    public static void MapStudentActivityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v2/activities/feed", FeedAsync).RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Student activities");
        app.MapGet("/api/v2/activities/{activityId:int}", DetailAsync).RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Student activities");
        app.MapPost("/api/internal/experience/student/candidates", async (StudentCandidateQuery request, ActivityDbContext db,
            HttpContext http, CurrentUserProfileClient users, ClubExperienceAuthorityClient clubs, IConfiguration config, CancellationToken ct) =>
        {
            if (!VerifiedExperienceRules.InternalRequest(http, config)) return Results.Forbid();
            if (request.UserId <= 0 || request.ActivityIds is { Count: > 100 }
                || request.ActivityIds?.Any(x => x <= 0) == true || request.SessionIds is { Count: 0 or > 100 }
                || request.SessionIds?.Any(x => x <= 0) == true) return Results.BadRequest();
            return await ReadAsync(request.UserId, null, null, null, request.SemesterId, request.Page, request.PageSize,
                db, http, users, clubs, ct, candidates: true, activityIds: request.ActivityIds, includeRegistered: request.IncludeRegistered,
                sessionIds: request.SessionIds);
        }).AllowAnonymous().ExcludeFromDescription();
        app.MapPost("/api/internal/experience/student/counters", CountersAsync).AllowAnonymous().ExcludeFromDescription();
        app.MapPost("/api/internal/experience/student/source-status", SourceStatusAsync).AllowAnonymous().ExcludeFromDescription();
    }

    private static Task<IResult> FeedAsync(int? clubId, string? search, string? status, string? semesterId,
        int? page, int? pageSize, ActivityDbContext db, HttpContext http, CurrentUserProfileClient users,
        ClubExperienceAuthorityClient clubs, CancellationToken ct)
        => ReadAsync(http.User.GetUserId(), clubId, search, status, semesterId, page ?? 1, pageSize ?? 50,
            db, http, users, clubs, ct);

    private static Task<IResult> DetailAsync(int activityId, ActivityDbContext db, HttpContext http,
        CurrentUserProfileClient users, ClubExperienceAuthorityClient clubs, CancellationToken ct)
        => ReadAsync(http.User.GetUserId(), null, null, null, null, 1, 1, db, http, users, clubs, ct, detailId: activityId);

    private static async Task<IResult> ReadAsync(int userId, int? clubId, string? search, string? status, string? semesterId,
        int page, int size, ActivityDbContext db, HttpContext http, CurrentUserProfileClient users,
        ClubExperienceAuthorityClient clubs, CancellationToken ct, bool candidates = false,
        int? detailId = null, IReadOnlyList<int>? activityIds = null, bool includeRegistered = false, IReadOnlyList<int>? sessionIds = null)
    {
        if (page < 1 || size is < 1 or > 100 || clubId is <= 0 || search?.Length > 200 || semesterId?.Length > 40)
            return ApiOperations.Error(400, "INVALID_DISCOVERY_QUERY", "Check filters and pagination.");
        var state = status?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(state) && state is not ("UPCOMING" or "LIVE"))
            return ApiOperations.Error(400, "INVALID_ACTIVITY_STATUS", "The actionable feed supports UPCOMING and LIVE.");
        InternalUserProfile? user; StudentClubScope authority;
        try
        {
            user = (await users.GetAsync([userId], ct)).GetValueOrDefault(userId);
            if (user is null || !user.IsActive || user.IsLocked) return Results.Forbid();
            authority = await clubs.StudentScopeAsync(userId, user.CampusCode, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        { return ApiOperations.Error(503, "DISCOVERY_AUTHORITY_UNAVAILABLE", "Account or club visibility authority is unavailable."); }
        var now = http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var campusIds = authority.Clubs.Where(x => x.CampusCode == user.CampusCode).Select(x => x.ClubId).ToArray();
        var privateIds = authority.Clubs.Where(x => x.CanViewPrivate).Select(x => x.ClubId).ToArray();
        var query = db.Activities.AsNoTracking().Where(x => (x.PublicationStatus == "PUBLISHED" || x.PublicationStatus == "CLOSED")
            && (privateIds.Contains(x.ClubId) || x.DiscoveryVisibility == "CAMPUS_PUBLIC" && campusIds.Contains(x.ClubId)));
        if (detailId.HasValue) query = query.Where(x => x.Id == detailId);
        else query = query.Where(x => x.Status != ActivityStatuses.Cancelled && x.Status != ActivityStatuses.Completed
            && (db.Sessions.Any(s => s.ActivityId == x.Id && s.Status == "SCHEDULED" && s.EndTimeUtc > now)
                || !db.Sessions.Any(s => s.ActivityId == x.Id) && x.EndTimeUtc > now));
        if (clubId.HasValue) query = query.Where(x => x.ClubId == clubId);
        if (!string.IsNullOrWhiteSpace(search)) { var text = search.Trim(); query = query.Where(x => x.Title.Contains(text) || x.Description.Contains(text)); }
        if (!string.IsNullOrWhiteSpace(semesterId)) query = query.Where(x => x.SemesterId == semesterId);
        if (sessionIds is not null) query = query.Where(x => db.Sessions.Any(s => s.ActivityId == x.Id
            && sessionIds.Contains(s.Id) && s.Status == "SCHEDULED" && s.StartTimeUtc > now));
        var timed = query.Select(x => new
        {
            Activity = x,
            Start = db.Sessions.Where(s => s.ActivityId == x.Id && s.Status == "SCHEDULED" && s.EndTimeUtc > now
                && (sessionIds == null || sessionIds.Contains(s.Id)))
                .OrderBy(s => s.StartTimeUtc).Select(s => (DateTimeOffset?)s.StartTimeUtc).FirstOrDefault() ?? x.StartTimeUtc
        });
        if (state == "UPCOMING") timed = timed.Where(x => x.Start > now);
        if (state == "LIVE") timed = timed.Where(x => x.Start <= now);
        if (candidates)
        {
            query = timed.Where(x => x.Start > now)
                .Select(x => x.Activity).Where(x => includeRegistered && db.ActivityParticipants.Any(p => p.ActivityId == x.Id && p.UserId == userId)
                    || x.PublicationStatus == "PUBLISHED" && x.RegistrationOpensAt <= now && x.RegistrationClosesAt > now
                        && (x.Capacity == null || x.RegisteredCount < x.Capacity));
            query = query.Where(Eligibility(authority, campusIds));
            timed = query.Select(x => new
            {
                Activity = x,
                Start = db.Sessions.Where(s => s.ActivityId == x.Id && s.Status == "SCHEDULED" && s.EndTimeUtc > now
                    && (sessionIds == null || sessionIds.Contains(s.Id)))
                    .OrderBy(s => s.StartTimeUtc).Select(s => (DateTimeOffset?)s.StartTimeUtc).FirstOrDefault() ?? x.StartTimeUtc
            });
            if (activityIds is not null) timed = timed.Where(x => activityIds.Contains(x.Activity.Id));
        }
        var total = await timed.CountAsync(ct);
        var rows = await timed.OrderBy(x => x.Start).ThenBy(x => x.Activity.Id)
            .Skip((int)Math.Min((long)(page - 1) * size, int.MaxValue)).Take(size).Select(x => x.Activity).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var sessions = await db.Sessions.AsNoTracking().Where(x => ids.Contains(x.ActivityId) && x.Status == "SCHEDULED" && x.EndTimeUtc > now
            && (sessionIds == null || sessionIds.Contains(x.Id)))
            .OrderBy(x => x.StartTimeUtc).ThenBy(x => x.Id).ToListAsync(ct);
        var registered = await db.ActivityParticipants.AsNoTracking().Where(x => x.UserId == userId && ids.Contains(x.ActivityId))
            .Select(x => x.ActivityId).ToListAsync(ct);
        var checkedIn = await db.ActivityAttendances.AsNoTracking().Where(x => x.UserId == userId
            && x.VerificationStatus == "VERIFIED" && ids.Contains(x.ActivityId)).Select(x => x.SessionId).ToListAsync(ct);
        var metadata = authority.Clubs.ToDictionary(x => x.ClubId);
        var items = rows.Select(a =>
        {
            var session = sessions.FirstOrDefault(s => s.ActivityId == a.Id);
            var club = metadata[a.ClubId];
            var eligible = a.Eligibility == "CAMPUS_PUBLIC" ? club.CampusCode == user.CampusCode
                : club.IsApprovedMember && club.JoinedAt <= a.StartTimeUtc;
            var canRegister = eligible && !registered.Contains(a.Id) && a.PublicationStatus == "PUBLISHED"
                && a.RegistrationOpensAt <= now && a.RegistrationClosesAt > now && (a.Capacity == null || a.RegisteredCount < a.Capacity);
            var start = session?.StartTimeUtc ?? a.StartTimeUtc; var end = session?.EndTimeUtc ?? a.EndTimeUtc;
            var action = canRegister ? "REGISTER" : eligible && registered.Contains(a.Id) && session is not null
                && session.OpensAt <= now && session.ClosesAt >= now && !checkedIn.Contains(session.Id) ? "CHECK_IN" : "VIEW";
            return new StudentActivityItem(a.Id, a.ClubId, club.ClubCode, club.ClubName, a.Title, a.Description,
                a.CoverImageUrl, start, end, session?.Location ?? a.Location,
                a.Status == ActivityStatuses.Cancelled ? "CANCELLED" : end <= now ? "COMPLETED" : start > now ? "UPCOMING" : "LIVE",
                action, canRegister, eligible, a.SemesterId, session?.Id, a.Version, session?.Version, a.PolicyVersion,
                JsonSerializer.Deserialize<List<PillarWeight>>(a.PillarWeightsJson, ActivitySessionEndpoints.JsonOptions) ?? []);
        }).ToArray();
        if (detailId.HasValue) return items.Length == 0 ? Results.NotFound() : Results.Ok(items[0]);
        return Results.Ok(new StudentActivityPage(items, page, size, total, (int)Math.Ceiling((decimal)total / size), now, authority.Version));
    }

    private static Expression<Func<ClubActivity, bool>> Eligibility(StudentClubScope scope, int[] campusIds)
    {
        var p = Expression.Parameter(typeof(ClubActivity), "activity");
        var audience = Expression.Property(p, nameof(ClubActivity.Eligibility));
        var clubId = Expression.Property(p, nameof(ClubActivity.ClubId));
        Expression predicate = Expression.AndAlso(Expression.Equal(audience, Expression.Constant("CAMPUS_PUBLIC")),
            Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(int)], Expression.Constant(campusIds), clubId));
        foreach (var member in scope.Clubs.Where(x => x.IsApprovedMember && x.JoinedAt.HasValue))
        {
            var joined = Expression.AndAlso(Expression.Equal(clubId, Expression.Constant(member.ClubId)),
                Expression.GreaterThanOrEqual(Expression.Property(p, nameof(ClubActivity.StartTimeUtc)), Expression.Constant(member.JoinedAt!.Value)));
            predicate = Expression.OrElse(predicate, Expression.AndAlso(Expression.Equal(audience, Expression.Constant("CLUB_MEMBER")), joined));
        }
        return Expression.Lambda<Func<ClubActivity, bool>>(predicate, p);
    }

    private static async Task<IResult> SourceStatusAsync(StudentSourceStatusQuery request, ActivityDbContext db,
        HttpContext http, IConfiguration configuration, CancellationToken ct)
    {
        if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
        if (request.Sources is not { Count: > 0 and <= 100 } || request.Sources.Any(x => x is null || x.ActivityId <= 0 || x.SessionId <= 0))
            return Results.BadRequest();
        var sessionIds = request.Sources.Select(x => x.SessionId).Distinct().ToArray();
        var available = await db.Sessions.AsNoTracking().Where(x => sessionIds.Contains(x.Id) && x.Status != "CANCELLED"
            && x.Activity.Status != ActivityStatuses.Cancelled && x.Activity.PublicationStatus != "CANCELLED"
            && x.Activity.PublicationStatus != "DRAFT")
            .Select(x => new StudentSourceReference(x.ActivityId, x.Id)).ToListAsync(ct);
        var requested = request.Sources.ToHashSet();
        return Results.Ok(new StudentSourceStatus(available.Where(requested.Contains).ToArray(), DateTimeOffset.UtcNow));
    }

    private static async Task<IResult> CountersAsync(StudentCounterQuery request, ActivityDbContext db,
        HttpContext http, IConfiguration configuration, CancellationToken ct)
    {
        if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
        if (request.UserId <= 0 || request.Scope is not ("PERIOD" or "CUMULATIVE")
            || request.Scope == "PERIOD" && string.IsNullOrWhiteSpace(request.SemesterId)) return Results.BadRequest();
        var query = db.ActivityAttendances.AsNoTracking().Where(x => x.UserId == request.UserId && x.SessionId != null
            && x.VerificationStatus == "VERIFIED" && (x.Status == AttendanceStatuses.Present || x.Status == AttendanceStatuses.Late)
            && x.Activity.Status != ActivityStatuses.Cancelled
            && db.Sessions.Any(s => s.Id == x.SessionId && s.Status != "CANCELLED"));
        if (request.Scope == "PERIOD") query = query.Where(x => db.Sessions.Any(s => s.Id == x.SessionId && s.SemesterId == request.SemesterId));
        var rows = await query.OrderBy(x => x.Id).Select(x => new { x.Id, x.SessionId, x.Version, ActivityVersion = x.Activity.Version }).ToListAsync(ct);
        var count = rows.Select(x => x.SessionId).Distinct().Count();
        var fingerprint = $"{request.UserId}:{request.Scope}:{request.SemesterId}:" + string.Join("|", rows.Select(x => $"{x.Id}:{x.SessionId}:{x.Version}:{x.ActivityVersion}"));
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
        return Results.Ok(new StudentCounters(request.UserId, count, null, DateTimeOffset.UtcNow, version));
    }
}

