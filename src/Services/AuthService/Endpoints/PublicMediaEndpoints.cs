using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.S3;
using AuthService.Data;
using AuthService.Models;
using AuthService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Endpoints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublicMediaIntentRequest(string Kind, string OwnerId, string ContentType, long SizeBytes);
public sealed record VerifyMediaRequest(string Kind, int OwnerId, string PublicUrl);

public static class PublicMediaEndpoints
{
    public static void MapPublicMediaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/media/upload-intents", async (PublicMediaIntentRequest input, AuthDbContext db, IPublicMediaStorage storage,
            ClubAccessClient access, IHttpClientFactory clients, HttpContext http, CancellationToken ct) =>
        {
            var self = input.Kind is "student-avatar" or "student-cover";
            if (input.Kind is not ("club-logo" or "club-cover" or "activity-cover" or "student-avatar" or "student-cover") || input.ContentType is not ("image/png" or "image/jpeg" or "image/webp")
                || input.SizeBytes is <= 0 or > 5_242_880 || !int.TryParse(input.OwnerId, NumberStyles.None, CultureInfo.InvariantCulture, out var owner) || owner <= 0)
                return ApiOperations.Error(400, "INVALID_MEDIA_INTENT", "Provide an owner, a supported basic image kind, JPEG/PNG/WebP and size up to 5 MiB.");
            if (self && (owner != http.User.GetUserId() || !await db.Users.AsNoTracking().AnyAsync(x => x.Id == owner && x.IsActive && !x.IsLocked, ct))) return Results.Forbid();
            var clubId = owner;
            try
            {
                if (input.Kind == "activity-cover")
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"api/activities/{owner}");
                    if (AuthenticationHeaderValue.TryParse(http.Request.Headers.Authorization, out var auth)) request.Headers.Authorization = auth;
                    using var response = await clients.CreateClient("MediaActivity").SendAsync(request, ct);
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return Results.NotFound();
                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden) return Results.Forbid();
                    response.EnsureSuccessStatusCode(); var activity = await response.Content.ReadFromJsonAsync<JsonElement>(ct); clubId = activity.GetProperty("clubId").GetInt32();
                }
                if (!self && !(await access.GetMyAccessAsync(http.GetBearerToken(), ct, true)).Any(x => x.ClubId == clubId && x.CanManage)) return Results.Forbid();
            }
            catch (HttpRequestException) { return ApiOperations.Error(503, "MEDIA_AUTHORITY_UNAVAILABLE", "Club/activity authority is unavailable."); }
            return await ApiOperations.ExecuteAsync(http, db, input, async () =>
            {
                if (await db.PublicMediaIntents.CountAsync(x => x.ActorId == http.User.GetUserId() && x.CreatedAt > DateTimeOffset.UtcNow.AddHours(-1), ct) >= 60)
                    return ApiOperations.Error(429, "MEDIA_QUOTA", "Upload quota exceeded.");
                var row = new PublicMediaIntent
                {
                    ActorId = http.User.GetUserId(),
                    Kind = input.Kind,
                    OwnerId = owner,
                    ContentType = input.ContentType,
                    SizeBytes = input.SizeBytes,
                    Key = "staging/" + Guid.NewGuid().ToString("N") + "." + PublicMediaStorage.Extension(input.ContentType),
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                string url;
                try { row.PublicUrl = storage.PublicUrl(row.Key); url = await storage.SignAsync(row, ct); }
                catch (Exception exception) when (exception is HttpRequestException or AmazonS3Exception) { return ApiOperations.Error(503, "MEDIA_STORAGE_UNAVAILABLE", "Signing storage is unavailable."); }
                db.PublicMediaIntents.Add(row); await db.SaveChangesAsync(ct);
                return Results.Created("/api/media/upload-intents", new { uploadUrl = url, row.PublicUrl, row.Key, row.ExpiresAt });
            }, ct, required: false);
        }).RequireAuthorization(AuthPolicies.AllActors);
        app.MapPost("/api/internal/media/verify", async (VerifyMediaRequest input, AuthDbContext db, IPublicMediaStorage storage, HttpContext http, IConfiguration config, CancellationToken ct) =>
        {
            if (!VerifiedExperienceRules.InternalRequest(http, config)) return Results.Forbid();
            var actor = http.User.GetUserId();
            if (input.Kind is "student-avatar" or "student-cover" && (input.OwnerId != actor
                || !await db.Users.AsNoTracking().AnyAsync(x => x.Id == actor && x.IsActive && !x.IsLocked, ct))) return Results.Forbid();
            var row = await db.PublicMediaIntents.SingleOrDefaultAsync(x => x.ActorId == actor && x.Kind == input.Kind && x.OwnerId == input.OwnerId
                && (x.PublicUrl == input.PublicUrl || x.VerifiedUrl == input.PublicUrl), ct);
            if (row is null) return ApiOperations.Error(409, "MEDIA_OWNER_MISMATCH", "The URL was not issued for this actor, kind and owner.");
            if (row.VerifiedUrl is not null) return Results.Ok(new { publicUrl = row.VerifiedUrl });
            if (row.ExpiresAt <= DateTimeOffset.UtcNow) return ApiOperations.Error(409, "MEDIA_INTENT_EXPIRED", "Upload a new image.");
            try { row.VerifiedUrl = await storage.VerifyAsync(row, ct); }
            catch (InvalidDataException) { return ApiOperations.Error(400, "INVALID_IMAGE", "Image size, decoded content or dimensions are invalid."); }
            catch (Exception exception) when (exception is AmazonS3Exception or HttpRequestException) { return ApiOperations.Error(503, "MEDIA_STORAGE_UNAVAILABLE", "Storage verification is unavailable."); }
            row.Version = Guid.NewGuid();
            try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return ApiOperations.Error(409, "MEDIA_CONCURRENT_VERIFICATION", "Retry attachment after the concurrent verification finishes."); }
            return Results.Ok(new { publicUrl = row.VerifiedUrl });
        }).RequireAuthorization(AuthPolicies.AllActors).ExcludeFromDescription();
    }
}
