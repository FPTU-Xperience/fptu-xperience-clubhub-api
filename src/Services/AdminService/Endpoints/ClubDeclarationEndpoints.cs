using AdminService.Contracts;
using AdminService.Data;
using AdminService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using Microsoft.EntityFrameworkCore;
using static AdminService.Services.VerifiedExperienceService;

namespace AdminService.Endpoints;

internal static class ClubDeclarationEndpoints
{
    internal sealed record ReviewInput(Guid ExpectedVersion, string Decision, string Reason);
    internal sealed record ForwardInput(Guid ExpectedVersion, string Reason);

    internal static void MapClubDeclarations(this RouteGroupBuilder declarations)
    {
        var clubs = declarations.MapGroup("/clubs/{clubId:int}");
        clubs.MapGet("/access", Access);
        clubs.MapGet("", List);
        clubs.MapPost("/{id:guid}/club-review", Review);
        clubs.MapPost("/{id:guid}/forward", Forward);
    }

    private static async Task<IResult> Access(int clubId, HttpContext http, CancellationToken ct)
    {
        try
        {
            var access = await ClubDeclarationWorkflow.AccessAsync(clubId, http, ct);
            return Results.Ok(new { clubId, canProcess = access is not null, canManagePermissions = access?.CanManage == true });
        }
        catch (HttpRequestException) { return ApiOperations.Error(503, "CLUB_AUTHORITY_UNAVAILABLE", "Reload when club authority is available."); }
    }

    private static async Task<IResult> List(int clubId, int? page, int? pageSize, string? status, string? query,
        string? semesterId, bool? unassigned, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        if (page is < 1 || pageSize is < 1 or > 100 || query?.Length > 200)
            return ApiOperations.Error(400, "INVALID_PAGINATION", "Use page >= 1, pageSize 1–100 and a short search.");
        try { if (await ClubDeclarationWorkflow.AccessAsync(clubId, http, ct) is null) return Results.Forbid(); }
        catch (HttpRequestException) { return ApiOperations.Error(503, "CLUB_AUTHORITY_UNAVAILABLE", "Club authority is unavailable."); }
        var rows = db.Contributions.AsNoTracking().Where(x => x.ClubId == clubId && x.ClubReviewStatus != null
            && (x.SourceType == "EXTERNAL" || x.SourceType == "REAL_WORLD"));
        if (!string.IsNullOrWhiteSpace(status))
        {
            var state = status.Trim().ToUpperInvariant();
            if (state is not ("SUBMITTED" or "APPROVED" or "REJECTED" or "REVISION_REQUESTED" or "REVOKED"))
                return ApiOperations.Error(400, "INVALID_STATUS", "Choose a declaration status.");
            rows = rows.Where(x => x.Status == state);
        }
        if (!string.IsNullOrWhiteSpace(semesterId)) rows = rows.Where(x => x.SemesterId == semesterId);
        if (unassigned == true) rows = rows.Where(x => x.SemesterId == "");
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            var userId = int.TryParse(term, out var parsed) ? parsed : 0;
            rows = rows.Where(x => x.Title.Contains(term) || x.Message.Contains(term) || x.UserId == userId
                || db.SelfDeclarations.Any(d => d.Id == x.Id && (d.StudentName.Contains(term) || d.StudentEmail.Contains(term))));
        }
        var current = page ?? 1; var size = pageSize ?? 20;
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((int)Math.Min((long)(current - 1) * size, int.MaxValue)).Take(size).ToListAsync(ct);
        var ids = items.Select(x => x.Id).ToArray();
        var authors = await db.SelfDeclarations.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        return Results.Ok(new
        {
            items = items.Select(x => Response(x) with
            {
                StudentName = authors.GetValueOrDefault(x.Id)?.StudentName,
                StudentEmail = authors.GetValueOrDefault(x.Id)?.StudentEmail,
                Capabilities = ClubDeclarationWorkflow.Capabilities(x, x.UserId != http.User.GetUserId(), false)
            }),
            page = current,
            pageSize = size,
            total,
            totalPages = (int)Math.Ceiling((decimal)total / size)
        });
    }

    private static Task<IResult> Review(int clubId, Guid id, ReviewInput input, AdminDbContext db, HttpContext http, CancellationToken ct) =>
        Change(clubId, id, input.ExpectedVersion, input.Decision, input.Reason, db, http, ct);
    private static Task<IResult> Forward(int clubId, Guid id, ForwardInput input, AdminDbContext db, HttpContext http, CancellationToken ct) =>
        Change(clubId, id, input.ExpectedVersion, "FORWARD", input.Reason, db, http, ct);

    private static async Task<IResult> Change(int clubId, Guid id, Guid version, string decision, string reason,
        AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        var row = await db.Contributions.SingleOrDefaultAsync(x => x.Id == id && x.ClubId == clubId && x.ClubReviewStatus != null, ct);
        if (row is null) return Results.NotFound();
        if (row.UserId == http.User.GetUserId()) return ApiOperations.Error(403, "SELF_REVIEW_FORBIDDEN", "An independent club reviewer is required.");
        if (decision is not ("ENDORSE" or "REQUEST_CHANGES" or "FORWARD") || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 5 or > 1000)
            return ApiOperations.Error(400, "INVALID_CLUB_REVIEW", "Choose a decision and a reason of 5–1000 characters.");
        try { if (!await ClubDeclarationWorkflow.CanProcessAsync(row, http, ct)) return Results.Forbid(); }
        catch (HttpRequestException) { return ApiOperations.Error(503, "CLUB_AUTHORITY_UNAVAILABLE", "Club authority is unavailable."); }
        return await ApiOperations.ExecuteAsync(http, db, new { id, clubId, version, decision, reason }, async () =>
        {
            if (!await ClubDeclarationWorkflow.CanProcessAsync(row, http, ct)) return Results.Forbid();
            if (row.Version != version || row.Status != "SUBMITTED" || row.CtsvSubmissionStatus != "NOT_SUBMITTED"
                || (decision == "FORWARD" ? row.ClubReviewStatus != "APPROVED" : row.ClubReviewStatus != "PENDING"))
                return ApiOperations.Error(409, "STALE_CLUB_DECLARATION", "Reload the current declaration and review stage.");
            if (decision == "FORWARD")
            {
                row.CtsvSubmissionStatus = "SUBMITTED"; row.CtsvSubmittedBy = http.User.GetUserId();
            }
            else
            {
                row.ClubReviewStatus = decision == "ENDORSE" ? "APPROVED" : "CHANGES_REQUESTED";
                row.ClubReviewReason = reason.Trim(); row.ClubReviewedBy = http.User.GetUserId();
                if (decision == "REQUEST_CHANGES") row.Status = "REVISION_REQUESTED";
            }
            Revision(db, row, "CLUB_" + decision, Read<ClubReportHub.Shared.Experience.ContributionAttributes>(row.ProposedAttributesJson), http.User.GetUserId(), reason.Trim());
            await DeclarationExperienceBridge.SyncAsync(db, row, ct);
            ExperienceAuthorization.Audit(db, http.User, "CLUB_DECLARATION_" + decision, "Contribution", row.Id.ToString(), new { clubId, reason });
            Notify(db, row, "CLUB_DECLARATION_" + decision, decision == "FORWARD" ? "CLB đã chuyển trải nghiệm lên CTSV" : "CLB đã phản hồi trải nghiệm tự khai");
            await db.SaveChangesAsync(ct);
            return Results.Ok(Response(row) with { Capabilities = ClubDeclarationWorkflow.Capabilities(row, true, false) });
        }, ct);
    }
}
