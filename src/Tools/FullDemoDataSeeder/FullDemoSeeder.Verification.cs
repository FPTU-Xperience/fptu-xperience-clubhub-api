using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using ActivityService.Models;
using AuthService.Models;
using ClubReportHub.Shared.Auth;
using ExportService.Models;
using Microsoft.EntityFrameworkCore;

namespace FullDemoDataSeeder;

public sealed partial class FullDemoSeeder
{
    private async Task CreateExportsAsync()
    {
        if (journal.Checkpoints.GetValueOrDefault("exports") == "committed") return;
        foreach (var club in anchors.Take(3))
        {
            var report = await reports.Reports.AsNoTracking().SingleAsync(r => r.SeedKey == $"FULLDEMO_V2:{club.Code}:REPORT:2");
            foreach (var type in new[] { "PDF", "XLSX" })
            {
                var key = $"api:export-{club.Code}-{type}";
                if (!journal.Checkpoints.ContainsKey(key))
                {
                    // ExportService does not implement idempotency headers. Its
                    // synthetic requester and owned report form the recovery key.
                    var prior = await exports.ExportRequests.AsNoTracking().Where(e => e.RequestedByUserId == users["admin01"].Id
                        && e.ReportId == report.Id && e.ExportType == type && e.Scope == "Report").ToArrayAsync();
                    if (prior.Length > 1) throw new InvalidOperationException("Multiple matching demo exports require reconciliation before resuming.");
                    if (prior.Length == 1)
                    {
                        journal.Checkpoints[key] = JsonSerializer.Serialize(new { prior[0].Id }, Json);
                        journal.Write(options.JournalPath); continue;
                    }
                }
                await CallAsync("POST", "/api/exports/", users["admin01"], new { exportType = type, scope = "Report", reportId = report.Id,
                    clubId = club.Id, period = semester.SemesterCode }, $"export-{club.Code}-{type}");
            }
        }
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var ids = ExportIds();
            var rows = await exports.ExportRequests.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync();
            if (rows.Any(r => r.Status == "Failed")) throw new InvalidOperationException("Demo export worker failed: " + string.Join("; ", rows.Where(r => r.Status == "Failed").Select(r => r.ErrorMessage)));
            if (rows.Count == 6 && rows.All(r => r.Status == "Completed"))
            {
                await VerifyExportsAsync();
                journal.Checkpoints["exports"] = "committed"; journal.Write(options.JournalPath); return;
            }
            Console.WriteLine($"Waiting for export worker: {rows.Count(r => r.Status == "Completed")}/6 complete.");
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
        throw new InvalidOperationException("Export worker did not complete six downloadable files within five minutes.");
    }

    private int[] ExportIds() => journal.Checkpoints.Where(kv => kv.Key.StartsWith("api:export-"))
        .Select(kv => JsonSerializer.Deserialize<JsonElement>(kv.Value).GetProperty("id").GetInt32()).ToArray();

    private async Task CheckDownloadAsync(string path, User actor, string type, string? checksum = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, options.ApiBaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(actor));
        using var response = await client.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (!response.IsSuccessStatusCode || bytes.Length < 100 || (type == "PDF" ? !bytes.AsSpan().StartsWith("%PDF-"u8) : !bytes.AsSpan().StartsWith("PK"u8)))
            throw new InvalidOperationException($"Invalid downloadable {type}: {path}.");
        if (checksum is not null && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(checksum, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Download checksum mismatch: " + path);
    }

    private async Task VerifyExportsAsync()
    {
        var ids = ExportIds();
        var rows = await exports.ExportRequests.AsNoTracking().Include(e => e.File).Where(e => ids.Contains(e.Id)).ToArrayAsync();
        if (ids.Length != 6 || rows.Length != 6 || rows.Any(e => e.Status != "Completed" || e.File is null
            || !e.File.IsAvailable || e.File.ExpiresAtUtc <= DateTimeOffset.UtcNow || e.File.SizeBytes < 100))
            throw new InvalidOperationException("All six exact demo exports must have completed, unexpired and available files.");
        foreach (var row in rows) await CheckDownloadAsync($"/api/exports/{row.Id}/download", users["admin01"], row.ExportType, row.File!.Checksum);
    }

    private async Task VerifyAsync()
    {
        await LoadUsersAsync();
        var ids = anchors.Select(c => c.Id).ToArray();
        var demoIds = users.Values.Select(u => u.Id).Append(protectedUser.Id).ToArray();
        var demoActivities = await activities.Activities.AsNoTracking().Where(a => ids.Contains(a.ClubId) && a.Title.StartsWith("[Demo]")).ToArrayAsync();
        var activityIds = demoActivities.Select(a => a.Id).ToArray();
        var sessions = await activities.Sessions.AsNoTracking().Where(s => activityIds.Contains(s.ActivityId)).ToArrayAsync();
        var attendance = await activities.ActivityAttendances.AsNoTracking().Where(a => activityIds.Contains(a.ActivityId)).ToArrayAsync();
        var membership = await clubs.ClubMemberships.AsNoTracking().Where(m => ids.Contains(m.ClubId) && demoIds.Contains(m.UserId)).ToArrayAsync();
        var reportRows = await reports.Reports.AsNoTracking().Where(r => r.SeedKey != null && r.SeedKey.StartsWith("FULLDEMO_V2:")).ToArrayAsync();
        var contributionRows = await admin.Contributions.AsNoTracking().Where(c => c.ActivityId != null && activityIds.Contains(c.ActivityId.Value)).ToArrayAsync();
        for (var retry = 0; contributionRows.Count(c => c.UserId == protectedUser.Id && c.Status == "APPROVED") < 6 && retry < 20; retry++)
        {
            Console.WriteLine("Waiting for canonical attendance recognition for the protected student.");
            await Task.Delay(TimeSpan.FromSeconds(3));
            contributionRows = await admin.Contributions.AsNoTracking().Where(c => c.ActivityId != null && activityIds.Contains(c.ActivityId.Value)).ToArrayAsync();
        }
        var syntheticIds = users.Values.Select(u => u.Id).ToArray();
        var realWorld = await admin.Contributions.AsNoTracking().Where(c => syntheticIds.Contains(c.UserId) && c.SourceType == "REAL_WORLD").ToArrayAsync();
        var allContributions = contributionRows.Concat(realWorld).ToArray();
        var contributionIds = allContributions.Select(c => c.Id).ToArray();
        var effects = await admin.ExperienceLedgerEffects.AsNoTracking().Where(e => contributionIds.Contains(e.ContributionId)).ToArrayAsync();
        var effectIds = effects.Select(e => e.Id).ToArray();
        var allocations = await admin.ExperienceLedgerAllocations.AsNoTracking().Where(a => effectIds.Contains(a.EffectId)).ToArrayAsync();
        var revisions = await admin.ContributionRevisions.AsNoTracking().Where(r => contributionIds.Contains(r.ContributionId)).ToDictionaryAsync(r => r.Id);
        var problems = new List<string>();
        if (users.Count != 120) problems.Add($"Expected 120 synthetic accounts, found {users.Count}.");
        if (demoActivities.Length < 60 || sessions.Length < 84) problems.Add($"Expected at least 60 activities/84 sessions, found {demoActivities.Length}/{sessions.Length}.");
        if (membership.Length < 231) problems.Add($"Expected at least 231 owned memberships, found {membership.Length}.");
        if (reportRows.Length < 40) problems.Add("Expected at least 40 reports.");
        var participantCount = await activities.ActivityParticipants.CountAsync(p => activityIds.Contains(p.ActivityId));
        var budgetRows = await finance.BudgetProposals.AsNoTracking().Include(b => b.Settlements).Where(b => ids.Contains(b.ClubId) && b.Title.StartsWith("[Demo]")).ToArrayAsync();
        var questRows = await admin.Quests.AsNoTracking().Where(q => q.Title.StartsWith("[Demo]")).ToArrayAsync();
        var questIds = questRows.Select(q => q.Id).ToArray();
        var questParticipants = await admin.QuestParticipants.AsNoTracking().Where(p => questIds.Contains(p.QuestId)).ToArrayAsync();
        var questParticipantIds = questParticipants.Select(p => p.Id).ToArray();
        var completionCount = await admin.QuestCompletionRecords.CountAsync(c => questParticipantIds.Contains(c.ParticipantId));
        if (participantCount < 444 || attendance.Length < 495) problems.Add("Participant or attendance fixtures are incomplete.");
        if (budgetRows.Length < 24 || budgetRows.Sum(b => b.Settlements.Count) < 12) problems.Add("Budget/settlement fixtures are incomplete.");
        if (budgetRows.Any(b => b.Settlements.Any(s => s.Status == "Approved") && b.Status != "Settled")) problems.Add("Approved settlement must have a settled proposal.");
        foreach (var budget in budgetRows)
        {
            var report = reportRows.SingleOrDefault(r => r.Id == budget.SourceReportId);
            if (report is null) { problems.Add("Budget source report missing."); continue; }
            if (budget.Status == "Rejected" ? report.BudgetProposalId is not null
                : report.BudgetProposalId != budget.Id || report.BudgetRequestedAmount != budget.RequestedAmount || report.BudgetApprovedAmount != budget.ApprovedAmount)
                problems.Add("Budget/report linkage mismatch.");
            if (report.PublishedActivityId is null && (budget.Status == "ManagerApproved" && report.Status != "UnderReview"
                || budget.Status == "Submitted" && report.Status != "Submitted")) problems.Add("Pending report/budget workflow mismatch.");
        }
        if (questRows.Length < 12 || questParticipants.Length < 60 || completionCount < 48) problems.Add("Quest fixtures or completion proofs are incomplete.");
        if (contributionRows.Count(c => c.UserId == protectedUser.Id && c.Status == "APPROVED") < 6 || realWorld.Length < 12 || effects.Length < 79)
            problems.Add("Canonical club/real-world/attendance ledger is incomplete.");
        if (attendance.Any(a => a.SessionId is null || sessions.SingleOrDefault(s => s.Id == a.SessionId)?.ActivityId != a.ActivityId)) problems.Add("Attendance/session lineage mismatch.");
        if (contributionRows.Count(c => c.UserId != protectedUser.Id) < 96) problems.Add("Expected at least 96 authored club contributions.");
        foreach (var effect in effects)
        {
            if (!revisions.TryGetValue(effect.RevisionId, out var revision) || revision.ContributionId != effect.ContributionId) problems.Add("Ledger revision lineage mismatch.");
            if (allocations.Where(a => a.EffectId == effect.Id).Sum(a => a.Amount) != effect.Amount) problems.Add("Ledger allocation sum differs from effect.");
        }
        foreach (var contribution in allContributions)
        {
            if (!revisions.TryGetValue(contribution.CurrentRevisionId, out var current) || current.ContributionId != contribution.Id)
                problems.Add("Current contribution revision missing.");
            if (effects.Where(e => e.ContributionId == contribution.Id).Sum(e => e.Amount) != contribution.RawPoints)
                problems.Add("Contribution points do not reconcile to immutable effects.");
        }
        if (sessions.Any(s => s.OpensAt < s.StartTimeUtc || s.ClosesAt > s.EndTimeUtc || s.OpensAt >= s.ClosesAt
            || s.Mode == "ONLINE" && (s.Latitude is not null || s.Longitude is not null || s.RadiusMeters is not null)
            || s.Mode == "ONSITE" && (s.Latitude is null || s.Longitude is null || s.RadiusMeters is null))) problems.Add("Invalid session attendance policy.");
        var initial = !journal.Checkpoints.ContainsKey("complete");
        if (initial && demoActivities.Count(a => a.PublicationStatus == "PUBLISHED" && a.DiscoveryVisibility == "CAMPUS_PUBLIC"
            && a.StartTimeUtc > DateTimeOffset.UtcNow && a.RegistrationOpensAt <= DateTimeOffset.UtcNow && a.RegistrationClosesAt > DateTimeOffset.UtcNow && a.RegisteredCount < a.Capacity) < 6)
            problems.Add("Open public upcoming scenarios are missing.");
        var timetableCount = await auth.Set<StudyScheduleBlock>().CountAsync(b => syntheticIds.Contains(b.UserId));
        if (timetableCount < 60) problems.Add("Both 10W and 3W timetable fixtures are required.");
        foreach (var campus in new[] { "HAN", "HCM", "DAN" })
        {
            var calendar = await CallAsync("GET", $"/api/academic-calendar/{options.ReferenceDate.Year}/FA?campusCode={campus}", Reviewer(campus));
            var version = calendar.GetProperty("version").GetGuid();
            if (calendar.GetProperty("availability").GetString() != "AVAILABLE" || !calendar.GetProperty("canWrite").GetBoolean()) problems.Add("Academic calendar is not writable.");
            var campusUsers = users.Values.Where(u => u.CampusCode == campus).Select(u => u.Id).ToArray();
            if (await auth.Set<StudyScheduleBlock>().AnyAsync(b => campusUsers.Contains(b.UserId) && b.CalendarVersion != version)) problems.Add("Timetable uses a stale calendar version.");
        }
        var usersById = users.Values.Append(protectedUser).ToDictionary(u => u.Id);
        if (membership.Any(m => usersById[m.UserId].CampusCode != anchors.Single(c => c.Id == m.ClubId).CampusCode)) problems.Add("Cross-campus membership mismatch.");
        var original = await auth.Users.AsNoTracking().Include(u => u.UserRoles).ThenInclude(r => r.Role).SingleAsync(u => u.Id == protectedUser.Id);
        if (original.UserRoles.Count != 1 || original.UserRoles.Single().Role.Name != AuthRoles.ClubMember) problems.Add("Protected account role changed.");
        var digest = DemoJournal.Digest(JsonSerializer.Serialize(new { original.Id, original.Email, original.Username, original.FullName,
            original.GoogleSubject, original.PasswordHash, original.CampusCode, original.SecurityVersion, original.IsActive, original.IsLocked,
            Roles = original.UserRoles.Select(r => r.Role.Name).Order() }));
        if (digest != journal.ProtectedIdentityDigest) problems.Add("Protected identity changed.");
        if (ExportIds().Length != 6) problems.Add("Expected six real export requests.");
        await VerifyExportsAsync();
        var uploadedFiles = await reports.ReportUploadedFiles.AsNoTracking().Where(f => reportRows.Select(r => r.Id).Contains(f.ReportId)).ToArrayAsync();
        var attachments = await reports.ReportAttachments.AsNoTracking().Where(f => reportRows.Select(r => r.Id).Contains(f.ReportId)).ToArrayAsync();
        if (uploadedFiles.Length < 40 || attachments.Length < 40) problems.Add("Report file fixtures are incomplete.");
        foreach (var file in uploadedFiles) await CheckDownloadAsync($"/api/reports/{file.ReportId}/uploaded-file/download", users["admin01"], "PDF", file.Checksum);
        foreach (var file in attachments) await CheckDownloadAsync($"/api/reports/{file.ReportId}/attachments/{file.Id}/download", users["admin01"], "PDF");
        await CallAsync("GET", $"/api/users/me/experience-summary?semesterId={Uri.EscapeDataString(semester.SemesterCode)}&scope=PERIOD", users["student001"]);
        var summary = new { dataset = FullDemoOptions.Dataset, accounts = users.Count, clubs = ids.Length, memberships = membership.Length,
            activities = demoActivities.Length, sessions = sessions.Length, participants = participantCount,
            attendances = attendance.Length, reports = reportRows.Length, budgets = budgetRows.Length,
            contributions = allContributions.Length, approved = allContributions.Count(c => c.Status == "APPROVED"), ledgerEffects = effects.Length,
            quests = questRows.Length, questCompletions = completionCount, timetables = timetableCount,
            exportCount = ExportIds().Length, protectedAccountIsStudent = original.UserRoles.Single().Role.Name == AuthRoles.ClubMember, problems };
        Console.WriteLine(JsonSerializer.Serialize(summary, Json));
        if (problems.Count > 0) throw new InvalidOperationException(string.Join("; ", problems));
    }
}
