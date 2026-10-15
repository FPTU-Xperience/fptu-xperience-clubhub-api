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

public static class ExperiencePolicyEndpoints
{
    public static void MapExperiencePolicyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/student-affairs/benchmarks/{id:guid}/draft", async (Guid id, SessionPolicyDraftRequest request,
            AdminDbContext db, HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000) return Results.BadRequest();
            return await ApiOperations.ExecuteAsync(http, db, request, async () =>
            {
                var config = await db.SemesterBenchmarkConfigs.SingleOrDefaultAsync(x => x.Id == id, ct);
                if (config is null) return Results.NotFound();
                if (config.Version != request.ExpectedVersion || config.ApprovalStatus != "APPROVED") return ApiOperations.Error(409, "STALE_POLICY", "Reload an approved benchmark before creating a new draft.");
                config.ApprovalStatus = "DRAFT"; config.IsLocked = false; config.Version = Guid.NewGuid();
                config.ApprovedAt = null; config.ApprovedByUserId = null; config.ApprovalBasis = null;
                ExperienceAuthorization.Audit(db, http.User, "EXPERIENCE_POLICY_DRAFTED", "Benchmark", id.ToString(), new { request.Reason });
                await db.SaveChangesAsync(ct); return Results.Ok(new { benchmarkId = id, version = config.Version, config.ApprovalStatus });
            }, ct);
        }).RequireAuthorization(AdminPolicies.AdminOnly).WithTags("Experience policy");
        var internalApi = app.MapGroup("/api/internal/experience").ExcludeFromDescription();
        internalApi.MapGet("/semesters/{identifier}", async (string identifier, AdminDbContext db,
            HttpContext http, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
            var id = Guid.TryParse(identifier, out var parsed) ? parsed : Guid.Empty;
            var code = identifier.Trim().ToUpperInvariant();
            var semester = await db.Semesters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id || x.SemesterCode == code, cancellationToken);
            return semester is null ? Results.NotFound() : Results.Ok(new SemesterReference(semester.Id, semester.SemesterCode,
                semester.StartDate, semester.EndDate, semester.IsActive));
        }).AllowAnonymous();
        internalApi.MapGet("/policies/{id:guid}", async (Guid id, string semesterId, AdminDbContext db,
            HttpContext http, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
            var policy = await db.ApprovedExperiencePolicies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Scope == "PERIOD", cancellationToken);
            if (policy is null) return Results.NotFound();
            var snapshot = JsonSerializer.Deserialize<ApprovedExperiencePolicySnapshot>(policy.SnapshotJson, ExperienceAuthorization.JsonOptions)!;
            var semester = await db.Semesters.AsNoTracking().SingleOrDefaultAsync(x => x.SemesterCode == snapshot.Policy.SemesterId, cancellationToken);
            if (semester is null || !(semesterId.Equals(semester.SemesterCode, StringComparison.OrdinalIgnoreCase)
                || semesterId.Equals(semester.Id.ToString(), StringComparison.OrdinalIgnoreCase))) return Results.NotFound();
            return Results.Ok(snapshot.Policy);
        }).AllowAnonymous();
        internalApi.MapGet("/capabilities", async (int clubId, string capability, AdminDbContext db,
            HttpContext http, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
            var grant = await ExperienceAuthorization.FindAsync(db, http.User.GetUserId(), "CLUB", clubId, capability, cancellationToken);
            return Results.Ok(new VerificationCapabilityResponse(grant is not null));
        }).RequireAuthorization(AdminPolicies.AnyActor);

        app.MapGet("/api/v1/student-affairs/benchmarks/{id:guid}/policy", async (Guid id, AdminDbContext db, CancellationToken cancellationToken) =>
        {
            var config = await db.SemesterBenchmarkConfigs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (config is null) return Results.NotFound();
            var versions = await db.ApprovedExperiencePolicies.AsNoTracking().Where(x => x.BenchmarkId == id)
                .Select(x => new { policyVersion = x.Id, x.SemesterId, x.Environment, x.ApprovedAt, x.ApprovalBasis, x.Scope, x.EffectiveAt }).ToListAsync(cancellationToken);
            return Results.Ok(new { benchmarkId = id, version = config.Version, config.ApprovalStatus, versions });
        }).RequireAuthorization(AdminPolicies.BackofficeUser).WithTags("Experience policies");
        app.MapPost("/api/v1/student-affairs/benchmarks/{id:guid}/approval", ApproveAsync)
            .RequireAuthorization(AdminPolicies.AdminOnly).WithTags("Experience policies");
    }

    private static async Task<IResult> ApproveAsync(Guid id, ApproveExperiencePolicyRequest request, AdminDbContext db,
        HttpContext http, CancellationToken cancellationToken)
    {
        if (!request.ParametersP01ThroughP06Agreed || request.Environment is not ("DEMO" or "PROJECT")
            || request.Scope is not ("PERIOD" or "CUMULATIVE") || request.EffectiveAt > DateTimeOffset.UtcNow
            || string.IsNullOrWhiteSpace(request.ApprovalBasis) || request.ApprovalBasis.Trim().Length > 1000
            || request.CancellationRule != "REVERSE_SOURCE_INVALIDATION")
            return ApiOperations.Error(400, "POLICY_APPROVAL_REQUIRED", "Record the agreed parameters, environment, approval basis and supported cancellation rule.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            var config = await db.SemesterBenchmarkConfigs.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (config is null) return Results.NotFound();
            if (config.Version != request.ExpectedVersion || config.ApprovalStatus == "APPROVED")
                return ApiOperations.Error(409, "POLICY_IMMUTABLE", "Reload the draft or create a new policy draft version.");
            var semester = await db.Semesters.AsNoTracking().SingleOrDefaultAsync(x => x.SemesterCode == config.SemesterCode, cancellationToken);
            if (semester is null || !semester.StartDate.HasValue || !semester.EndDate.HasValue)
                return ApiOperations.Error(409, "SEMESTER_CONFIGURATION_REQUIRED", "Configure the authoritative semester period first.");
            if (config.ToTauDictionary().Values.Any(x => x <= 0) || config.ThresholdStarter <= 0
                || config.ThresholdPractitioner <= config.ThresholdStarter || config.ThresholdLeader <= config.ThresholdPractitioner
                || config.MinPillarScoreAllRounder is < 0 or > 100 || config.MinJAllRounder is < 0 or > 1)
                return ApiOperations.Error(400, "INVALID_POLICY", "Check tau and ordered tier thresholds.");
            var version = Guid.NewGuid();
            var policy = new ExperiencePolicyReference(version, "P_B_R_Q_K_V1", semester.SemesterCode,
                semester.StartDate, semester.EndDate, "APPROVED", request.Environment, 4, request.CancellationRule);
            var snapshot = new ApprovedExperiencePolicySnapshot(policy, config.Id, config.ToTauDictionary(),
                config.ThresholdStarter, config.ThresholdPractitioner, config.ThresholdLeader,
                config.MinPillarScoreAllRounder, config.MinJAllRounder,
                Scope: request.Scope, EffectiveAt: request.EffectiveAt ?? DateTimeOffset.UtcNow);
            db.ApprovedExperiencePolicies.Add(new ApprovedExperiencePolicy
            {
                Id = version,
                BenchmarkId = id,
                SemesterId = semester.SemesterCode,
                Environment = request.Environment,
                Scope = request.Scope,
                EffectiveAt = snapshot.EffectiveAt,
                ApprovedBy = http.User.GetUserId(),
                ApprovedAt = DateTimeOffset.UtcNow,
                ApprovalBasis = request.ApprovalBasis.Trim(),
                SnapshotJson = JsonSerializer.Serialize(snapshot, ExperienceAuthorization.JsonOptions)
            });
            config.ApprovalStatus = "APPROVED"; config.Environment = request.Environment;
            config.ApprovedAt = DateTimeOffset.UtcNow; config.ApprovedByUserId = http.User.GetUserId();
            config.ApprovalBasis = request.ApprovalBasis.Trim(); config.Version = Guid.NewGuid();
            ExperienceAuthorization.Audit(db, http.User, "EXPERIENCE_POLICY_APPROVED", "Benchmark", id.ToString(),
                new { policyVersion = version, request.Environment, request.ApprovalBasis });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { policyVersion = version, benchmarkId = id, version = config.Version, snapshot });
        }, cancellationToken);
    }
}
public sealed record SessionPolicyDraftRequest(Guid ExpectedVersion, string Reason);
