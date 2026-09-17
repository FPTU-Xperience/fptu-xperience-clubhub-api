using AdminService.Errors;
using ClubReportHub.Shared.Tracing;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Middleware;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
            if (!context.Response.HasStarted && string.IsNullOrEmpty(context.Response.ContentType))
            {
                await WriteStatusEnvelopeIfNeededAsync(context);
            }
        }
        catch (ApplicationExceptionBase exception)
        {
            await WriteEnvelopeAsync(
                context, exception.StatusCode, exception.Code, exception.Message, exception.Details);
        }
        catch (BadHttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Invalid request. Method={HttpMethod} Path={RequestPath}",
                context.Request.Method,
                context.Request.Path);
            await WriteEnvelopeAsync(
                context,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.Validation,
                "Request validation failed.",
                [new ErrorDetail(null, "The request body or parameters are invalid.")]);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(
                exception,
                "Concurrent update conflict. Method={HttpMethod} Path={RequestPath}",
                context.Request.Method,
                context.Request.Path);
            await WriteEnvelopeAsync(
                context,
                StatusCodes.Status409Conflict,
                ApiErrorCodes.Conflict,
                "The resource was modified by another request. Reload it and try again.");
        }
        catch (Exception exception)
        {
            var correlationId = ResolveCorrelationId(context);
            logger.LogError(
                exception,
                "Unhandled API exception. CorrelationId={CorrelationId} Method={HttpMethod} Path={RequestPath}",
                correlationId,
                context.Request.Method,
                context.Request.Path);
            await WriteEnvelopeAsync(
                context,
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.Internal,
                "An unexpected error occurred.");
        }
    }

    private static Task WriteStatusEnvelopeIfNeededAsync(HttpContext context) =>
        context.Response.StatusCode switch
        {
            StatusCodes.Status401Unauthorized => WriteEnvelopeAsync(
                context, 401, ApiErrorCodes.Unauthenticated, "Authentication is required."),
            StatusCodes.Status403Forbidden => WriteEnvelopeAsync(
                context, 403, ApiErrorCodes.Forbidden, "You do not have permission to perform this operation."),
            StatusCodes.Status404NotFound => WriteEnvelopeAsync(
                context, 404, ApiErrorCodes.NotFound, "The requested resource was not found."),
            _ => Task.CompletedTask
        };

    private static async Task WriteEnvelopeAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        IReadOnlyCollection<ErrorDetail>? details = null)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var correlationId = ResolveCorrelationId(context);
        context.Response.Headers[CorrelationIdConstants.HeaderName] = correlationId;
        await context.Response.WriteAsJsonAsync(
            new ApiErrorEnvelope(new ApiError(code, message, details ?? [], correlationId)));
    }

    private static string ResolveCorrelationId(HttpContext context) =>
        context.GetCorrelationId() ?? Guid.NewGuid().ToString("N");
}
