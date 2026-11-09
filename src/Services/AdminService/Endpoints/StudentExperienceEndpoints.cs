using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdminService.Data;
using AdminService.Security;
using AdminService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Endpoints;

public static class StudentExperienceEndpoints
{
    public static void MapStudentExperienceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/users/me/experience-summary", SummaryAsync).RequireAuthorization(AdminPolicies.AnyActor).WithTags("Personal experience");
        app.MapGet("/api/v2/recommendations/activities", RecommendationsAsync).RequireAuthorization(AdminPolicies.AnyActor).WithTags("Student recommendations");
    }

    private static async Task<IResult> SummaryAsync(string? semesterId, string? scope, DateTimeOffset? asOf,
        AdminDbContext db, HttpContext http, IConfiguration configuration, StudentExperienceSourceClient sources, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        var read = await StudentExperienceReadService.ReadAsync(db, userId, semesterId, scope, asOf,
            http.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "Student", string.Empty,
            configuration["Experience:Environment"] ?? "PROJECT", ct);
        if (read.Radar is null) return ApiOperations.Error(read.StatusCode, read.Code!, read.Message!);
        var radar = read.Radar;
        async Task<StudentCounters?> CounterAsync(string service)
        {
            if (asOf.HasValue) return null; // These current-state services do not retain historical membership/attendance projections.
            try
            {
                var result = await sources.CountersAsync(service, new(userId, radar.Scope, radar.SemesterId), ct);
                return result.UserId == userId ? result : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException) { return null; }
        }
        var results = await Task.WhenAll(CounterAsync("Activity"), CounterAsync("Club"));
        var attendance = results[0]; var clubs = results[1];
        return Results.Ok(new
        {
            userId,
            radar.Scope,
            radar.SemesterId,
            recognizedContributionCount = radar.ApprovedClubContributionsCount + radar.ApprovedDeclarationsCount,
            rawExperiencePoints = radar.Pillars.Sum(x => x.RawPointsSum),
            verifiedAttendanceCount = attendance?.VerifiedAttendanceCount,
            approvedClubCount = clubs?.ApprovedClubCount,
            availability = new
            {
                contributions = true,
                radar = true,
                attendance = attendance?.VerifiedAttendanceCount is not null,
                clubs = clubs?.ApprovedClubCount is not null,
                externalSourceReason = asOf.HasValue ? "HISTORICAL_SNAPSHOT_UNSUPPORTED" : "CURRENT_SOURCE"
            },
            sourceSnapshots = new
            {
                contributions = new { radar.AsOf, radar.LedgerRevision },
                attendance = attendance is null ? null : new { attendance.AsOf, attendance.Version },
                clubs = clubs is null ? null : new { clubs.AsOf, clubs.Version }
            },
            radar.PolicyVersion,
            radar.FormulaVersion,
            radar.ContributionFormulaVersion,
            radar.LedgerRevision,
            radar.AsOf,
            version = $"{radar.Version}:{attendance?.Version ?? "UNAVAILABLE"}:{clubs?.Version ?? "UNAVAILABLE"}"
        });
    }

    private static async Task<IResult> RecommendationsAsync(int? limit, string? semesterId, string? scope,
        AdminDbContext db, HttpContext http, IConfiguration configuration, StudentExperienceSourceClient sources, CancellationToken ct)
    {
        if (configuration.GetValue<bool>("Recommendation:Enabled"))
            return await ProcessedRecommendationEndpoints.ReadAsync("activities", limit, semesterId, scope, db, http,
                http.RequestServices.GetRequiredService<RecommendationClient>(), sources,
                http.RequestServices.GetRequiredService<ClubExperienceAuthorityClient>(),
                http.RequestServices.GetRequiredService<CurrentUserProfileClient>(), ct);
        return await ReadBaselineRecommendationsAsync(limit, semesterId, scope, db, http, configuration, sources, ct);
    }

    internal static async Task<IResult> ReadBaselineRecommendationsAsync(int? limit, string? semesterId, string? scope,
        AdminDbContext db, HttpContext http, IConfiguration configuration, StudentExperienceSourceClient sources, CancellationToken ct)
    {
        if (limit is < 1 or > 6) return ApiOperations.Error(400, "INVALID_LIMIT", "Use limit 1–6.");
        var userId = http.User.GetUserId();
        var read = await StudentExperienceReadService.ReadAsync(db, userId, semesterId, scope, null, "Student", string.Empty,
            configuration["Experience:Environment"] ?? "PROJECT", ct);
        // Missing XP approval disables radar personalization, not activity discovery.
        // No unapproved policy, synthetic radar or ledger effect is created here.
        var basic = read.Radar is null && read.Code == "SCORE_POLICY_UNAPPROVED";
        if (read.Radar is null && !basic) return ApiOperations.Error(read.StatusCode, read.Code!, read.Message!);
        var radar = read.Radar;
        var selectedSemester = radar?.SemesterId;
        var selectedScope = radar?.Scope ?? (string.IsNullOrWhiteSpace(scope) ? "PERIOD" : scope.Trim().ToUpperInvariant());
        if (basic && selectedScope == "PERIOD")
        {
            var key = Guid.TryParse(semesterId, out var parsed) ? parsed : Guid.Empty;
            var code = semesterId?.Trim().ToUpperInvariant();
            var periods = await db.Semesters.AsNoTracking()
                .Where(x => code == null || code == "" ? x.IsActive : x.Id == key || x.SemesterCode == code)
                .Select(x => x.SemesterCode).Take(2).ToArrayAsync(ct);
            if (periods.Length != 1)
                return ApiOperations.Error(409, "RECOMMENDATION_CONTEXT_CHANGED", "Semester configuration changed. Reload recommendations.");
            selectedSemester = periods[0];
        }
        var axes = radar?.RadarAxes.OrderBy(x => ExperienceIndexCalculator.CalculateSaturatedScore(x.RawPointsSum, x.Tau))
            .ThenBy(x => Array.IndexOf(RadarPillars.CoreSix, x.Pillar)).ToArray() ?? [];
        var candidates = new List<StudentActivityItem>();
        DateTimeOffset sourceAsOf = default; string? scopeVersion = null;
        try
        {
            for (var page = 1; ; page++)
            {
                var result = await sources.CandidatesAsync(new(userId, page, 100, selectedSemester), ct);
                if (scopeVersion is not null && scopeVersion != result.ScopeVersion)
                    return ApiOperations.Error(503, "RECOMMENDATION_SOURCE_CHANGED", "Club eligibility changed while reading candidates. Retry.");
                scopeVersion = result.ScopeVersion; sourceAsOf = result.AsOf;
                candidates.AddRange(result.Items.Where(x => x.IsEligible && x.CanRegister && x.StartTime > result.AsOf));
                if (page >= result.TotalPages) break;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        { return ApiOperations.Error(503, "RECOMMENDATION_SOURCE_UNAVAILABLE", "Eligible activity candidates are unavailable."); }
        var ranked = candidates.DistinctBy(x => x.Id).Select(activity => new
        {
            activity,
            axisRank = Array.FindIndex(axes, axis => activity.PillarWeights.Any(w => w.Pillar == axis.Pillar && w.Weight > 0))
        }).Where(x => basic || x.axisRank >= 0).OrderBy(x => basic ? 0 : x.axisRank).ThenBy(x => x.activity.StartTime).ThenBy(x => x.activity.Id)
            .Take(limit ?? 6).ToArray();
        var rule = basic ? "ELIGIBLE_UPCOMING_V1" : "LOW_AXIS_UNROUNDED_START_ID_V1";
        var sourceLabel = basic ? "ELIGIBLE_UPCOMING" : "LOW_AXIS_RULE";
        var rankingVersion = radar?.Version ?? $"{selectedScope}:{selectedSemester ?? "ALL"}:NO_APPROVED_POLICY";
        var items = ranked.Select((x, index) => new
        {
            recommendationId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{rankingVersion}:{x.activity.Id}:{x.activity.Version}:{rule}"))),
            rank = index + 1,
            targetPillar = basic ? null : axes[x.axisRank].Pillar,
            reason = basic ? "This activity is eligible and open for registration, ordered by its next start time. XP personalization is unavailable."
                : $"This eligible activity contributes to {axes[x.axisRank].Pillar}, prioritized by the student's lowest core-axis score.",
            ruleVersion = rule,
            sourceLabel,
            x.activity,
            radarSnapshot = radar is null ? null : new { radar.PolicyVersion, radar.FormulaVersion, radar.LedgerRevision, radar.AsOf, radar.Scope, radar.SemesterId }
        }).ToArray();
        return Results.Ok(new
        {
            items,
            available = true,
            ruleVersion = rule,
            sourceLabel,
            sourceAsOf,
            scopeVersion,
            mode = basic ? "BASIC" : "PERSONALIZED",
            personalizationAvailable = !basic,
            personalizationUnavailableReason = basic ? "SCORE_POLICY_UNAPPROVED" : null,
            scope = selectedScope,
            semesterId = selectedSemester
        });
    }
}

