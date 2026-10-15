using System.Text.Json;
using AdminService.Contracts;
using AdminService.Data;
using AdminService.Security;
using AdminService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Endpoints;

public static class VerificationAssignmentEndpoints
{
    public static void MapVerificationAssignmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/verification-assignments").RequireAuthorization(AdminPolicies.AdminOnly)
            .WithTags("Verification assignments");
        group.MapGet("/", async (int? userId, int? clubId, AdminDbContext db, CancellationToken cancellationToken) =>
        {
            var query = db.VerificationAssignments.AsNoTracking();
            if (userId.HasValue) query = query.Where(x => x.UserId == userId);
            if (clubId.HasValue) query = query.Where(x => x.ClubId == clubId);
            return Results.Ok(await query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken));
        });
        group.MapPost("/", CreateAsync);
        group.MapPatch("/{id:guid}", UpdateAsync);
    }

    private static async Task<IResult> CreateAsync(CreateVerificationAssignmentRequest request, AdminDbContext db,
        HttpContext http, CancellationToken cancellationToken)
    {
        if (request.UserId <= 0 || request.SourceType is not ("CLUB" or "EXTERNAL" or "REAL_WORLD")
            || request.SourceType == "CLUB" && request.ClubId is not > 0 || request.SourceType != "CLUB" && request.ClubId.HasValue
            || request.Capabilities is null || request.Capabilities.Count == 0 || request.Capabilities.Any(x => !ExperienceAuthorization.Capabilities.Contains(x))
            || request.Capabilities.Distinct().Count() != request.Capabilities.Count || request.ExpiresAt <= request.StartsAt
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            return ApiOperations.Error(400, "INVALID_ASSIGNMENT", "Check the account, scope, capabilities, period and reason.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            try
            {
                var account = (await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([request.UserId], cancellationToken)).GetValueOrDefault(request.UserId);
                if (account is null || !account.IsActive || account.IsLocked) return ApiOperations.Error(400, "INVALID_VERIFIER_ACCOUNT", "Assign an active project account.");
                if (request.ClubId.HasValue && (await http.RequestServices.GetRequiredService<ClubExperienceAuthorityClient>().GetAsync(request.ClubId.Value, null, cancellationToken))?.IsActive != true)
                    return ApiOperations.Error(400, "INVALID_CLUB_SCOPE", "Assign an active club scope.");
            }
            catch (HttpRequestException) { return ApiOperations.Error(503, "ASSIGNMENT_AUTHORITY_UNAVAILABLE", "Account or club authority is unavailable."); }
            var grant = new VerificationAssignment
            {
                UserId = request.UserId,
                ClubId = request.ClubId,
                SourceType = request.SourceType,
                CapabilitiesJson = JsonSerializer.Serialize(request.Capabilities, ExperienceAuthorization.JsonOptions),
                StartsAt = request.StartsAt,
                ExpiresAt = request.ExpiresAt,
                AssignedBy = http.User.GetUserId(),
                Reason = request.Reason.Trim(),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.VerificationAssignments.Add(grant);
            ExperienceAuthorization.Audit(db, http.User, "VERIFICATION_ASSIGNED", "VerificationAssignment", grant.Id.ToString(),
                new { grant.UserId, grant.ClubId, grant.SourceType, request.Capabilities, grant.Reason });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/verification-assignments/{grant.Id}", grant);
        }, cancellationToken);
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateVerificationAssignmentRequest request,
        AdminDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            return ApiOperations.Error(400, "REASON_REQUIRED", "Provide a reason.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            var grant = await db.VerificationAssignments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (grant is null) return Results.NotFound();
            if (grant.Version != request.ExpectedVersion) return ApiOperations.Error(409, "STALE_VERSION", "Reload the assignment.");
            if (request.ExpiresAt <= grant.StartsAt) return ApiOperations.Error(400, "INVALID_PERIOD", "Expiration must follow the start.");
            grant.IsRevoked = request.IsRevoked; grant.ExpiresAt = request.ExpiresAt ?? grant.ExpiresAt;
            grant.Reason = request.Reason.Trim(); grant.Version = Guid.NewGuid();
            ExperienceAuthorization.Audit(db, http.User, "VERIFICATION_ASSIGNMENT_UPDATED", "VerificationAssignment", id.ToString(),
                new { grant.IsRevoked, grant.ExpiresAt, grant.Reason });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(grant);
        }, cancellationToken);
    }
}
