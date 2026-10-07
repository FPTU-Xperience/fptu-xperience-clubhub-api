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

        // Radar 6+1 overview for CTSV (campus-scoped) and Admin (global or specific campus)
        api.MapGroup("/student-affairs/radar")
            .WithTags("Student Affairs Radar Overview")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .MapGet("/overview", GetRadarOverviewAsync)
            .WithName("GetRadarOverview")
            .Produces<CampusRadarOverviewResponse>();

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

    private static async Task<IResult> GetRadarOverviewAsync(
        string? semester,
        string? campusCode,
        ICurrentActor actor,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var isAdmin = actor.Roles.Any(r => string.Equals(r, AuthRoles.Admin, StringComparison.OrdinalIgnoreCase));
        var isCtsv = actor.Roles.Any(r => string.Equals(r, AuthRoles.StudentAffairsAdmin, StringComparison.OrdinalIgnoreCase));

        if (!isAdmin && !isCtsv)
        {
            throw new OperationForbiddenException("Chỉ Quản trị viên (Admin) hoặc Cán bộ CTSV mới có quyền xem tổng quan Radar trải nghiệm.");
        }

        string targetCampus;
        if (isAdmin)
        {
            if (string.IsNullOrWhiteSpace(campusCode) ||
                string.Equals(campusCode.Trim(), "GLOBAL", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(campusCode.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
            {
                targetCampus = CampusCodes.Global;
            }
            else
            {
                targetCampus = CampusCodes.Normalize(campusCode);
            }
        }
        else
        {
            var ctsvCampus = CampusCodes.Normalize(actor.CampusCode);
            if (!string.IsNullOrWhiteSpace(campusCode))
            {
                var requestedCampus = CampusCodes.Normalize(campusCode);
                if (!string.Equals(requestedCampus, ctsvCampus, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(ctsvCampus, CampusCodes.Global, StringComparison.OrdinalIgnoreCase))
                {
                    throw new OperationForbiddenException("Cán bộ CTSV chỉ có quyền xem tổng quan dữ liệu tại cơ sở của mình.");
                }
            }
            targetCampus = ctsvCampus;
        }

        SemesterBenchmarkConfig? activeBenchmark = null;
        if (!string.IsNullOrWhiteSpace(semester))
        {
            var code = semester.Trim().ToUpperInvariant();
            activeBenchmark = await dbContext.SemesterBenchmarkConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.SemesterCode == code, cancellationToken);
        }

        activeBenchmark ??= await dbContext.SemesterBenchmarkConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsActive, cancellationToken)
            ?? new SemesterBenchmarkConfig { SemesterCode = "FA26", AcademicYear = "2026-2027" };

        var tauDict = activeBenchmark.ToTauDictionary();

        // Query declarations
        var decQuery = dbContext.SelfDeclarations
            .AsNoTracking()
            .Where(x => x.Status == DeclarationStatuses.Approved);

        if (targetCampus != CampusCodes.Global)
        {
            decQuery = decQuery.Where(x => x.CampusCode == targetCampus);
        }

        var approvedDeclarations = await decQuery.ToListAsync(cancellationToken);

        // Group declarations per student to calculate individual student radars
        var studentGroups = approvedDeclarations.GroupBy(x => x.StudentId).ToList();
        var studentRadars = new List<(int StudentId, string CampusCode, RadarIndexResult Radar)>();

        foreach (var group in studentGroups)
        {
            var studentCampus = group.FirstOrDefault()?.CampusCode ?? targetCampus;
            var rawScores = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var leaderPillars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in RadarPillars.AllSeven)
            {
                rawScores[p] = 0m;
            }

            foreach (var dec in group)
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

            var radar = ExperienceIndexCalculator.ComputeRadar(rawScores, tauDict, leaderPillars);
            studentRadars.Add((group.Key, studentCampus, radar));
        }

        var totalStudents = studentRadars.Count;
        var totalApprovedDeclarations = approvedDeclarations.Count;
        var totalRawPointsAwarded = approvedDeclarations.Sum(x => x.RawPoints ?? 0m);

        decimal avgD = totalStudents > 0 ? Math.Round(studentRadars.Average(s => s.Radar.D), 4) : 0m;
        decimal avgJ = totalStudents > 0 ? Math.Round(studentRadars.Average(s => s.Radar.J), 4) : 0m;
        decimal avgM = totalStudents > 0 ? Math.Round(studentRadars.Average(s => s.Radar.M), 4) : 1.0m;
        decimal avgERI = totalStudents > 0 ? Math.Round(studentRadars.Average(s => s.Radar.ERI), 4) : 0m;

        // Pillar details for the 6 core pillars + 1 real world work pillar
        var pillarOverviews = RadarPillars.AllSeven.Select(pillar =>
        {
            var tau = tauDict.TryGetValue(pillar, out var t) ? t : 1000m;
            var decsForPillar = approvedDeclarations.Where(d => string.Equals(d.FinalCategory ?? d.Category, pillar, StringComparison.OrdinalIgnoreCase)).ToList();
            var totalPillarRaw = decsForPillar.Sum(d => d.RawPoints ?? 0m);
            var studentsInPillar = studentRadars.Where(s => s.Radar.Pillars.TryGetValue(pillar, out var pAcc) && pAcc.RawPointsSum > 0m).ToList();
            var studentCount = studentsInPillar.Count;
            var avgRaw = studentCount > 0 ? Math.Round(totalPillarRaw / studentCount, 2) : 0m;
            var avgSat = totalStudents > 0
                ? Math.Round(studentRadars.Average(s => s.Radar.Pillars.TryGetValue(pillar, out var pAcc) ? pAcc.SaturatedScore : 0m), 2)
                : 0m;
            var leaderCount = studentRadars.Count(s => s.Radar.Pillars.TryGetValue(pillar, out var pAcc) && pAcc.HasLeaderProof);

            var masteryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var tier in ExperienceMasteryTiers.All)
            {
                masteryCounts[tier] = studentRadars.Count(s => s.Radar.Pillars.TryGetValue(pillar, out var pAcc) && string.Equals(pAcc.MasteryTier, tier, StringComparison.OrdinalIgnoreCase));
            }

            return new PillarOverviewResponse(
                Pillar: pillar,
                PillarName: RadarPillars.GetVietnameseName(pillar),
                Description: RadarPillars.GetDescription(pillar),
                Tau: tau,
                TotalRawPoints: totalPillarRaw,
                AverageRawPoints: avgRaw,
                AverageSaturatedScore: avgSat,
                StudentCount: studentCount,
                LeaderCount: leaderCount,
                MasteryTierCounts: masteryCounts);
        }).ToArray();

        // Profile titles distribution
        var profileTitlesDistribution = studentRadars
            .GroupBy(s => s.Radar.ProfileTitle, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProfileTitleStatResponse(
                Title: g.Key,
                Count: g.Count(),
                Percentage: totalStudents > 0 ? Math.Round((decimal)g.Count() / totalStudents * 100m, 2) : 0m))
            .OrderByDescending(x => x.Count)
            .ToArray();

        // Campuses comparison (computed when target is Global or Admin requests)
        List<CampusComparisonItemResponse>? campusesComparison = null;
        if (targetCampus == CampusCodes.Global)
        {
            campusesComparison = CampusCodes.FiveCampuses.Select(cCode =>
            {
                var campusStudents = studentRadars.Where(s => string.Equals(s.CampusCode, cCode, StringComparison.OrdinalIgnoreCase)).ToList();
                var campusDecsCount = approvedDeclarations.Count(d => string.Equals(d.CampusCode, cCode, StringComparison.OrdinalIgnoreCase));
                var cTotal = campusStudents.Count;
                var cAvgD = cTotal > 0 ? Math.Round(campusStudents.Average(s => s.Radar.D), 4) : 0m;
                var cAvgJ = cTotal > 0 ? Math.Round(campusStudents.Average(s => s.Radar.J), 4) : 0m;
                var cAvgM = cTotal > 0 ? Math.Round(campusStudents.Average(s => s.Radar.M), 4) : 1.0m;
                var cAvgERI = cTotal > 0 ? Math.Round(campusStudents.Average(s => s.Radar.ERI), 4) : 0m;

                string topPillar = RadarPillars.CoreSix[0];
                decimal topScore = -1m;
                if (cTotal > 0)
                {
                    foreach (var p in RadarPillars.CoreSix)
                    {
                        var pAvg = campusStudents.Average(s => s.Radar.Pillars.TryGetValue(p, out var pAcc) ? pAcc.SaturatedScore : 0m);
                        if (pAvg > topScore)
                        {
                            topScore = pAvg;
                            topPillar = p;
                        }
                    }
                }

                return new CampusComparisonItemResponse(
                    CampusCode: cCode,
                    CampusName: CampusCodes.GetDisplayName(cCode),
                    TotalStudents: cTotal,
                    TotalApprovedDeclarations: campusDecsCount,
                    AverageD: cAvgD,
                    AverageJ: cAvgJ,
                    AverageM: cAvgM,
                    AverageERI: cAvgERI,
                    TopStrengthPillar: topScore > 0m ? RadarPillars.GetVietnameseName(topPillar) : "Chưa có");
            }).ToList();
        }

        var response = new CampusRadarOverviewResponse(
            Scope: targetCampus == CampusCodes.Global ? "GLOBAL" : "CAMPUS",
            CampusCode: targetCampus,
            CampusName: CampusCodes.GetDisplayName(targetCampus),
            SemesterCode: activeBenchmark.SemesterCode,
            AcademicYear: activeBenchmark.AcademicYear,
            TotalStudents: totalStudents,
            TotalApprovedDeclarations: totalApprovedDeclarations,
            TotalRawPointsAwarded: totalRawPointsAwarded,
            AverageD: avgD,
            AverageJ: avgJ,
            AverageM: avgM,
            AverageERI: avgERI,
            Pillars: pillarOverviews,
            ProfileTitlesDistribution: profileTitlesDistribution,
            CampusesComparison: campusesComparison);

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
