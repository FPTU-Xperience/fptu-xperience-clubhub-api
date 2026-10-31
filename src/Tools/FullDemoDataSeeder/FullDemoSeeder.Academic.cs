using System.Text.Json;
using AdminService.Contracts;
using AdminService.Data;
using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace FullDemoDataSeeder;

public sealed partial class FullDemoSeeder
{
    private async Task SeedAcademicDataAsync()
    {
        if (journal.Checkpoints.GetValueOrDefault("academic") == "committed") return;
        foreach (var campus in new[] { "HAN", "HCM", "DAN" })
        {
            var header = await admin.Set<SemesterRoster>().AsNoTracking().SingleOrDefaultAsync(r => r.SemesterId == semester.Id && r.CampusCode == campus);
            var prior = header is null ? [] : await admin.Set<SemesterRosterRow>().AsNoTracking().Where(r => r.RevisionId == header.RevisionId).ToArrayAsync();
            journal.Checkpoints.TryAdd("before:roster:" + campus, JsonSerializer.Serialize(new { header, rows = prior }, Json));
            if (header is not null) journal.Observe("Admin", admin.Entry(header), false);
            journal.Write(options.JournalPath);
            var rows = prior.Select(r => (object)new { r.UserId, r.Email, r.StudentCode, r.CampusCode, status = r.Active ? "ACTIVE" : "INACTIVE" }).ToList();
            var knownIds = prior.Select(r => r.UserId).ToHashSet();
            foreach (var user in users.Values.Where(u => u.CampusCode == campus && u.Username.StartsWith("fulldemo.student") && !knownIds.Contains(u.Id)))
                rows.Add(new { userId = user.Id, user.Email, studentCode = "DEMO" + user.Username[^3..], campusCode = campus, status = "ACTIVE" });
            if (rows.Count != prior.Length)
            {
                var preview = await CallAsync("POST", $"/api/v1/semesters/{semester.SemesterCode}/roster-imports/preview?campusCode={campus}", Reviewer(campus),
                    new { schemaVersion = 1, rows, expectedRosterVersion = header?.Version ?? Guid.Empty, mode = "replace" }, "roster-preview-" + campus);
                if (!preview.GetProperty("canCommit").GetBoolean() || preview.GetProperty("omissions").GetInt32() != 0)
                    throw new InvalidOperationException("Roster preview rejected or would omit an existing student: " + preview.GetRawText());
                await CallAsync("POST", $"/api/v1/semesters/{semester.SemesterCode}/roster-imports/{preview.GetProperty("importId").GetGuid()}/commit?campusCode={campus}",
                    Reviewer(campus), new { expectedRosterVersion = preview.GetProperty("baseVersion").GetGuid() }, "roster-commit-" + campus);
            }
        }
        var calendars = new Dictionary<string, Guid>();
        foreach (var campus in new[] { "HAN", "HCM", "DAN" })
        {
            var path = $"/api/academic-calendar/{options.ReferenceDate.Year}/FA?campusCode={campus}";
            var current = await CallAsync("GET", path, Reviewer(campus));
            if (current.GetProperty("availability").GetString() == "UNCONFIGURED")
            {
                var start = DateOnly.FromDateTime(semester.StartDate!.Value.Date);
                var monday = start.AddDays((8 - (int)start.DayOfWeek) % 7);
                current = await CallAsync("PUT", path, Reviewer(campus), new
                {
                    termStartDate = start.ToString("yyyy-MM-dd"), block10StartDate = monday.ToString("yyyy-MM-dd"),
                    block3StartDate = monday.AddDays(70).ToString("yyyy-MM-dd"), expectedVersion = current.GetProperty("version").GetGuid()
                }, "calendar-" + campus);
            }
            if (current.GetProperty("availability").GetString() != "AVAILABLE" || !current.GetProperty("canWrite").GetBoolean())
                throw new InvalidOperationException("Existing academic calendar is not current and writable; it will not be silently replaced.");
            calendars[campus] = current.GetProperty("version").GetGuid();
        }
        await PhaseAsync("study-schedules", "Auth", async () =>
        {
            foreach (var campus in new[] { "HAN", "HCM", "DAN" })
            {
                foreach (var user in users.Values.Where(u => u.Username.StartsWith("fulldemo.student") && u.CampusCode == campus).OrderBy(u => u.Username).Take(10))
                {
                    foreach (var block in new[] { "10W", "3W" })
                    {
                        var blockId = FullDemoOptions.StableId("timetable:" + user.Username + ":" + block);
                        if (await auth.Set<StudyScheduleBlock>().AnyAsync(b => b.Id == blockId)) continue;
                        auth.Add(new StudyScheduleBlock { Id = blockId, UserId = user.Id, Year = options.ReferenceDate.Year, Term = "FA", Block = block,
                            SemesterKey = semester.Id, CalendarVersion = calendars[campus], UpdatedAtUtc = options.At(-30) });
                        for (var week = 1; week <= (block == "10W" ? 10 : 3); week++)
                            foreach (var day in new[] { "mon", "wed", "fri" })
                                auth.Add(new StudyScheduleCell { BlockId = blockId, Week = week, Day = day, SlotNumber = week % 3 + 1,
                                    Subject = week % 2 == 0 ? "SWP391 · Dự án phần mềm" : "PRN212 · Lập trình ứng dụng", Room = campus + "-Alpha-302", UpdatedAtUtc = options.At(-30) });
                    }
                }
            }
        });
        journal.Checkpoints["academic"] = "committed"; journal.Write(options.JournalPath);
    }

    private async Task SeedQuestsAsync()
    {
        if (journal.Checkpoints.GetValueOrDefault("quests") == "committed") return;
        await PhaseAsync("quest-fixtures", "Admin", async () =>
        {
            for (var index = 0; index < anchors.Count; index++)
            {
                var club = anchors[index]; var questId = FullDemoOptions.StableId("quest:" + club.Code);
                if (await admin.Quests.AnyAsync(q => q.Id == questId)) continue;
                var titles = new[] { ActivityKey(club, 0), ActivityKey(club, 1), ActivityKey(club, 3) };
                var eligible = await activities.Activities.AsNoTracking().Where(a => a.ClubId == club.Id && titles.Contains(a.Title)).Select(a => a.Id).ToArrayAsync();
                var criteria = new QuestCriteria(Weights(club.Category)[0].Pillar, eligible, periodPolicy.PolicyVersion);
                var version = FullDemoOptions.StableId("quest-criteria:" + club.Code);
                var quest = new Quest { Id = questId, Title = $"[Demo] Hành trình trải nghiệm · {club.Code}",
                    Description = "Hoàn thành một đóng góp đã xác minh ở hoạt động đủ điều kiện. Thành tích lấy từ sổ trải nghiệm, không cộng điểm lần hai.",
                    Category = criteria.TargetPillar, Kind = "Cá nhân", Scope = "Toàn bộ sinh viên", RewardXp = 0,
                    TargetParticipants = 20, SemesterCode = semester.SemesterCode, CampusCode = club.CampusCode, Icon = "sparkles",
                    DeadlineUtc = options.At(28, 23), Status = "published", CreatedByUserId = Reviewer(club.CampusCode).Id,
                    CreatedByName = Reviewer(club.CampusCode).FullName, CreatedAtUtc = options.At(-36), UpdatedAtUtc = options.At(-36),
                    CriteriaVersion = version, CriteriaSnapshotJson = JsonSerializer.Serialize(criteria, Json) };
                admin.Quests.Add(quest);
                var members = (await MembersAsync(club)).Take(index < 3 ? 4 : 5).ToList();
                if (index < 3 && club.CampusCode == protectedUser.CampusCode) members.Add(protectedUser);
                foreach (var user in members)
                    admin.QuestParticipants.Add(new() { Id = FullDemoOptions.StableId($"quest-member:{club.Code}:{user.Id}"), QuestId = questId,
                        StudentUserId = user.Id, StudentName = user.FullName, StudentEmail = user.Email, CampusCode = club.CampusCode,
                        Status = "Joined", JoinedAtUtc = options.At(-35), Note = "[Demo] Lịch sử tham gia mô phỏng trước hoạt động.",
                        CriteriaVersion = version, CriteriaSnapshotJson = JsonSerializer.Serialize(new JoinedQuestCriteria(criteria, semester.SemesterCode, club.CampusCode, quest.DeadlineUtc, version), Json) });
            }
        });
        foreach (var club in anchors)
        {
            var questId = FullDemoOptions.StableId("quest:" + club.Code);
            var quest = await admin.Quests.AsNoTracking().SingleAsync(q => q.Id == questId);
            var criteria = JsonSerializer.Deserialize<QuestCriteria>(quest.CriteriaSnapshotJson!, Json)!;
            foreach (var user in (await MembersAsync(club)).Take(4))
            {
                var participant = await admin.QuestParticipants.AsNoTracking().SingleAsync(p => p.QuestId == questId && p.StudentUserId == user.Id);
                if (participant.Status == "Completed") continue;
                var proofs = await admin.Contributions.AsNoTracking().Where(c => c.UserId == user.Id && c.Status == "APPROVED"
                    && c.ActivityId != null && criteria.EligibleActivityIds.Contains(c.ActivityId.Value)).Select(c => c.Id).ToArrayAsync();
                await CallAsync("POST", $"/api/quests/{questId}/complete", user,
                    new CompleteQuestRequest(user.Id, "[Demo] Hoàn thành bằng đóng góp đã được xác minh.") { ExpectedVersion = participant.Version, LinkedContributionIds = proofs },
                    $"quest-complete-{club.Code}-{user.Id}");
            }
        }
        journal.Checkpoints["quests"] = "committed"; journal.Write(options.JournalPath);
    }
}
