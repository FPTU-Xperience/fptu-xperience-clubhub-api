using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ClubReportHub.Shared.Tracing;

public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    private const int MaximumLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        var requested = context.Request.Headers[CorrelationIdConstants.HeaderName].FirstOrDefault();
        var correlationId = IsValid(requested) ? requested! : Guid.NewGuid().ToString("N");

        context.Items[CorrelationIdConstants.ItemKey] = correlationId;
        context.Request.Headers[CorrelationIdConstants.HeaderName] = correlationId;

        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(CorrelationIdConstants.HeaderName))
            {
                context.Response.Headers[CorrelationIdConstants.HeaderName] = correlationId;
            }
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationIdConstants.ItemKey] = correlationId
        }))
        {
            await next(context);
        }
    }

    private static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaximumLength
        && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }
}
