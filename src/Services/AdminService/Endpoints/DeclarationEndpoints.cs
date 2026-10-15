using AdminService.Auditing;
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

    private static async Task<IResult> SubmitDeclarationAsync(
        SubmitDeclarationRequest request,
        ICurrentActor actor,
        AdminDbContext dbContext,
        IAuditService auditService,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (request.SemesterId is not null || request.ProposedAttributes is not null || request.EvidenceRefs is not null)
            return await DeclarationExperienceBridge.SubmitAsync(request, dbContext, http, cancellationToken);
        if (!actor.UserId.HasValue)
        {
            throw new OperationForbiddenException("An authenticated user ID is required to submit a declaration.");
        }

        ValidateSubmission(request);

        var campusCode = ClubReportHub.Shared.Auth.CampusCodes.Normalize(request.CampusCode);

        var declaration = new SelfDeclaration
        {
            StudentId = actor.UserId.Value,
            StudentName = actor.Email ?? actor.SubjectId ?? $"User {actor.UserId.Value}",
            StudentEmail = actor.Email ?? string.Empty,
            CampusCode = campusCode,
            Title = request.Title.Trim(),
            Category = request.Category.Trim(),
            OrganizationSource = request.OrganizationSource.Trim(),
            IsOutsideClub = request.IsOutsideClub,
            ClubId = request.ClubId,
            EvidenceUrl = request.EvidenceUrl.Trim(),
            EvidenceDescription = request.EvidenceDescription.Trim(),
            RoleProposed = string.IsNullOrWhiteSpace(request.RoleProposed) ? null : request.RoleProposed.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = DeclarationStatuses.Submitted,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.SelfDeclarations.Add(declaration);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(new AuditWriteRequest(
            Action: "SELF_DECLARATION_SUBMITTED",
            ResourceType: "SelfDeclaration",
            ResourceId: declaration.Id.ToString(),
            Outcome: AuditOutcomes.Succeeded,
            Metadata: new Dictionary<string, string?>
            {
                ["StudentId"] = declaration.StudentId.ToString(),
                ["Category"] = declaration.Category,
                ["Title"] = declaration.Title
            }), cancellationToken);

        return Results.Created($"/api/v1/declarations/{declaration.Id}", DeclarationResponse.From(declaration));
    }

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
        var isStaff = actor.Roles.Any(r =>
            string.Equals(r, AuthRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, AuthRoles.StudentAffairsAdmin, StringComparison.OrdinalIgnoreCase));

        var grant = await ExperienceAuthorization.FindAsync(dbContext, actor.UserId ?? 0,
            declaration.IsOutsideClub ? "EXTERNAL" : "CLUB", declaration.ClubId, "contribution.review", cancellationToken);
        if (declaration.StudentId != actor.UserId && grant is null && (!isStaff || !declaration.IsOutsideClub))
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
        IAuditService auditService,
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
        if (!actor.UserId.HasValue)
        {
            throw new OperationForbiddenException("An authenticated user ID is required to update a declaration.");
        }

        var declaration = await dbContext.SelfDeclarations
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new ResourceNotFoundException("The requested declaration was not found.");

        if (declaration.StudentId != actor.UserId.Value)
        {
            throw new OperationForbiddenException("You do not have permission to edit another student's declaration.");
        }

        if (declaration.Status == DeclarationStatuses.Approved)
        {
            throw new BusinessRuleException("Minh chứng đã được phê duyệt không thể chỉnh sửa.");
        }

        if (declaration.Status == DeclarationStatuses.Rejected)
        {
            throw new BusinessRuleException("Minh chứng đã bị từ chối không thể chỉnh sửa. Vui lòng tạo minh chứng mới nếu cần.");
        }

        ValidateSubmission(request);

        declaration.Title = request.Title.Trim();
        declaration.Category = request.Category.Trim();
        declaration.OrganizationSource = request.OrganizationSource.Trim();
        declaration.IsOutsideClub = request.IsOutsideClub;
        declaration.ClubId = request.ClubId;
        declaration.EvidenceUrl = request.EvidenceUrl.Trim();
        declaration.EvidenceDescription = request.EvidenceDescription.Trim();
        declaration.RoleProposed = string.IsNullOrWhiteSpace(request.RoleProposed) ? null : request.RoleProposed.Trim();
        declaration.StartDate = request.StartDate;
        declaration.EndDate = request.EndDate;
        declaration.Status = DeclarationStatuses.Submitted; // Reset to Submitted for CTSV review

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(new AuditWriteRequest(
            Action: "SELF_DECLARATION_UPDATED",
            ResourceType: "SelfDeclaration",
            ResourceId: declaration.Id.ToString(),
            Outcome: AuditOutcomes.Succeeded,
            Metadata: new Dictionary<string, string?>
            {
                ["StudentId"] = declaration.StudentId.ToString(),
                ["Category"] = declaration.Category,
                ["Title"] = declaration.Title
            }), cancellationToken);

        return Results.Ok(DeclarationResponse.From(declaration));
    }

    private static async Task<IResult> GetDeclarationsQueueAsync(
        int? page,
        int? pageSize,
        string? status,
        string? category,
        string? campus,
        bool? isOutsideClub,
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
        if (!staff && clubs.Count == 0 && !external && !realWorld) return Results.Forbid();
        query = query.Where(x => x.StudentId != actor.UserId &&
            (x.ClubId != null && clubs.Contains(x.ClubId.Value) ||
             dbContext.Contributions.Any(c => c.Id == x.Id && (external && c.SourceType == "EXTERNAL" || realWorld && c.SourceType == "REAL_WORLD")) ||
             staff && x.IsOutsideClub && !dbContext.Contributions.Any(c => c.Id == x.Id)));

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

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var ids = items.Select(x => x.Id).ToArray();
        var canonical = await dbContext.Contributions.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var responses = items.Select(item => canonical.TryGetValue(item.Id, out var row)
            ? (object)VerifiedExperienceService.Response(row) : DeclarationResponse.From(item)).ToArray();
        return Results.Ok(PagedResult<object>.Create(responses, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> ReviewDeclarationAsync(
        Guid id,
        ReviewDeclarationRequest request,
        ICurrentActor actor,
        AdminDbContext dbContext,
        IAuditService auditService,
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
        if (!declaration.IsOutsideClub || !actor.Roles.Any(role => role is AuthRoles.Admin or AuthRoles.StudentAffairsAdmin))
            return ApiOperations.Error(409, "LEGACY_SOURCE_MIGRATION_REQUIRED", "Legacy records need a linked, versioned source before review. Use the contribution workflow for club sessions.");

        // SOD Invariant: Reviewer cannot self-approve their own declaration (Appendix B & NFR01)
        if (actor.UserId.HasValue && declaration.StudentId == actor.UserId.Value)
        {
            throw new BusinessRuleException("Cán bộ CTSV không được tự thẩm định hoặc phê duyệt minh chứng của chính mình.");
        }

        if (declaration.Status == DeclarationStatuses.Approved)
        {
            throw new BusinessRuleException("Minh chứng này đã được phê duyệt trước đó và không thể thẩm định lại.");
        }

        ValidateReview(request);

        var decision = NormalizeDecision(request.Decision);
        if (string.Equals(decision, DeclarationDecisions.Approved, StringComparison.OrdinalIgnoreCase))
        {
            var tier = !string.IsNullOrWhiteSpace(request.Tier) ? request.Tier.Trim() : (declaration.Tier ?? EffortTiers.T1);
            var role = !string.IsNullOrWhiteSpace(request.Role) ? request.Role.Trim() : (declaration.Role ?? ContributionRoles.Participant);
            var scale = !string.IsNullOrWhiteSpace(request.Scale) ? request.Scale.Trim() : (declaration.Scale ?? ActivityScales.Club);
            var bonusResult = !string.IsNullOrWhiteSpace(request.BonusResult) ? request.BonusResult.Trim() : declaration.BonusResult;

            var rawPoints = ExperienceScoringCalculator.CalculateRawPoints(tier, role, scale, bonusResult);

            declaration.FinalCategory = string.IsNullOrWhiteSpace(request.FinalCategory)
                ? declaration.Category
                : request.FinalCategory.Trim();
            declaration.Tier = tier;
            declaration.Role = role;
            declaration.Scale = scale;
            declaration.BonusResult = bonusResult;
            declaration.RawPoints = rawPoints;
            declaration.Status = DeclarationStatuses.Approved;
        }
        else if (string.Equals(decision, DeclarationDecisions.RevisionRequested, StringComparison.OrdinalIgnoreCase))
        {
            declaration.Status = DeclarationStatuses.RevisionRequested;
            declaration.Tier = null;
            declaration.Role = null;
            declaration.Scale = null;
            declaration.BonusResult = null;
            declaration.RawPoints = null;
        }
        else
        {
            declaration.Status = DeclarationStatuses.Rejected;
            declaration.Tier = null;
            declaration.Role = null;
            declaration.Scale = null;
            declaration.BonusResult = null;
            declaration.RawPoints = null;
        }

        declaration.ReviewNote = request.ReviewNote?.Trim();
        declaration.ReviewedAtUtc = DateTimeOffset.UtcNow;
        declaration.ReviewedByUserId = actor.UserId;
        declaration.ReviewedByName = actor.Email ?? actor.SubjectId;

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(new AuditWriteRequest(
            Action: "SELF_DECLARATION_REVIEWED",
            ResourceType: "SelfDeclaration",
            ResourceId: declaration.Id.ToString(),
            Outcome: AuditOutcomes.Succeeded,
            Metadata: new Dictionary<string, string?>
            {
                ["Decision"] = declaration.Status,
                ["StudentId"] = declaration.StudentId.ToString(),
                ["Category"] = declaration.FinalCategory ?? declaration.Category,
                ["RawPoints"] = declaration.RawPoints?.ToString("F1")
            }), cancellationToken);

        return Results.Ok(DeclarationResponse.From(declaration));
    }

    private static void ValidateSubmission(SubmitDeclarationRequest request)
    {
        var details = new List<ErrorDetail>();

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            details.Add(new ErrorDetail("title", "Title is required."));
        }
        else if (request.Title.Length > 200)
        {
            details.Add(new ErrorDetail("title", "Title cannot exceed 200 characters."));
        }

        if (string.IsNullOrWhiteSpace(request.Category) || !ExperienceCategories.IsValid(request.Category))
        {
            details.Add(new ErrorDetail(
                "category",
                $"Category must be one of: {string.Join(", ", ExperienceCategories.All)}."));
        }

        if (string.IsNullOrWhiteSpace(request.OrganizationSource))
        {
            details.Add(new ErrorDetail("organizationSource", "Organization source is required."));
        }
        else if (request.OrganizationSource.Length > 150)
        {
            details.Add(new ErrorDetail("organizationSource", "Organization source cannot exceed 150 characters."));
        }

        if (string.IsNullOrWhiteSpace(request.EvidenceUrl))
        {
            details.Add(new ErrorDetail("evidenceUrl", "Evidence URL or reference is required."));
        }
        else if (request.EvidenceUrl.Length > 1000)
        {
            details.Add(new ErrorDetail("evidenceUrl", "Evidence URL cannot exceed 1000 characters."));
        }

        if (string.IsNullOrWhiteSpace(request.EvidenceDescription))
        {
            details.Add(new ErrorDetail("evidenceDescription", "Evidence description is required."));
        }
        else if (request.EvidenceDescription.Length > 2000)
        {
            details.Add(new ErrorDetail("evidenceDescription", "Evidence description cannot exceed 2000 characters."));
        }

        if (!string.IsNullOrWhiteSpace(request.RoleProposed) && !ContributionRoles.IsValid(request.RoleProposed))
        {
            details.Add(new ErrorDetail(
                "roleProposed",
                $"Proposed role must be one of: {string.Join(", ", ContributionRoles.All)}."));
        }

        if (request.EndDate < request.StartDate)
        {
            details.Add(new ErrorDetail("endDate", "End date must be greater than or equal to start date."));
        }

        if (details.Count > 0)
        {
            throw new RequestValidationException(details);
        }
    }

    private static void ValidateReview(ReviewDeclarationRequest request)
    {
        var details = new List<ErrorDetail>();

        var normalizedDecision = NormalizeDecision(request.Decision);
        if (string.IsNullOrWhiteSpace(normalizedDecision) || !DeclarationDecisions.IsValid(normalizedDecision))
        {
            details.Add(new ErrorDetail(
                "decision",
                $"Decision must be one of: {string.Join(", ", DeclarationDecisions.All)} (or NEED_CLARIFICATION)."));
        }

        if (string.Equals(normalizedDecision, DeclarationDecisions.Approved, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(request.Tier) && !EffortTiers.IsValid(request.Tier))
            {
                details.Add(new ErrorDetail(
                    "tier",
                    $"Tier must be one of: {string.Join(", ", EffortTiers.All)}."));
            }

            if (!string.IsNullOrWhiteSpace(request.Role) && !ContributionRoles.IsValid(request.Role))
            {
                details.Add(new ErrorDetail(
                    "role",
                    $"Role must be one of: {string.Join(", ", ContributionRoles.All)}."));
            }

            if (!string.IsNullOrWhiteSpace(request.Scale) && !ActivityScales.IsValid(request.Scale))
            {
                details.Add(new ErrorDetail(
                    "scale",
                    $"Scale must be one of: {string.Join(", ", ActivityScales.All)}."));
            }

            if (!string.IsNullOrWhiteSpace(request.BonusResult) && !BonusResults.IsValid(request.BonusResult))
            {
                details.Add(new ErrorDetail(
                    "bonusResult",
                    $"Bonus result must be one of: {string.Join(", ", BonusResults.All)}."));
            }

            if (!string.IsNullOrWhiteSpace(request.FinalCategory) && !ExperienceCategories.IsValid(request.FinalCategory))
            {
                details.Add(new ErrorDetail(
                    "finalCategory",
                    $"Final category must be one of: {string.Join(", ", ExperienceCategories.All)}."));
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.ReviewNote))
            {
                details.Add(new ErrorDetail(
                    "reviewNote",
                    "Review note is required when requesting revisions or rejecting a declaration."));
            }
        }

        if (details.Count > 0)
        {
            throw new RequestValidationException(details);
        }
    }

    private static string NormalizeDecision(string? decision)
    {
        if (string.IsNullOrWhiteSpace(decision)) return string.Empty;
        var trimmed = decision.Trim();
        if (string.Equals(trimmed, "NEED_CLARIFICATION", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "CLARIFICATION", StringComparison.OrdinalIgnoreCase))
        {
            return DeclarationDecisions.RevisionRequested;
        }
        return trimmed;
    }
}

