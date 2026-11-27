using AdminService.Contracts;
using AdminService.Data;
using AdminService.Errors;
using AdminService.Security;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using AdminService.Services;
using ClubReportHub.Shared.Data;

namespace AdminService.Endpoints;

public static class DeclarationEndpoints
{
    public static IEndpointRouteBuilder MapDeclarationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1").WithTags("API v1");

        // Student-facing endpoints: submit, view personal submissions
        var declarations = api.MapGroup("/declarations")
            .WithTags("Self Declarations")
            .RequireAuthorization(AdminPolicies.AnyActor);

        declarations.MapPost("/", SubmitDeclarationAsync)
            .WithName("SubmitDeclaration")
            .Produces<DeclarationResponse>(StatusCodes.Status201Created);
        declarations.MapClubDeclarations();
        declarations.MapPost("/{id:guid}/upgrade", UpgradeDeclarationAsync);

        declarations.MapGet("/me", GetMyDeclarationsAsync)
            .WithName("GetMyDeclarations")
            .Produces<StudentContributionPage>();

        declarations.MapGet("/{id:guid}", GetDeclarationByIdAsync)
            .WithName("GetDeclarationById")
            .Produces<DeclarationResponse>();

        declarations.MapPut("/{id:guid}", UpdateDeclarationAsync)
            .WithName("UpdateDeclaration")
            .Produces<DeclarationResponse>();

        // CTSV (Student Affairs) verification queue and review endpoints
        var studentAffairs = api.MapGroup("/student-affairs/declarations")
            .WithTags("Student Affairs Declarations")
            .RequireAuthorization(AdminPolicies.AnyActor);

        studentAffairs.MapGet("/", GetDeclarationsQueueAsync)
            .WithName("GetDeclarationsQueue")
            .Produces<PagedResult<DeclarationResponse>>();

        studentAffairs.MapPost("/{id:guid}/review", ReviewDeclarationAsync)
            .WithName("ReviewDeclaration")
            .Produces<DeclarationResponse>();

        return endpoints;
    }

    private static Task<IResult> SubmitDeclarationAsync(SubmitDeclarationRequest request,
        AdminDbContext dbContext, HttpContext http, CancellationToken cancellationToken) =>
        DeclarationExperienceBridge.SubmitAsync(request, dbContext, http, cancellationToken);

    private static Task<IResult> UpgradeDeclarationAsync(Guid id, UpgradeDeclarationRequest request,
        AdminDbContext dbContext, HttpContext http, CancellationToken cancellationToken) =>
        DeclarationExperienceBridge.SubmitAsync(request.Submission, dbContext, http, cancellationToken, id, request.ExpectedLegacyVersion);

    private static async Task<IResult> GetMyDeclarationsAsync(
        string? semesterId, string? status, string? pillar, int? page, int? pageSize,
        ICurrentActor actor,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!actor.UserId.HasValue)
        {
            throw new OperationForbiddenException("An authenticated user ID is required to query declarations.");
        }

        return await StudentContributionReadService.PageAsync(actor.UserId.Value, semesterId, status, pillar,
            page, pageSize, dbContext, cancellationToken, declarationsOnly: true);
    }

    private static async Task<IResult> GetDeclarationByIdAsync(
        Guid id,
        ICurrentActor actor,
        AdminDbContext dbContext,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Contributions.AnyAsync(x => x.Id == id && x.SourceType != "CLUB", cancellationToken))
            return await VerifiedContributionEndpoints.GetAsync(id, dbContext, http, cancellationToken);
        var declaration = await dbContext.SelfDeclarations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("The requested declaration was not found.");

        // Non-staff users can only access their own declaration
        var isAdmin = actor.Roles.Contains(AuthRoles.Admin);
        var canReadAsStaff = declaration.IsOutsideClub && declaration.ClubId is null
            && (isAdmin || actor.Roles.Contains(AuthRoles.StudentAffairsAdmin) && declaration.CampusCode == actor.CampusCode);

        var grant = await ExperienceAuthorization.FindAsync(dbContext, actor.UserId ?? 0,
            declaration.IsOutsideClub ? "EXTERNAL" : "CLUB", declaration.ClubId, "contribution.review", cancellationToken);
        if (declaration.StudentId != actor.UserId && grant is null && !canReadAsStaff)
        {
            throw new OperationForbiddenException("You do not have permission to view declarations of another student.");
        }

        return Results.Ok(DeclarationResponse.From(declaration));
    }

    private static async Task<IResult> UpdateDeclarationAsync(
        Guid id,
        SubmitDeclarationRequest request,
        ICurrentActor actor,
        AdminDbContext dbContext,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Contributions.AnyAsync(x => x.Id == id && x.SourceType != "CLUB", cancellationToken))
        {
            if (!request.ExpectedVersion.HasValue || request.ProposedAttributes is null || request.EvidenceRefs is null)
                return ApiOperations.Error(400, "DECLARATION_REVISION_REQUIRED", "Provide expectedVersion, attributes and evidence.");
            return await VerifiedContributionEndpoints.ReviseAsync(id, new ReviseClubContributionRequest(request.ExpectedVersion.Value,
                request.ProposedAttributes, request.EvidenceDescription ?? "", request.EvidenceRefs), dbContext, http,
                http.RequestServices.GetRequiredService<ActivitySourceClient>(), cancellationToken);
        }
        var legacy = await dbContext.SelfDeclarations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("The requested declaration was not found.");
        if (legacy.StudentId != actor.UserId) return Results.Forbid();
        return ApiOperations.Error(409, "LEGACY_DECLARATION_UPGRADE_REQUIRED", "Use the owned, versioned declaration upgrade with a semester and private evidence. Historical decisions are read-only.");
    }

    private static async Task<IResult> GetDeclarationsQueueAsync(
        int? page,
        int? pageSize,
        string? status,
        string? category,
        string? campus,
        bool? isOutsideClub,
        bool? viaClub,
        AdminDbContext dbContext,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var request = new PageRequest(page ?? 1, pageSize ?? 20);
        request.Validate();

        var query = dbContext.SelfDeclarations.AsNoTracking().AsQueryable();
        var clubs = await ExperienceAuthorization.ClubsAsync(dbContext, actor.UserId ?? 0, "contribution.review", cancellationToken);
        var external = await ExperienceAuthorization.FindAsync(dbContext, actor.UserId ?? 0, "EXTERNAL", null, "contribution.review", cancellationToken) is not null;
        var realWorld = await ExperienceAuthorization.FindAsync(dbContext, actor.UserId ?? 0, "REAL_WORLD", null, "contribution.review", cancellationToken) is not null;
        var staff = actor.Roles.Any(role => role is AuthRoles.Admin or AuthRoles.StudentAffairsAdmin);
        var admin = actor.Roles.Contains(AuthRoles.Admin);
        var actorCampus = actor.CampusCode;
        if (!staff && clubs.Count == 0 && !external && !realWorld) return Results.Forbid();
        // Club referrals have a separate BOD queue. Only independent campus staff may see them here after forwarding.
        query = query.Where(x => !dbContext.Contributions.Any(c => c.Id == x.Id && c.ClubReviewStatus != null)
            || staff && (admin || x.CampusCode == actorCampus) && dbContext.Contributions.Any(c => c.Id == x.Id
                && c.ClubReviewStatus == "APPROVED" && c.CtsvSubmissionStatus == "SUBMITTED"
                && c.ClubReviewedBy != actor.UserId && c.CtsvSubmittedBy != actor.UserId));
        query = query.Where(x => x.StudentId != actor.UserId &&
            (x.ClubId != null && clubs.Contains(x.ClubId.Value) ||
             dbContext.Contributions.Any(c => c.Id == x.Id && (external && c.SourceType == "EXTERNAL" || realWorld && c.SourceType == "REAL_WORLD")) ||
             staff && x.IsOutsideClub && (admin || x.CampusCode == actorCampus) && (x.ClubId == null
                 || dbContext.Contributions.Any(c => c.Id == x.Id && c.ClubReviewStatus == "APPROVED" && c.CtsvSubmissionStatus == "SUBMITTED"))));

        if (!string.IsNullOrWhiteSpace(campus))
        {
            var normalizedCampus = ClubReportHub.Shared.Auth.CampusCodes.Normalize(campus);
            if (normalizedCampus != ClubReportHub.Shared.Auth.CampusCodes.Global)
            {
                query = query.Where(x => x.CampusCode == normalizedCampus);
            }
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim();
            query = query.Where(x => x.Status == normalizedStatus);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalizedCategory = category.Trim();
            query = query.Where(x => x.Category == normalizedCategory || x.FinalCategory == normalizedCategory);
        }

        if (isOutsideClub.HasValue)
        {
            query = query.Where(x => x.IsOutsideClub == isOutsideClub.Value);
        }
        if (viaClub.HasValue)
            query = query.Where(x => dbContext.Contributions.Any(c => c.Id == x.Id && c.ClubReviewStatus != null) == viaClub.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var ids = items.Select(x => x.Id).ToArray();
        var canonical = await dbContext.Contributions.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var responses = items.Select(item => canonical.TryGetValue(item.Id, out var row)
            ? (object)(VerifiedExperienceService.Response(row) with
            {
                Capabilities = ClubDeclarationWorkflow.IsReferral(row) ? new(false, false, row.Status == "SUBMITTED") : null
                ,
                StudentName = item.StudentName,
                StudentEmail = item.StudentEmail
            }) : DeclarationResponse.From(item)).ToArray();
        return Results.Ok(PagedResult<object>.Create(responses, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> ReviewDeclarationAsync(
        Guid id,
        ReviewDeclarationRequest request,
        ICurrentActor actor,
        AdminDbContext dbContext,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Contributions.AnyAsync(x => x.Id == id && x.SourceType != "CLUB", cancellationToken))
        {
            if (!request.ExpectedVersion.HasValue) return ApiOperations.Error(400, "EXPECTED_VERSION_REQUIRED", "Provide the declaration version.");
            var canonicalDecision = request.Decision.ToUpperInvariant() switch { "APPROVED" => "APPROVE", "REJECTED" => "REJECT", "REVISIONREQUESTED" => "REQUEST_REVISION", var value => value };
            return await VerifiedContributionEndpoints.ReviewAsync(id, new ReviewContributionRequest(canonicalDecision,
                request.Reason ?? request.ReviewNote ?? "", request.ExpectedVersion.Value, request.VerifiedAttributes), dbContext, http,
                http.RequestServices.GetRequiredService<ActivitySourceClient>(), http.RequestServices.GetRequiredService<IConfiguration>(), cancellationToken);
        }
        var declaration = await dbContext.SelfDeclarations
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("The requested declaration was not found.");
        if (!declaration.IsOutsideClub || declaration.ClubId.HasValue
            || !actor.Roles.Any(role => role is AuthRoles.Admin or AuthRoles.StudentAffairsAdmin))
            return ApiOperations.Error(409, "LEGACY_SOURCE_MIGRATION_REQUIRED", "Legacy records need a linked, versioned source before review. Use the contribution workflow for club sessions.");
        if (!actor.Roles.Contains(AuthRoles.Admin) && declaration.CampusCode != actor.CampusCode)
            throw new OperationForbiddenException("Only student affairs in the declaration's campus may review it.");

        return ApiOperations.Error(409, "LEGACY_DECLARATION_UPGRADE_REQUIRED", "Legacy declarations cannot receive new decisions or points. The author must upgrade an eligible pending record with a semester and private evidence.");
    }
}
