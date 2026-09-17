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
            .RequireAuthorization(AdminPolicies.SystemAdminOnly);
        admin.MapGet("/me", GetCurrentActor)
            .WithName("GetSystemAdminActor")
            .Produces<CurrentActorResponse>();
        admin.MapGet("/audit-events", GetAuditEventsAsync)
            .WithName("GetAuditEvents")
            .Produces<PagedResult<AuditEventResponse>>();
        admin.MapGet("/audit-events/{id:guid}", GetAuditEventAsync)
            .WithName("GetAuditEvent")
            .Produces<AuditEventResponse>();

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
            .RequireAuthorization(AdminPolicies.SystemAdminOnly)
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
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var request = new PageRequest(page ?? 1, pageSize ?? 20);
        request.Validate();
        var query = dbContext.AuditRecords.AsNoTracking().OrderByDescending(record => record.TimestampUtc);
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
