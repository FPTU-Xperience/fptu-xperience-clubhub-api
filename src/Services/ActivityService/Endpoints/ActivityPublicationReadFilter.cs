using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Endpoints;

// A canonical adapter for new application activities. Legacy responses retain
// their GUID lifecycle. New objects use bool/integer publication in the FE.
public sealed class ActivityPublicationReadFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method) || result is not IValueHttpResult value) return result;
        var single = value.Value as ActivityResponse;
        var rows = single is not null ? [single] : (value.Value as IEnumerable<ActivityResponse>)?.ToArray();
        if (rows is null || rows.Length == 0) return result;
        var http = context.HttpContext; var ct = http.RequestAborted;
        var db = http.RequestServices.GetRequiredService<ActivityDbContext>(); var ids = rows.Select(x => x.Id).ToArray();
        var executions = await db.Set<ActivityExecution>().AsNoTracking().Where(x => ids.Contains(x.ActivityId)).ToDictionaryAsync(x => x.ActivityId, ct);
        if (executions.Count == 0) return result;
        try
        {
            var access = await http.RequestServices.GetRequiredService<ClubAccessClient>().GetMyAccessAsync(http.GetBearerToken(), ct, true);
            var semesters = new Dictionary<string, SemesterReference?>();
            var catalog = http.RequestServices.GetRequiredService<SemesterCatalogClient>();
            foreach (var code in rows.Where(x => executions.ContainsKey(x.Id)).Select(x => x.SemesterId).OfType<string>().Distinct())
                semesters[code] = await catalog.ResolveAsync(code, http, ct);
            var now = http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
            var projected = rows.Select(row =>
            {
                if (!executions.TryGetValue(row.Id, out var execution)) return row;
                var semester = row.SemesterId is not null ? semesters.GetValueOrDefault(row.SemesterId) : null;
                var allowed = access.Any(x => x.ClubId == row.ClubId && x.CanManage)
                    && row.Status == ActivityStatuses.Scheduled && row.EndTimeUtc > now
                    && semester is { IsActive: true, StartDate: not null, EndDate: not null }
                    && row.StartTimeUtc >= semester.StartDate && row.EndTimeUtc < semester.EndDate.Value.AddDays(1);
                return row with
                {
                    PublicationStatus = null,
                    IsPublished = execution.IsPublished,
                    PublicationVersion = execution.PublicationVersion,
                    SourceApplicationId = execution.ApplicationId,
                    PublicationCapabilities = new(allowed)
                };
            }).ToArray();
            return Results.Ok(single is not null ? (object)projected[0] : projected);
        }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        { return ApiOperations.Error(503, "PUBLICATION_AUTHORITY_UNAVAILABLE", "Publication authority could not be verified."); }
    }
}
