using System.Text.Json;
using ClubReportHub.Shared.Data;

namespace AuthService.Endpoints;

public sealed class SelfProfileRequestFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (SelfProfileMediaException e) { return ApiOperations.Error(e.Status, e.Code, e.Message); }
        catch (BadHttpRequestException e) { return ApiOperations.Error(e.StatusCode, "INVALID_SELF_PROFILE", e.Message); }
        catch (JsonException) { return ApiOperations.Error(400, "INVALID_SELF_PROFILE", "Use unique canonical JSON fields and valid types."); }
    }
}
