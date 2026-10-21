using System.Text.Json;
using System.Text.Json.Serialization;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using ClubService.Data;
using ClubService.Extensions;
using ClubService.Mappers;
using Microsoft.EntityFrameworkCore;

namespace ClubService.Endpoints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClubPublicImagesRequest(string? LogoUrl, string? CoverImageUrl, Guid ExpectedVersion);
public static class ClubPublicImageEndpoints
{
    public static void MapClubPublicImageEndpoints(this WebApplication app) =>
        app.MapPut("/api/clubs/{clubId:int}/public-images", async (int clubId, JsonElement request,
            ClubDbContext db, [Microsoft.AspNetCore.Mvc.FromServices] MediaAttachmentClient media, HttpContext http, CancellationToken ct) =>
        {
            if (!await db.CanManageMembershipsAsync(clubId, http.User, ct)) return Results.Forbid();
            var club = await db.Clubs.Include(x => x.ManagerAssignments).Include(x => x.Memberships).SingleOrDefaultAsync(x => x.Id == clubId, ct);
            if (club is null) return Results.NotFound();
            if (request.ValueKind != JsonValueKind.Object
                || request.EnumerateObject().Any(x => x.Name is not ("logoUrl" or "coverImageUrl" or "expectedVersion"))
                || request.EnumerateObject().GroupBy(x => x.Name).Any(x => x.Count() != 1))
                return ApiOperations.Error(400, "INVALID_IMAGE_FIELDS", "Use unique logoUrl, coverImageUrl and expectedVersion fields.");
            if (!request.TryGetProperty("expectedVersion", out var version) || version.ValueKind != JsonValueKind.String
                || !version.TryGetGuid(out var expected) || expected == Guid.Empty)
                return ApiOperations.Error(400, "VERSION_REQUIRED", "Provide the club version.");
            var hasLogo = request.TryGetProperty("logoUrl", out var logoValue);
            var hasCover = request.TryGetProperty("coverImageUrl", out var coverValue);
            if (!hasLogo && !hasCover)
                return ApiOperations.Error(400, "EMPTY_IMAGE_UPDATE", "Provide at least one image field.");
            if (hasLogo && logoValue.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
                || hasCover && coverValue.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                return ApiOperations.Error(400, "INVALID_IMAGE_URL", "An image must be a URL string or explicit null.");
            // Keep the raw body in the operation fingerprint: omission and explicit clearing are different commands.
            return await ApiOperations.ExecuteAsync(http, db, request, async () =>
            {
                if (!club.IsActive || club.DeletedAtUtc.HasValue || expected != club.ConcurrencyToken)
                    return ApiOperations.Error(409, "STALE_CLUB", "Reload an active club before changing its images.");
                var logo = await media.VerifyAsync(hasLogo ? logoValue.GetString() : club.LogoUrl, club.LogoUrl, "club-logo", clubId, http, ct);
                if (logo.StatusCode != 200) return ApiOperations.Error(logo.StatusCode, logo.Code!, "The logo could not be attached.");
                var cover = await media.VerifyAsync(hasCover ? coverValue.GetString() : club.CoverImageUrl, club.CoverImageUrl, "club-cover", clubId, http, ct);
                if (cover.StatusCode != 200) return ApiOperations.Error(cover.StatusCode, cover.Code!, "The cover could not be attached.");
                club.LogoUrl = logo.PublicUrl; club.CoverImageUrl = cover.PublicUrl; club.ConcurrencyToken = Guid.NewGuid();
                await db.SaveChangesAsync(ct); return Results.Ok(ClubMappers.ToPublicDetailResponse(club));
            }, ct, required: false);
        }).RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Club public images");
}
