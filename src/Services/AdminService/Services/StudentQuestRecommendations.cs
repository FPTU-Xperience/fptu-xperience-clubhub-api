using System.Text.Json;
using AdminService.Contracts;
using AdminService.Data;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Services;

internal static class StudentQuestRecommendations
{
    internal static async Task<IResult> ReadAsync(AdminDbContext db, HttpContext http, IConfiguration configuration,
        StudentExperienceSourceClient sources, string? semester, string? status, string? category, string? search,
        string? campusCode, int? page, int? pageSize, CancellationToken ct)
    {
        if (http.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
        var userId = http.User.GetUserId();
        if (userId <= 0) return Results.Unauthorized();
        var profiles = http.RequestServices.GetService<CurrentUserProfileClient>();
        if (profiles is null) return ApiOperations.Error(503, "PROFILE_SERVICE_UNAVAILABLE", "Canonical student profile is unavailable.");
        InternalUserProfile? profile;
        try { profile = (await profiles.GetAsync([userId], ct)).GetValueOrDefault(userId); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        { return ApiOperations.Error(503, "PROFILE_SERVICE_UNAVAILABLE", "Canonical student profile is unavailable."); }
        if (profile is null || !profile.IsActive || profile.IsLocked) return Results.Forbid();
        var read = await StudentExperienceReadService.ReadAsync(db, userId, semester, "PERIOD", null,
            profile.DisplayName, profile.Email, configuration["Experience:Environment"] ?? "PROJECT", ct);
        if (read.Radar is null) return ApiOperations.Error(read.StatusCode, read.Code!, read.Message!);
        var radar = read.Radar;
        var axes = radar.RadarAxes.OrderBy(x => ExperienceIndexCalculator.CalculateSaturatedScore(x.RawPointsSum, x.Tau))
            .ThenBy(x => Array.IndexOf(RadarPillars.CoreSix, x.Pillar)).Select(x => x.Pillar).ToArray();
        var now = DateTimeOffset.UtcNow;
        var query = db.Quests.AsNoTracking().Where(x => x.Status == "published" && x.CriteriaSnapshotJson != null
            && x.CriteriaVersion != null && x.SemesterCode == radar.SemesterId && (x.CampusCode == CampusCodes.Global || x.CampusCode == profile.CampusCode)
            && (x.DeadlineUtc == null || x.DeadlineUtc > now) && !db.QuestParticipants.Any(p => p.QuestId == x.Id && p.StudentUserId == userId));
        if (!string.IsNullOrWhiteSpace(status) && status != "all") query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(category) && category != "all") query = query.Where(x => x.Category == category);
        if (!string.IsNullOrWhiteSpace(campusCode) && campusCode != CampusCodes.Global)
            query = query.Where(x => x.CampusCode == CampusCodes.Global || x.CampusCode == campusCode);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Title.Contains(search) || x.Description.Contains(search));
        var candidates = new List<StudentActivityItem>();
        string? scopeVersion = null;
        try
        {
            for (var current = 1; ; current++)
            {
                var source = await sources.CandidatesAsync(new(userId, current, 100, radar.SemesterId, IncludeRegistered: true), ct);
                if (scopeVersion is not null && scopeVersion != source.ScopeVersion)
                    return ApiOperations.Error(503, "QUEST_SOURCE_CHANGED", "Eligibility changed while reading sources; retry.");
                scopeVersion = source.ScopeVersion;
                candidates.AddRange(source.Items.Where(x => x.IsEligible && x.StartTime > source.AsOf));
                if (current >= source.TotalPages) break;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        { return ApiOperations.Error(503, "ACTIVITY_SERVICE_UNAVAILABLE", "Eligible quest sources are unavailable."); }
        var rows = await query.Select(x => new
        {
            Quest = x,
            Joined = db.QuestParticipants.Count(p => p.QuestId == x.Id),
            Completed = db.QuestParticipants.Count(p => p.QuestId == x.Id && p.Status == "Completed")
        }).ToListAsync(ct);
        var approvedIds = await db.ApprovedExperiencePolicies.AsNoTracking().Where(x => x.Scope == "PERIOD"
            && x.SemesterId == radar.SemesterId && x.Environment == (configuration["Experience:Environment"] ?? "PROJECT"))
            .Select(x => x.Id).ToListAsync(ct);
        // A recurring activity's first session may differ from the quest's selected sessions.
        // Resolve these selectors through the same visibility/eligibility authority, in bounded batches.
        try
        {
            var selectors = rows.Select(x => QuestResponse.From(x.Quest).Criteria)
                .Where(x => x?.EligibleSessionIds is { Count: > 0 and <= 100 }).Select(x => x!)
                .GroupBy(x => string.Join(",", x.EligibleSessionIds!.OrderBy(i => i)));
            foreach (var selector in selectors)
                foreach (var activityBatch in selector.SelectMany(x => x.EligibleActivityIds).Distinct().Chunk(100))
                {
                    var result = await sources.CandidatesAsync(new(userId, 1, 100, radar.SemesterId, activityBatch,
                        IncludeRegistered: true, SessionIds: selector.First().EligibleSessionIds), ct);
                    if (scopeVersion is not null && scopeVersion != result.ScopeVersion)
                        return ApiOperations.Error(503, "QUEST_SOURCE_CHANGED", "Eligibility changed while reading sources; retry.");
                    candidates.AddRange(result.Items.Where(x => x.IsEligible && x.StartTime > result.AsOf));
                }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        { return ApiOperations.Error(503, "ACTIVITY_SERVICE_UNAVAILABLE", "Eligible quest sessions are unavailable."); }
        var ranked = rows.Select(x => new { row = x, response = QuestResponse.From(x.Quest, x.Joined, x.Completed) })
            .Where(x => x.response.Criteria is { } c && approvedIds.Contains(c.PolicyVersion) && axes.Contains(c.TargetPillar))
            .Select(x => new
            {
                x.response,
                axisRank = Array.IndexOf(axes, x.response.Criteria!.TargetPillar),
                source = candidates.Where(a => x.response.Criteria.EligibleActivityIds.Contains(a.Id)
                    && a.PillarWeights.Any(p => p.Pillar == x.response.Criteria.TargetPillar && p.Weight > 0)
                    && (x.response.Criteria.EligibleSessionIds == null || a.NextSessionId.HasValue && x.response.Criteria.EligibleSessionIds.Contains(a.NextSessionId.Value)))
                    .OrderBy(a => a.StartTime).ThenBy(a => a.Id).FirstOrDefault()
            })
            .Where(x => x.source is not null).OrderBy(x => x.axisRank).ThenBy(x => x.source!.StartTime)
            .ThenBy(x => x.source!.Id).ThenBy(x => x.response.Id, StringComparer.Ordinal).ToArray();
        const string rule = "LOW_AXIS_QUEST_START_ID_V1";
        var currentPage = page ?? 1; var size = pageSize ?? 20; var total = ranked.Length;
        var items = ranked.Skip((int)Math.Min((long)(currentPage - 1) * size, int.MaxValue)).Take(size).Select((x, i) => x.response with
        {
            CreatedByName = null,
            SourceLabel = "LOW_AXIS_RULE",
            Rank = (int)Math.Min((long)(currentPage - 1) * size + i + 1, int.MaxValue),
            RuleVersion = rule,
            Reason = $"Verified work toward {x.response.TargetPillar}, prioritized using the student's lowest core-axis score.",
            RadarSnapshot = new(radar.PolicyVersion, radar.FormulaVersion, radar.LedgerRevision, radar.AsOf, radar.SemesterId)
        }).ToArray();
        http.Response.Headers["X-Total-Count"] = total.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return page.HasValue || pageSize.HasValue ? Results.Ok(new
        {
            items,
            page = currentPage,
            pageSize = size,
            totalItems = total,
            totalPages = (int)Math.Ceiling((decimal)total / size),
            available = true,
            sourceLabel = "LOW_AXIS_RULE",
            ruleVersion = rule,
            scopeVersion
        })
            : Results.Ok(items);
    }
}
