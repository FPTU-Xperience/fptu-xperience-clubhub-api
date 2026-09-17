using System.Diagnostics;
using AdminService.Observability;
using AdminService.Security;

namespace AdminService.Middleware;

public sealed class StructuredRequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<StructuredRequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context,
        ICurrentActor currentActor,
        ICorrelationContext correlationContext)
    {
        var startedAt = Stopwatch.GetTimestamp();
        context.Response.OnCompleted(() =>
        {
            logger.LogInformation(
                "HTTP request completed. CorrelationId={CorrelationId} ActorId={ActorId} RequestPath={RequestPath} HttpMethod={HttpMethod} StatusCode={StatusCode} DurationMs={DurationMs}",
                correlationContext.Id,
                currentActor.SubjectId ?? "anonymous",
                context.Request.Path.Value,
                context.Request.Method,
                context.Response.StatusCode,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            return Task.CompletedTask;
        });

        await next(context);
    }
}
