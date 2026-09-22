using ClubReportHub.Shared.Errors;
using ClubReportHub.Shared.Health;

namespace ActivityService.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapStandardHealthChecks();

        app.MapGlobalErrorEndpoint();

        app.MapGet("/", () =>
            Results.Ok(new
            {
                service = "Activity Service",
                status = "running"
            }));

        return app;
    }
}
