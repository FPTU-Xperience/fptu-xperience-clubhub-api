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
        app.MapPut("/api/clubs/{clubId:int}/public-images", async (int clubId, ClubPublicImagesRequest request,
            ClubDbContext db, [Microsoft.AspNetCore.Mvc.FromServices] MediaAttachmentClient media, HttpContext http, CancellationToken ct) =>
        {
            if (!await db.CanManageMembershipsAsync(clubId, http.User, ct)) return Results.Forbid();
            var club = await db.Clubs.Include(x => x.ManagerAssignments).Include(x => x.Memberships).SingleOrDefaultAsync(x => x.Id == clubId, ct);
            if (club is null) return Results.NotFound();
            return await ApiOperations.ExecuteAsync(http, db, request, async () =>
            {
                if (!club.IsActive || club.DeletedAtUtc.HasValue || request.ExpectedVersion != club.ConcurrencyToken)
                    return ApiOperations.Error(409, "STALE_CLUB", "Reload an active club before changing its images.");
                var logo = await media.VerifyAsync(request.LogoUrl, club.LogoUrl, "club-logo", clubId, http, ct);
                if (logo.StatusCode != 200) return ApiOperations.Error(logo.StatusCode, logo.Code!, "The logo could not be attached.");
                var cover = await media.VerifyAsync(request.CoverImageUrl, club.CoverImageUrl, "club-cover", clubId, http, ct);
                if (cover.StatusCode != 200) return ApiOperations.Error(cover.StatusCode, cover.Code!, "The cover could not be attached.");
                club.LogoUrl = logo.PublicUrl; club.CoverImageUrl = cover.PublicUrl; club.ConcurrencyToken = Guid.NewGuid();
                await db.SaveChangesAsync(ct); return Results.Ok(ClubMappers.ToPublicDetailResponse(club));
            }, ct, required: false);
        }).RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Club public images");
}
