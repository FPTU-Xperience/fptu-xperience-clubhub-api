using System.Text.Json;
using AdminService.Contracts;
using AdminService.Data;
using AdminService.Security;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Endpoints;

public static class WorkspaceCatalogEndpoints
{
    public static void MapWorkspaceCatalogEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/internal/experience/workspace-catalog", ReadAsync)
            .RequireAuthorization(AdminPolicies.AnyActor).ExcludeFromDescription();

    private static async Task<IResult> ReadAsync(AdminDbContext db, HttpContext http, IConfiguration configuration, CancellationToken ct)
    {
        if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
        var environment = configuration["Experience:Environment"] ?? "PROJECT";
        if (environment is not ("PROJECT" or "DEMO"))
            return ApiOperations.Error(503, "POLICY_ENVIRONMENT_UNCONFIGURED", "Configure a supported policy environment.");
        var now = http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var rows = await db.Semesters.AsNoTracking().OrderByDescending(x => x.StartDate).ThenBy(x => x.SemesterCode).ToListAsync(ct);
        var activeCount = rows.Count(x => x.IsActive);
        var currentAvailability = activeCount == 1 ? "AVAILABLE" : activeCount == 0 ? "UNCONFIGURED" : "AMBIGUOUS";
        var semesters = rows.Select(x =>
        {
            var alias = configuration.GetSection($"Workspace:SemesterAliases:{x.Id:D}");
            int? year = int.TryParse(alias["Year"], out var parsed) && parsed is >= 1900 and <= 2200 ? parsed : null;
            var term = alias["Term"] is "SP" or "SU" or "FA" ? alias["Term"] : null;
            var reportPeriod = alias["ReportPeriod"]?.Trim();
            if (string.IsNullOrEmpty(reportPeriod) || reportPeriod.Length > 20) reportPeriod = null;
            var isCurrent = x.IsActive && activeCount == 1;
            var validBounds = x.StartDate.HasValue && x.EndDate >= x.StartDate;
            // A catalog read does not confer authority or invent a governance/report mutation window.
            var opensParsed = DateTimeOffset.TryParse(alias["EditableFromUtc"], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var opens);
            var closesParsed = DateTimeOffset.TryParse(alias["EditableUntilUtc"], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var closes);
            var hasEditWindow = opensParsed && closesParsed
                && opens < closes && validBounds && opens >= x.StartDate && closes <= x.EndDate;
            var editable = isCurrent && hasEditWindow && now >= opens && now < closes;
            return new WorkspaceSemesterMetadata(x.Id, x.SemesterCode, x.Label ?? x.SemesterCode, isCurrent, editable,
                x.StartDate?.ToUniversalTime(), x.EndDate?.ToUniversalTime(), reportPeriod, year, term,
                !isCurrent ? "NOT_CURRENT" : !hasEditWindow ? "EDIT_WINDOW_UNCONFIGURED" : editable ? "AVAILABLE" : "OUTSIDE_EDIT_WINDOW",
                year.HasValue && term is not null && reportPeriod is not null ? "AVAILABLE" : "PARTIAL_OR_UNCONFIGURED");
        }).ToArray();
        var policies = await db.ApprovedExperiencePolicies.AsNoTracking().Where(x => x.Scope == "PERIOD" && x.Environment == environment
            && x.ApprovedAt <= now && (x.EffectiveAt == null || x.EffectiveAt <= now))
            .OrderBy(x => x.SemesterId).ThenByDescending(x => x.ApprovedAt).ThenBy(x => x.Id).ToListAsync(ct);
        var safePolicies = new List<WorkspacePolicyMetadata>();
        foreach (var row in policies)
        {
            ApprovedExperiencePolicySnapshot? snapshot;
            try { snapshot = JsonSerializer.Deserialize<ApprovedExperiencePolicySnapshot>(row.SnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
            catch (JsonException) { return ApiOperations.Error(503, "POLICY_SNAPSHOT_UNAVAILABLE", "An approved policy snapshot is invalid."); }
            var policy = snapshot?.Policy;
            if (policy is null || policy.PolicyVersion != row.Id || policy.ApprovalStatus != "APPROVED"
                || policy.Environment != environment || policy.SemesterId != row.SemesterId
                || !semesters.Any(x => x.Code == policy.SemesterId))
                return ApiOperations.Error(503, "POLICY_SNAPSHOT_UNAVAILABLE", "Approved policy metadata is inconsistent.");
            safePolicies.Add(new(policy, row.EffectiveAt));
        }
        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new WorkspaceCatalog(semesters, safePolicies, environment, currentAvailability, now));
    }
}
