using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AdminService.Data;
using AdminService.Security;
using AdminService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using static AdminService.Services.VerifiedExperienceService;

namespace AdminService.Endpoints;

public static class ClubExperienceStatisticsEndpoints
{
    public static void MapClubExperienceStatisticsEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/clubs/{clubId:int}/experience-statistics", GetAsync)
            .RequireAuthorization(AdminPolicies.AnyActor).WithTags("Club experience statistics");

    private static async Task<IResult> GetAsync(int clubId, string semesterId, AdminDbContext db, HttpContext http,
        ClubAccessClient access, IHttpClientFactory clients, IConfiguration configuration, CancellationToken ct)
    {
        ClubAccessSnapshot? club;
        try { club = (await access.GetMyAccessAsync(http.GetBearerToken(), ct, true)).SingleOrDefault(x => x.ClubId == clubId && x.CanManage); }
        catch (HttpRequestException) { return ApiOperations.Error(503, "CLUB_SERVICE_UNAVAILABLE", "Club authorization is unavailable."); }
        if (club is null && !http.User.IsInRole(AuthRoles.Admin)) return Results.Forbid();
        var isGuid = Guid.TryParse(semesterId, out var id);
        var semester = await db.Semesters.AsNoTracking().SingleOrDefaultAsync(x => x.SemesterCode == semesterId || isGuid && x.Id == id, ct);
        if (semester is null) return ApiOperations.Error(404, "SEMESTER_NOT_FOUND", "Unknown semester.");
        ActivityExperienceMetrics metrics; InternalActivityExperienceMetrics activitySources; ClubExperienceTargets? plan;
        try
        {
            using var metricsRequest = Request($"api/internal/experience/clubs/{clubId}/metrics?semesterId={Uri.EscapeDataString(semester.SemesterCode)}", http, configuration);
            using var metricsResponse = await clients.CreateClient("ExperienceActivity").SendAsync(metricsRequest, ct);
            metricsResponse.EnsureSuccessStatusCode(); activitySources = (await metricsResponse.Content.ReadFromJsonAsync<InternalActivityExperienceMetrics>(ct))!;
            metrics = activitySources.Metrics;
            using var planRequest = Request($"api/clubs/{clubId}/semester-plans/{Uri.EscapeDataString(semester.SemesterCode)}", http, configuration);
            using var planResponse = await clients.CreateClient("ExperienceClub").SendAsync(planRequest, ct);
            if (planResponse.StatusCode == HttpStatusCode.NotFound) plan = null;
            else
            {
                planResponse.EnsureSuccessStatusCode(); var json = await planResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
                plan = new ClubExperienceTargets(json.GetProperty("version").GetGuid(), json.GetProperty("targetActivityCount").GetInt32(),
                    json.GetProperty("targetSessionCount").GetInt32(), json.GetProperty("targetUniqueParticipantCount").GetInt32());
            }
        }
        catch (HttpRequestException) { return ApiOperations.Error(503, "EXPERIENCE_DEPENDENCY_UNAVAILABLE", "The activity metrics or club plan is unavailable."); }
        var contributions = await db.Contributions.AsNoTracking().Where(x => x.ClubId == clubId && x.SemesterId == semester.SemesterCode && x.Status == "APPROVED").ToListAsync(ct);
        var contributionIds = db.Contributions.Where(x => x.ClubId == clubId && x.SemesterId == semester.SemesterCode).Select(x => x.Id);
        var allocations = await (from effect in db.ExperienceLedgerEffects.AsNoTracking()
                                 join allocation in db.ExperienceLedgerAllocations.AsNoTracking() on effect.Id equals allocation.EffectId
                                 where contributionIds.Contains(effect.ContributionId)
                                 select new { allocation.Pillar, allocation.Amount, effect.PolicyVersion }).ToListAsync(ct);
        var points = ExperienceCategories.All.ToDictionary(x => x, x => allocations.Where(a => a.Pillar == x).Sum(a => a.Amount));
        var roles = ContributionRoles.All.ToDictionary(x => x, x => contributions.Count(c => c.EffectiveAttributesJson is not null && Read<ContributionAttributes>(c.EffectiveAttributesJson).Role == x));
        var participation = contributions.Select(x => new VerifiedParticipation(x.UserId, x.OccurredAt)).Concat(activitySources.CurrentParticipation).ToArray();
        var users = participation.Select(x => x.UserId).Distinct().ToArray();
        metrics = metrics with { UniqueVerifiedParticipantCount = users.Length };
        var priorSources = await db.Contributions.AsNoTracking().Where(x => users.Contains(x.UserId) && x.Status == "APPROVED")
            .Select(x => new { x.UserId, x.OccurredAt }).ToListAsync(ct);
        // Include already approved legacy external/+1 history in first participation.
        var external = await db.SelfDeclarations.AsNoTracking().Where(x => users.Contains(x.StudentId) && x.Status == "Approved"
            && !db.Contributions.Any(c => c.Id == x.Id))
            .Select(x => new { x.StudentId, x.StartDate }).ToListAsync(ct);
        var first = participation.GroupBy(x => x.UserId).Count(group =>
        {
            var start = group.Min(x => x.OccurredAt);
            return !priorSources.Any(x => x.UserId == group.Key && x.OccurredAt < start)
                && !activitySources.FirstVerifiedInProject.Any(x => x.UserId == group.Key && x.OccurredAt < start)
                && !external.Any(x => x.StudentId == group.Key && new DateTimeOffset(x.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)) < start);
        });
        TargetProgress Progress(int actual, int target) => new(actual, target, target == 0 ? null : decimal.Round((decimal)actual / target, 4));
        var progress = new Dictionary<string, TargetProgress>
        {
            ["activities"] = Progress(metrics.PublishedActivityCount, plan?.TargetActivityCount ?? 0),
            ["sessions"] = Progress(metrics.CompletedSessionCount, plan?.TargetSessionCount ?? 0),
            ["uniqueParticipants"] = Progress(metrics.UniqueVerifiedParticipantCount, plan?.TargetUniqueParticipantCount ?? 0)
        };
        var revision = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Json(new
        {
            metrics,
            firstTimeParticipantCount = first,
            roles,
            versions = contributions.OrderBy(x => x.Id).Select(x => new { x.Id, x.Version }),
            points,
            effects = await db.ExperienceLedgerEffects.Where(x => contributionIds.Contains(x.ContributionId)).MaxAsync(x => (long?)x.Id, ct) ?? 0
        }))));
        return Results.Ok(new ClubExperienceStatistics(1, clubId, club?.ClubName ?? $"Club {clubId}", semester.SemesterCode,
            DateTimeOffset.UtcNow, "VERIFIED_NET_LEDGER", metrics, plan, progress, contributions.Count, first, roles, points,
            allocations.Select(x => x.PolicyVersion).Distinct().Order().ToArray())
        { LedgerRevision = revision });
    }

    internal static HttpRequestMessage Request(string path, HttpContext http, IConfiguration configuration)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (AuthenticationHeaderValue.TryParse(http.Request.Headers.Authorization, out var auth)) request.Headers.Authorization = auth;
        var secret = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(secret)) throw new HttpRequestException("Internal workflow token is not configured.");
        request.Headers.Add("X-Internal-Workflow-Token", secret); return request;
    }
}
