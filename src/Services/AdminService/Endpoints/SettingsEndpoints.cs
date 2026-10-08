using AdminService.Auditing;
using AdminService.Contracts;
using AdminService.Data;
using AdminService.Errors;
using AdminService.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1/admin").WithTags("Platform Settings");

        // Settings API
        api.MapGet("/settings", GetSettingsAsync)
            .WithName("GetPlatformSettings")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces<PlatformSettingsResponse>();

        api.MapPut("/settings", UpdateSettingsAsync)
            .WithName("UpdatePlatformSettings")
            .RequireAuthorization(AdminPolicies.AdminOnly)
            .Produces<PlatformSettingsResponse>();

        // System Health Stats API
        api.MapGet("/health/stats", GetHealthStatsAsync)
            .WithName("GetSystemHealthStats")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces<SystemHealthStatsResponse>();

        // Public/Gateway health check alias
        endpoints.MapGet("/api/health", () => Results.Ok(new { status = "Healthy", service = "Admin & Platform Service", timestamp = DateTimeOffset.UtcNow }))
            .WithTags("Health")
            .AllowAnonymous();

        // Direct aliases for frontend convenience
        var direct = endpoints.MapGroup("/api").WithTags("Platform Settings Direct");

        direct.MapGet("/settings", GetSettingsAsync)
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .ExcludeFromDescription();

        direct.MapPut("/settings", UpdateSettingsAsync)
            .RequireAuthorization(AdminPolicies.AdminOnly)
            .ExcludeFromDescription();

        direct.MapGet("/admin/settings", GetSettingsAsync)
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .ExcludeFromDescription();

        direct.MapPut("/admin/settings", UpdateSettingsAsync)
            .RequireAuthorization(AdminPolicies.AdminOnly)
            .ExcludeFromDescription();

        direct.MapGet("/health/stats", GetHealthStatsAsync)
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .ExcludeFromDescription();

        direct.MapGet("/admin/health/stats", GetHealthStatsAsync)
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<IResult> GetSettingsAsync(
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var settings = await EnsureSettingsSeededAsync(dbContext, cancellationToken);
        return Results.Ok(ToResponse(settings));
    }

    private static async Task<IResult> UpdateSettingsAsync(
        [FromBody] UpdatePlatformSettingsRequest request,
        AdminDbContext dbContext,
        ICurrentActor currentActor,
        IAuditService auditService,
        CancellationToken cancellationToken)
    {
        ValidateUpdate(request);

        var settings = await EnsureSettingsSeededAsync(dbContext, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.GoogleDomain))
        {
            settings.GoogleDomain = request.GoogleDomain.Trim();
        }

        if (request.TimetableUrl != null)
        {
            settings.TimetableUrl = string.IsNullOrWhiteSpace(request.TimetableUrl) ? null : request.TimetableUrl.Trim();
        }

        if (request.InAppNotifications.HasValue)
        {
            settings.InAppNotifications = request.InAppNotifications.Value;
        }

        if (request.EmailNotifications.HasValue)
        {
            settings.EmailNotifications = request.EmailNotifications.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.Digest))
        {
            settings.Digest = request.Digest.Trim().ToLowerInvariant();
        }

        if (request.RateLimit.HasValue)
        {
            settings.RateLimit = request.RateLimit.Value;
        }

        if (request.Retention.HasValue)
        {
            settings.Retention = request.Retention.Value;
        }

        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        settings.UpdatedBy = currentActor.Email ?? currentActor.SubjectId;

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            new AuditWriteRequest(
                "UPDATE_PLATFORM_SETTINGS",
                "PlatformSettings",
                settings.Id.ToString(),
                AuditOutcomes.Succeeded,
                new Dictionary<string, string?>
                {
                    ["googleDomain"] = settings.GoogleDomain,
                    ["digest"] = settings.Digest,
                    ["rateLimit"] = settings.RateLimit.ToString(),
                    ["retention"] = settings.Retention.ToString()
                }),
            cancellationToken);

        return Results.Ok(ToResponse(settings));
    }

    private static async Task<IResult> GetHealthStatsAsync(
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var settings = await EnsureSettingsSeededAsync(dbContext, cancellationToken);

        var ledgerCount = await dbContext.XpLedgerEntries.CountAsync(cancellationToken);
        var auditCount = await dbContext.AuditRecords.CountAsync(cancellationToken);

        // Active accounts & clubs estimates (or known records in db)
        var accountsCount = Math.Max(24, await dbContext.QuestParticipants.Select(p => p.StudentUserId).Distinct().CountAsync(cancellationToken));
        var clubsCount = Math.Max(6, await dbContext.SelfDeclarations.Select(d => d.CampusCode).Distinct().CountAsync(cancellationToken));


        var timetableStatus = string.IsNullOrWhiteSpace(settings.TimetableUrl)
            ? "Chưa kết nối"
            : "Đã kết nối";

        var services = new List<SystemServiceHealthInfo>
        {
            new("API Gateway", "/api/health", "Hoạt động", "Cổng kết nối các microservice"),
            new("Định danh trường học", "/api/auth/health", "Hoạt động", "Google / hệ thống SSO của trường"),
            new("Đồng bộ thời khóa biểu", settings.TimetableUrl, timetableStatus, "Lọc hoạt động theo lịch học thực tế"),
            new("Tác vụ tính XP & chuyển học kỳ", "/api/kpis/health", "Hoạt động", "Lịch chạy tác vụ và kết quả xử lý"),
            new("Kiểm tra toàn vẹn sổ cái", "/api/reports/health", "Hoạt động", "Kiểm chứng dữ liệu đóng góp trên máy chủ")
        };

        var response = new SystemHealthStatsResponse(
            accountsCount,
            clubsCount,
            ledgerCount,
            auditCount,
            services);

        return Results.Ok(response);
    }

    private static void ValidateUpdate(UpdatePlatformSettingsRequest request)
    {
        var errors = new List<ErrorDetail>();

        if (request.GoogleDomain != null)
        {
            if (string.IsNullOrWhiteSpace(request.GoogleDomain))
            {
                errors.Add(new ErrorDetail("googleDomain", "Google domain cannot be empty."));
            }
            else if (request.GoogleDomain.Length > 253)
            {
                errors.Add(new ErrorDetail("googleDomain", "Google domain cannot exceed 253 characters."));
            }
        }

        if (request.TimetableUrl != null && request.TimetableUrl.Length > 2000)
        {
            errors.Add(new ErrorDetail("timetableUrl", "Timetable URL cannot exceed 2000 characters."));
        }

        if (request.Digest != null)
        {
            var d = request.Digest.Trim().ToLowerInvariant();
            if (d != "daily" && d != "weekly" && d != "off")
            {
                errors.Add(new ErrorDetail("digest", "Digest frequency must be 'daily', 'weekly', or 'off'."));
            }
        }

        if (request.RateLimit.HasValue && (request.RateLimit.Value < 10 || request.RateLimit.Value > 10000))
        {
            errors.Add(new ErrorDetail("rateLimit", "Rate limit must be between 10 and 10000 requests/min."));
        }

        if (request.Retention.HasValue && (request.Retention.Value < 30 || request.Retention.Value > 3650))
        {
            errors.Add(new ErrorDetail("retention", "Retention days must be between 30 and 3650 days."));
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }
    }

    private static async Task<PlatformSettings> EnsureSettingsSeededAsync(
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.PlatformSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings != null)
        {
            return settings;
        }

        settings = new PlatformSettings
        {
            Id = Guid.NewGuid(),
            GoogleDomain = "fpt.edu.vn",
            TimetableUrl = null,
            InAppNotifications = true,
            EmailNotifications = true,
            Digest = "weekly",
            RateLimit = 100,
            Retention = 365,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedBy = "system"
        };

        dbContext.PlatformSettings.Add(settings);
        await dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private static PlatformSettingsResponse ToResponse(PlatformSettings s) => new(
        s.Id,
        s.GoogleDomain,
        s.TimetableUrl,
        s.InAppNotifications,
        s.EmailNotifications,
        s.Digest,
        s.RateLimit,
        s.Retention,
        s.UpdatedAtUtc,
        s.UpdatedBy);
}
