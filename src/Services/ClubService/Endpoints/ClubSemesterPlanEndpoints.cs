using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using ClubService.Data;
using ClubService.Extensions;
using ClubService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClubService.Endpoints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveClubSemesterPlanRequest(string Title, int TargetActivityCount,
    int TargetSessionCount, int TargetUniqueParticipantCount, Guid? ExpectedVersion = null);

public static class ClubSemesterPlanEndpoints
{
    public static void MapClubSemesterPlanEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/clubs/{clubId:int}/semester-plans").RequireAuthorization(AuthPolicies.BusinessAccess)
            .WithTags("Club semester plans");
        group.MapGet("/{semesterId}", GetAsync);
        group.MapPut("/{semesterId}", SaveAsync);
    }

    private static async Task<IResult> GetAsync(int clubId, string semesterId, ClubDbContext db,
        ClaimsPrincipal user, HttpContext http, [FromServices] SemesterCatalogClient catalog, CancellationToken cancellationToken)
    {
        if (!await db.CanManageMembershipsAsync(clubId, user, cancellationToken)) return Results.Forbid();
        SemesterReference? semester;
        try { semester = await catalog.ResolveAsync(semesterId, http, cancellationToken); }
        catch (HttpRequestException) { return ApiOperations.Error(503, "SEMESTER_SERVICE_UNAVAILABLE", "Semester catalog is unavailable."); }
        if (semester is null) return ApiOperations.Error(404, "SEMESTER_NOT_FOUND", "Unknown semester.");
        var plan = await db.SemesterPlans.AsNoTracking().SingleOrDefaultAsync(
            x => x.ClubId == clubId && x.SemesterId == semester.SemesterCode, cancellationToken);
        return plan is null ? ApiOperations.Error(404, "PLAN_NOT_FOUND", "No plan has been saved for this semester.") : Results.Ok(plan);
    }

    private static async Task<IResult> SaveAsync(int clubId, string semesterId, SaveClubSemesterPlanRequest request,
        ClubDbContext db, ClaimsPrincipal user, HttpContext http, [FromServices] SemesterCatalogClient catalog, CancellationToken cancellationToken)
    {
        if (!await db.CanManageMembershipsAsync(clubId, user, cancellationToken)) return Results.Forbid();
        if (!await db.Clubs.AnyAsync(x => x.Id == clubId && x.IsActive && x.DeletedAtUtc == null, cancellationToken))
            return ApiOperations.Error(409, "CLUB_INACTIVE", "The club must be active.");
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200
            || request.TargetActivityCount < 0 || request.TargetSessionCount < 0 || request.TargetUniqueParticipantCount < 0)
            return ApiOperations.Error(400, "INVALID_PLAN", "Provide a title and nonnegative integer targets.");
        SemesterReference? semester;
        try { semester = await catalog.ResolveAsync(semesterId, http, cancellationToken); }
        catch (HttpRequestException) { return ApiOperations.Error(503, "SEMESTER_SERVICE_UNAVAILABLE", "Semester catalog is unavailable."); }
        if (semester is null) return ApiOperations.Error(404, "SEMESTER_NOT_FOUND", "Unknown semester.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            var plan = await db.SemesterPlans.SingleOrDefaultAsync(
                x => x.ClubId == clubId && x.SemesterId == semester.SemesterCode, cancellationToken);
            if (plan is null)
            {
                if (request.ExpectedVersion.HasValue) return ApiOperations.Error(409, "STALE_VERSION", "The plan does not exist.");
                plan = new ClubSemesterPlan
                {
                    ClubId = clubId,
                    SemesterId = semester.SemesterCode,
                    CreatedBy = user.GetUserId(),
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                db.SemesterPlans.Add(plan);
            }
            else if (request.ExpectedVersion != plan.Version)
                return ApiOperations.Error(409, "STALE_VERSION", "Reload the plan before saving.");
            plan.Title = request.Title.Trim();
            plan.TargetActivityCount = request.TargetActivityCount;
            plan.TargetSessionCount = request.TargetSessionCount;
            plan.TargetUniqueParticipantCount = request.TargetUniqueParticipantCount;
            plan.UpdatedAtUtc = DateTimeOffset.UtcNow;
            plan.Version = Guid.NewGuid();
            await db.SaveChangesAsync(cancellationToken);
            db.PlanRevisions.Add(new ClubPlanRevision
            {
                PlanId = plan.Id,
                Version = plan.Version,
                ActorId = user.GetUserId(),
                CreatedAtUtc = plan.UpdatedAtUtc,
                SnapshotJson = JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(plan);
        }, cancellationToken);
    }
}
