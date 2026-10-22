using System.Data;
using System.Text;
using System.Text.Json;
using AdminService.Data;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Rosters;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Services;

internal static class SemesterRosterWorkflow
{
    internal static async Task<Semester> Semester(string identifier, AdminDbContext db, CancellationToken ct)
    {
        RosterErrors.Check(!string.IsNullOrWhiteSpace(identifier) && identifier.Length <= 40, 400, "INVALID_SEMESTER", "Use a canonical semester ID/code.");
        var isGuid = Guid.TryParse(identifier, out var id); var code = identifier.ToUpperInvariant();
        return await db.Semesters.SingleOrDefaultAsync(x => isGuid ? x.Id == id : x.SemesterCode.ToUpper() == code, ct)
            ?? throw new RosterException(404, "SEMESTER_NOT_FOUND", "Semester not found.");
    }
    internal static async Task<SemesterRoster?> Current(Semester semester, string campus, AdminDbContext db, CancellationToken ct)
    {
        var current = await db.Set<SemesterRoster>().SingleOrDefaultAsync(x => x.SemesterId == semester.Id && x.CampusCode == campus, ct);
        if (current is not null || !semester.StartDate.HasValue) return current;
        // Lazy semester initialization: only a real preceding semester in this campus
        // can supply a roster. An explicit empty revision is still a known roster.
        var predecessor = await (from r in db.Set<SemesterRoster>().AsNoTracking()
                                 join s in db.Semesters.AsNoTracking() on r.SemesterId equals s.Id
                                 where r.CampusCode == campus && s.StartDate < semester.StartDate
                                 orderby s.StartDate descending, s.Id
                                 select r).FirstOrDefaultAsync(ct);
        if (predecessor is null) return null;
        var rows = await db.Set<SemesterRosterRow>().AsNoTracking().Where(x => x.RevisionId == predecessor.RevisionId).ToArrayAsync(ct);
        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
                var winner = await db.Set<SemesterRoster>().SingleOrDefaultAsync(x => x.SemesterId == semester.Id && x.CampusCode == campus, ct);
                if (winner is not null) return winner;
                var header = new SemesterRoster { SemesterId = semester.Id, CampusCode = campus };
                var revision = new SemesterRosterRevision
                {
                    RosterId = header.Id,
                    Source = "CARRY_FORWARD",
                    SourceRevisionId = predecessor.RevisionId,
                    ActorId = 0,
                    ImportedAt = DateTimeOffset.UtcNow
                };
                header.RevisionId = revision.Id; header.Version = revision.Version;
                db.Add(header); db.Add(revision);
                db.AddRange(rows.Select(x => new SemesterRosterRow
                {
                    RevisionId = revision.Id,
                    UserId = x.UserId,
                    Email = x.Email,
                    StudentCode = x.StudentCode,
                    CampusCode = campus,
                    Active = x.Active
                }));
                await db.SaveChangesAsync(ct); if (transaction is not null) await transaction.CommitAsync(ct); return header;
            });
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return await db.Set<SemesterRoster>().SingleOrDefaultAsync(x => x.SemesterId == semester.Id && x.CampusCode == campus, ct)
                ?? throw new RosterException(409, "ROSTER_CHANGED", "Roster initialization raced another operation; reload.");
        }
    }
    internal static async Task<(List<RosterCandidate> Candidates, List<RosterImportError> Errors)> Validate(
        IReadOnlyList<RosterRowInput> rows, string campus, SchoolAccountClient accounts, CancellationToken ct)
    {
        var emails = rows.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Email) && x.Email.Length <= 200).Select(x => x.Email.Trim().ToLowerInvariant());
        var profiles = (await accounts.EmailsAsync(emails, ct)).ToDictionary(x => x.Email, StringComparer.OrdinalIgnoreCase);
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenUsers = new HashSet<int>(); var result = new List<RosterCandidate>(); var errors = new List<RosterImportError>();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index]; var line = index + 1;
            void Error(string field, string code, string text) => errors.Add(new(line, field, code, text));
            if (row is null || string.IsNullOrWhiteSpace(row.Email) || row.Email.Length > 200
                || string.IsNullOrWhiteSpace(row.StudentCode) || row.StudentCode.Length > 40
                || row.StudentCode.Trim().Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))
                || row.CampusCode != campus || row.Status is not ("ACTIVE" or "INACTIVE"))
            { Error("row", "INVALID_ROW", "Use a school email, bounded student code, canonical campus and ACTIVE/INACTIVE."); continue; }
            var email = row.Email.Trim().ToLowerInvariant(); var studentCode = row.StudentCode.Trim().ToUpperInvariant();
            if (!seenEmails.Add(email) || !seenCodes.Add(studentCode)) { Error("email", "DUPLICATE_IDENTITY", "Email and student code must be unique in one campus roster."); continue; }
            if (!profiles.TryGetValue(email, out var account)) { Error("email", "IDENTITY_UNMATCHED", "No existing canonical account matches this email; accounts are never created by import."); continue; }
            if (!seenUsers.Add(account.UserId) || row.UserId.HasValue && row.UserId != account.UserId || account.CampusCode != campus)
            { Error("userId", "IDENTITY_MISMATCH", "User/campus does not match the canonical account."); continue; }
            if (!account.IsStudent || !account.IsSchoolEmail || row.Status == "ACTIVE" && (!account.IsActive || account.IsLocked))
            { Error("status", "STUDENT_NOT_ELIGIBLE", "Use a school student account; an active row requires an active, unlocked account."); continue; }
            result.Add(new(account.UserId, email, studentCode, campus, row.Status == "ACTIVE"));
        }
        return (result, errors);
    }
    internal static async Task Revalidate(IReadOnlyList<RosterCandidate> rows, string campus, SchoolAccountClient accounts, CancellationToken ct)
    {
        var users = (await accounts.UsersAsync(rows.Select(x => x.UserId), ct)).ToDictionary(x => x.UserId);
        foreach (var row in rows)
            RosterErrors.Check(users.TryGetValue(row.UserId, out var account) && account.IsStudent && account.IsSchoolEmail
                && account.Email.Equals(row.Email, StringComparison.OrdinalIgnoreCase) && account.CampusCode == campus
                && (!row.Active || account.IsActive && !account.IsLocked), 409, "ROSTER_CHANGED", "Account identity/eligibility changed after preview. Create a new preview.");
    }
    internal static IReadOnlyList<RosterRowInput> Csv(string value)
    {
        var records = new List<string[]>(); var row = new List<string>(); var field = new StringBuilder();
        var quoted = false; var closed = false; var atStart = true;
        void EndField() { row.Add(field.ToString()); field.Clear(); atStart = true; closed = false; }
        void EndRow() { EndField(); if (row.Any(x => x.Length != 0)) records.Add(row.ToArray()); row.Clear(); RosterErrors.Check(records.Count <= 10001, 400, "ROW_LIMIT", "At most 10,000 roster rows."); }
        for (var index = 0; index < value.Length; index++)
        {
            var c = value[index];
            if (quoted)
            {
                if (c == '"')
                {
                    if (index + 1 < value.Length && value[index + 1] == '"') { field.Append('"'); index++; }
                    else { quoted = false; closed = true; }
                }
                else field.Append(c);
            }
            else if (c == '"') { RosterErrors.Check(atStart && !closed, 400, "INVALID_CSV", "Malformed quoted field."); quoted = true; atStart = false; }
            else if (c == ',') EndField();
            else if (c is '\r' or '\n') { if (c == '\r' && index + 1 < value.Length && value[index + 1] == '\n') index++; EndRow(); }
            else { RosterErrors.Check(!closed, 400, "INVALID_CSV", "Characters after a quoted field."); field.Append(c); atStart = false; }
        }
        RosterErrors.Check(!quoted, 400, "INVALID_CSV", "Unclosed quoted field.");
        if (field.Length > 0 || row.Count > 0 || closed) EndRow();
        RosterErrors.Check(records.Count > 0 && (records[0].SequenceEqual(new[] { "email", "studentCode", "campusCode", "status" })
            || records[0].SequenceEqual(new[] { "email", "studentCode", "campusCode", "status", "userId" })),
            400, "INVALID_CSV_HEADERS", "Use exact headers email,studentCode,campusCode,status with optional trailing userId.");
        var result = new List<RosterRowInput>();
        foreach (var cells in records.Skip(1))
        {
            RosterErrors.Check(cells.Length == records[0].Length, 400, "INVALID_CSV", "Every row must match the header.");
            int? id = null;
            if (cells.Length == 5 && cells[4].Length > 0)
            { RosterErrors.Check(int.TryParse(cells[4], out var parsed) && parsed > 0, 400, "INVALID_USER_ID", "Use a positive canonical user ID."); id = parsed; }
            result.Add(new(cells[0], cells[1], cells[2], cells[3], id));
        }
        return result;
    }
}
