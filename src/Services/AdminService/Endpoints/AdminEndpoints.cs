using AdminService.Auditing;
using AdminService.Contracts;
using AdminService.Data;
using AdminService.Errors;
using AdminService.Security;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Endpoints;

public static class AdminEndpoints
{
    private static readonly HashSet<string> ValidAuditOutcomes =
    [
        AuditOutcomes.Succeeded,
        AuditOutcomes.Failed,
        AuditOutcomes.Denied
    ];

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1").WithTags("API v1");

        api.MapGet("/me", GetCurrentActor)
            .WithName("GetCurrentActor")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces<CurrentActorResponse>();

        var admin = api.MapGroup("/admin")
            .WithTags("System Admin")
            .RequireAuthorization(AdminPolicies.AdminOnly);
        admin.MapGet("/me", GetCurrentActor)
            .WithName("GetSystemAdminActor")
            .Produces<CurrentActorResponse>();
        admin.MapGet("/audit-events", GetAuditEventsAsync)
            .WithName("GetAuditEvents")
            .Produces<PagedResult<AuditEventResponse>>();
        admin.MapGet("/audit-events/{id:guid}", GetAuditEventAsync)
            .WithName("GetAuditEvent")
            .Produces<AuditEventResponse>();

        // Direct alias for audit events
        api.MapGet("/audit-events", GetAuditEventsAsync)
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .ExcludeFromDescription();

        api.MapGroup("/student-affairs")
            .WithTags("Student Affairs")
            .RequireAuthorization(AdminPolicies.StudentAffairsOnly)
            .MapGet("/me", GetCurrentActor)
            .WithName("GetStudentAffairsActor")
            .Produces<CurrentActorResponse>();

        return endpoints;
    }

    public static IEndpointRouteBuilder MapAdminTestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/__test/audited-operation", CreateAuditEventAsync)
            .RequireAuthorization(AdminPolicies.AdminOnly)
            .ExcludeFromDescription();
        return endpoints;
    }

    private static IResult GetCurrentActor(ICurrentActor currentActor) =>
        Results.Ok(new CurrentActorResponse(
            currentActor.SubjectId!,
            currentActor.UserId,
            currentActor.Email,
            currentActor.Roles));

    private static async Task<IResult> CreateAuditEventAsync(
        CreateAuditEventRequest request,
        IAuditService auditService,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var outcome = string.IsNullOrWhiteSpace(request.Outcome)
            ? AuditOutcomes.Succeeded
            : request.Outcome.Trim().ToUpperInvariant();
        var auditEvent = await auditService.WriteAsync(
            new AuditWriteRequest(
                request.Action!.Trim(),
                request.ResourceType!.Trim(),
                Normalize(request.ResourceId),
                outcome,
                request.Metadata),
            cancellationToken);
        return Results.Ok(AuditEventResponse.From(auditEvent));
    }

    private static async Task<PagedResult<AuditEventResponse>> GetAuditEventsAsync(
        int? page,
        int? pageSize,
        string? search,
        string? area,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var request = new PageRequest(page ?? 1, pageSize ?? 20);
        request.Validate();
        await EnsureDefaultAuditRecordsAsync(dbContext, cancellationToken);

        var query = dbContext.AuditRecords.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(record =>
                record.Action.Contains(s) ||
                record.ResourceType.Contains(s) ||
                (record.ResourceId != null && record.ResourceId.Contains(s)) ||
                (record.ActorEmail != null && record.ActorEmail.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(area) && !string.Equals(area, "all", StringComparison.OrdinalIgnoreCase))
        {
            var a = area.Trim().ToLowerInvariant();
            if (a == "affairs")
            {
                query = query.Where(record =>
                    record.Action.Contains("QUEST") ||
                    record.Action.Contains("ANOMALY") ||
                    record.Action.Contains("BENCHMARK") ||
                    record.Action.Contains("DECLARATION") ||
                    record.ResourceType.Contains("QUEST") ||
                    record.ResourceType.Contains("ANOMALY") ||
                    record.ResourceType.Contains("BENCHMARK") ||
                    record.ResourceType.Contains("DECLARATION") ||
                    record.ActorRolesJson.Contains("STUDENT_AFFAIRS_ADMIN"));
            }
            else if (a == "admin")
            {
                query = query.Where(record =>
                    record.Action.Contains("SETTING") ||
                    record.Action.Contains("USER") ||
                    record.Action.Contains("ACCOUNT") ||
                    record.Action.Contains("ROLE") ||
                    record.ResourceType.Contains("SETTING") ||
                    record.ActorRolesJson.Contains("ADMIN"));
            }
            else if (a == "system")
            {
                query = query.Where(record =>
                    record.ActorRolesJson.Contains("SYSTEM") ||
                    record.Action.Contains("INIT") ||
                    record.ResourceType.Contains("SYSTEM"));
            }
        }

        query = query.OrderByDescending(record => record.TimestampUtc);
        var total = await query.CountAsync(cancellationToken);
        var records = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var items = records
            .Select(EfAuditService.ToAuditEvent)
            .Select(AuditEventResponse.From)
            .ToArray();
        return PagedResult<AuditEventResponse>.Create(items, request.Page, request.PageSize, total);
    }

    private static async Task EnsureDefaultAuditRecordsAsync(AdminDbContext dbContext, CancellationToken cancellationToken)
    {
        if (await dbContext.AuditRecords.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var initialRecords = new List<AuditRecord>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ActorSubjectId = "system",
                ActorEmail = "system@fpt.edu.vn",
                ActorRolesJson = "[\"SYSTEM\"]",
                Action = "SYSTEM_INITIALIZE",
                ResourceType = "SystemPlatform",
                ResourceId = "core",
                CorrelationId = Guid.NewGuid().ToString("N"),
                TimestampUtc = now.AddHours(-12),
                Outcome = AuditOutcomes.Succeeded,
                MetadataJson = "{\"reason\":\"Khởi tạo hạ tầng nền tảng FPTU ClubHub API\"}"
            },
            new()
            {
                Id = Guid.NewGuid(),
                ActorSubjectId = "admin-sys-01",
                ActorEmail = "admin@fpt.edu.vn",
                ActorRolesJson = "[\"ADMIN\"]",
                Action = "CONFIG_PLATFORM_SETTINGS",
                ResourceType = "PlatformSettings",
                ResourceId = "default",
                CorrelationId = Guid.NewGuid().ToString("N"),
                TimestampUtc = now.AddHours(-6),
                Outcome = AuditOutcomes.Succeeded,
                MetadataJson = "{\"reason\":\"Thiết lập tên miền Google Workspace fpt.edu.vn và giới hạn lưu trữ\"}"
            },
            new()
            {
                Id = Guid.NewGuid(),
                ActorSubjectId = "ctsv-hanoi-01",
                ActorEmail = "ctsv.hanoi@fpt.edu.vn",
                ActorRolesJson = "[\"STUDENT_AFFAIRS_ADMIN\"]",
                Action = "UPDATE_SEMESTER_BENCHMARK",
                ResourceType = "SemesterBenchmark",
                ResourceId = "FA24",
                CorrelationId = Guid.NewGuid().ToString("N"),
                TimestampUtc = now.AddHours(-2),
                Outcome = AuditOutcomes.Succeeded,
                MetadataJson = "{\"reason\":\"Cập nhật ngưỡng chuẩn trải nghiệm học kỳ Fall 2024 cơ sở Hà Nội\"}"
            }
        };

        dbContext.AuditRecords.AddRange(initialRecords);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<AuditEventResponse> GetAuditEventAsync(
        Guid id,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.AuditRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("The requested audit event was not found.");
        return AuditEventResponse.From(EfAuditService.ToAuditEvent(record));
    }

    private static void Validate(CreateAuditEventRequest request)
    {
        var details = new List<ErrorDetail>();
        ValidateRequired(details, "action", request.Action, 100);
        ValidateRequired(details, "resourceType", request.ResourceType, 100);
        if (request.ResourceId?.Length > 200)
        {
            details.Add(new ErrorDetail("resourceId", "Resource ID cannot exceed 200 characters."));
        }

        var outcome = string.IsNullOrWhiteSpace(request.Outcome)
            ? AuditOutcomes.Succeeded
            : request.Outcome.Trim().ToUpperInvariant();
        if (!ValidAuditOutcomes.Contains(outcome))
        {
            details.Add(new ErrorDetail(
                "outcome",
                $"Outcome must be one of: {string.Join(", ", ValidAuditOutcomes)}."));
        }

        if (details.Count > 0)
        {
            throw new RequestValidationException(details);
        }
    }

    private static void ValidateRequired(
        ICollection<ErrorDetail> details,
        string field,
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            details.Add(new ErrorDetail(field, $"{field} is required."));
        }
        else if (value.Length > maximumLength)
        {
            details.Add(new ErrorDetail(field, $"{field} cannot exceed {maximumLength} characters."));
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
