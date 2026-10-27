using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdminService.Data;
using AdminService.Endpoints;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Services;

/// <summary>Reconciles every active account from authoritative service APIs.
/// No SQL joins across service databases, no ranking in a public GET.</summary>
public sealed class RecommendationFeedWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<RecommendationFeedWorker> logger, RecommendationWakeSignal signal) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Recommendation:Enabled")) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Recommendation source reconciliation failed; next sweep will retry."); }
            try { await signal.WaitAsync(TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Recommendation:RefreshSeconds", 60), 15, 3600)), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AdminDbContext>();
        var semesters = await db.Semesters.AsNoTracking().Where(x => x.IsActive).Take(2).ToArrayAsync(ct);
        if (semesters.Length != 1) return; // Never choose an ambiguous semester.
        var semester = semesters[0];
        var rows = await db.Set<AcademicCalendarConfiguration>().AsNoTracking().Where(x => x.SemesterId == semester.Id).ToArrayAsync(ct);
        var calendars = new List<RecommendationCalendar>();
        if (int.TryParse(configuration[$"Workspace:SemesterAliases:{semester.Id:D}:Year"], out var year)
            && configuration[$"Workspace:SemesterAliases:{semester.Id:D}:Term"] is { } term)
            foreach (var row in rows)
            {
                var projected = AcademicCalendarEndpoints.Project(year, term, row.CampusCode, semester, row);
                if (projected.Availability == "AVAILABLE") calendars.Add(new(semester.Id, row.CampusCode, projected.Version, row.Block10StartDate, row.Block3StartDate));
            }
        var http = services.GetRequiredService<IHttpClientFactory>().CreateClient("RecommendationAuth");
        var token = configuration["Security:InternalWorkflowToken"];
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Source authority is unconfigured.");
        var after = 0;
        while (!ct.IsCancellationRequested)
        {
            var readStartedAt = DateTimeOffset.UtcNow;
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/internal/recommendation/students")
            { Content = JsonContent.Create(new RecommendationExportQuery(after, 50, calendars)) };
            request.Headers.Add("X-Internal-Workflow-Token", token);
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadFromJsonAsync<RecommendationStudentPage>(ct) ?? throw new HttpRequestException("Invalid source page.");
            if (page.Items.Count == 0) break;
            foreach (var student in page.Items)
            {
                try { await ExportAsync(student, semester.SemesterCode, readStartedAt, services, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception) { logger.LogWarning("One recommendation feed failed; retaining its previous expiry and retrying next sweep."); }
            }
            if (page.NextUserId <= after) throw new HttpRequestException("Non-advancing source page.");
            after = page.NextUserId;
        }
    }

    private async Task ExportAsync(RecommendationStudentSource student, string semester, DateTimeOffset readStartedAt, IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AdminDbContext>();
        var read = await StudentExperienceReadService.ReadAsync(db, student.UserId, semester, "PERIOD", null,
            "Student", string.Empty, configuration["Experience:Environment"] ?? "PROJECT", ct);
        if (read.Radar is null) throw new HttpRequestException("Authoritative radar is unavailable.");
        var radar = read.Radar;
        var authority = await services.GetRequiredService<ClubExperienceAuthorityClient>().StudentScopeAsync(student.UserId, student.CampusCode, ct);
        var source = services.GetRequiredService<StudentExperienceSourceClient>();
        var activities = new List<StudentActivityItem>();
        string? scopeVersion = null;
        for (var page = 1; page <= 500; page++)
        {
            var result = await source.CandidatesAsync(new(student.UserId, page, 100, semester), ct);
            if (result.ScopeVersion != authority.Version || scopeVersion is not null && result.ScopeVersion != scopeVersion)
                throw new HttpRequestException("Eligibility changed during export.");
            scopeVersion = result.ScopeVersion;
            activities.AddRange(result.Items.Where(x => x.IsEligible && x.CanRegister && x.RecommendationSource is { Sessions.Count: > 0 }));
            if (page >= result.TotalPages) break;
            if (page == 500) throw new HttpRequestException("Candidate feed limit exceeded.");
        }
        var now = DateTimeOffset.UtcNow;
        var interests = MapInterests(student.Interests);
        var mappedMajor = student.DeclaredMajor is null ? null : configuration[$"Recommendation:MajorMappings:{student.DeclaredMajor}"];
        var catalogVersion = Hash(JsonSerializer.Serialize(activities.OrderBy(x => x.Id).Select(x => new { x.Id, x.RecommendationSource, x.PillarWeights })) + authority.Version);
        object Meta(string version) => new { version, asOf = now };
        var versions = new Dictionary<string, object>
        {
            ["account"] = Meta(student.AccountVersion), ["catalog"] = Meta(catalogVersion),
            ["preferences"] = Meta(student.PreferenceVersion.ToString()), ["schedule"] = Meta(student.ScheduleVersion),
            ["experience"] = new { radar.PolicyVersion, radar.LedgerRevision, asOf = now }
        };
        var membership = authority.Clubs.Where(x => x.IsApprovedMember).Select(x => x.ClubId).ToArray();
        var candidateRows = activities.OrderBy(x => x.Id).Select(x => new
        {
            x.Id, version = x.RecommendationSource!.Version, x.ClubId, student.CampusCode,
            published = true, cancelled = false, registrationOpen = true, x.RecommendationSource.RegistrationDeadline,
            capacityAvailable = true, eligibleUserIds = new[] { student.UserId },
            interestIds = MapInterests(configuration.GetSection($"Recommendation:Activities:{x.Id}:Interests").Get<string[]>() ?? []),
            majorCodes = configuration.GetSection($"Recommendation:Activities:{x.Id}:MajorCodes").Get<string[]>() ?? [],
            pillarWeights = x.PillarWeights.Where(p => RadarPillars.CoreSix.Contains(p.Pillar)).ToDictionary(p => p.Pillar, p => p.Weight),
            x.RecommendationSource.Sessions, scheduleAvailability = "KNOWN"
        }).Where(x => x.pillarWeights.Count > 0 && Math.Abs(x.pillarWeights.Values.Sum() - 1) < 0.000001m).ToArray();
        // Club matching requires explicit recruitment + scoring metadata. Category
        // labels alone are not sufficient evidence to fabricate a club profile.
        var clubRows = authority.Clubs.Where(x => x.CampusCode == student.CampusCode && x.IsRecruiting)
            .Select(x => new
            {
                id = x.ClubId, version = x.Version.ToString(), x.CampusCode, active = true, recruiting = true,
                interestIds = MapInterests(configuration.GetSection($"Recommendation:Clubs:{x.ClubCode}:Interests").Get<string[]>() ?? []),
                majorCodes = configuration.GetSection($"Recommendation:Clubs:{x.ClubCode}:MajorCodes").Get<string[]>() ?? [],
                pillarWeights = configuration.GetSection($"Recommendation:Clubs:{x.ClubCode}:PillarWeights").Get<Dictionary<string, double>>() ?? [],
                meetingIntervals = Array.Empty<RecommendationBusyInterval>(), scheduleAvailability = "UNKNOWN"
            }).Where(x => x.pillarWeights.Count > 0 && x.pillarWeights.Keys.All(RadarPillars.CoreSix.Contains)
                && x.pillarWeights.Values.All(w => w is >= 0 and <= 1) && Math.Abs(x.pillarWeights.Values.Sum() - 1) < 0.000001).ToArray();
        var snapshot = new
        {
            schemaVersion = "1.0", snapshotId = Guid.NewGuid().ToString(), asOf = now, expiresAt = now.AddMinutes(15),
            semesterId = semester, radar.PolicyVersion,
            students = new[] { new
            {
                student.UserId, student.CampusCode,
                preferences = new { version = student.PreferenceVersion.ToString(), interestIds = interests, customInterests = Array.Empty<string>(), declaredMajorCode = mappedMajor },
                schedule = new { version = student.ScheduleVersion, availability = student.ScheduleAvailability, student.BusyIntervals },
                experience = new { semesterId = semester, radar.PolicyVersion, radar.LedgerRevision, isAuthoritative = true,
                    pillars = radar.RadarAxes.Select(x => new { x.Pillar, score = ExperienceIndexCalculator.CalculateSaturatedScore(x.RawPointsSum, x.Tau) }) },
                memberships = membership, activityRegistrations = Array.Empty<int>(), walletSignals = new Dictionary<string, object>(), sourceVersions = versions
            } },
            clubs = clubRows, activities = candidateRows
        };
        await services.GetRequiredService<RecommendationClient>().FeedAsync(snapshot, readStartedAt, ct);
    }

    private string[] MapInterests(IEnumerable<string> labels)
    {
        string[] canonical = ["technology", "design", "business", "arts", "sports", "community", "languages", "leadership"];
        return labels.Select(x => canonical.Contains(x) ? x : configuration[$"Recommendation:InterestMappings:{x}"])
            .Where(x => x is not null && canonical.Contains(x)).Select(x => x!).Distinct().Order().Take(30).ToArray();
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
