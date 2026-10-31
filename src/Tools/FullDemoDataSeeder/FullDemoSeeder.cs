using System.Text.Json;
using ActivityService.Data;
using ActivityService.Models;
using AdminService.Data;
using AuthService.Data;
using AuthService.Models;
using ClubReportHub.Shared.Auth;
using ClubService.Data;
using ClubService.Models;
using ExportService.Data;
using FinanceService.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using ReportService.Data;

namespace FullDemoDataSeeder;

public sealed partial class FullDemoSeeder : IAsyncDisposable
{
    private readonly FullDemoOptions options;
    private DemoJournal journal;
    private readonly AuthDbContext auth;
    private readonly ClubDbContext clubs;
    private readonly ActivityDbContext activities;
    private readonly AdminDbContext admin;
    private readonly ReportDbContext reports;
    private readonly FinanceDbContext finance;
    private readonly NotificationDbContext notifications;
    private readonly ExportDbContext exports;
    private readonly Dictionary<string, DbContext> contexts;
    private readonly Dictionary<string, User> users = [];
    private readonly List<Club> anchors = [];
    private readonly Dictionary<int, List<User>> clubStudents = [];
    private User protectedUser = null!;
    private Semester semester = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Majors = ["Kỹ thuật phần mềm", "Trí tuệ nhân tạo", "An toàn thông tin", "Thiết kế mỹ thuật số", "Truyền thông đa phương tiện", "Kinh doanh quốc tế", "Digital Marketing", "Ngôn ngữ Anh", "Ngôn ngữ Nhật"];

    public FullDemoSeeder(FullDemoOptions options)
    {
        this.options = options;
        journal = DemoJournal.Load(options);
        DbContextOptions<T> Sql<T>(string service) where T : DbContext =>
            new DbContextOptionsBuilder<T>().UseSqlServer(options.Connections[service], sql => sql.CommandTimeout(90)).Options;
        auth = new(Sql<AuthDbContext>("Auth")); clubs = new(Sql<ClubDbContext>("Club"));
        activities = new(Sql<ActivityDbContext>("Activity")); admin = new(Sql<AdminDbContext>("Admin"));
        reports = new(Sql<ReportDbContext>("Report")); finance = new(Sql<FinanceDbContext>("Finance"));
        notifications = new(Sql<NotificationDbContext>("Notification")); exports = new(Sql<ExportDbContext>("Export"));
        contexts = new() { ["Auth"] = auth, ["Club"] = clubs, ["Activity"] = activities, ["Admin"] = admin,
            ["Report"] = reports, ["Finance"] = finance, ["Notification"] = notifications, ["Export"] = exports };
    }

    public async Task RunAsync(string command)
    {
        await using var runLock = command == "seed" ? AcquireSeedLock() : null;
        if (command == "seed") journal = DemoJournal.Load(options);
        // Preflight and plan are strictly SELECT-only: no upgraders, migrations,
        // EnsureCreated, outbox writes, HTTP calls or journal writes occur here.
        foreach (var (name, db) in contexts)
        {
            if (!await db.Database.CanConnectAsync()) throw new InvalidOperationException($"Cannot connect to {name}.");
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
            if (pending.Length != 0) throw new InvalidOperationException($"{name} requires migrations before seeding: {string.Join(',', pending)}");
        }
        var ownedUserIds = OwnedUserIds();
        var namespaceUsers = await auth.Users.AsNoTracking().Where(u => u.Username.StartsWith("fulldemo.") || u.Email.StartsWith("fulldemo.")).ToArrayAsync();
        if (namespaceUsers.Any(u => !ownedUserIds.Contains(u.Id)))
            throw new InvalidOperationException("Reserved demo identities exist without ownership receipts. Restore the original journal; existing accounts will not be adopted.");
        protectedUser = await auth.Users.Include(u => u.UserRoles).ThenInclude(r => r.Role)
            .SingleAsync(u => u.Email == options.ProtectedEmail);
        if (protectedUser.UserRoles.Count != 1 || protectedUser.UserRoles.Single().Role.Name != AuthRoles.ClubMember)
            throw new InvalidOperationException("Protected account must remain a student.");
        var identity = DemoJournal.Digest(JsonSerializer.Serialize(new { protectedUser.Id, protectedUser.Email, protectedUser.Username,
            protectedUser.FullName, protectedUser.GoogleSubject, protectedUser.PasswordHash, protectedUser.CampusCode,
            protectedUser.SecurityVersion, protectedUser.IsActive, protectedUser.IsLocked, Roles = protectedUser.UserRoles.Select(r => r.Role.Name).Order() }));
        if (journal.ProtectedIdentityDigest is not null && journal.ProtectedIdentityDigest != identity)
            throw new InvalidOperationException("Protected identity changed since the first run; investigate before resuming.");
        journal.ProtectedIdentityDigest = identity;
        var firstDay = options.At(0); var lastDay = options.At(21);
        semester = await admin.Semesters.AsNoTracking().SingleAsync(s => s.IsActive && s.StartDate <= firstDay && s.EndDate >= lastDay);
        if (semester.StartDate > options.At(-35)) throw new InvalidOperationException("Demo history must fit the active semester.");
        await SelectAnchorsAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { dataset = FullDemoOptions.Dataset, command, options.ReferenceDate,
            semester = semester.SemesterCode, clubs = anchors.Select(c => new { c.Id, c.Code, c.CampusCode }),
            targets = new { users = 120, memberships = 231, activities = 60, sessions = 84, participants = 444, attendances = 495,
                reports = 40, budgets = 24, settlements = 12, clubContributions = 102, realWorldContributions = 12, quests = 12, exports = 6 } }, Json));
        if (command == "plan") return;
        if (command == "verify") { await VerifyAsync(); return; }
        if (options.Password.Length is < 12 or > 128) throw new InvalidOperationException("Set FullDemo__Password to 12–128 characters for synthetic accounts only.");
        journal.Write(options.JournalPath);
        await PhaseAsync("users", "Auth", SeedUsersAsync);
        await LoadUsersAsync();
        await PhaseAsync("memberships", "Club", SeedClubsAsync);
        await EnsurePoliciesAsync();
        await PhaseAsync("activities", "Activity", SeedActivitiesAsync);
        await SeedApplicationsAsync();
        await SeedAcademicDataAsync();
        await SeedInvitationsAsync();
        await PhaseAsync("reports", "Report", SeedReportsAsync);
        await PhaseAsync("report-activity-links", "Activity", SeedReportActivityLinksAsync);
        await PhaseAsync("finance", "Finance", SeedFinanceAsync);
        await PhaseAsync("finance-report-links", "Report", SeedFinanceReportLinksAsync);
        await SeedCanonicalExperienceAsync();
        await SeedRealWorldExperienceAsync();
        await SeedAdjustmentExampleAsync();
        await SeedQuestsAsync();
        await PhaseAsync("notifications", "Notification", SeedNotificationsAsync);
        await CreateExportsAsync();
        await VerifyAsync();
        if (!journal.Checkpoints.ContainsKey("complete")) await CaptureReceiptsAsync();
        journal.Checkpoints["complete"] = "verified"; journal.Write(options.JournalPath);
    }

    private FileStream AcquireSeedLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.JournalPath)!);
        return new(options.JournalPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private HashSet<int> OwnedUserIds() => journal.Rows.Where(r => r.Service == "Auth" && r.Table == "Users" && r.BeforeJson is null)
        .Select(r => JsonSerializer.Deserialize<JsonElement>(r.KeyJson).GetProperty("Id").GetInt32()).ToHashSet();

    private async Task PhaseAsync(string phase, string service, Func<Task> seed)
    {
        if (journal.Checkpoints.GetValueOrDefault(phase) == "committed") { Console.WriteLine($"Preserving checkpoint {phase}."); return; }
        if (journal.Checkpoints.GetValueOrDefault(phase) == "prepared")
            throw new InvalidOperationException($"Checkpoint {phase} was interrupted at commit. Reconcile its row receipts against SQL before resuming; no automatic overwrite is permitted.");
        Console.WriteLine($"Seeding {phase}...");
        var db = contexts[service];
        await using var tx = await db.Database.BeginTransactionAsync();
        await seed();
        await journal.SaveAsync(service, db);
        journal.Checkpoints[phase] = "prepared"; journal.Write(options.JournalPath);
        await tx.CommitAsync();
        journal.Checkpoints[phase] = "committed"; journal.Write(options.JournalPath);
    }

    private async Task SelectAnchorsAsync()
    {
        if (journal.ClubIds.Count == 12)
            anchors.AddRange((await clubs.Clubs.Where(c => journal.ClubIds.Contains(c.Id)).ToListAsync()).OrderBy(c => journal.ClubIds.IndexOf(c.Id)));
        else
        {
            foreach (var (campus, count) in new[] { ("HAN", 8), ("HCM", 2), ("DAN", 2) })
            {
                var candidates = await clubs.Clubs.Where(c => c.CampusCode == campus && c.IsActive
                    && (c.Code.StartsWith("FU-") || c.Code.StartsWith("FPTU-") || c.Code.StartsWith("HCM-") || c.Code.StartsWith("DN-"))
                    && !c.ManagerAssignments.Any(m => m.IsActive) && !c.Memberships.Any()).OrderBy(c => c.Code).ToListAsync();
                // Cycle categories so the HAN showcase covers all six categories.
                var groups = candidates.GroupBy(c => c.Category).OrderBy(g => g.Key).Select(g => new Queue<Club>(g)).ToArray();
                var selected = new List<Club>();
                while (selected.Count < count && groups.Any(g => g.Count > 0))
                    foreach (var group in groups.Where(g => g.Count > 0)) { if (selected.Count == count) break; selected.Add(group.Dequeue()); }
                if (selected.Count != count) throw new InvalidOperationException($"Need {count} unmanaged empty catalog clubs in {campus}.");
                anchors.AddRange(selected);
            }
            journal.ClubIds = anchors.Select(c => c.Id).ToList();
        }
        if (anchors.Count != 12) throw new InvalidOperationException("Manifest anchor clubs are missing.");
    }

    private async Task SeedUsersAsync()
    {
        var roles = await auth.Roles.ToDictionaryAsync(r => r.Name);
        var hasher = new PasswordHasher<User>();
        string[] family = ["Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Vũ", "Đặng", "Bùi", "Đỗ", "Hồ"];
        string[] given = ["Minh Anh", "Quang Huy", "Thảo Linh", "Đức Duy", "Khánh Vy", "Gia Bảo", "Hà My", "Tuấn Kiệt", "Ngọc Mai", "Nhật Minh"];
        for (var index = 0; index < 120; index++)
        {
            var key = index < 100 ? $"student{index + 1:000}" : index < 112 ? $"manager{index - 99:00}" : index < 118 ? $"ctsv{index - 111:00}" : $"admin{index - 117:00}";
            var email = $"fulldemo.{key}@fpt.edu.vn";
            var existing = await auth.Users.SingleOrDefaultAsync(u => u.Email == email);
            if (existing is not null)
            {
                if (existing.Username != $"fulldemo.{key}" || !OwnedUserIds().Contains(existing.Id)) throw new InvalidOperationException($"Demo identity collision: {key}.");
                continue;
            }
            var campus = index < 60 ? "HAN" : index < 80 ? "HCM" : index < 100 ? "DAN" : index < 112
                ? anchors[index - 100].CampusCode : index < 118 ? new[] { "HAN", "HCM", "DAN" }[(index - 112) / 2] : "HAN";
            var role = index < 100 ? AuthRoles.ClubMember : index < 112 ? AuthRoles.ClubManager : index < 118 ? AuthRoles.StudentAffairsAdmin : AuthRoles.Admin;
            if (!roles.TryGetValue(role, out var assignedRole)) throw new InvalidOperationException($"Missing actor role {role}.");
            var user = new User { Username = $"fulldemo.{key}", Email = email, FullName = $"{family[index % 10]} {given[index / 10 % 10]} (Demo {index + 1:000})",
                CampusCode = campus, CreatedAtUtc = options.At(-75), IsActive = true, SecurityVersion = 1 };
            user.PasswordHash = hasher.HashPassword(user, options.Password);
            user.UserRoles.Add(new UserRole { Role = assignedRole }); auth.Users.Add(user);
            await journal.SaveAsync("Auth", auth);
            auth.Set<CanonicalUserProfile>().Add(new() { UserId = user.Id, DisplayName = user.FullName,
                About = "Hồ sơ mô phỏng phục vụ trình diễn ClubHub. Quan tâm hoạt động học thuật, văn hóa và cộng đồng.",
                Headline = $"{Majors[index % Majors.Length]} · {campus}", DeclaredMajor = Majors[index % Majors.Length],
                SkillsJson = JsonSerializer.Serialize(new[] { "Làm việc nhóm", index % 2 == 0 ? "Thuyết trình" : "Thiết kế", "Quản lý thời gian" }),
                InterestsJson = JsonSerializer.Serialize(new[] { "Công nghệ", "Âm nhạc", index % 2 == 0 ? "Tình nguyện" : "Thể thao" }),
                DateOfBirth = $"2005-{index % 12 + 1:00}-{index % 27 + 1:00}",
                AvatarUrl = options.AssetBaseUrl + $"/avatars/a{index % 12 + 1:00}.png", OnboardingVersion = 1,
                SyncTimetable = index < 10 || index is >= 60 and < 70 || index is >= 80 and < 90,
                OnboardingCompletedAtUtc = options.At(-74), UpdatedAtUtc = options.At(-74) });
        }
    }

    private async Task LoadUsersAsync()
    {
        users.Clear();
        var owned = OwnedUserIds().ToArray();
        foreach (var user in await auth.Users.Where(u => owned.Contains(u.Id) && u.Username.StartsWith("fulldemo.")).ToListAsync()) users[user.Username[9..]] = user;
    }

    private User Manager(int index) => users[$"manager{index + 1:00}"];
    private User Reviewer(string campus) => users[campus == "HAN" ? "ctsv01" : campus == "HCM" ? "ctsv03" : "ctsv05"];
    private List<User> Students(Club club, int index) => users.Values.Where(u => u.Username.StartsWith("fulldemo.student") && u.CampusCode == club.CampusCode)
        .OrderBy(u => u.Username).Skip(index * 5 % (club.CampusCode == "HAN" ? 45 : 5)).Take(16).ToList();

    private async Task SeedClubsAsync()
    {
        for (var index = 0; index < anchors.Count; index++)
        {
            var club = anchors[index]; var manager = Manager(index); var members = Students(club, index);
            if (index is 1 or 2 && club.CampusCode == "HAN") members[1] = users["student001"];
            if (members.Count != 16) throw new InvalidOperationException("Insufficient same-campus demo students.");
            if (!await clubs.ClubManagerAssignments.AnyAsync(m => m.ClubId == club.Id && m.IsActive))
                clubs.ClubManagerAssignments.Add(new() { ClubId = club.Id, ManagerUserId = manager.Id, ManagerName = manager.FullName, AssignedAtUtc = options.At(-70) });
            else if (!await clubs.ClubManagerAssignments.AnyAsync(m => m.ClubId == club.Id && m.ManagerUserId == manager.Id && m.IsActive))
                throw new InvalidOperationException("Anchor already has a different manager; refusing to replace them.");
            club.IsRecruiting = true;
            if (!club.Description.Contains("[Demo ClubHub]"))
                club.Description += "\n\n[Demo ClubHub] Sinh hoạt hằng tuần, dự án thực hành và hoạt động cộng đồng. Có lịch mở đăng ký, tài liệu và vị trí cộng tác viên để trải nghiệm.";
            club.ScheduleLabel = "Thứ Tư & Thứ Bảy · 18:00–20:00";
            club.LogoUrl = options.AssetBaseUrl + $"/clubs/c{index + 1:00}-logo.png";
            club.CoverImageUrl = options.AssetBaseUrl + $"/clubs/c{index + 1:00}-cover.png";
            var people = members.Append(manager).ToList();
            if (index < 3 && club.CampusCode == protectedUser.CampusCode) people.Add(protectedUser);
            for (var i = 0; i < people.Count; i++)
            {
                var person = people[i];
                if (await clubs.ClubMemberships.AnyAsync(m => m.ClubId == club.Id && m.UserId == person.Id)) continue;
                var status = i == 12 || i == 13 ? "Pending" : i == 14 ? "Rejected" : i == 15 ? "Withdrawn" : "Approved";
                clubs.ClubMemberships.Add(new() { ClubId = club.Id, UserId = person.Id, FullName = person.FullName, Email = person.Email,
                    Source = "APPLICATION", Status = status, Role = i == 0 ? "TREASURER" : "MEMBER", TreasurerSlot = i == 0 ? 1 : null,
                    PersonalInfo = "Dữ liệu mô phỏng cho buổi demo.", Goals = "Học hỏi và đóng góp cho câu lạc bộ.",
                    Reason = "Muốn tham gia dự án nhóm và hoạt động trải nghiệm.", Skills = "Làm việc nhóm, giao tiếp", Hobbies = "Sáng tạo, thể thao",
                    Expectations = "Tham gia ít nhất hai buổi mỗi tháng.", Contributions = "Hỗ trợ truyền thông và hậu cần.",
                    AcceptedClubRules = true, CommittedToParticipate = true, RequestMessage = "[FullDemo v2] Đơn tham gia trải nghiệm.",
                    RequestedAtUtc = options.At(-69), ReviewedAtUtc = status is "Approved" or "Rejected" ? options.At(-68) : null,
                    ReviewedByUserId = status is "Approved" or "Rejected" ? manager.Id : null,
                    ReviewNote = status == "Approved" ? "[Demo] Phê duyệt hồ sơ mẫu." : status == "Rejected" ? "[Demo] Chưa phù hợp lịch sinh hoạt." : null,
                    WithdrawnAtUtc = status == "Withdrawn" ? options.At(-60) : null, WithdrawnByUserId = status == "Withdrawn" ? person.Id : null });
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var db in contexts.Values) await db.DisposeAsync();
        client.Dispose();
    }
}
