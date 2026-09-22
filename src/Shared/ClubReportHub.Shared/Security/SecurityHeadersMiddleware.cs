using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ClubReportHub.Shared.Security;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            if (!headers.ContainsKey("X-Content-Type-Options"))
            {
                headers["X-Content-Type-Options"] = "nosniff";
            }

            if (!headers.ContainsKey("X-Frame-Options"))
            {
                headers["X-Frame-Options"] = "DENY";
            }

            if (!headers.ContainsKey("Referrer-Policy"))
            {
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            }

            if (!headers.ContainsKey("X-XSS-Protection"))
            {
                headers["X-XSS-Protection"] = "0";
            }

            if (!headers.ContainsKey("Permissions-Policy"))
            {
                headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
            }

            return Task.CompletedTask;
        });

        await next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
