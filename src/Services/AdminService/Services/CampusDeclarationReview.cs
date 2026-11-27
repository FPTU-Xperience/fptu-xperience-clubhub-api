using System.Security.Claims;
using AdminService.Data;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Services;

// Role-based CTSV intake is limited to direct declarations in the actor's campus.
// Club contributions and delegated verification continue to require assignments.
internal static class CampusDeclarationReview
{
    internal static async Task<bool> CanReviewAsync(AdminDbContext db, ClaimsPrincipal user,
        VerifiedContribution row, CancellationToken ct)
    {
        if (row.UserId == user.GetUserId() || row.ClubId.HasValue
            || row.SourceType is not ("EXTERNAL" or "REAL_WORLD")) return false;
        var admin = user.IsInRole(AuthRoles.Admin);
        if (!admin && !user.IsInRole(AuthRoles.StudentAffairsAdmin)) return false;
        var campus = user.GetCampus();
        return await db.SelfDeclarations.AsNoTracking().AnyAsync(x => x.Id == row.Id
            && x.StudentId == row.UserId && x.IsOutsideClub && x.ClubId == null
            && (admin || x.CampusCode == campus), ct);
    }
}
