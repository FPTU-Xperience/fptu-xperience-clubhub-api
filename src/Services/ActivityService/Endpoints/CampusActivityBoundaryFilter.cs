using ActivityService.Data;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Endpoints;

internal sealed class CampusActivityBoundaryFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!ActivityEndpointHelpers.CanReviewAllActivities(http.User)
            || !int.TryParse(http.Request.RouteValues["id"]?.ToString(), out var id)) return await next(context);
        var db = http.RequestServices.GetRequiredService<ActivityDbContext>();
        var ct = http.RequestAborted;
        var campus = await db.Activities.AsNoTracking().Where(a => a.Id == id)
            .Select(a => a.OrganizerCampusCode).SingleOrDefaultAsync(ct);
        if (campus is null) return await next(context);
        try
        {
            var actor = await CampusActivityEndpoints.CampusReviewer(http, ct);
            return actor.CampusCode == campus ? await next(context) : Results.Forbid();
        }
        catch (ActivityActionHelpers.WorkflowException e) { return ApiOperations.Error(e.Status, e.Code, e.Message); }
        catch (HttpRequestException) { return ApiOperations.Error(503, "AUTHORITY_UNAVAILABLE", "Campus authority could not be verified."); }
    }
}
