using System.Text.Json;
using AdminService.Data;
using AdminService.Security;
using AdminService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Rosters;
using Microsoft.EntityFrameworkCore;
using static ClubReportHub.Shared.Rosters.RosterErrors;

namespace AdminService.Endpoints;

public static class SemesterRosterEndpoints
{
    private const int MaxBytes = 5 * 1024 * 1024;
    public static void MapSemesterRosterEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/semesters/{identifier}").RequireAuthorization(AdminPolicies.BackofficeUser)
            .AddEndpointFilter<RosterExceptionFilter>().WithTags("Semester student roster");
        api.MapPost("/roster-imports/preview", Preview);
        api.MapPost("/roster-imports/{importId:guid}/commit", Commit);
        api.MapGet("/student-roster", Read);
        var internalApi = app.MapGroup("/api/internal/semester-roster").AllowAnonymous().ExcludeFromDescription().AddEndpointFilter<RosterExceptionFilter>();
        internalApi.MapPost("/lookup", Lookup);
        internalApi.MapGet("/summaries", Summaries);
    }
    private static DateTimeOffset Now(HttpContext http) => http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
    private static string Campus(string? campusCode, HttpContext http)
    {
        var campus = campusCode ?? http.User.GetCampus();
        Check(CampusCodes.FiveCampuses.Contains(campus), 400, "INVALID_CAMPUS", "Use one canonical campus code.");
        Check(http.User.IsInRole(AuthRoles.Admin) || campus == http.User.GetCampus(), 403, "CAMPUS_SCOPE_DENIED", "CTSV may only administer its own campus.");
        return campus;
    }
    private static async Task<IResult> Preview(string identifier, string? campusCode, HttpContext http, AdminDbContext db, SchoolAccountClient accounts, CancellationToken ct)
    {
        var campus = Campus(campusCode, http); var semester = await SemesterRosterWorkflow.Semester(identifier, db, ct);
        RosterPreviewInput input;
        if (http.Request.HasFormContentType)
        {
            var bytes = await BoundedJsonRequest.ReadBytesAsync(http.Request.Body, MaxBytes, ct);
            http.Request.Body = new MemoryStream(bytes); http.Request.ContentLength = bytes.Length;
            var form = await http.Request.ReadFormAsync(ct);
            Check(form.Files.Count == 1 && form.Files[0].Name == "file" && form["mode"] == "replace"
                && form.Keys.All(x => x is "mode" or "expectedRosterVersion"), 400, "INVALID_IMPORT_FORM", "Send file, mode=replace and expectedRosterVersion.");
            Check(Guid.TryParse(form["expectedRosterVersion"], out var version), 400, "ROSTER_VERSION_REQUIRED", "Use Guid.Empty for an uninitialized roster.");
            using var stream = form.Files[0].OpenReadStream();
            var text = BoundedJsonRequest.Utf8(await BoundedJsonRequest.ReadBytesAsync(stream, MaxBytes, ct));
            input = new(1, SemesterRosterWorkflow.Csv(text), version);
        }
        else input = await BoundedJsonRequest.ReadAsync<RosterPreviewInput>(http, MaxBytes, ct);
        Check(input.SchemaVersion == 1 && input.Mode == "replace" && input.Rows is { Count: <= 10000 },
            400, "INVALID_IMPORT", "Use schemaVersion 1, replace mode and at most 10,000 rows (empty is explicit).");
        var current = await SemesterRosterWorkflow.Current(semester, campus, db, ct);
        Check(input.ExpectedRosterVersion == (current?.Version ?? Guid.Empty), 409, "ROSTER_CHANGED", "Reload the roster version before preview.");
        var candidate = await SemesterRosterWorkflow.Validate(input.Rows, campus, accounts, ct);
        var prior = current is null ? [] : await db.Set<SemesterRosterRow>().AsNoTracking().Where(x => x.RevisionId == current.RevisionId).ToArrayAsync(ct);
        var priorByUser = prior.ToDictionary(x => x.UserId);
        var candidateIds = candidate.Candidates.Select(x => x.UserId).ToHashSet();
        var preview = new SemesterRosterImport
        {
            SemesterId = semester.Id,
            CampusCode = campus,
            BaseVersion = input.ExpectedRosterVersion,
            ActorId = http.User.GetUserId(),
            CreatedAt = Now(http),
            ExpiresAt = Now(http).AddHours(1),
            CandidatesJson = JsonSerializer.Serialize(candidate.Candidates, BoundedJsonRequest.Options),
            ErrorsJson = JsonSerializer.Serialize(candidate.Errors, BoundedJsonRequest.Options)
        };
        db.Add(preview); await db.SaveChangesAsync(ct);
        return Results.Ok(new
        {
            importId = preview.Id,
            semesterKey = semester.Id,
            semesterId = semester.SemesterCode,
            campusCode = campus,
            baseVersion = preview.BaseVersion,
            preview.ExpiresAt,
            rowCount = input.Rows.Count,
            additions = candidate.Candidates.Count(x => !priorByUser.ContainsKey(x.UserId)),
            updates = candidate.Candidates.Count(x => priorByUser.TryGetValue(x.UserId, out var old) && (old.Email != x.Email || old.StudentCode != x.StudentCode || old.Active != x.Active)),
            omissions = prior.Count(x => !candidateIds.Contains(x.UserId)),
            errors = candidate.Errors,
            canCommit = candidate.Errors.Count == 0
        });
    }
    private static async Task<IResult> Commit(string identifier, Guid importId, string? campusCode, HttpContext http, AdminDbContext db, SchoolAccountClient accounts, CancellationToken ct)
    {
        var campus = Campus(campusCode, http); var semester = await SemesterRosterWorkflow.Semester(identifier, db, ct);
        var preview = await db.Set<SemesterRosterImport>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == importId && x.SemesterId == semester.Id && x.CampusCode == campus && x.ActorId == http.User.GetUserId(), ct);
        Check(preview is not null, 404, "IMPORT_NOT_FOUND", "Your import preview was not found.");
        var input = await BoundedJsonRequest.ReadAsync<RosterCommitInput>(http, 4096, ct);
        var rows = JsonSerializer.Deserialize<RosterCandidate[]>(preview!.CandidatesJson, BoundedJsonRequest.Options)!;
        // Revalidate current identity even before an idempotent replay.
        await SemesterRosterWorkflow.Revalidate(rows, campus, accounts, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            Check(preview.ExpiresAt > Now(http), 409, "IMPORT_EXPIRED", "Create a new import preview.");
            Check(JsonSerializer.Deserialize<RosterImportError[]>(preview.ErrorsJson, BoundedJsonRequest.Options)!.Length == 0,
                409, "IMPORT_HAS_ERRORS", "Resolve every preview error before committing.");
            var header = await db.Set<SemesterRoster>().SingleOrDefaultAsync(x => x.SemesterId == semester.Id && x.CampusCode == campus, ct);
            Check(input.ExpectedRosterVersion == preview.BaseVersion && input.ExpectedRosterVersion == (header?.Version ?? Guid.Empty),
                409, "ROSTER_CHANGED", "The roster changed after preview.");
            Check(!await db.Set<SemesterRosterRevision>().AnyAsync(x => x.ImportId == importId, ct), 409, "IMPORT_ALREADY_COMMITTED", "This preview already has a committed revision.");
            if (header is null) { header = new SemesterRoster { SemesterId = semester.Id, CampusCode = campus }; db.Add(header); }
            var revision = new SemesterRosterRevision
            {
                RosterId = header.Id,
                ImportId = importId,
                SourceRevisionId = header.RevisionId == Guid.Empty ? null : header.RevisionId,
                ActorId = http.User.GetUserId(),
                ImportedAt = Now(http)
            };
            db.Add(revision); header.Version = revision.Version; header.RevisionId = revision.Id;
            db.AddRange(rows.Select(x => new SemesterRosterRow
            {
                RevisionId = revision.Id,
                UserId = x.UserId,
                Email = x.Email,
                StudentCode = x.StudentCode,
                CampusCode = campus,
                Active = x.Active
            }));
            db.AddOutboxMessage(new ExperienceNotificationEvent(Guid.NewGuid(), Now(http), 0, [http.User.GetUserId()],
                "SEMESTER_ROSTER_IMPORTED", "Danh sách sinh viên học kỳ đã được cập nhật", "/"), ExperienceRoutingKeys.Notification);
            return Results.Ok(new
            {
                revisionId = revision.Id,
                version = revision.Version,
                semesterKey = semester.Id,
                semesterId = semester.SemesterCode,
                campusCode = campus,
                activeCount = rows.Count(x => x.Active),
                inactiveCount = rows.Count(x => !x.Active),
                source = "IMPORT"
            });
        }, ct);
    }
    private static async Task<IResult> Read(string identifier, string? campusCode, string? search, string? status, int? page, int? pageSize,
        HttpContext http, AdminDbContext db, SchoolAccountClient accounts, CancellationToken ct)
    {
        var campus = Campus(campusCode, http); var semester = await SemesterRosterWorkflow.Semester(identifier, db, ct);
        var current = await SemesterRosterWorkflow.Current(semester, campus, db, ct);
        var p = page ?? 1; var size = pageSize ?? 20;
        Check(p is >= 1 and <= 100000 && size is >= 1 and <= 100 && (search?.Length ?? 0) <= 100
            && status is null or "ACTIVE" or "INACTIVE", 400, "INVALID_ROSTER_FILTERS", "Check pagination, search and status.");
        if (current is null) return Results.Ok(new
        {
            semesterKey = semester.Id,
            semesterId = semester.SemesterCode,
            campusCode = campus,
            availability = "ROSTER_UNAVAILABLE",
            version = Guid.Empty,
            revisionId = (Guid?)null,
            items = Array.Empty<object>(),
            page = p,
            pageSize = size,
            totalItems = 0,
            totalPages = 0
        });
        var rows = await db.Set<SemesterRosterRow>().AsNoTracking().Where(x => x.RevisionId == current.RevisionId).ToArrayAsync(ct);
        var profiles = (await accounts.UsersAsync(rows.Select(x => x.UserId), ct)).ToDictionary(x => x.UserId);
        var query = rows.Where(x => status is null || x.Active == (status == "ACTIVE"));
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.StudentCode.Contains(search, StringComparison.OrdinalIgnoreCase)
            || x.Email.Contains(search, StringComparison.OrdinalIgnoreCase) || profiles.GetValueOrDefault(x.UserId)?.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
        var filtered = query.OrderBy(x => profiles.GetValueOrDefault(x.UserId)?.FullName, StringComparer.Ordinal).ThenBy(x => x.UserId).ToArray();
        var revision = await db.Set<SemesterRosterRevision>().AsNoTracking().SingleAsync(x => x.Id == current.RevisionId, ct);
        return Results.Ok(new
        {
            semesterKey = semester.Id,
            semesterId = semester.SemesterCode,
            campusCode = campus,
            availability = "AVAILABLE",
            current.Version,
            current.RevisionId,
            revision.Source,
            revision.SourceRevisionId,
            revision.ImportedAt,
            items = filtered.Skip((p - 1) * size).Take(size).Select(x => new
            {
                x.UserId,
                x.StudentCode,
                x.Email,
                x.CampusCode,
                x.Active,
                fullName = profiles.GetValueOrDefault(x.UserId)?.FullName,
                accountAvailable = profiles.GetValueOrDefault(x.UserId) is { IsActive: true, IsLocked: false }
            }),
            page = p,
            pageSize = size,
            totalItems = filtered.Length,
            totalPages = (int)Math.Ceiling(filtered.Length / (double)size)
        });
    }
    private static async Task<IResult> Lookup(HttpContext http, AdminDbContext db, SchoolAccountClient accounts, IConfiguration config, CancellationToken ct)
    {
        if (!VerifiedExperienceRules.InternalRequest(http, config)) return Results.Forbid();
        var input = await BoundedJsonRequest.ReadAsync<RosterLookupQuery>(http, 256 * 1024, ct);
        Check(CampusCodes.FiveCampuses.Contains(input.CampusCode) && input.Page is >= 1 and <= 100000 && input.PageSize is >= 1 and <= 50
            && input.ExcludedUserIds is null or { Length: <= 10000 } && (input.Search?.Length ?? 0) <= 100 && input.UserId is null or > 0,
            400, "INVALID_ROSTER_LOOKUP", "Check roster lookup scope and pagination.");
        var semester = await SemesterRosterWorkflow.Semester(input.SemesterId, db, ct);
        var current = await SemesterRosterWorkflow.Current(semester, input.CampusCode, db, ct);
        RosterLookupResult Result(string availability, IReadOnlyList<EligibleRosterStudent> items, int total) =>
            new(semester.Id, semester.SemesterCode, input.CampusCode, availability, current?.Version ?? Guid.Empty, current?.RevisionId,
                items, input.Page, input.PageSize, total, (int)Math.Ceiling(total / (double)input.PageSize));
        if (current is null) return Results.Ok(Result("ROSTER_UNAVAILABLE", [], 0));
        if (!semester.IsActive) return Results.Ok(Result("SEMESTER_INACTIVE", [], 0));
        if (input.MetadataOnly) return Results.Ok(Result("AVAILABLE", [], 0));
        var rows = await db.Set<SemesterRosterRow>().AsNoTracking().Where(x => x.RevisionId == current.RevisionId
            && (!input.UserId.HasValue || x.UserId == input.UserId)).ToArrayAsync(ct);
        var profiles = (await accounts.UsersAsync(rows.Select(x => x.UserId), ct)).ToDictionary(x => x.UserId);
        var excluded = input.ExcludedUserIds?.ToHashSet() ?? [];
        var items = rows.Where(x => !excluded.Contains(x.UserId)).Select(x =>
        {
            var account = profiles.GetValueOrDefault(x.UserId);
            return new EligibleRosterStudent(x.UserId, account?.FullName ?? "", x.StudentCode, account?.IsActive == true, account?.IsLocked ?? true,
                x.Active && account is { IsActive: true, IsLocked: false, IsStudent: true, IsSchoolEmail: true }
                && account.CampusCode == input.CampusCode && account.Email.Equals(x.Email, StringComparison.OrdinalIgnoreCase));
        });
        if (!input.UserId.HasValue) items = items.Where(x => x.Eligible);
        if (!string.IsNullOrWhiteSpace(input.Search)) items = items.Where(x => x.FullName.Contains(input.Search, StringComparison.OrdinalIgnoreCase) || x.StudentCode.Contains(input.Search, StringComparison.OrdinalIgnoreCase));
        var result = items.OrderBy(x => x.FullName, StringComparer.Ordinal).ThenBy(x => x.UserId).ToArray();
        return Results.Ok(Result("AVAILABLE", result.Skip((input.Page - 1) * input.PageSize).Take(input.PageSize).ToArray(), result.Length));
    }
    private static async Task<IResult> Summaries(string campusCode, HttpContext http, AdminDbContext db, IConfiguration config, CancellationToken ct)
    {
        if (!VerifiedExperienceRules.InternalRequest(http, config)) return Results.Forbid();
        Check(CampusCodes.FiveCampuses.Contains(campusCode), 400, "INVALID_CAMPUS", "Use a canonical campus.");
        var semesters = await db.Semesters.AsNoTracking().OrderBy(x => x.StartDate).ToArrayAsync(ct);
        foreach (var semester in semesters) await SemesterRosterWorkflow.Current(semester, campusCode, db, ct);
        var headers = await db.Set<SemesterRoster>().AsNoTracking().Where(x => x.CampusCode == campusCode).ToArrayAsync(ct);
        var items = new List<RosterSummary>();
        foreach (var header in headers)
            items.Add(new(header.SemesterId, semesters.Single(x => x.Id == header.SemesterId).SemesterCode, header.Version, header.RevisionId,
                await db.Set<SemesterRosterRow>().CountAsync(x => x.RevisionId == header.RevisionId && x.Active, ct)));
        return Results.Ok(items);
    }
}
