using System.Text.Json.Serialization;
using AdminService.Data;
using AdminService.Security;
using AdminService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using static AdminService.Services.RawFundingLedger;

namespace AdminService.Endpoints;

public static class RawFundingEndpoints
{
    public static void MapRawFundingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/raw-funding").RequireAuthorization(AdminPolicies.AnyActor).WithTags("Raw-point funding");
        group.MapGet("/{clubId:int}", Read);
        group.MapGet("/reservations/{id:guid}", Detail).RequireAuthorization(AdminPolicies.BackofficeUser);
        group.MapPost("/{clubId:int}/grants", Grant).RequireAuthorization(AdminPolicies.BackofficeUser);
        group.MapPost("/reservations/{id:guid}/reconcile", ReconcileSource).RequireAuthorization(AdminPolicies.BackofficeUser);
        group.MapPost("/reservations/{id:guid}/settle", FinalizeSettlement).RequireAuthorization(AdminPolicies.BackofficeUser);
        group.MapPost("/reservations/{id:guid}/increase-budget", IncreaseBudget).RequireAuthorization(AdminPolicies.BackofficeUser);
        app.MapPost("/api/internal/experience/raw-funding/reserve", ReserveSource)
            .RequireAuthorization(AdminPolicies.BackofficeUser).ExcludeFromDescription();
    }

    private sealed record Scope(string Campus, InternalUserProfile Actor, bool Reviewer);
    private static async Task<Scope> Access(int clubId, string? campus, HttpContext http, CancellationToken ct)
    {
        var actorId = http.User.GetUserId();
        var actor = (await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync([actorId], ct)).GetValueOrDefault(actorId);
        Require(actor is { IsActive: true, IsLocked: false }, "ACCOUNT_UNAVAILABLE", "An active account is required.", 403);
        var staff = http.User.IsInRole(AuthRoles.Admin) || http.User.IsInRole(AuthRoles.StudentAffairsAdmin);
        if (clubId > 0)
        {
            var club = await http.RequestServices.GetRequiredService<ClubExperienceAuthorityClient>().GetAsync(clubId, actorId, ct);
            Require(club is { IsActive: true }, "CLUB_UNAVAILABLE", "The active club must exist.", 404);
            campus = club!.CampusCode;
            if (!staff)
            {
                var scopes = await http.RequestServices.GetRequiredService<ClubAccessClient>().GetMyAccessAsync(http.GetBearerToken(), ct, true);
                Require(scopes.Any(x => x.ClubId == clubId && x.HasCapability("club.finance.read")), "RAW_FUNDING_ACCESS_DENIED", "Current club finance authority is required.", 403);
            }
        }
        else Require(clubId == 0 && staff, "RAW_FUNDING_ACCESS_DENIED", "Campus raw pools require current staff authority.", 403);
        campus ??= actor!.CampusCode;
        Require(CampusCodes.FiveCampuses.Contains(campus) && (http.User.IsInRole(AuthRoles.Admin) || campus == actor!.CampusCode),
            "CAMPUS_SCOPE_DENIED", "Raw funding belongs to the actor's current campus.", 403);
        return new(campus, actor!, staff);
    }

    private static async Task<Semester> Semester(AdminDbContext db, string identifier, CancellationToken ct)
    {
        Require(!string.IsNullOrWhiteSpace(identifier) && identifier.Length <= 40, "SEMESTER_REQUIRED", "Choose a configured semester.", 400);
        var guid = Guid.TryParse(identifier, out var parsed) ? parsed : Guid.Empty;
        var code = identifier.Trim().ToUpperInvariant();
        var row = await db.Semesters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == guid || x.SemesterCode == code, ct);
        Require(row is not null, "SEMESTER_NOT_FOUND", "Choose a configured semester.", 404); return row!;
    }

    private static async Task<IResult> Read(int clubId, string semesterId, string? campus, int? page, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        var scope = await Access(clubId, campus, http, ct); var semester = await Semester(db, semesterId, ct);
        var account = await db.Set<RawFundingAccount>().AsNoTracking().SingleOrDefaultAsync(x => x.ClubId == clubId && x.CampusCode == scope.Campus && x.SemesterId == semester.SemesterCode, ct);
        if (account is not null) await Reconcile(db, account, ct);
        var current = page ?? 1; Require(current is >= 1 and <= 100000, "INVALID_PAGINATION", "Use page 1–100000.", 400);
        var query = db.Set<RawFundingEntry>().AsNoTracking().Where(x => x.ClubId == clubId && x.CampusCode == scope.Campus && x.SemesterId == semester.SemesterCode);
        var total = await query.CountAsync(ct);
        var entries = await query.OrderByDescending(x => x.RecordedAt).ThenBy(x => x.Id).Skip((current - 1) * 20).Take(20).ToListAsync(ct);
        var reservationQuery = db.Set<RawFundingReservation>().AsNoTracking().Where(x => x.ClubId == clubId && x.CampusCode == scope.Campus && x.SemesterId == semester.SemesterCode);
        var reservationTotal = await reservationQuery.CountAsync(ct);
        var reservations = await reservationQuery
            .OrderByDescending(x => x.ReservedAt).Skip((current - 1) * 20).Take(20).ToListAsync(ct);
        return Results.Ok(new
        {
            clubId, scope.Campus, semesterId = semester.SemesterCode, version = account?.Version ?? Guid.Empty,
            granted = account?.Granted ?? 0, available = account?.Available ?? 0, reserved = account?.Reserved ?? 0, spent = account?.Spent ?? 0,
            entries = entries.Select(x => new { x.Id, x.Kind, x.GrantedDelta, x.AvailableDelta, x.ReservedDelta, x.SpentDelta, x.ReservationId, x.ContributionId, x.ExperienceEffectId, x.ActorId, x.RecordedAt, x.Reason }),
            reservations = reservations.Select(x => new { x.Id, x.SourceKey, x.SourceVersion, x.ActivityId, x.PolicyVersion, x.Budget, x.Spent, x.Status, x.Version, x.ReservedAt }),
            page = current, pageSize = 20, total, reservationTotal, capabilities = new { canGrant = scope.Reviewer && semester.IsActive, canReconcile = scope.Reviewer, canSettle = scope.Reviewer },
            basis = "IMMUTABLE_RAW_LEDGER", unit = "RAW_POINTS"
        });
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record GrantInput(string SemesterId, string? Campus, Guid ExpectedVersion, decimal Amount, string Reason);
    private static async Task<IResult> Grant(int clubId, GrantInput input, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        var scope = await Access(clubId, input.Campus, http, ct);
        Require(scope.Reviewer, "RAW_GRANT_DENIED", "Only campus staff may grant raw points.", 403);
        Require(input.Amount is > 0 and <= 1_000_000_000 && decimal.Round(input.Amount, 4) == input.Amount && input.Reason?.Trim().Length is >= 5 and <= 1000,
            "INVALID_RAW_GRANT", "Provide a positive four-decimal raw grant and a reason of 5–1000 characters.", 400);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            var current = await Access(clubId, input.Campus, http, ct); var semester = await Semester(db, input.SemesterId, ct);
            Require(current.Reviewer && semester.IsActive, "RAW_GRANT_DENIED", "Grants require an active semester and current campus staff.", 403);
            var account = await db.Set<RawFundingAccount>().SingleOrDefaultAsync(x => x.ClubId == clubId && x.CampusCode == current.Campus && x.SemesterId == semester.SemesterCode, ct);
            Require(input.ExpectedVersion == (account?.Version ?? Guid.Empty), "STALE_RAW_ACCOUNT", "Reload the current raw funding balance.");
            if (account is null) { account = new RawFundingAccount { ClubId = clubId, CampusCode = current.Campus, SemesterId = semester.SemesterCode }; db.Add(account); }
            else await Reconcile(db, account, ct);
            Append(db, account, $"grant:{Guid.NewGuid():N}", "GRANT", input.Amount, input.Amount, 0, 0, current.Actor.Id, input.Reason.Trim());
            return Results.Ok(new { account.Version, account.Granted, account.Available, account.Reserved, account.Spent });
        }, ct);
    }

    private static async Task<IResult> ReserveSource(ReserveRawFundingRequest input, AdminDbContext db, RawFundingActivityClient sources, IConfiguration configuration, HttpContext http, CancellationToken ct)
    {
        Require(VerifiedExperienceRules.InternalRequest(http, configuration), "INTERNAL_AUTHORITY_REQUIRED", "Internal source authority is required.", 403);
        Require(input.SourceKey is { Length: <= 160 } && System.Text.RegularExpressions.Regex.IsMatch(input.SourceKey, "^(APPLICATION|CAMPUS):[1-9][0-9]*$")
            && input.ExpectedSourceVersion is { Length: > 0 and <= 100 }, "INVALID_RAW_SOURCE", "Use a versioned opening source.", 400);
        var proof = await sources.Read(input.SourceKey, ct);
        Require(proof is { Status: "SUBMITTED", Budget: > 0, PolicyVersion: not null } && proof.Version == input.ExpectedSourceVersion && proof.SourceKey == input.SourceKey,
            "RAW_SOURCE_CHANGED", "Reserve only the current submitted opening revision.");
        var scope = await Access(proof!.ClubId, proof.CampusCode, http, ct);
        Require(scope.Reviewer && (proof.SourceKey.StartsWith("CAMPUS:", StringComparison.Ordinal) || proof.CreatedBy != scope.Actor.Id)
            && scope.Campus == proof.CampusCode, "INDEPENDENT_REVIEW_REQUIRED", "An independent campus reviewer must approve a club funding source.", 403);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            var policy = await VerifiedExperienceService.PolicyAsync(db, proof.PolicyVersion!.Value, proof.SemesterId, configuration["Experience:Environment"] ?? "PROJECT", ct);
            Require(policy is { Scope: "PERIOD" }, "RAW_POLICY_REQUIRED", "Use the approved period policy for this exact semester and environment.");
            var row = await Reserve(db, proof.ClubId, proof.CampusCode, proof.SemesterId, proof.SourceKey, proof.Version, proof.PolicyVersion.Value, proof.Budget!.Value, scope.Actor.Id, ct);
            return Results.Ok(new RawFundingReceipt(row.Id, row.ClubId, row.CampusCode, row.SemesterId, row.SourceKey, row.SourceVersion, row.PolicyVersion, row.Budget, row.Status));
        }, ct);
    }

    private sealed record CandidateDecision(Guid Id, Guid ExpectedVersion, string Decision, string? Role);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record BudgetIncreaseInput(Guid ExpectedVersion, Guid ExpectedAccountVersion, decimal Amount, string Reason);
    private static async Task<IResult> IncreaseBudget(Guid id, BudgetIncreaseInput input, AdminDbContext db, RawFundingActivityClient sources, HttpContext http, CancellationToken ct)
    {
        Require(input.Amount is > 0 and <= 1_000_000_000 && decimal.Round(input.Amount, 4) == input.Amount
            && input.Reason?.Trim().Length is >= 5 and <= 1000, "INVALID_BUDGET_INCREASE", "Use positive four-decimal raw points and a reason of 5–1000 characters.", 400);
        var row = await db.Set<RawFundingReservation>().SingleOrDefaultAsync(x => x.Id == id, ct);
        Require(row is not null, "RAW_RESERVATION_NOT_FOUND", "The raw reservation does not exist.", 404);
        var scope = await Access(row.ClubId, row.CampusCode, http, ct);
        Require(scope.Reviewer, "RAW_REVIEW_DENIED", "Current campus review authority is required.", 403);
        var proof = await sources.Read(row.SourceKey, ct);
        Require(proof is { ActivityId: not null } && proof.FundingReservationId == id && proof.Status is "APPROVED" or "SCHEDULED" or "COMPLETED" or "POSTPONED",
            "RAW_SOURCE_CHANGED", "Increase only a confirmed funded activity that has not been cancelled.");
        Require(proof.CreatedBy != scope.Actor.Id, "INDEPENDENT_REVIEW_REQUIRED", "A different campus reviewer must approve a budget increase.", 403);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(row).ReloadAsync(ct);
            var current = await Access(row.ClubId, row.CampusCode, http, ct);
            Require(current.Reviewer && proof.CreatedBy != current.Actor.Id, "INDEPENDENT_REVIEW_REQUIRED", "Current independent review authority is required.", 403);
            Require(row.Status == "RESERVED" && row.Version == input.ExpectedVersion && row.Budget + input.Amount <= 1_000_000_000,
                "STALE_RAW_RESERVATION", "Reload the open raw reservation before changing its budget.");
            var account = await Account(db, row.ClubId, row.CampusCode, row.SemesterId, ct);
            Require(account.Version == input.ExpectedAccountVersion, "STALE_RAW_ACCOUNT", "Reload the current pool balance.");
            Append(db, account, $"budget-increase:{Guid.NewGuid():N}", "BUDGET_INCREASE", 0, -input.Amount, input.Amount, 0,
                current.Actor.Id, input.Reason.Trim(), row);
            row.Budget += input.Amount; row.Version = Guid.NewGuid();
            return Results.Ok(new { row.Id, row.Version, row.Budget, accountVersion = account.Version });
        }, ct);
    }
    private sealed record DecisionInput(Guid ExpectedVersion, string Reason, IReadOnlyList<CandidateDecision>? Decisions = null);

    private static async Task<IResult> Detail(Guid id, AdminDbContext db, RawFundingActivityClient sources, HttpContext http, CancellationToken ct)
    {
        var row = await db.Set<RawFundingReservation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        Require(row is not null, "RAW_RESERVATION_NOT_FOUND", "The raw reservation does not exist.", 404);
        var scope = await Access(row!.ClubId, row.CampusCode, http, ct); Require(scope.Reviewer, "RAW_REVIEW_DENIED", "Campus review authority is required.", 403);
        var proof = await sources.Read(row.SourceKey, ct);
        var activityId = proof?.FundingReservationId == id ? proof.ActivityId : null;
        var rows = activityId is null ? [] : await db.Contributions.AsNoTracking().Where(x => x.ActivityId == activityId).OrderBy(x => x.UserId).ToListAsync(ct);
        var profiles = new Dictionary<int, InternalUserProfile>();
        foreach (var batch in rows.Select(x => x.UserId).Distinct().Chunk(100))
            foreach (var profile in await http.RequestServices.GetRequiredService<CurrentUserProfileClient>().GetAsync(batch, ct)) profiles[profile.Key] = profile.Value;
        var missing = proof?.Candidates.Count(expected => !rows.Any(x => x.UserId == expected.UserId && x.SourceKey == expected.SourceKey)) ?? 0;
        return Results.Ok(new
        {
            row.Id, row.Version, row.Status, row.Budget, row.Spent, row.PolicyVersion,
            activityId, closingApproved = proof?.ClosingApproved == true, pendingCandidateMessages = missing,
            canSettle = row.Status == "RESERVED" && proof?.ClosingApproved == true && missing == 0 && rows.All(x => x.UserId != scope.Actor.Id),
            candidates = rows.Select(x => new { x.Id, x.UserId, x.SourceKey, x.SourceType, x.Status, x.Version, x.RawPoints,
                studentName = profiles.GetValueOrDefault(x.UserId)?.DisplayName, studentEmail = profiles.GetValueOrDefault(x.UserId)?.Email,
                evidenceRefs = VerifiedExperienceService.Read<List<Guid>>(x.EvidenceRefsJson),
                proposedAttributes = VerifiedExperienceService.Read<ContributionAttributes>(x.ProposedAttributesJson),
                eligible = proof?.Candidates.Any(expected => expected.UserId == x.UserId && expected.SourceKey == x.SourceKey) == true })
        });
    }

    private static async Task ApproveBatch(RawFundingReservation reservation, RawFundingSource proof, DecisionInput input,
        AdminDbContext db, HttpContext http, IConfiguration configuration, int actor, CancellationToken ct)
    {
        var rows = await db.Contributions.Where(x => x.ActivityId == proof.ActivityId).ToListAsync(ct);
        Require(rows.All(x => x.UserId != actor), "SELF_REVIEW_FORBIDDEN", "An independent reviewer must decide the whole funded settlement.", 403);
        Require(proof.Candidates.All(expected => rows.Any(x => x.UserId == expected.UserId && x.SourceKey == expected.SourceKey)),
            "XP_CANDIDATE_MESSAGES_PENDING", "Wait for all generated candidates before deciding the complete settlement.");
        var pending = rows.Where(x => x.Status is "SUBMITTED" or "REVISION_REQUESTED").ToArray();
        var decisions = input.Decisions ?? [];
        Require(decisions.Count == pending.Length && decisions.All(x => x is not null) && decisions.Select(x => x.Id).Distinct().Count() == decisions.Count,
            "COMPLETE_SETTLEMENT_REQUIRED", "Decide every pending candidate exactly once in the same settlement.", 400);
        var plan = new List<(VerifiedContribution Row, CandidateDecision Decision, ContributionAttributes Attributes, AdminService.Contracts.ApprovedExperiencePolicySnapshot Policy)>();
        decimal total = 0;
        foreach (var row in pending)
        {
            var decision = decisions.SingleOrDefault(x => x.Id == row.Id);
            Require(decision is not null && decision.ExpectedVersion == row.Version, "STALE_SETTLEMENT_CANDIDATE", "Reload the candidate versions before settling.");
            Require(row.UserId != actor, "SELF_REVIEW_FORBIDDEN", "An independent reviewer must decide the full settlement.", 403);
            Require(decision!.Decision is "APPROVE" or "REJECT", "INVALID_SETTLEMENT_DECISION", "Choose approval or rejection for each candidate.", 400);
            Require(SourceReservation(row) == reservation.Id, "RAW_SOURCE_MISMATCH", "Every funded candidate must belong to this raw reservation.");
            var source = VerifiedExperienceService.Read<ExperienceSourceSnapshot>(row.SourceSnapshotJson);
            Require(source.ActivityId == proof.ActivityId && source.Policy.PolicyVersion == reservation.PolicyVersion && row.RawPoints == 0,
                "RAW_SOURCE_MISMATCH", "The candidate source and policy must match the opening reservation.");
            var attributes = source.Attributes with
            {
                Role = decision.Decision == "APPROVE" && row.SourceType == "CLOSING_DUTY" ? decision.Role ?? "" : ContributionRoles.Participant,
                Result = BonusResults.None
            };
            var eligible = proof.Candidates.Any(x => x.UserId == row.UserId && x.SourceKey == row.SourceKey);
            Require(decision.Decision != "APPROVE" || eligible && row.Status == "SUBMITTED" && VerifiedExperienceRules.Validate(attributes) is null,
                "UNVERIFIED_CANDIDATE", "Only a confirmed submitted closing candidate with valid attributes can receive XP.");
            var policy = await VerifiedExperienceService.PolicyAsync(db, source.Policy.PolicyVersion, row.SemesterId, configuration["Experience:Environment"] ?? "PROJECT", ct);
            Require(policy is not null, "RAW_POLICY_REQUIRED", "The immutable source policy must remain available.");
            if (decision.Decision == "APPROVE") total += ExperienceScoringCalculator.CalculateRawPoints(attributes.Tier, attributes.Role, attributes.Scale, attributes.Result);
            plan.Add((row, decision, attributes, policy!));
        }
        Require(reservation.Spent + total <= reservation.Budget, "RAW_BUDGET_EXCEEDED", "The complete closing settlement exceeds the reserved budget; no XP was posted.");
        foreach (var item in plan)
        {
            var before = item.Row.CurrentRevisionId; var approve = item.Decision.Decision == "APPROVE";
            item.Row.Status = approve ? "APPROVED" : "REJECTED";
            var revision = VerifiedExperienceService.Revision(db, item.Row, approve ? "APPROVED" : "REJECTED", item.Attributes, actor, input.Reason.Trim());
            var decision = new ContributionDecision { ContributionId = item.Row.Id, TargetRevisionId = before, RevisionId = revision.Id,
                ReviewerUserId = actor, Decision = item.Decision.Decision, Reason = input.Reason.Trim(), CreatedAtUtc = DateTimeOffset.UtcNow };
            db.ContributionDecisions.Add(decision);
            if (approve) await VerifiedExperienceService.AppendEffectAsync(db, item.Row, revision, null, item.Attributes, item.Policy, "AWARD", decision.Id, actor,
                "FUNDED_CLOSING_SETTLEMENT", input.Reason.Trim(), null, ct);
            VerifiedExperienceService.Notify(db, item.Row, "REVIEW_RESULT", "Kết quả nghiệm thu điểm trải nghiệm hoạt động");
        }
    }
    private static async Task<IResult> ReconcileSource(Guid id, DecisionInput input, AdminDbContext db, RawFundingActivityClient sources, HttpContext http, CancellationToken ct) =>
        await Decide(id, input, false, db, sources, http, ct);
    private static async Task<IResult> FinalizeSettlement(Guid id, DecisionInput input, AdminDbContext db, RawFundingActivityClient sources, HttpContext http, CancellationToken ct) =>
        await Decide(id, input, true, db, sources, http, ct);
    private static async Task<IResult> Decide(Guid id, DecisionInput input, bool settle, AdminDbContext db, RawFundingActivityClient sources, HttpContext http, CancellationToken ct)
    {
        var row = await db.Set<RawFundingReservation>().SingleOrDefaultAsync(x => x.Id == id, ct);
        Require(row is not null, "RAW_RESERVATION_NOT_FOUND", "The raw reservation does not exist.", 404);
        var scope = await Access(row!.ClubId, row.CampusCode, http, ct); Require(scope.Reviewer, "RAW_REVIEW_DENIED", "Campus review authority is required.", 403);
        Require(input.Reason?.Trim().Length is >= 5 and <= 1000, "REASON_REQUIRED", "Provide a reason of 5–1000 characters.", 400);
        // A dependency failure is never treated as a missing/abandoned source.
        var proof = await sources.Read(row.SourceKey, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await db.Entry(row).ReloadAsync(ct);
            Require(row.Version == input.ExpectedVersion, "STALE_RAW_RESERVATION", "Reload the raw reservation.");
            var current = await Access(row.ClubId, row.CampusCode, http, ct); Require(current.Reviewer, "RAW_REVIEW_DENIED", "Current review authority is required.", 403);
            if (settle)
            {
                Require(proof is { ClosingApproved: true, ActivityId: not null } && proof.FundingReservationId == id,
                    "CLOSING_SETTLEMENT_NOT_READY", "The funded source needs an approved closing report.");
                var activityId = proof!.ActivityId!.Value;
                await ApproveBatch(row, proof, input, db, http, http.RequestServices.GetRequiredService<IConfiguration>(), current.Actor.Id, ct);
                row.ActivityId ??= activityId; await Settle(db, row, current.Actor.Id, input.Reason.Trim(), ct);
            }
            else if (proof?.FundingReservationId == id && proof.ActivityId.HasValue)
            { row.ActivityId = proof.ActivityId; row.Version = Guid.NewGuid(); await Reconcile(db, await Account(db, row.ClubId, row.CampusCode, row.SemesterId, ct), ct); }
            else
            {
                Require(proof is null || proof.Status != "SUBMITTED" || proof.Version != row.SourceVersion,
                    "RAW_APPROVAL_PENDING", "The matching opening revision is still awaiting a decision; retain its reservation.");
                await Refund(db, row, current.Actor.Id, input.Reason.Trim(), ct);
            }
            return Results.Ok(new { row.Id, row.Status, row.ActivityId, row.Budget, row.Spent, row.Version });
        }, ct);
    }
}
