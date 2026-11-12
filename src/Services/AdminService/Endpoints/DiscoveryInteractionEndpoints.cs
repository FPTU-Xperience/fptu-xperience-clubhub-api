using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdminService.Data;
using AdminService.Security;
using AdminService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Endpoints;

public static class DiscoveryInteractionEndpoints
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Input(Guid EventId, string EntityKind, int EntityId, string Action,
        [property: JsonRequired] Guid CaptureVersion);
    private sealed record TagProjection(IReadOnlyList<DiscoveryTag>? DiscoveryTags, bool? IsActive);

    public static void MapDiscoveryInteractionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v2/recommendations/interaction-settings", async (AdminDbContext db, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await DiscoveryCapturePolicy.ReadAsync(db, ct));
        }).RequireAuthorization(AdminPolicies.AnyActor).WithTags("Student recommendations");

        app.MapPost("/api/v2/recommendations/interactions", RecordAsync)
            .RequireAuthorization(AdminPolicies.AnyActor).WithTags("Student recommendations");
    }

    private static async Task<IResult> RecordAsync(Input input, AdminDbContext db, IHttpClientFactory clients,
        HttpContext http, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        if (userId <= 0) return Results.Forbid();
        if (input.EventId == Guid.Empty || input.EntityId <= 0 || input.EntityKind is not ("club" or "activity")
            || input.Action is not ("click" or "view"))
            return ApiOperations.Error(400, "INVALID_INTERACTION", "Provide a valid club or activity interaction.");

        var events = db.DiscoveryInteractions.Where(x => x.UserId == userId && x.EventId == input.EventId);
        if (await events.AnyAsync(ct)) return Results.NoContent();
        async Task<IResult?> CheckCaptureAsync()
        {
            var capture = await DiscoveryCapturePolicy.ReadAsync(db, ct);
            if (input.Action == "click" ? !capture.ClickEnabled : !capture.ViewEnabled) return Results.NoContent();
            return capture.Version != input.CaptureVersion
                ? ApiOperations.Error(409, "DISCOVERY_CAPTURE_CHANGED", "Reload the capture settings.") : null;
        }
        if (await CheckCaptureAsync() is { } denied) return denied;

        var isClub = input.EntityKind == "club";
        var client = clients.CreateClient(isClub ? "ExperienceClub" : "ExperienceActivity");
        using var request = new HttpRequestMessage(HttpMethod.Get, isClub
            ? $"api/clubs/{input.EntityId}" : $"api/v2/activities/{input.EntityId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", http.GetBearerToken());
        TagProjection? source;
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) return Results.NotFound();
            response.EnsureSuccessStatusCode();
            source = await response.Content.ReadFromJsonAsync<TagProjection>(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return ApiOperations.Error(503, "DISCOVERY_SOURCE_UNAVAILABLE", "The discovery source could not be read.");
        }
        if (source?.IsActive == false) return Results.NotFound();
        if (source?.DiscoveryTags is not { Count: > 0 } tags) return Results.NoContent();
        if (await CheckCaptureAsync() is { } changed) return changed;
        var row = new DiscoveryInteraction
        {
            UserId = userId,
            EventId = input.EventId,
            EntityKind = input.EntityKind,
            EntityId = input.EntityId,
            Action = input.Action,
            OccurredAtUtc = DateTime.UtcNow,
            TagIdsJson = JsonSerializer.Serialize(tags.Select(x => x.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        };
        db.DiscoveryInteractions.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
            if (!await events.AnyAsync(ct)) throw;
        }
        return Results.NoContent();
    }
}
