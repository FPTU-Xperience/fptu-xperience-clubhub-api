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

public static class ProcessedRecommendationEndpoints
{
    public static void MapProcessedRecommendationEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/v2/recommendations/clubs", async (int? limit, string? semesterId, string? scope,
            AdminDbContext db, HttpContext http, RecommendationClient client, StudentExperienceSourceClient sources,
            ClubExperienceAuthorityClient clubs, CurrentUserProfileClient profiles, CancellationToken ct) =>
            await ReadAsync("clubs", limit, semesterId, scope, db, http, client, sources, clubs, profiles, ct))
            .RequireAuthorization(AdminPolicies.AnyActor).WithTags("Student recommendations");

    internal static async Task<IResult> ReadAsync(string kind, int? limit, string? semesterId, string? scope,
        AdminDbContext db, HttpContext http, RecommendationClient client, StudentExperienceSourceClient sources,
        ClubExperienceAuthorityClient clubs, CurrentUserProfileClient profiles, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (limit is < 1 or > 6 || scope is not (null or "PERIOD"))
            return ApiOperations.Error(400, "INVALID_RECOMMENDATION_QUERY", "Use PERIOD and limit 1-6.");
        if (!client.Enabled) return ApiOperations.Error(503, "RECOMMENDATION_DISABLED", "Processed recommendation delivery is disabled.");
        var code = semesterId?.Trim().ToUpperInvariant();
        var semester = await db.Semesters.AsNoTracking().Where(x => code == null ? x.IsActive : x.SemesterCode == code)
            .Take(2).ToArrayAsync(ct);
        if (semester.Length != 1) return ApiOperations.Error(400, "SEMESTER_REQUIRED", "Select one configured semester.");
        var userId = http.User.GetUserId();
        try
        {
            var user = (await profiles.GetAsync([userId], ct)).GetValueOrDefault(userId);
            if (user is not { IsActive: true, IsLocked: false }) return Results.Forbid();
            var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
            var environment = configuration["Experience:Environment"] ?? "PROJECT";
            var now = DateTimeOffset.UtcNow;
            if (kind == "activities" && !await db.ApprovedExperiencePolicies.AsNoTracking().AnyAsync(x =>
                x.SemesterId == semester[0].SemesterCode && x.Scope == "PERIOD" && x.Environment == environment
                && x.ApprovedAt <= now && (x.EffectiveAt == null || x.EffectiveAt <= now), ct))
                return await StudentExperienceEndpoints.ReadBaselineRecommendationsAsync(limit, semester[0].SemesterCode,
                    "PERIOD", db, http, configuration, sources, ct);
            var result = await client.ReadAsync(userId, semester[0].SemesterCode, kind, ct);
            var available = result.GetProperty("available").GetBoolean();
            var status = result.GetProperty("status").GetString();
            if (!available) return Results.Ok(new { items = Array.Empty<object>(), available = false, status, sourceLabel = "PROCESSED_RECOMMENDATION" });
            if (result.GetProperty("userId").GetInt32() != userId || result.GetProperty("semesterId").GetString() != semester[0].SemesterCode
                || result.GetProperty("type").GetString() != kind || result.GetProperty("expiresAt").GetDateTimeOffset() <= DateTimeOffset.UtcNow)
                throw new HttpRequestException("Invalid processed recommendation scope.");
            var ranking = result.GetProperty("items").EnumerateArray().ToArray();
            if (ranking.Length > 20 || ranking.Select(x => x.GetProperty("id").GetInt32()).Distinct().Count() != ranking.Length)
                throw new HttpRequestException("Invalid processed ranking.");
            var items = new List<object>();
            var filtered = false;
            var snapshot = result.GetProperty("snapshotId").GetString();
            var rule = result.GetProperty("ruleVersion").GetString();
            if (kind == "activities")
            {
                var ids = ranking.Select(x => x.GetProperty("id").GetInt32()).ToArray();
                var live = ids.Length == 0 ? null : await sources.CandidatesAsync(new(userId, 1, 100, semester[0].SemesterCode, ids), ct);
                var byId = live?.Items.ToDictionary(x => x.Id) ?? [];
                foreach (var item in ranking)
                {
                    if (!byId.TryGetValue(item.GetProperty("id").GetInt32(), out var activity) || !activity.IsEligible || !activity.CanRegister
                        || activity.StartTime <= DateTimeOffset.UtcNow || activity.RecommendationSource?.Version != item.GetProperty("candidateVersion").GetString())
                    { filtered = true; continue; }
                    items.Add(new
                    {
                        recommendationId = RecommendationId(userId, snapshot, kind, activity.Id),
                        rank = items.Count + 1,
                        targetPillar = item.GetProperty("targetPillar"),
                        reason = "Precomputed content recommendation.",
                        reasons = item.GetProperty("reasons"),
                        score = item.GetProperty("score"),
                        suggestedSessionIds = item.GetProperty("suggestedSessionIds"),
                        ruleVersion = rule,
                        sourceLabel = "PROCESSED_RECOMMENDATION",
                        activity
                    });
                }
            }
            else
            {
                var live = await clubs.StudentScopeAsync(userId, user.CampusCode, ct);
                foreach (var item in ranking)
                {
                    var club = live.Clubs.SingleOrDefault(x => x.ClubId == item.GetProperty("id").GetInt32());
                    if (club is null || !club.IsRecruiting || club.IsApprovedMember || club.CampusCode != user.CampusCode || club.Version.ToString() != item.GetProperty("candidateVersion").GetString())
                    { filtered = true; continue; }
                    items.Add(new
                    {
                        recommendationId = RecommendationId(userId, snapshot, kind, club.ClubId),
                        rank = items.Count + 1,
                        score = item.GetProperty("score"),
                        reasons = item.GetProperty("reasons"),
                        club
                    });
                }
            }
            if (filtered)
            {
                await client.InvalidateAsync([userId], ct);
                http.RequestServices.GetService<RecommendationWakeSignal>()?.Wake();
            }
            return Results.Ok(new
            {
                items = items.Take(limit ?? 6),
                available = true,
                status = filtered ? "PARTIAL" : status,
                ruleVersion = rule,
                sourceLabel = "PROCESSED_RECOMMENDATION",
                snapshotId = snapshot,
                generatedAt = result.GetProperty("generatedAt"),
                expiresAt = result.GetProperty("expiresAt"),
                sourceVersions = result.GetProperty("sourceVersions"),
                warnings = result.GetProperty("warnings")
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return ApiOperations.Error(503, "RECOMMENDATION_UNAVAILABLE", "Processed recommendations or live eligibility authority are unavailable."); }
    }

    private static string RecommendationId(int user, string? snapshot, string kind, int item) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{user}:{snapshot}:{kind}:{item}")));
}
