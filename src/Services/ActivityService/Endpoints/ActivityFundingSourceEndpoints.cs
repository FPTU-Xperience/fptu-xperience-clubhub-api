using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Endpoints;

internal static class ActivityFundingSourceEndpoints
{
    internal static string SourceVersion(ActivityApplication row) => row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.ContentJson)));

    internal static void MapActivityFundingSourceEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/internal/experience/funding-sources/{sourceKey}", async (string sourceKey, ActivityDbContext db, IConfiguration configuration, HttpContext http, CancellationToken ct) =>
        {
            if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
            var parts = sourceKey.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[1], out var id) || id <= 0) return Results.BadRequest();
            if (parts[0] == "APPLICATION")
            {
                var row = await db.Set<ActivityApplication>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
                if (row is null) return Results.NotFound();
                var content = JsonSerializer.Deserialize<ActivityApplicationInput>(row.ContentJson, ActivityActionHelpers.Json)!;
                var activityId = await db.Set<ActivityExecution>().Where(x => x.ApplicationId == id).Select(x => (int?)x.ActivityId).SingleOrDefaultAsync(ct);
                var activity = activityId is null ? null : await db.Activities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == activityId, ct);
                var status = row.Status == "SUBMITTED" && content.StartTimeUtc <= ActivityActionHelpers.Now(http) ? "EXPIRED" : row.Status;
                return Results.Ok(await Response(sourceKey, SourceVersion(row), status, row.ClubId, row.CampusCode, row.SemesterId, row.CreatedBy,
                    content.PolicyVersion, content.RawBudget, activity, db, ct));
            }
            if (parts[0] == "CAMPUS")
            {
                var activity = await db.Activities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OrganizerCampusCode != null, ct);
                if (activity is null) return Results.NotFound();
                return Results.Ok(await Response(sourceKey, activity.Version.ToString("N"), activity.FundingReservationId is null && activity.PublicationStatus == "DRAFT" ? "SUBMITTED" : activity.Status,
                    0, activity.OrganizerCampusCode!, activity.SemesterId ?? "", activity.CreatedByUserId, activity.PolicyVersion, activity.RawBudget, activity, db, ct));
            }
            return Results.BadRequest();
        }).AllowAnonymous().ExcludeFromDescription();

    private static async Task<RawFundingSource> Response(string key, string version, string status, int club, string campus, string semester, int author,
        Guid? policy, decimal? budget, ClubActivity? activity, ActivityDbContext db, CancellationToken ct)
    {
        var candidates = activity is null ? [] : await db.Set<ActivityFundingCandidate>().AsNoTracking().Where(x => x.ActivityId == activity.Id)
            .Select(x => new RawFundingCandidate(x.UserId, x.SourceKey)).ToListAsync(ct);
        status = activity?.Status == ActivityStatuses.Cancelled ? "CANCELLED" : status.ToUpperInvariant();
        var closed = activity is not null && status != "CANCELLED" && await db.Set<ActivityClosingReport>().AnyAsync(x => x.ActivityId == activity.Id && (x.Status == "APPROVED" || x.Status == "FINALIZED"), ct);
        return new(key, version, status, club, campus, semester, author, policy, budget, activity?.Id, activity?.FundingReservationId, closed, candidates);
    }
}
