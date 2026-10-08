using AdminService.Auditing;
using AdminService.Contracts;
using AdminService.Data;
using AdminService.Errors;
using AdminService.Security;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Endpoints;

public static class AnomalyEndpoints
{
    public static IEndpointRouteBuilder MapAnomalyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapStudentAffairsGroup(endpoints, "/api/v1/student-affairs");
        MapStandaloneGroup(endpoints, "/api");

        return endpoints;
    }

    private static void MapStudentAffairsGroup(IEndpointRouteBuilder endpoints, string prefix)
    {
        var api = endpoints.MapGroup(prefix)
            .WithTags("Student Affairs Anomalies and XP Ledger")
            .RequireAuthorization(AdminPolicies.BackofficeUser);

        var anomalies = api.MapGroup("/anomalies");

        anomalies.MapGet("/", GetAnomaliesAsync)
            .WithName("GetAnomalies")
            .Produces<IReadOnlyList<AnomalyResponse>>();

        anomalies.MapGet("/stats", GetAnomalyStatsAsync)
            .WithName("GetAnomalyStats")
            .Produces<AnomalyStatsResponse>();

        anomalies.MapGet("/{id:guid}", GetAnomalyByIdAsync)
            .WithName("GetAnomalyById")
            .Produces<AnomalyResponse>()
            .Produces(StatusCodes.Status404NotFound);

        anomalies.MapPost("/{id:guid}/resolve", ResolveAnomalyAsync)
            .WithName("ResolveAnomaly")
            .Produces<AnomalyResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        api.MapGet("/ledger", GetLedgerEntriesAsync)
            .WithName("GetXpLedgerEntries")
            .Produces<IReadOnlyList<XpLedgerEntryResponse>>();
    }

    private static void MapStandaloneGroup(IEndpointRouteBuilder endpoints, string prefix)
    {
        var api = endpoints.MapGroup(prefix)
            .WithTags("Anomalies and Ledger")
            .RequireAuthorization(AdminPolicies.BackofficeUser);

        var anomalies = api.MapGroup("/anomalies");

        anomalies.MapGet("/", GetAnomaliesAsync)
            .WithName("GetAnomalies_Direct")
            .Produces<IReadOnlyList<AnomalyResponse>>();

        anomalies.MapGet("/stats", GetAnomalyStatsAsync)
            .WithName("GetAnomalyStats_Direct")
            .Produces<AnomalyStatsResponse>();

        anomalies.MapGet("/{id:guid}", GetAnomalyByIdAsync)
            .WithName("GetAnomalyById_Direct")
            .Produces<AnomalyResponse>()
            .Produces(StatusCodes.Status404NotFound);

        anomalies.MapPost("/{id:guid}/resolve", ResolveAnomalyAsync)
            .WithName("ResolveAnomaly_Direct")
            .Produces<AnomalyResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        api.MapGet("/ledger", GetLedgerEntriesAsync)
            .WithName("GetXpLedgerEntries_Direct")
            .Produces<IReadOnlyList<XpLedgerEntryResponse>>();
    }

    private static async Task<IResult> GetAnomaliesAsync(
        string? status,
        string? severity,
        string? semesterCode,
        string? campusCode,
        AdminDbContext dbContext,
        ICurrentActor currentActor,
        CancellationToken cancellationToken)
    {
        await EnsureDefaultAnomaliesAndLedgerAsync(dbContext, cancellationToken);

        var query = dbContext.XpAnomalies.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var st = status.Trim().ToLowerInvariant();
            query = query.Where(x => x.Status.ToLower() == st);
        }

        if (!string.IsNullOrWhiteSpace(severity) && !severity.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var sev = severity.Trim().ToLowerInvariant();
            query = query.Where(x => x.Severity.ToLower() == sev);
        }

        if (!string.IsNullOrWhiteSpace(semesterCode))
        {
            query = query.Where(x => x.SemesterCode == semesterCode.Trim());
        }

        // Campus scope filter
        var effectiveCampus = !string.IsNullOrWhiteSpace(campusCode) ? campusCode : currentActor.CampusCode;
        if (!string.IsNullOrWhiteSpace(effectiveCampus) && !effectiveCampus.Equals(CampusCodes.Global, StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.CampusCode == CampusCodes.Global || x.CampusCode == effectiveCampus);
        }

        var list = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(list.Select(AnomalyResponse.From).ToList());
    }

    private static async Task<IResult> GetAnomalyStatsAsync(
        string? semesterCode,
        string? campusCode,
        AdminDbContext dbContext,
        ICurrentActor currentActor,
        CancellationToken cancellationToken)
    {
        await EnsureDefaultAnomaliesAndLedgerAsync(dbContext, cancellationToken);

        var query = dbContext.XpAnomalies.AsNoTracking().AsQueryable();

        var effectiveCampus = !string.IsNullOrWhiteSpace(campusCode) ? campusCode : currentActor.CampusCode;
        if (!string.IsNullOrWhiteSpace(effectiveCampus) && !effectiveCampus.Equals(CampusCodes.Global, StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.CampusCode == CampusCodes.Global || x.CampusCode == effectiveCampus);
        }

        if (!string.IsNullOrWhiteSpace(semesterCode))
        {
            query = query.Where(x => x.SemesterCode == semesterCode.Trim());
        }

        var open = await query.CountAsync(x => x.Status == "open", cancellationToken);
        var high = await query.CountAsync(x => x.Status == "open" && x.Severity == "high", cancellationToken);
        var resolved = await query.CountAsync(x => x.Status == "resolved", cancellationToken);
        var ledgerCount = await dbContext.XpLedgerEntries.CountAsync(cancellationToken);

        return Results.Ok(new AnomalyStatsResponse(open, high, resolved, ledgerCount));
    }

    private static async Task<IResult> GetAnomalyByIdAsync(
        Guid id,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var anomaly = await dbContext.XpAnomalies
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (anomaly is null)
        {
            return Results.NotFound(new { message = "Không tìm thấy trường hợp bất thường với ID được cung cấp." });
        }

        return Results.Ok(AnomalyResponse.From(anomaly));
    }

    private static async Task<IResult> ResolveAnomalyAsync(
        Guid id,
        ResolveAnomalyRequest request,
        AdminDbContext dbContext,
        ICurrentActor currentActor,
        IAuditService auditService,
        CancellationToken cancellationToken)
    {
        var anomaly = await dbContext.XpAnomalies
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (anomaly is null)
        {
            return Results.NotFound(new { message = "Không tìm thấy trường hợp bất thường với ID được cung cấp." });
        }

        var decision = request.Decision?.Trim().ToLowerInvariant();
        if (decision is not ("keep" or "adjust" or "revoke"))
        {
            return Results.BadRequest(new { message = "Quyết định không hợp lệ. Cho phép: keep, adjust, revoke." });
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 10)
        {
            return Results.BadRequest(new { message = "Lý do và kết quả kiểm tra phải có ít nhất 10 ký tự." });
        }

        if (decision == "adjust")
        {
            if (!request.Adjustment.HasValue || request.Adjustment.Value < 0 || request.Adjustment.Value >= anomaly.Amount)
            {
                return Results.BadRequest(new { message = $"Điểm XP điều chỉnh phải nằm trong khoảng từ 0 đến {anomaly.Amount - 1}." });
            }
        }

        var resolvedBy = currentActor.Email ?? currentActor.SubjectId ?? "Cán bộ CTSV";
        var now = DateTimeOffset.UtcNow;

        anomaly.Decision = decision;
        anomaly.Status = "resolved";
        anomaly.Adjustment = decision == "adjust" ? request.Adjustment : null;
        anomaly.Reason = request.Reason.Trim();
        anomaly.ResolvedByName = resolvedBy;
        anomaly.ResolvedAtUtc = now;
        anomaly.UpdatedAtUtc = now;

        // If adjust or revoke, append an offsetting entry to the XP Ledger
        if (decision is "adjust" or "revoke")
        {
            var offsetAmount = decision == "revoke"
                ? -anomaly.Amount
                : request.Adjustment!.Value - anomaly.Amount;

            var ledgerEntry = new XpLedgerEntry
            {
                StudentUserId = anomaly.StudentUserId,
                StudentName = anomaly.Student,
                Type = decision == "revoke" ? "revoke" : "adjust",
                Source = "Điều chỉnh CTSV",
                Actor = resolvedBy,
                Amount = offsetAmount,
                RubricVersion = 1,
                Reason = $"Xử lý bất thường '{anomaly.Title}': {anomaly.Reason}",
                PillarCategory = "Community",
                SemesterCode = anomaly.SemesterCode,
                CampusCode = anomaly.CampusCode,
                RelatedAnomalyId = anomaly.Id,
                CreatedAtUtc = now
            };

            dbContext.XpLedgerEntries.Add(ledgerEntry);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(new AuditWriteRequest(
            Action: "XP_ANOMALY_RESOLVED",
            ResourceType: "XpAnomaly",
            ResourceId: anomaly.Id.ToString(),
            Outcome: AuditOutcomes.Succeeded,
            Metadata: new Dictionary<string, string?>
            {
                ["Decision"] = decision,
                ["Student"] = anomaly.Student,
                ["Amount"] = anomaly.Amount.ToString(),
                ["Adjustment"] = anomaly.Adjustment?.ToString()
            }), cancellationToken);

        return Results.Ok(AnomalyResponse.From(anomaly));
    }

    private static async Task<IResult> GetLedgerEntriesAsync(
        int? studentUserId,
        string? semesterCode,
        string? campusCode,
        string? type,
        int? page,
        int? pageSize,
        AdminDbContext dbContext,
        ICurrentActor currentActor,
        CancellationToken cancellationToken)
    {
        await EnsureDefaultAnomaliesAndLedgerAsync(dbContext, cancellationToken);

        var query = dbContext.XpLedgerEntries.AsNoTracking().AsQueryable();

        if (studentUserId.HasValue && studentUserId.Value > 0)
        {
            query = query.Where(x => x.StudentUserId == studentUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(semesterCode))
        {
            query = query.Where(x => x.SemesterCode == semesterCode.Trim());
        }

        if (!string.IsNullOrWhiteSpace(type) && !type.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.Type.ToLower() == type.Trim().ToLowerInvariant());
        }

        var effectiveCampus = !string.IsNullOrWhiteSpace(campusCode) ? campusCode : currentActor.CampusCode;
        if (!string.IsNullOrWhiteSpace(effectiveCampus) && !effectiveCampus.Equals(CampusCodes.Global, StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.CampusCode == CampusCodes.Global || x.CampusCode == effectiveCampus);
        }

        var p = Math.Max(1, page ?? 1);
        var ps = Math.Clamp(pageSize ?? 50, 1, 100);

        var list = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((p - 1) * ps)
            .Take(ps)
            .ToListAsync(cancellationToken);

        return Results.Ok(list.Select(XpLedgerEntryResponse.From).ToList());
    }

    private static async Task EnsureDefaultAnomaliesAndLedgerAsync(AdminDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!await dbContext.XpAnomalies.AnyAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;

            var defaultAnomalies = new List<XpAnomaly>
            {
                new()
                {
                    Title = "Tăng điểm đột biến trong 24h (+120 XP)",
                    Severity = "high",
                    Status = "open",
                    Club = "CLB Lập trình F-Code",
                    Student = "Nguyễn Văn An (HE170001)",
                    StudentUserId = 301,
                    Source = "Sự kiện CLB",
                    Evidence = "Ghi nhận 3 sự kiện liên tiếp cùng một khung giờ vào ngày 05/10/2026. Cần đối chiếu danh sách điểm danh thực tế.",
                    Amount = 120,
                    Count = 3,
                    SemesterCode = "FALL2026",
                    CampusCode = "HAN",
                    CreatedAtUtc = now.AddHours(-2),
                    UpdatedAtUtc = now.AddHours(-2)
                },
                new()
                {
                    Title = "Điểm vượt ngưỡng bão hòa Pillar Community (+80 XP)",
                    Severity = "medium",
                    Status = "open",
                    Club = "CLB Tình nguyện Vì Nụ Cười",
                    Student = "Trần Thị Mai (HE170045)",
                    StudentUserId = 302,
                    Source = "Tự khai báo",
                    Evidence = "Sinh viên nộp minh chứng tham gia 2 chiến dịch ngoài trường cùng lúc. Điểm khai báo vượt trần tối đa của kỳ.",
                    Amount = 80,
                    Count = 2,
                    SemesterCode = "FALL2026",
                    CampusCode = "HCM",
                    CreatedAtUtc = now.AddHours(-6),
                    UpdatedAtUtc = now.AddHours(-6)
                },
                new()
                {
                    Title = "Minh chứng ảnh chụp trùng lặp",
                    Severity = "medium",
                    Status = "resolved",
                    Decision = "adjust",
                    Adjustment = 25,
                    Reason = "Sinh viên nộp trùng ảnh minh chứng giải chạy phong trào. Đã xác nhận lại cự ly hợp lệ và điều chỉnh từ 50 XP về 25 XP.",
                    Club = "CLB Thể thao Vovinam",
                    Student = "Lê Hoàng Phúc (HE170088)",
                    StudentUserId = 303,
                    Source = "Tự khai báo",
                    Evidence = "Ảnh số BIB chạy bị trùng với một sinh viên khác đã nộp trước đó.",
                    Amount = 50,
                    Count = 1,
                    SemesterCode = "FALL2026",
                    CampusCode = "DAN",
                    ResolvedByName = "ctsv@fpt.edu.vn",
                    ResolvedAtUtc = now.AddDays(-1),
                    CreatedAtUtc = now.AddDays(-2),
                    UpdatedAtUtc = now.AddDays(-1)
                }
            };

            dbContext.XpAnomalies.AddRange(defaultAnomalies);

            // Add corresponding initial ledger entries
            var defaultLedger = new List<XpLedgerEntry>
            {
                new()
                {
                    StudentUserId = 301,
                    StudentName = "Nguyễn Văn An (HE170001)",
                    Type = "award",
                    Source = "Tự khai báo",
                    Actor = "ctsv@fpt.edu.vn",
                    Amount = 40,
                    RubricVersion = 1,
                    Reason = "Duyệt minh chứng Hội thảo Trí tuệ Nhân tạo thế hệ mới",
                    PillarCategory = "Academic",
                    SemesterCode = "FALL2026",
                    CampusCode = "HAN",
                    CreatedAtUtc = now.AddDays(-3)
                },
                new()
                {
                    StudentUserId = 302,
                    StudentName = "Trần Thị Mai (HE170045)",
                    Type = "award",
                    Source = "Nhiệm vụ Quest",
                    Actor = "System",
                    Amount = 30,
                    RubricVersion = 1,
                    Reason = "Hoàn thành Chiến dịch Xanh: Một giờ vì Trái Đất",
                    PillarCategory = "Community",
                    SemesterCode = "FALL2026",
                    CampusCode = "HCM",
                    CreatedAtUtc = now.AddDays(-2)
                },
                new()
                {
                    StudentUserId = 303,
                    StudentName = "Lê Hoàng Phúc (HE170088)",
                    Type = "adjust",
                    Source = "Điều chỉnh CTSV",
                    Actor = "ctsv@fpt.edu.vn",
                    Amount = -25,
                    RubricVersion = 1,
                    Reason = "Xử lý bất thường 'Minh chứng ảnh chụp trùng lặp': Điều chỉnh giảm 25 XP do trùng ảnh số BIB",
                    PillarCategory = "CultureSports",
                    SemesterCode = "FALL2026",
                    CampusCode = "DAN",
                    CreatedAtUtc = now.AddDays(-1)
                }
            };

            dbContext.XpLedgerEntries.AddRange(defaultLedger);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
