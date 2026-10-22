using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Profiles;

namespace AuthService.Endpoints;

// Enrich after the write transaction commits, including idempotent receipts.
// Internal member projections deliberately never run this self-only filter.
public sealed class CanonicalAcademicReadFilter : IEndpointFilter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        if (result is not IValueHttpResult value || value.Value is null || result is IStatusCodeHttpResult { StatusCode: >= 400 }) return result;
        var json = JsonSerializer.SerializeToElement(value.Value, Options); var http = context.HttpContext;
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("userId", out var userId)
            || !userId.TryGetInt32(out var id) || id != http.User.GetUserId()) return result;
        var client = http.RequestServices.GetService<CanonicalAcademicClient>();
        var academic = client is null ? new AcademicStudentProfile("UNCONFIGURED") : await client.Read(http, http.RequestAborted);
        var output = json.EnumerateObject().ToDictionary(x => x.Name, x => (object?)x.Value.Clone()); output["academic"] = academic;
        http.Response.Headers.CacheControl = "no-store";
        return Results.Json(output, statusCode: (result as IStatusCodeHttpResult)?.StatusCode ?? 200);
    }
}
