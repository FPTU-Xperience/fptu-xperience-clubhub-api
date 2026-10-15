using AdminService.Contracts;
using AdminService.Data;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Services;

internal static class StudentContributionReadService
{
    internal static async Task<IResult> PageAsync(int userId, string? semesterId, string? status, string? pillar,
        int? page, int? pageSize, AdminDbContext db, CancellationToken ct, bool declarationsOnly = false)
    {
        if (page is < 1 || pageSize is < 1 or > 100)
            return ApiOperations.Error(400, "INVALID_PAGINATION", "Use page >= 1 and pageSize 1–100.");
        if (!string.IsNullOrWhiteSpace(pillar) && !ExperienceCategories.All.Contains(pillar))
            return ApiOperations.Error(400, "INVALID_PILLAR", "Unknown pillar.");
        var state = status?.Trim().ToUpperInvariant();
        if (state is not null && state is not ("" or "SUBMITTED" or "APPROVED" or "REJECTED" or "REVISION_REQUESTED" or "REVOKED"))
            return ApiOperations.Error(400, "INVALID_STATUS", "Unknown contribution status.");
        var canonical = db.Contributions.AsNoTracking().Where(x => x.UserId == userId);
        var legacy = db.SelfDeclarations.AsNoTracking().Where(x => x.StudentId == userId
            && !db.Contributions.Any(c => c.Id == x.Id));
        if (declarationsOnly) canonical = canonical.Where(x => x.SourceType != "CLUB");
        if (!string.IsNullOrWhiteSpace(semesterId))
        {
            var id = Guid.TryParse(semesterId, out var parsed) ? parsed : Guid.Empty;
            var code = semesterId.Trim().ToUpperInvariant();
            var semester = await db.Semesters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id || x.SemesterCode == code, ct);
            if (semester is null) return ApiOperations.Error(400, "INVALID_SEMESTER", "Use a configured canonical semester.");
            canonical = canonical.Where(x => x.SemesterId == semester.SemesterCode);
            // Historical declarations have no authoritative semester assignment.
            legacy = legacy.Where(_ => false);
        }
        if (!string.IsNullOrWhiteSpace(state))
        {
            canonical = canonical.Where(x => x.Status == state);
            legacy = legacy.Where(x => x.Status.ToUpper() == state);
        }
        if (!string.IsNullOrWhiteSpace(pillar))
        {
            canonical = canonical.Where(x => db.ExperienceLedgerEffects.Any(e => e.ContributionId == x.Id
                && db.ExperienceLedgerAllocations.Any(a => a.EffectId == e.Id && a.Pillar == pillar)));
            legacy = legacy.Where(x => (x.FinalCategory ?? x.Category) == pillar);
        }
        // UNION only the equally mapped primitive index columns. Deserialize snapshots for the selected page.
        var index = canonical.Select(x => new { x.Id, x.CreatedAtUtc, Legacy = false })
            .Concat(legacy.Select(x => new { x.Id, x.CreatedAtUtc, Legacy = true }));
        var current = page ?? 1; var size = pageSize ?? 20;
        var total = await index.CountAsync(ct);
        var selected = await index.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((int)Math.Min((long)(current - 1) * size, int.MaxValue)).Take(size).ToListAsync(ct);
        var canonicalIds = selected.Where(x => !x.Legacy).Select(x => x.Id).ToArray();
        var legacyIds = selected.Where(x => x.Legacy).Select(x => x.Id).ToArray();
        var rows = await db.Contributions.AsNoTracking().Where(x => x.UserId == userId && canonicalIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var old = await db.SelfDeclarations.AsNoTracking().Where(x => x.StudentId == userId && legacyIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var items = selected.Select(x => x.Legacy ? (object)Legacy(old[x.Id]) : VerifiedExperienceService.Response(rows[x.Id])).ToArray();
        return Results.Ok(new StudentContributionPage(items, current, size, total, total,
            (int)Math.Ceiling((decimal)total / size)));
    }

    private static LegacyContributionItem Legacy(SelfDeclaration row) => new(row.Id, row.StudentId,
        row.IsOutsideClub ? "EXTERNAL" : "CLUB", "LEGACY_DECLARATION", $"LEGACY_DECLARATION:{row.Id}",
        row.Id.ToString(), null, new DateTimeOffset(row.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        row.Title, row.Status.ToUpperInvariant(), false, row.RawPoints, null, null, []);
}

