using AdminService.Contracts;
using AdminService.Data;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using static AdminService.Services.VerifiedExperienceService;

namespace AdminService.Services;

// Existing declaration routes are the intake for external / +1 evidence.
// Their canonical experience record uses the same revisions and ledger as clubs.
internal static class DeclarationExperienceBridge
{
    internal static async Task<IResult> SubmitAsync(SubmitDeclarationRequest input, AdminDbContext db,
        HttpContext http, CancellationToken ct)
    {
        var error = VerifiedExperienceRules.Validate(input.ProposedAttributes);
        var actor = http.User.GetUserId();
        if (error is not null || !input.IsOutsideClub || input.ClubId.HasValue || string.IsNullOrWhiteSpace(input.Title)
            || input.Title.Length > 200 || input.EvidenceDescription?.Length > 2000 || !input.OccurredAt.HasValue
            || input.OccurredAt > DateTimeOffset.UtcNow || !await OwnsEvidenceAsync(db, actor, input.EvidenceRefs, ct))
            return ApiOperations.Error(400, "INVALID_DECLARATION", error ?? "Provide an external declaration, occurredAt and owned evidence.");
        var identifier = input.SemesterId?.Trim() ?? "";
        var semesterGuid = Guid.TryParse(identifier, out var parsed) ? parsed : Guid.Empty;
        var semester = await db.Semesters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == semesterGuid || x.SemesterCode == identifier.ToUpper(), ct);
        if (semester is null) return ApiOperations.Error(404, "SEMESTER_NOT_FOUND", "Use an existing project semester.");
        if (!semester.StartDate.HasValue || !semester.EndDate.HasValue || input.OccurredAt < semester.StartDate
            || input.OccurredAt >= semester.EndDate.Value.AddDays(1)) return ApiOperations.Error(400, "DECLARATION_OUTSIDE_SEMESTER", "The effort must fall inside the semester.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            var kind = input.ProposedAttributes!.PillarWeights.All(x => x.Pillar == ExperienceCategories.RealWorldWork) ? "REAL_WORLD" : "EXTERNAL";
            var declaration = new SelfDeclaration
            {
                StudentId = actor,
                StudentName = http.User.Identity?.Name ?? $"User {actor}",
                StudentEmail = http.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "",
                CampusCode = http.User.GetCampus(),
                Title = input.Title.Trim(),
                Category = input.ProposedAttributes.PillarWeights[0].Pillar,
                OrganizationSource = input.OrganizationSource?.Trim() ?? "",
                IsOutsideClub = true,
                EvidenceUrl = "",
                EvidenceDescription = input.EvidenceDescription?.Trim() ?? "",
                StartDate = DateOnly.FromDateTime(input.OccurredAt.Value.UtcDateTime),
                EndDate = DateOnly.FromDateTime(input.OccurredAt.Value.UtcDateTime)
            };
            var row = new VerifiedContribution
            {
                Id = declaration.Id,
                UserId = actor,
                SourceType = kind,
                SourceKey = $"DECLARATION:{declaration.Id:N}",
                SourceId = declaration.Id.ToString(),
                SemesterId = semester.SemesterCode,
                Title = declaration.Title,
                Message = declaration.EvidenceDescription,
                OccurredAt = input.OccurredAt.Value,
                ProposedAttributesJson = Json(input.ProposedAttributes),
                EvidenceRefsJson = Json(input.EvidenceRefs),
                SourceSnapshotJson = Json(new
                {
                    declarationId = declaration.Id,
                    organizationSource = declaration.OrganizationSource,
                    semesterId = semester.SemesterCode,
                    input.OccurredAt,
                    requestedPolicyVersion = input.PolicyVersion
                })
            };
            db.SelfDeclarations.Add(declaration); db.Contributions.Add(row);
            Revision(db, row, "SUBMITTED", input.ProposedAttributes, actor, "External declaration submitted.");
            ExperienceAuthorization.Audit(db, http.User, "DECLARATION_SUBMITTED", "SelfDeclaration", row.Id.ToString(), new { row.SourceType, row.SemesterId });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/declarations/{row.Id}", Response(row));
        }, ct);
    }

    internal static async Task SyncAsync(AdminDbContext db, VerifiedContribution row, CancellationToken ct)
    {
        if (row.SourceType == "CLUB") return;
        var declaration = await db.SelfDeclarations.SingleOrDefaultAsync(x => x.Id == row.Id, ct);
        if (declaration is null) return;
        declaration.Status = row.Status switch { "APPROVED" => "Approved", "REJECTED" => "Rejected", "REVISION_REQUESTED" => "RevisionRequested", "REVOKED" => "Revoked", _ => "Submitted" };
        declaration.RawPoints = row.Status == "APPROVED" ? row.RawPoints : null;
        if (row.EffectiveAttributesJson is not null)
        {
            var attributes = Read<ContributionAttributes>(row.EffectiveAttributesJson);
            declaration.Tier = attributes.Tier; declaration.Role = attributes.Role; declaration.Scale = attributes.Scale;
            declaration.BonusResult = attributes.Result; declaration.FinalCategory = attributes.PillarWeights[0].Pillar;
        }
    }
}
