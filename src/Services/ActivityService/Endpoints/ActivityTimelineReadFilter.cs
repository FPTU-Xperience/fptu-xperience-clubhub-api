using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Models;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Endpoints;

public sealed class ActivityTimelineReadFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method) || result is not IValueHttpResult value) return result;
        var single = value.Value as ActivityResponse;
        var rows = single is not null ? [single] : (value.Value as IEnumerable<ActivityResponse>)?.ToArray();
        if (rows is null || rows.Length == 0) return result;
        var db = context.HttpContext.RequestServices.GetRequiredService<ActivityDbContext>();
        var ids = rows.Select(a => a.Id).ToArray();
        var sessions = await db.Sessions.AsNoTracking().Where(s => ids.Contains(s.ActivityId))
            .ToListAsync(context.HttpContext.RequestAborted);
        var now = context.HttpContext.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var groups = sessions.ToLookup(s => s.ActivityId);
        var projected = rows.Select(a => a with { Timeline = Resolve(a.Status, a.StartTimeUtc, a.EndTimeUtc, groups[a.Id], now) }).ToArray();
        return Results.Ok(single is not null ? (object)projected[0] : projected);
    }

    public static ActivityTimeline Resolve(string workflowStatus, DateTimeOffset start, DateTimeOffset end,
        IEnumerable<ActivitySession> sessions, DateTimeOffset now)
    {
        var rows = sessions.ToArray();
        var next = rows.Where(s => s.Status == "SCHEDULED" && s.EndTimeUtc > now)
            .OrderBy(s => s.StartTimeUtc).ThenBy(s => s.Id).FirstOrDefault();
        if (next is not null) { start = next.StartTimeUtc; end = next.EndTimeUtc; }
        var phase = workflowStatus.ToUpperInvariant();
        if (phase == "SCHEDULED")
            phase = rows.Length > 0 && next is null || end <= now ? "COMPLETED" : start > now ? "UPCOMING" : "LIVE";
        return new ActivityTimeline(phase, start, end, now);
    }
}
