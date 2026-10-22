using Amazon.S3;
using AuthService.Data;
using AuthService.Services;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Endpoints;

public sealed class SelfProfileMediaException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public static class SelfProfileMediaVerifier
{
    public static async Task<string?> Resolve(string field, string? value, AuthDbContext db, HttpContext http, CancellationToken ct)
    {
        if (value is null) return null;
        if (value.Length > 1000 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new SelfProfileMediaException(400, "INVALID_PROFILE_IMAGE", "Use an owned HTTPS upload URL or null to clear.");
        var actor = http.User.GetUserId(); var kind = field == "profile.avatarUrl" ? "student-avatar" : "student-cover";
        var row = await db.PublicMediaIntents.SingleOrDefaultAsync(x => x.ActorId == actor && x.OwnerId == actor && x.Kind == kind
            && (x.PublicUrl == value || x.VerifiedUrl == value), ct);
        if (row is null) throw new SelfProfileMediaException(409, "MEDIA_OWNER_MISMATCH", "The image was not issued for this account and field.");
        if (row.VerifiedUrl is not null) return row.VerifiedUrl;
        if (row.ExpiresAt <= DateTimeOffset.UtcNow) throw new SelfProfileMediaException(409, "MEDIA_INTENT_EXPIRED", "Create a new image upload intent.");
        var storage = http.RequestServices.GetService<IPublicMediaStorage>()
            ?? throw new SelfProfileMediaException(503, "MEDIA_STORAGE_UNAVAILABLE", "Image verification storage is unconfigured.");
        try { row.VerifiedUrl = await storage.VerifyAsync(row, ct); }
        catch (InvalidDataException) { throw new SelfProfileMediaException(400, "INVALID_IMAGE", "Uploaded image content, size or dimensions are invalid."); }
        catch (TaskCanceledException) when (!http.RequestAborted.IsCancellationRequested) { throw new SelfProfileMediaException(503, "MEDIA_STORAGE_UNAVAILABLE", "Image verification storage timed out."); }
        catch (Exception e) when (e is AmazonS3Exception or HttpRequestException) { throw new SelfProfileMediaException(503, "MEDIA_STORAGE_UNAVAILABLE", "Image verification storage is unavailable."); }
        row.Version = Guid.NewGuid(); return row.VerifiedUrl;
    }
}
