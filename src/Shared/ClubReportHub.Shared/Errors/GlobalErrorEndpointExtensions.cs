using System.Diagnostics;
using ClubReportHub.Shared.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClubReportHub.Shared.Errors;

public static class GlobalErrorEndpointExtensions
{
    public static IEndpointConventionBuilder MapGlobalErrorEndpoint(this IEndpointRouteBuilder endpoints)
    {
        // Use Map with pattern "/error" so all HTTP methods (GET, POST, PUT, DELETE, PATCH, etc.)
        // are accepted by the global exception handler (REL-F06).
        return endpoints.Map("/error", (HttpContext context) =>
        {
            var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
            var exception = exceptionFeature?.Error;
            var correlationId = context.GetCorrelationId() ?? context.TraceIdentifier;

            var title = "An unexpected error occurred.";
            var detail = exception?.Message;

            var extensions = new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId,
                ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier
            };

            return Results.Problem(
                title: title,
                detail: detail,
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: extensions);
        }).AllowAnonymous();
    }
}
