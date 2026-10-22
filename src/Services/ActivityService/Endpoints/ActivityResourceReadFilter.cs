using ActivityService.Contracts;
using ActivityService.Data;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;

namespace ActivityService.Endpoints;

// Only detail reads are expanded; lists retain their existing cost and shape.
public sealed class ActivityResourceReadFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method) || result is not IValueHttpResult { Value: ActivityResponse row }) return result;
        var http = context.HttpContext; var services = http.RequestServices;
        if (services.GetService<CurrentUserProfileClient>() is null || services.GetService<ClubExperienceAuthorityClient>() is null)
            return Results.Ok(row with { ResourceAvailability = "AUTHORITY_UNAVAILABLE" });
        try
        {
            var db = services.GetRequiredService<ActivityDbContext>();
            var scope = await ActivityResourceEndpoints.Access(row.Id, db, http, http.RequestAborted);
            var gallery = await ActivityResourceEndpoints.Gallery(scope, db, http.RequestAborted);
            return Results.Ok(row with { Resources = gallery.Items, ResourceCapabilities = gallery.ResourceCapabilities, ResourceAvailability = gallery.Availability });
        }
        catch (ActivityActionHelpers.WorkflowException e)
        { return Results.Ok(row with { ResourceAvailability = e.Status == 503 ? "AUTHORITY_UNAVAILABLE" : "ACCESS_DENIED" }); }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !http.RequestAborted.IsCancellationRequested)
        { return Results.Ok(row with { ResourceAvailability = "AUTHORITY_UNAVAILABLE" }); }
    }
}
