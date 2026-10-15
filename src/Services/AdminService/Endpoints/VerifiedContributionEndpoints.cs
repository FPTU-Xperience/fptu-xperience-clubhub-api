using AdminService.Contracts;
using AdminService.Data;
using AdminService.Security;
using AdminService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using static AdminService.Services.VerifiedExperienceService;

namespace AdminService.Endpoints;

public static class VerifiedContributionEndpoints
{
    public static void MapVerifiedContributionEndpoints(this IEndpointRouteBuilder app)
    {
        var activities = app.MapGroup("/api/activities/{activityId:int}/contributions")
            .RequireAuthorization(AdminPolicies.AnyActor).WithTags("Verified club contributions");
        activities.MapPost("/", SubmitAsync);
        activities.MapGet("/", ListActivityAsync);
        var contributions = app.MapGroup("/api/contributions").RequireAuthorization(AdminPolicies.AnyActor)
            .WithTags("Verified club contributions");
        contributions.MapGet("/{id:guid}", GetAsync);
        contributions.MapPatch("/{id:guid}", ReviseAsync);
        contributions.MapPost("/{id:guid}/reviews", ReviewAsync);
        contributions.MapPost("/{id:guid}/role-upgrade-requests", RequestUpgradeAsync);
        contributions.MapPost("/{id:guid}/role-upgrade-requests/{requestId:guid}/reviews", ReviewUpgradeAsync);
        contributions.MapPost("/{id:guid}/adjustments", AdjustAsync);
        app.MapGet("/api/users/me/contributions", ListSelfAsync).RequireAuthorization(AdminPolicies.AnyActor).WithTags("Personal experience");
        app.MapGet("/api/users/me/experience-ledger", LedgerAsync).RequireAuthorization(AdminPolicies.AnyActor).WithTags("Personal experience");
        app.MapGet("/api/verifications/me/queue", QueueAsync).RequireAuthorization(AdminPolicies.AnyActor).WithTags("Verification queue");
    }

    private static async Task<IResult> SubmitAsync(int activityId, SubmitClubContributionRequest request,
        AdminDbContext db, HttpContext http, ActivitySourceClient sources, CancellationToken cancellationToken)
    {
        var actor = http.User.GetUserId();
        var error = VerifiedExperienceRules.Validate(request.ProposedAttributes);
        if (error is not null || request.SessionId <= 0 || string.IsNullOrWhiteSpace(request.ContributionKind)
            || request.ContributionKind.Length > 40 || request.Message?.Length > 2000
            || !await OwnsEvidenceAsync(db, actor, request.EvidenceRefs, cancellationToken))
            return ApiOperations.Error(400, "INVALID_CONTRIBUTION", error ?? "Check the session, message and owned evidence references.");
        ActivitySourceContext? context;
        try { context = await sources.GetAsync(activityId, request.SessionId, actor, cancellationToken); }
        catch (HttpRequestException) { return ApiOperations.Error(503, "ACTIVITY_SERVICE_UNAVAILABLE", "Activity source is unavailable."); }
        if (context is null) return Results.NotFound();
        var source = context.Source;
        if (!context.IsRegistered) return Results.Forbid();
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            if (source.ActivityVersion != request.ExpectedActivityVersion || source.Status == "CANCELLED")
                return ApiOperations.Error(409, "STALE_ACTIVITY_SOURCE", "Reload the activity and session.");
            if (source.StartTimeUtc > DateTimeOffset.UtcNow || !SameBlueprint(request.ProposedAttributes, source.Attributes))
                return ApiOperations.Error(400, "INVALID_SOURCE_ATTRIBUTES", "The effort must have occurred and match its published tier, scale and vector.");
            var key = ClubSourceKey(activityId, request.SessionId);
            if (await db.Contributions.AnyAsync(x => x.UserId == actor && x.SourceKey == key, cancellationToken))
                return ApiOperations.Error(409, "SOURCE_ALREADY_SUBMITTED", "Revise the existing contribution or request a role upgrade for this effort.");
            var row = new VerifiedContribution
            {
                UserId = actor,
                ClubId = source.ClubId,
                ActivityId = activityId,
                SessionId = source.SessionId,
                SourceKey = key,
                SourceId = $"{activityId}/{source.SessionId}",
                SemesterId = source.SemesterId,
                Title = source.Title,
                Message = request.Message?.Trim() ?? string.Empty,
                ProposedAttributesJson = Json(request.ProposedAttributes),
                EvidenceRefsJson = Json(request.EvidenceRefs),
                SourceSnapshotJson = Json(source),
                OccurredAt = source.StartTimeUtc,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.Contributions.Add(row);
            Revision(db, row, "SUBMITTED", request.ProposedAttributes, actor, "Submitted by author.");
            ExperienceAuthorization.Audit(db, http.User, "CONTRIBUTION_SUBMITTED", "Contribution", row.Id.ToString(),
                new { activityId, source.SessionId });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/contributions/{row.Id}", Response(row));
        }, cancellationToken);
    }

    internal static async Task<IResult> GetAsync(Guid id, AdminDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var row = await db.Contributions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null) return Results.NotFound();
        if (row.UserId != http.User.GetUserId() && await ExperienceAuthorization.FindAsync(db, http.User.GetUserId(),
                row.SourceType, row.ClubId, "contribution.review", cancellationToken) is null) return Results.Forbid();
        var revisions = await db.ContributionRevisions.AsNoTracking().Where(x => x.ContributionId == id).OrderBy(x => x.Ordinal).ToListAsync(cancellationToken);
        var decisions = await db.ContributionDecisions.AsNoTracking().Where(x => x.ContributionId == id).OrderBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        var upgrades = await db.ContributionUpgradeRequests.AsNoTracking().Where(x => x.ContributionId == id).ToListAsync(cancellationToken);
        var effects = await db.ExperienceLedgerEffects.AsNoTracking().Where(x => x.ContributionId == id).OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var effectIds = effects.Select(x => x.Id).ToArray();
        var allocations = await db.ExperienceLedgerAllocations.AsNoTracking().Where(x => effectIds.Contains(x.EffectId)).ToListAsync(cancellationToken);
        var policyIds = effects.Select(x => x.PolicyVersion).Distinct().ToArray();
        var policies = await db.ApprovedExperiencePolicies.AsNoTracking().Where(x => policyIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var history = new List<object>();
        decimal historicalRaw = 0;
        var historicalAllocations = new Dictionary<string, decimal>();
        foreach (var effect in effects)
        {
            var delta = allocations.Where(a => a.EffectId == effect.Id).ToDictionary(a => a.Pillar, a => a.Amount);
            history.Add(StudentCalculationService.Effect(effect, revisions.SingleOrDefault(r => r.Id == effect.RevisionId),
                policies.TryGetValue(effect.PolicyVersion, out var policy) ? Read<ApprovedExperiencePolicySnapshot>(policy.SnapshotJson) : null,
                delta, historicalRaw, new Dictionary<string, decimal>(historicalAllocations)));
            historicalRaw += effect.Amount;
            foreach (var allocation in delta) historicalAllocations[allocation.Key] = historicalAllocations.GetValueOrDefault(allocation.Key) + allocation.Value;
        }
        return Results.Ok(new
        {
            contribution = Response(row),
            revisions,
            decisions,
            roleUpgradeRequests = upgrades,
            calculationBreakdown = StudentCalculationService.Current(row),
            ledgerEffects = history
        });
    }

    private static async Task<IResult> ListActivityAsync(int activityId, int? page, int? pageSize, AdminDbContext db,
        HttpContext http, ActivitySourceClient sources, CancellationToken cancellationToken)
    {
        var clubs = await ExperienceAuthorization.ClubsAsync(db, http.User.GetUserId(), "contribution.review", cancellationToken);
        if (clubs.Count == 0) return Results.Forbid();
        ActivityExperienceScope? scope;
        try { scope = await sources.GetScopeAsync(activityId, cancellationToken); }
        catch (HttpRequestException) { return ApiOperations.Error(503, "ACTIVITY_SERVICE_UNAVAILABLE", "Activity scope is unavailable."); }
        if (scope is null) return Results.NotFound();
        if (!clubs.Contains(scope.ClubId)) return Results.Forbid();
        var query = db.Contributions.AsNoTracking().Where(x => x.ActivityId == activityId && x.ClubId == scope.ClubId);
        return await PageAsync(query, page, pageSize, cancellationToken);
    }

    private static Task<IResult> ListSelfAsync(string? semesterId, string? status, string? pillar, int? page, int? pageSize,
        AdminDbContext db, HttpContext http, CancellationToken cancellationToken)
        => StudentContributionReadService.PageAsync(http.User.GetUserId(), semesterId, status, pillar, page, pageSize, db, cancellationToken);

    private static async Task<IResult> PageAsync(IQueryable<VerifiedContribution> query, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var current = Math.Max(page ?? 1, 1); var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((int)Math.Min((long)(current - 1) * size, int.MaxValue)).Take(size).ToListAsync(cancellationToken);
        return Results.Ok(new
        {
            items = rows.Select(Response),
            page = current,
            pageSize = size,
            total,
            totalPages = (int)Math.Ceiling((decimal)total / size)
        });
    }

    internal static async Task<IResult> ReviseAsync(Guid id, ReviseClubContributionRequest request, AdminDbContext db,
        HttpContext http, ActivitySourceClient sources, CancellationToken cancellationToken)
    {
        var row = await db.Contributions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null) return Results.NotFound();
        var actor = http.User.GetUserId();
        if (row.UserId != actor) return Results.Forbid();
        var error = VerifiedExperienceRules.Validate(request.ProposedAttributes);
        if (error is not null || request.Message?.Length > 2000 || !await OwnsEvidenceAsync(db, actor, request.EvidenceRefs, cancellationToken))
            return ApiOperations.Error(400, "INVALID_CONTRIBUTION", error ?? "Check the message and evidence.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            if (row.Version != request.ExpectedVersion) return ApiOperations.Error(409, "STALE_VERSION", "Reload the contribution.");
            if (row.Status is not ("SUBMITTED" or "REVISION_REQUESTED"))
                return ApiOperations.Error(409, "CONTRIBUTION_IMMUTABLE", "This contribution cannot be edited by its author.");
            if (row.SourceType == "CLUB")
            {
                ActivitySourceContext? live;
                try { live = await sources.GetAsync(row.ActivityId!.Value, row.SessionId!.Value, row.UserId, cancellationToken); }
                catch (HttpRequestException) { return ApiOperations.Error(503, "ACTIVITY_SERVICE_UNAVAILABLE", "Activity source is unavailable."); }
                if (live is null || !live.IsRegistered || live.Source.Status == "CANCELLED") return ApiOperations.Error(409, "STALE_ACTIVITY_SOURCE", "The source is no longer eligible.");
                var source = live.Source;
                row.SourceSnapshotJson = Json(source);
                if (!SameBlueprint(request.ProposedAttributes, source.Attributes)) return ApiOperations.Error(400, "INVALID_SOURCE_ATTRIBUTES", "Keep the published tier, scale and vector.");
            }
            else if ((row.SourceType == "REAL_WORLD") != request.ProposedAttributes.PillarWeights.All(x => x.Pillar == ExperienceCategories.RealWorldWork))
                return ApiOperations.Error(400, "SOURCE_TYPE_IMMUTABLE", "Keep this declaration in its assigned RealWorldWork scope.");
            row.ProposedAttributesJson = Json(request.ProposedAttributes); row.EvidenceRefsJson = Json(request.EvidenceRefs);
            row.Message = request.Message?.Trim() ?? string.Empty; row.Status = "SUBMITTED";
            Revision(db, row, "SUBMITTED", request.ProposedAttributes, actor, "Author revision.");
            await DeclarationExperienceBridge.SyncAsync(db, row, cancellationToken);
            ExperienceAuthorization.Audit(db, http.User, "CONTRIBUTION_REVISED", "Contribution", id.ToString(), new { row.CurrentRevisionId });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(Response(row));
        }, cancellationToken);
    }

    internal static async Task<IResult> ReviewAsync(Guid id, ReviewContributionRequest request, AdminDbContext db,
        HttpContext http, ActivitySourceClient sources, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var row = await db.Contributions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null) return Results.NotFound();
        var actor = http.User.GetUserId();
        if (row.UserId == actor) return ApiOperations.Error(403, "SELF_REVIEW_FORBIDDEN", "You cannot verify your own effort.");
        if (await ExperienceAuthorization.FindAsync(db, actor, row.SourceType, row.ClubId, "contribution.review", cancellationToken) is null)
            return Results.Forbid();
        if (!ValidDecision(request.Decision, request.Reason)) return ApiOperations.Error(400, "INVALID_REVIEW", "Provide a valid decision and reason.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            var grant = await ExperienceAuthorization.FindAsync(db, actor, row.SourceType, row.ClubId, "contribution.review", cancellationToken);
            if (grant is null) return Results.Forbid();
            if (row.Version != request.ExpectedVersion || row.Status != "SUBMITTED")
                return ApiOperations.Error(409, "STALE_CONTRIBUTION", "Reload the submitted contribution.");
            var target = row.CurrentRevisionId;
            var proposed = Read<ContributionAttributes>(row.ProposedAttributesJson);
            ApprovedExperiencePolicySnapshot? policy = null;
            var attributes = proposed;
            if (request.Decision == "APPROVE")
            {
                if (request.VerifiedAttributes is null || VerifiedExperienceRules.Validate(request.VerifiedAttributes) is { } validation)
                    return ApiOperations.Error(400, "VERIFIED_ATTRIBUTES_REQUIRED", "Provide valid verified attributes.");
                if (row.SourceType == "CLUB")
                {
                    ActivitySourceContext? live;
                    try { live = await sources.GetAsync(row.ActivityId!.Value, row.SessionId!.Value, row.UserId, cancellationToken); }
                    catch (HttpRequestException) { return ApiOperations.Error(503, "ACTIVITY_SERVICE_UNAVAILABLE", "Activity source is unavailable."); }
                    var saved = Read<ExperienceSourceSnapshot>(row.SourceSnapshotJson);
                    if (live is null || live.Source.Status == "CANCELLED" || !live.IsRegistered
                        || live.Source.SessionVersion != saved.SessionVersion || live.Source.Policy.PolicyVersion != saved.Policy.PolicyVersion
                        || !SameBlueprint(request.VerifiedAttributes, saved.Attributes))
                        return ApiOperations.Error(409, "STALE_ACTIVITY_SOURCE", "The source changed or is no longer valid.");
                    policy = await PolicyAsync(db, saved.Policy.PolicyVersion, row.SemesterId,
                        configuration["Experience:Environment"] ?? "PROJECT", cancellationToken);
                }
                else
                {
                    if ((row.SourceType == "REAL_WORLD") != request.VerifiedAttributes.PillarWeights.All(x => x.Pillar == ExperienceCategories.RealWorldWork))
                        return ApiOperations.Error(400, "SOURCE_TYPE_IMMUTABLE", "RealWorldWork declarations stay inside their assigned scope.");
                    var environment = configuration["Experience:Environment"] ?? "PROJECT";
                    using var snapshot = System.Text.Json.JsonDocument.Parse(row.SourceSnapshotJson);
                    var requestedPolicy = snapshot.RootElement.TryGetProperty("requestedPolicyVersion", out var version)
                        && version.ValueKind == System.Text.Json.JsonValueKind.String && Guid.TryParse(version.GetString(), out var parsedVersion) ? (Guid?)parsedVersion : null;
                    var approved = await db.ApprovedExperiencePolicies.AsNoTracking().Where(x => x.SemesterId == row.SemesterId && x.Environment == environment
                        && (requestedPolicy == null || x.Id == requestedPolicy))
                        .OrderByDescending(x => x.ApprovedAt).FirstOrDefaultAsync(cancellationToken);
                    policy = approved is null ? null : Read<ApprovedExperiencePolicySnapshot>(approved.SnapshotJson);
                }
                if (policy is null) return ApiOperations.Error(503, "SCORE_POLICY_UNAPPROVED", "No approved policy is available for this environment.");
                attributes = request.VerifiedAttributes;
            }
            grant.Version = Guid.NewGuid();
            var status = request.Decision switch { "APPROVE" => "APPROVED", "REJECT" => "REJECTED", _ => "REVISION_REQUESTED" };
            var revision = Revision(db, row, status, attributes, actor, request.Reason.Trim());
            var decision = new ContributionDecision
            {
                ContributionId = id,
                TargetRevisionId = target,
                RevisionId = revision.Id,
                ReviewerUserId = actor,
                AssignmentId = grant.Id,
                AssignmentVersion = grant.Version,
                Decision = request.Decision,
                Reason = request.Reason.Trim(),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.ContributionDecisions.Add(decision); row.Status = status;
            if (policy is not null) await AppendEffectAsync(db, row, revision, null, attributes, policy,
                "AWARD", decision.Id, actor, "ASSIGNED_REVIEW", request.Reason.Trim(), null, cancellationToken);
            await DeclarationExperienceBridge.SyncAsync(db, row, cancellationToken);
            Notify(db, row, "REVIEW_RESULT", "Kết quả xác minh đóng góp");
            ExperienceAuthorization.Audit(db, http.User, "CONTRIBUTION_REVIEWED", "Contribution", id.ToString(), new { request.Decision, decision.Id });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(Response(row));
        }, cancellationToken);
    }

    private static bool ValidDecision(string decision, string? reason) => decision is "APPROVE" or "REJECT" or "REQUEST_REVISION"
        && !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 1000;

    private static async Task<IResult> RequestUpgradeAsync(Guid id, RequestContributionUpgradeRequest request,
        AdminDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var row = await db.Contributions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null) return Results.NotFound();
        if (row.UserId != http.User.GetUserId()) return Results.Forbid();
        if (!ContributionRoles.All.Contains(request.ProposedRole) || request.ProposedResult is not null && !BonusResults.All.Contains(request.ProposedResult)
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000
            || !await OwnsEvidenceAsync(db, row.UserId, request.EvidenceRefs, cancellationToken))
            return ApiOperations.Error(400, "INVALID_UPGRADE", "Check role, result, reason and evidence.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            if (row.Version != request.ExpectedVersion || row.Status != "APPROVED")
                return ApiOperations.Error(409, "STALE_CONTRIBUTION", "Reload the approved contribution.");
            var current = Read<ContributionAttributes>(row.EffectiveAttributesJson!);
            var next = current with { Role = request.ProposedRole, Result = request.ProposedResult ?? current.Result };
            if (ExperienceScoringCalculator.GetRoleMultiplier(next.Role) < ExperienceScoringCalculator.GetRoleMultiplier(current.Role)
                || ExperienceScoringCalculator.GetBonusResultMultiplier(next.Result) < ExperienceScoringCalculator.GetBonusResultMultiplier(current.Result)
                || next == current) return ApiOperations.Error(400, "UPGRADE_REQUIRED", "Request a higher role or result.");
            if (await db.ContributionUpgradeRequests.AnyAsync(x => x.ContributionId == id && x.Status == "SUBMITTED", cancellationToken))
                return ApiOperations.Error(409, "UPGRADE_ALREADY_PENDING", "A role upgrade is already pending.");
            var upgrade = new ContributionUpgradeRequest
            {
                ContributionId = id,
                ContributionVersion = row.Version,
                ProposedRole = request.ProposedRole,
                ProposedResult = request.ProposedResult,
                Reason = request.Reason.Trim(),
                EvidenceRefsJson = Json(request.EvidenceRefs),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.ContributionUpgradeRequests.Add(upgrade);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/contributions/{id}/role-upgrade-requests/{upgrade.Id}", upgrade);
        }, cancellationToken);
    }

    private static async Task<IResult> ReviewUpgradeAsync(Guid id, Guid requestId, ReviewContributionRequest request,
        AdminDbContext db, HttpContext http, ActivitySourceClient sources, CancellationToken cancellationToken)
    {
        var row = await db.Contributions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null) return Results.NotFound();
        var actor = http.User.GetUserId();
        if (row.UserId == actor) return ApiOperations.Error(403, "SELF_REVIEW_FORBIDDEN", "You cannot verify your own effort.");
        if (await ExperienceAuthorization.FindAsync(db, actor, row.SourceType, row.ClubId, "contribution.review", cancellationToken) is null) return Results.Forbid();
        if (!ValidDecision(request.Decision, request.Reason)) return ApiOperations.Error(400, "INVALID_REVIEW", "Provide a valid decision and reason.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            var grant = await ExperienceAuthorization.FindAsync(db, actor, row.SourceType, row.ClubId, "contribution.review", cancellationToken);
            if (grant is null) return Results.Forbid();
            var upgrade = await db.ContributionUpgradeRequests.SingleOrDefaultAsync(x => x.Id == requestId && x.ContributionId == id, cancellationToken);
            if (upgrade is null) return Results.NotFound();
            if (upgrade.Version != request.ExpectedVersion || upgrade.Status != "SUBMITTED" || row.Status != "APPROVED"
                || upgrade.ContributionVersion != row.Version) return ApiOperations.Error(409, "STALE_UPGRADE", "Reload the contribution and pending upgrade.");
            grant.Version = Guid.NewGuid();
            if (request.Decision == "APPROVE")
            {
                if (row.SourceType == "CLUB")
                {
                    ActivitySourceContext? live;
                    try { live = await sources.GetAsync(row.ActivityId!.Value, row.SessionId!.Value, row.UserId, cancellationToken); }
                    catch (HttpRequestException) { return ApiOperations.Error(503, "ACTIVITY_SERVICE_UNAVAILABLE", "Activity source is unavailable."); }
                    if (live is null || !live.IsRegistered || live.Source.Status == "CANCELLED"
                        || await db.InvalidatedExperienceSources.AnyAsync(x => x.ActivityId == row.ActivityId && (x.SessionId == null || x.SessionId == row.SessionId), cancellationToken))
                        return ApiOperations.Error(409, "SOURCE_INVALIDATED", "The source is no longer eligible for upgrade.");
                }
                var before = Read<ContributionAttributes>(row.EffectiveAttributesJson!);
                var next = before with { Role = upgrade.ProposedRole, Result = upgrade.ProposedResult ?? before.Result };
                if (request.VerifiedAttributes is not null && Json(request.VerifiedAttributes) != Json(next))
                    return ApiOperations.Error(400, "UPGRADE_ATTRIBUTES_MISMATCH", "Review the requested role/result while preserving tier, scale, vector and policy.");
                var policy = Read<ApprovedExperiencePolicySnapshot>(row.PolicySnapshotJson!);
                var revision = Revision(db, row, "APPROVED", next, actor, request.Reason.Trim(), Read<List<Guid>>(upgrade.EvidenceRefsJson));
                var decision = new ContributionDecision
                {
                    ContributionId = id,
                    TargetRevisionId = upgrade.Id,
                    RevisionId = revision.Id,
                    ReviewerUserId = actor,
                    AssignmentId = grant.Id,
                    AssignmentVersion = grant.Version,
                    Decision = "APPROVE",
                    Reason = request.Reason.Trim(),
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                db.ContributionDecisions.Add(decision);
                await AppendEffectAsync(db, row, revision, before, next, policy, "UPGRADE_DELTA", decision.Id, actor,
                    "ASSIGNED_REVIEW", request.Reason.Trim(), null, cancellationToken);
                upgrade.Status = "APPROVED";
            }
            else
            {
                upgrade.Status = request.Decision == "REJECT" ? "REJECTED" : "REVISION_REQUESTED";
                db.ContributionDecisions.Add(new ContributionDecision
                {
                    ContributionId = id,
                    TargetRevisionId = upgrade.Id,
                    RevisionId = row.CurrentRevisionId,
                    ReviewerUserId = actor,
                    AssignmentId = grant.Id,
                    AssignmentVersion = grant.Version,
                    Decision = request.Decision,
                    Reason = request.Reason.Trim(),
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            }
            upgrade.ReviewReason = request.Reason.Trim(); upgrade.ReviewedBy = actor;
            upgrade.ReviewedAt = DateTimeOffset.UtcNow; upgrade.Version = Guid.NewGuid();
            Notify(db, row, "CONTRIBUTION_UPDATED", "Kết quả yêu cầu nâng vai trò");
            await DeclarationExperienceBridge.SyncAsync(db, row, cancellationToken);
            ExperienceAuthorization.Audit(db, http.User, "CONTRIBUTION_UPGRADE_REVIEWED", "Contribution", id.ToString(), new { requestId, request.Decision });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { contribution = Response(row), upgrade });
        }, cancellationToken);
    }

    private static async Task<IResult> AdjustAsync(Guid id, AdjustContributionRequest request,
        AdminDbContext db, HttpContext http, ActivitySourceClient sources, CancellationToken cancellationToken)
    {
        var row = await db.Contributions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null) return Results.NotFound();
        var actor = http.User.GetUserId();
        if (row.UserId == actor) return ApiOperations.Error(403, "SELF_REVIEW_FORBIDDEN", "You cannot adjust your own effort.");
        if (await ExperienceAuthorization.FindAsync(db, actor, row.SourceType, row.ClubId, "experience.adjust", cancellationToken) is null) return Results.Forbid();
        if (request.Kind is not ("REVOKE" or "CORRECT") || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            return ApiOperations.Error(400, "INVALID_ADJUSTMENT", "Provide REVOKE/CORRECT and a reason.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            var grant = await ExperienceAuthorization.FindAsync(db, actor, row.SourceType, row.ClubId, "experience.adjust", cancellationToken);
            if (grant is null) return Results.Forbid();
            if (row.Version != request.ExpectedVersion || row.Status != "APPROVED") return ApiOperations.Error(409, "STALE_CONTRIBUTION", "Reload the approved contribution.");
            var before = Read<ContributionAttributes>(row.EffectiveAttributesJson!);
            var after = request.Kind == "REVOKE" ? before : request.CorrectedAttributes;
            var error = VerifiedExperienceRules.Validate(after);
            if (error is not null) return ApiOperations.Error(400, "INVALID_SCORING_ATTRIBUTES", error);
            if (request.Kind == "CORRECT")
            {
                if (row.SourceType == "CLUB")
                {
                    ActivitySourceContext? live;
                    try { live = await sources.GetAsync(row.ActivityId!.Value, row.SessionId!.Value, row.UserId, cancellationToken); }
                    catch (HttpRequestException) { return ApiOperations.Error(503, "ACTIVITY_SERVICE_UNAVAILABLE", "Activity source is unavailable."); }
                    if (live is null || !live.IsRegistered || live.Source.Status == "CANCELLED"
                        || await db.InvalidatedExperienceSources.AnyAsync(x => x.ActivityId == row.ActivityId && (x.SessionId == null || x.SessionId == row.SessionId), cancellationToken))
                        return ApiOperations.Error(409, "SOURCE_INVALIDATED", "The source is no longer eligible for correction.");
                    if (!SameBlueprint(after!, live.Source.Attributes)) return ApiOperations.Error(400, "INVALID_SOURCE_ATTRIBUTES", "Preserve the published tier, scale and vector.");
                }
                else if ((row.SourceType == "REAL_WORLD") != after!.PillarWeights.All(x => x.Pillar == ExperienceCategories.RealWorldWork))
                    return ApiOperations.Error(400, "SOURCE_TYPE_IMMUTABLE", "Keep this declaration in its assigned source scope.");
            }
            grant.Version = Guid.NewGuid();
            var revision = Revision(db, row, request.Kind == "REVOKE" ? "REVOKED" : "APPROVED", after!, actor, request.Reason.Trim());
            var decision = new ContributionDecision
            {
                ContributionId = id,
                TargetRevisionId = Guid.NewGuid(),
                RevisionId = revision.Id,
                ReviewerUserId = actor,
                AssignmentId = grant.Id,
                AssignmentVersion = grant.Version,
                Decision = request.Kind,
                Reason = request.Reason.Trim(),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.ContributionDecisions.Add(decision);
            await AppendEffectAsync(db, row, revision, before, after!, Read<ApprovedExperiencePolicySnapshot>(row.PolicySnapshotJson!),
                request.Kind == "REVOKE" ? "REVERSAL" : "CORRECTION", decision.Id, actor, "ASSIGNED_ADJUSTMENT",
                request.Reason.Trim(), null, cancellationToken, request.Kind == "REVOKE");
            Notify(db, row, "CONTRIBUTION_UPDATED", "Đóng góp được điều chỉnh");
            await DeclarationExperienceBridge.SyncAsync(db, row, cancellationToken);
            ExperienceAuthorization.Audit(db, http.User, "CONTRIBUTION_ADJUSTED", "Contribution", id.ToString(), new { request.Kind, decision.Id });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(Response(row));
        }, cancellationToken);
    }

    private static async Task<IResult> LedgerAsync(string? semesterId, string? pillar, string? sourceType, int? page, int? pageSize,
        AdminDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var actor = http.User.GetUserId(); var current = Math.Max(page ?? 1, 1); var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var query = from effect in db.ExperienceLedgerEffects.AsNoTracking()
                    join allocation in db.ExperienceLedgerAllocations.AsNoTracking() on effect.Id equals allocation.EffectId
                    join contribution in db.Contributions.AsNoTracking() on effect.ContributionId equals contribution.Id
                    where effect.UserId == actor && (semesterId == null || effect.SemesterId == semesterId) && (pillar == null || allocation.Pillar == pillar)
                        && (sourceType == null || contribution.SourceType == sourceType)
                    select new
                    {
                        entryId = allocation.Id,
                        effect.Id,
                        effect.ContributionId,
                        effect.RevisionId,
                        effect.UserId,
                        effect.SemesterId,
                        contribution.SourceType,
                        contribution.SourceId,
                        contribution.SourceKey,
                        sourceNamespace = "PROJECT_VERIFIED_EXPERIENCE",
                        allocation.Pillar,
                        entryType = effect.EffectType,
                        allocation.Amount,
                        effect.VerificationId,
                        effect.VerifiedByUserId,
                        effect.VerificationMethod,
                        effect.PolicyVersion,
                        effect.FormulaVersion,
                        effect.OccurredAt,
                        effect.RecordedAt,
                        effect.Reason
                    };
        if (pillar is not null && !ExperienceCategories.All.Contains(pillar)) return ApiOperations.Error(400, "INVALID_PILLAR", "Unknown pillar.");
        if (sourceType is not null && sourceType is not ("CLUB" or "EXTERNAL" or "REAL_WORLD")) return ApiOperations.Error(400, "INVALID_SOURCE_TYPE", "Unknown source type.");
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.entryId).Skip((int)Math.Min((long)(current - 1) * size, int.MaxValue)).Take(size).ToListAsync(cancellationToken);
        return Results.Ok(new { items = rows, total, page = current, pageSize = size, totalPages = (int)Math.Ceiling((decimal)total / size) });
    }

    private static async Task<IResult> QueueAsync(int? clubId, int? page, int? pageSize, AdminDbContext db,
        HttpContext http, CancellationToken cancellationToken)
    {
        var actor = http.User.GetUserId(); var clubs = await ExperienceAuthorization.ClubsAsync(db, actor, "contribution.review", cancellationToken);
        var manualClubs = await ExperienceAuthorization.ClubsAsync(db, actor, "attendance.verify", cancellationToken);
        var external = await ExperienceAuthorization.FindAsync(db, actor, "EXTERNAL", null, "contribution.review", cancellationToken) is not null;
        var realWorld = await ExperienceAuthorization.FindAsync(db, actor, "REAL_WORLD", null, "contribution.review", cancellationToken) is not null;
        if (clubs.Count == 0 && manualClubs.Count == 0 && !external && !realWorld) return Results.Forbid();
        if (clubId.HasValue && !clubs.Contains(clubId.Value) && !manualClubs.Contains(clubId.Value)) return Results.Forbid();
        var current = Math.Max(page ?? 1, 1); var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var pending = db.Contributions.AsNoTracking().Where(x => (x.ClubId != null && clubs.Contains(x.ClubId.Value)
                || clubId == null && (external && x.SourceType == "EXTERNAL" || realWorld && x.SourceType == "REAL_WORLD"))
            && x.UserId != actor && x.Status == "SUBMITTED" && (clubId == null || x.ClubId == clubId));
        var total = await pending.CountAsync(cancellationToken);
        var rows = await pending.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((int)Math.Min((long)(current - 1) * size, int.MaxValue)).Take(size).ToListAsync(cancellationToken);
        var upgrades = await (from upgrade in db.ContributionUpgradeRequests.AsNoTracking()
                              join contribution in db.Contributions.AsNoTracking() on upgrade.ContributionId equals contribution.Id
                              where (contribution.ClubId != null && clubs.Contains(contribution.ClubId.Value)
                                      || clubId == null && (external && contribution.SourceType == "EXTERNAL" || realWorld && contribution.SourceType == "REAL_WORLD")) && contribution.UserId != actor
                                  && upgrade.Status == "SUBMITTED" && (clubId == null || contribution.ClubId == clubId)
                              select new { upgrade, contribution.UserId, contribution.ClubId, contribution.Title }).OrderBy(x => x.upgrade.CreatedAtUtc)
                .Skip((int)Math.Min((long)(current - 1) * size, int.MaxValue)).Take(size).ToListAsync(cancellationToken);
        var manual = await db.ManualAttendanceRequests.AsNoTracking().Where(x => manualClubs.Contains(x.ClubId) && x.UserId != actor
            && x.Status == "PENDING" && (clubId == null || x.ClubId == clubId)).OrderBy(x => x.CreatedAtUtc)
            .Skip((int)Math.Min((long)(current - 1) * size, int.MaxValue)).Take(size).ToListAsync(cancellationToken);
        return Results.Ok(new
        {
            items = rows.Select(Response),
            total,
            page = current,
            pageSize = size,
            totalPages = (int)Math.Ceiling((decimal)total / size),
            roleUpgradeRequests = upgrades,
            manualAttendanceRequests = manual,
            assignedClubIds = clubs
        });
    }
}
