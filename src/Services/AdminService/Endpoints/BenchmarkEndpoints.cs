using AdminService.Auditing;
using AdminService.Contracts;
using AdminService.Data;
using AdminService.Errors;
using AdminService.Security;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Endpoints;

public static class BenchmarkEndpoints
{
    public static IEndpointRouteBuilder MapBenchmarkEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1").WithTags("API v1");

        // CTSV & Admin Quota Benchmark Catalog management
        var benchmarks = api.MapGroup("/student-affairs/benchmarks")
            .WithTags("Student Affairs Benchmarks")
            .RequireAuthorization(AdminPolicies.BackofficeUser);

        benchmarks.MapGet("/", GetBenchmarksAsync)
            .WithName("GetBenchmarks")
            .Produces<IReadOnlyList<BenchmarkConfigResponse>>();

        benchmarks.MapGet("/active", GetActiveBenchmarkAsync)
            .WithName("GetActiveBenchmark")
            .Produces<BenchmarkConfigResponse>();

        benchmarks.MapPost("/", CreateBenchmarkAsync)
            .WithName("CreateBenchmark")
            .Produces<BenchmarkConfigResponse>(StatusCodes.Status201Created);

        benchmarks.MapPut("/{id:guid}", UpdateBenchmarkAsync)
            .WithName("UpdateBenchmark")
            .Produces<BenchmarkConfigResponse>();

        benchmarks.MapPost("/{id:guid}/lock", LockBenchmarkAsync)
            .WithName("LockBenchmark")
            .Produces<BenchmarkConfigResponse>();

        // CTSV inspection of student 6+1 radar
        api.MapGroup("/student-affairs/students")
            .WithTags("Student Affairs Student Radars")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .MapGet("/{studentId:int}/radar", GetStudentRadarAsync)
            .WithName("GetStudentRadarByCtsv")
            .Produces<StudentRadarResponse>();

        // Student personal 6+1 radar
        api.MapGroup("/declarations")
            .WithTags("Self Declarations")
            .RequireAuthorization(AdminPolicies.AnyActor)
            .MapGet("/me/radar", GetMyRadarAsync)
            .WithName("GetMyRadar")
            .Produces<StudentRadarResponse>();

        return endpoints;
    }

    private static async Task<IResult> GetBenchmarksAsync(
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.SemesterBenchmarkConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var responses = items.Select(BenchmarkConfigResponse.From).ToArray();
        return Results.Ok(responses);
    }

    private static async Task<IResult> GetActiveBenchmarkAsync(
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var config = await dbContext.SemesterBenchmarkConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsActive, cancellationToken);

        if (config is null)
        {
            // Fallback default FA26 benchmark if none created yet
            config = new SemesterBenchmarkConfig
            {
                SemesterCode = "FA26",
                AcademicYear = "2026-2027",
                IsActive = true
            };
        }

        return Results.Ok(BenchmarkConfigResponse.From(config));
    }

    private static async Task<IResult> CreateBenchmarkAsync(
        CreateBenchmarkConfigRequest request,
        ICurrentActor actor,
        AdminDbContext dbContext,
        IAuditService auditService,
        CancellationToken cancellationToken)
    {
        ValidateCreateRequest(request);

        var normalizedCode = request.SemesterCode.Trim().ToUpperInvariant();
        var existing = await dbContext.SemesterBenchmarkConfigs
            .AnyAsync(x => x.SemesterCode == normalizedCode, cancellationToken);
        if (existing)
        {
            throw new ResourceConflictException($"Benchmark configuration for semester '{normalizedCode}' already exists.");
        }

        if (request.IsActive)
        {
            await DeactivateOtherBenchmarksAsync(dbContext, null, cancellationToken);
        }

        var config = new SemesterBenchmarkConfig
        {
            SemesterCode = normalizedCode,
            AcademicYear = request.AcademicYear.Trim(),
            IsActive = request.IsActive,
            IsLocked = false,
            TauAcademic = request.TauAcademic,
            TauResearch = request.TauResearch,
            TauGlobal = request.TauGlobal,
            TauCultureSports = request.TauCultureSports,
            TauCommunity = request.TauCommunity,
            TauEntrepreneurship = request.TauEntrepreneurship,
            TauRealWorldWork = request.TauRealWorldWork,
            ThresholdStarter = request.ThresholdStarter,
            ThresholdPractitioner = request.ThresholdPractitioner,
            ThresholdLeader = request.ThresholdLeader,
            MinPillarScoreAllRounder = request.MinPillarScoreAllRounder,
            MinJAllRounder = request.MinJAllRounder,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = actor.UserId,
            CreatedByName = actor.Email ?? actor.SubjectId
        };

        dbContext.SemesterBenchmarkConfigs.Add(config);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(new AuditWriteRequest(
            Action: "BENCHMARK_CONFIG_CREATED",
            ResourceType: "SemesterBenchmarkConfig",
            ResourceId: config.Id.ToString(),
            Outcome: AuditOutcomes.Succeeded,
            Metadata: new Dictionary<string, string?>
            {
                ["SemesterCode"] = config.SemesterCode,
                ["AcademicYear"] = config.AcademicYear,
                ["IsActive"] = config.IsActive.ToString()
            }), cancellationToken);

        return Results.Created($"/api/v1/student-affairs/benchmarks/{config.Id}", BenchmarkConfigResponse.From(config));
    }

    private static async Task<IResult> UpdateBenchmarkAsync(
        Guid id,
        UpdateBenchmarkConfigRequest request,
        ICurrentActor actor,
        AdminDbContext dbContext,
        IAuditService auditService,
        CancellationToken cancellationToken)
    {
        var config = await dbContext.SemesterBenchmarkConfigs
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("The requested benchmark configuration was not found.");

        if (config.IsLocked)
        {
            throw new BusinessRuleException("Cấu hình chỉ tiêu học kỳ đã bị khóa và không thể chỉnh sửa (Quy tắc Phụ lục A4).");
        }

        ValidateUpdateRequest(request);

        if (request.IsActive && !config.IsActive)
        {
            await DeactivateOtherBenchmarksAsync(dbContext, config.Id, cancellationToken);
        }

        config.IsActive = request.IsActive;
        config.TauAcademic = request.TauAcademic;
        config.TauResearch = request.TauResearch;
        config.TauGlobal = request.TauGlobal;
        config.TauCultureSports = request.TauCultureSports;
        config.TauCommunity = request.TauCommunity;
        config.TauEntrepreneurship = request.TauEntrepreneurship;
        config.TauRealWorldWork = request.TauRealWorldWork;
        config.ThresholdStarter = request.ThresholdStarter;
        config.ThresholdPractitioner = request.ThresholdPractitioner;
        config.ThresholdLeader = request.ThresholdLeader;
        config.MinPillarScoreAllRounder = request.MinPillarScoreAllRounder;
        config.MinJAllRounder = request.MinJAllRounder;

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(new AuditWriteRequest(
            Action: "BENCHMARK_CONFIG_UPDATED",
            ResourceType: "SemesterBenchmarkConfig",
            ResourceId: config.Id.ToString(),
            Outcome: AuditOutcomes.Succeeded,
            Metadata: new Dictionary<string, string?>
            {
                ["SemesterCode"] = config.SemesterCode,
                ["IsActive"] = config.IsActive.ToString()
            }), cancellationToken);

        return Results.Ok(BenchmarkConfigResponse.From(config));
    }

    private static async Task<IResult> LockBenchmarkAsync(
        Guid id,
        ICurrentActor actor,
        AdminDbContext dbContext,
        IAuditService auditService,
        CancellationToken cancellationToken)
    {
        var config = await dbContext.SemesterBenchmarkConfigs
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("The requested benchmark configuration was not found.");

        if (config.IsLocked)
        {
            return Results.Ok(BenchmarkConfigResponse.From(config));
        }

        config.IsLocked = true;
        config.LockedAtUtc = DateTimeOffset.UtcNow;
        config.LockedByUserId = actor.UserId;
        config.LockedByName = actor.Email ?? actor.SubjectId;

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(new AuditWriteRequest(
            Action: "BENCHMARK_CONFIG_LOCKED",
            ResourceType: "SemesterBenchmarkConfig",
            ResourceId: config.Id.ToString(),
            Outcome: AuditOutcomes.Succeeded,
            Metadata: new Dictionary<string, string?>
            {
                ["SemesterCode"] = config.SemesterCode
            }), cancellationToken);

        return Results.Ok(BenchmarkConfigResponse.From(config));
    }

    private static async Task<IResult> GetMyRadarAsync(
        string? semester,
        ICurrentActor actor,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!actor.UserId.HasValue)
        {
            throw new OperationForbiddenException("An authenticated user ID is required to calculate personal radar.");
        }

        return await BuildStudentRadarAsync(
            studentId: actor.UserId.Value,
            requestedSemester: semester,
            fallbackName: actor.Email ?? actor.SubjectId ?? $"User {actor.UserId.Value}",
            fallbackEmail: actor.Email ?? string.Empty,
            dbContext: dbContext,
            cancellationToken: cancellationToken);
    }

    private static async Task<IResult> GetStudentRadarAsync(
        int studentId,
        string? semester,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await BuildStudentRadarAsync(
            studentId: studentId,
            requestedSemester: semester,
            fallbackName: $"Student {studentId}",
            fallbackEmail: string.Empty,
            dbContext: dbContext,
            cancellationToken: cancellationToken);
    }

    private static async Task<IResult> BuildStudentRadarAsync(
        int studentId,
        string? requestedSemester,
        string fallbackName,
        string fallbackEmail,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        SemesterBenchmarkConfig? activeBenchmark = null;
        if (!string.IsNullOrWhiteSpace(requestedSemester))
        {
            var code = requestedSemester.Trim().ToUpperInvariant();
            activeBenchmark = await dbContext.SemesterBenchmarkConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.SemesterCode == code, cancellationToken);
        }

        activeBenchmark ??= await dbContext.SemesterBenchmarkConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsActive, cancellationToken)
            ?? new SemesterBenchmarkConfig { SemesterCode = "FA26", AcademicYear = "2026-2027" };

        var approvedDeclarations = await dbContext.SelfDeclarations
            .AsNoTracking()
            .Where(x => x.StudentId == studentId && x.Status == DeclarationStatuses.Approved)
            .ToListAsync(cancellationToken);

        var studentName = approvedDeclarations.FirstOrDefault()?.StudentName ?? fallbackName;
        var studentEmail = approvedDeclarations.FirstOrDefault()?.StudentEmail ?? fallbackEmail;

        var rawScores = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var leaderPillars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in RadarPillars.AllSeven)
        {
            rawScores[p] = 0m;
        }

        foreach (var dec in approvedDeclarations)
        {
            var pillar = dec.FinalCategory ?? dec.Category;
            var points = dec.RawPoints ?? 0m;

            if (rawScores.ContainsKey(pillar))
            {
                rawScores[pillar] += points;
            }
            else
            {
                rawScores[pillar] = points;
            }

            if (string.Equals(dec.Role, ContributionRoles.Leader, StringComparison.OrdinalIgnoreCase))
            {
                leaderPillars.Add(pillar);
            }
        }

        var tauDict = activeBenchmark.ToTauDictionary();
        var radarResult = ExperienceIndexCalculator.ComputeRadar(rawScores, tauDict, leaderPillars);

        var pillarResponses = RadarPillars.AllSeven
            .Select(p =>
            {
                var accum = radarResult.Pillars[p];
                return new PillarRadarItemResponse(
                    Pillar: accum.Pillar,
                    RawPointsSum: Math.Round(accum.RawPointsSum, 4),
                    Tau: accum.Tau,
                    SaturatedScore: Math.Round(accum.SaturatedScore, 2),
                    MasteryTier: accum.MasteryTier,
                    HasLeaderProof: accum.HasLeaderProof);
            })
            .ToArray();

        var response = new StudentRadarResponse(
            StudentId: studentId,
            StudentName: studentName,
            StudentEmail: studentEmail,
            SemesterCode: activeBenchmark.SemesterCode,
            D: radarResult.D,
            J: radarResult.J,
            M: radarResult.M,
            ERI: radarResult.ERI,
            ProfileTitle: radarResult.ProfileTitle,
            ApprovedDeclarationsCount: approvedDeclarations.Count,
            Pillars: pillarResponses);

        return Results.Ok(response);
    }

    private static async Task DeactivateOtherBenchmarksAsync(
        AdminDbContext dbContext,
        Guid? currentId,
        CancellationToken cancellationToken)
    {
        var otherActive = await dbContext.SemesterBenchmarkConfigs
            .Where(x => x.IsActive && (!currentId.HasValue || x.Id != currentId.Value))
            .ToListAsync(cancellationToken);

        foreach (var cfg in otherActive)
        {
            cfg.IsActive = false;
        }
    }

    private static void ValidateCreateRequest(CreateBenchmarkConfigRequest request)
    {
        var details = new List<ErrorDetail>();

        if (string.IsNullOrWhiteSpace(request.SemesterCode))
            details.Add(new ErrorDetail("semesterCode", "Semester code is required."));
        else if (request.SemesterCode.Length > 20)
            details.Add(new ErrorDetail("semesterCode", "Semester code cannot exceed 20 characters."));

        if (string.IsNullOrWhiteSpace(request.AcademicYear))
            details.Add(new ErrorDetail("academicYear", "Academic year is required."));
        else if (request.AcademicYear.Length > 20)
            details.Add(new ErrorDetail("academicYear", "Academic year cannot exceed 20 characters."));

        ValidateTaus(request.TauAcademic, request.TauResearch, request.TauGlobal, request.TauCultureSports,
            request.TauCommunity, request.TauEntrepreneurship, request.TauRealWorldWork, details);

        if (details.Count > 0)
            throw new RequestValidationException(details);
    }

    private static void ValidateUpdateRequest(UpdateBenchmarkConfigRequest request)
    {
        var details = new List<ErrorDetail>();

        ValidateTaus(request.TauAcademic, request.TauResearch, request.TauGlobal, request.TauCultureSports,
            request.TauCommunity, request.TauEntrepreneurship, request.TauRealWorldWork, details);

        if (details.Count > 0)
            throw new RequestValidationException(details);
    }

    private static void ValidateTaus(
        decimal tauAcademic, decimal tauResearch, decimal tauGlobal, decimal tauCultureSports,
        decimal tauCommunity, decimal tauEntrepreneurship, decimal tauRealWorldWork, List<ErrorDetail> details)
    {
        if (tauAcademic <= 0m) details.Add(new ErrorDetail("tauAcademic", "Tau must be greater than zero."));
        if (tauResearch <= 0m) details.Add(new ErrorDetail("tauResearch", "Tau must be greater than zero."));
        if (tauGlobal <= 0m) details.Add(new ErrorDetail("tauGlobal", "Tau must be greater than zero."));
        if (tauCultureSports <= 0m) details.Add(new ErrorDetail("tauCultureSports", "Tau must be greater than zero."));
        if (tauCommunity <= 0m) details.Add(new ErrorDetail("tauCommunity", "Tau must be greater than zero."));
        if (tauEntrepreneurship <= 0m) details.Add(new ErrorDetail("tauEntrepreneurship", "Tau must be greater than zero."));
        if (tauRealWorldWork <= 0m) details.Add(new ErrorDetail("tauRealWorldWork", "Tau must be greater than zero."));
    }
}
