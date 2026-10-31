using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Models;
using AdminService.Contracts;
using AdminService.Data;
using AuthService.Models;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace FullDemoDataSeeder;

public sealed partial class FullDemoSeeder
{
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly Dictionary<int, (string Token, DateTimeOffset At)> tokens = [];
    private async Task<string> TokenAsync(User actor)
    {
        if (!actor.Username.StartsWith("fulldemo.") || actor.Id == protectedUser.Id)
            throw new InvalidOperationException("The demo tool never impersonates an existing account.");
        if (tokens.TryGetValue(actor.Id, out var cached) && cached.At > DateTimeOffset.UtcNow.AddMinutes(-20)) return cached.Token;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var response = await client.PostAsJsonAsync(options.ApiBaseUrl + "/api/auth/email-login", new { actor.Email, options.Password });
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Console.WriteLine("Login rate limit respected; waiting 30 seconds before retrying.");
                await Task.Delay(TimeSpan.FromSeconds(30)); continue;
            }
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Demo email login failed for {actor.Username}: {(int)response.StatusCode}.");
            var data = await response.Content.ReadFromJsonAsync<JsonElement>();
            var token = data.GetProperty("accessToken").GetString()!;
            tokens[actor.Id] = (token, DateTimeOffset.UtcNow); return token;
        }
        throw new InvalidOperationException("Demo login rate limit did not clear.");
    }

    private async Task<JsonElement> CallAsync(string method, string path, User actor, object? body = null, string? key = null)
    {
        var receiptKey = key is null ? null : "api:" + key;
        if (receiptKey is not null && journal.Checkpoints.TryGetValue(receiptKey, out var receipt))
            return JsonSerializer.Deserialize<JsonElement>(receipt);
        var concurrencyRetries = 0;
        for (var attempt = 0; attempt < 20; attempt++)
        {
        using var request = new HttpRequestMessage(new HttpMethod(method), options.ApiBaseUrl + path);
        request.Headers.Authorization = new("Bearer", await TokenAsync(actor));
        if (key is not null) request.Headers.Add("Idempotency-Key", FullDemoOptions.StableId(key).ToString());
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            Console.WriteLine("API rate limit respected; waiting 30 seconds before retrying.");
            await Task.Delay(TimeSpan.FromSeconds(30)); continue;
        }
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == HttpStatusCode.Conflict && concurrencyRetries < 4
            && text.Contains("\"code\":\"CONCURRENT_UPDATE\"", StringComparison.Ordinal))
        {
            // Retry the identical request after an infrastructure race. Never
            // reload or replace an expected version supplied by the operator.
            concurrencyRetries++;
            await Task.Delay(TimeSpan.FromMilliseconds(200 * (1 << concurrencyRetries))); continue;
        }
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{method} {path} ({actor.Username}) returned {(int)response.StatusCode}: {text[..Math.Min(1500, text.Length)]}");
        var result = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        if (receiptKey is not null) { journal.Checkpoints[receiptKey] = result.GetRawText(); journal.Write(options.JournalPath); }
        return result;
        }
        throw new InvalidOperationException("API rate limit did not clear: " + path);
    }

    private async Task EnsurePoliciesAsync()
    {
        if (journal.Checkpoints.TryGetValue("period-policy", out var stored))
        {
            periodPolicy = JsonSerializer.Deserialize<ExperiencePolicyReference>(stored, Json)!;
            if (!await admin.ApprovedExperiencePolicies.AnyAsync(p => p.Id == periodPolicy.PolicyVersion)) throw new InvalidOperationException("Recorded demo policy is missing.");
            return;
        }
        var actor = users["admin01"];
        foreach (var active in await admin.SemesterBenchmarkConfigs.AsNoTracking().Where(c => c.IsActive).ToArrayAsync())
            journal.Observe("Admin", admin.Entry(active), false);
        journal.Write(options.JournalPath);
        var config = await admin.SemesterBenchmarkConfigs.AsNoTracking().SingleOrDefaultAsync(c => c.SemesterCode == semester.SemesterCode);
        if (config is not null) { journal.Observe("Admin", admin.Entry(config), false); journal.Write(options.JournalPath); }
        if (config is null)
        {
            await CallAsync("POST", "/api/v1/student-affairs/benchmarks/", actor,
                new CreateBenchmarkConfigRequest(SemesterCode: semester.SemesterCode, AcademicYear: semester.AcademicYear, IsActive: true), "benchmark-create");
            config = await admin.SemesterBenchmarkConfigs.AsNoTracking().SingleAsync(c => c.SemesterCode == semester.SemesterCode);
        }
        foreach (var scope in new[] { "PERIOD", "CUMULATIVE" })
        {
            var existing = await admin.ApprovedExperiencePolicies.AsNoTracking().Where(p => p.SemesterId == semester.SemesterCode
                && p.Environment == "PROJECT" && p.Scope == scope).OrderByDescending(p => p.ApprovedAt).FirstOrDefaultAsync();
            if (existing is null)
            {
                config = await admin.SemesterBenchmarkConfigs.AsNoTracking().SingleAsync(c => c.Id == config.Id);
                if (config.ApprovalStatus == "APPROVED")
                {
                    if (config.ApprovalBasis?.StartsWith("[FullDemo v2]") != true) throw new InvalidOperationException("Refusing to reopen a policy approved outside this demo.");
                    await CallAsync("POST", $"/api/v1/student-affairs/benchmarks/{config.Id}/draft", actor,
                        new { expectedVersion = config.Version, reason = "[FullDemo v2] Tạo thêm snapshot cho phạm vi tích lũy; giữ nguyên snapshot học kỳ." }, "policy-draft-" + scope);
                    config = await admin.SemesterBenchmarkConfigs.AsNoTracking().SingleAsync(c => c.Id == config.Id);
                }
                await CallAsync("POST", $"/api/v1/student-affairs/benchmarks/{config.Id}/approval", actor,
                    new ApproveExperiencePolicyRequest(config.Version, "PROJECT",
                        "[FullDemo v2] Chính sách minh họa theo tham số benchmark hiện có; dùng cho dữ liệu mô phỏng theo kế hoạch demo đã được chấp thuận.", true, Scope: scope), "policy-approve-" + scope);
                existing = await admin.ApprovedExperiencePolicies.AsNoTracking().Where(p => p.BenchmarkId == config.Id && p.Scope == scope)
                    .OrderByDescending(p => p.ApprovedAt).FirstAsync();
            }
            if (scope == "PERIOD") periodPolicy = JsonSerializer.Deserialize<ApprovedExperiencePolicySnapshot>(existing.SnapshotJson, Json)!.Policy;
        }
        journal.Checkpoints["period-policy"] = JsonSerializer.Serialize(periodPolicy, Json); journal.Write(options.JournalPath);
    }

    private async Task SeedApplicationsAsync()
    {
        if (journal.Checkpoints.GetValueOrDefault("applications") == "committed") return;
        for (var index = 0; index < anchors.Count; index++)
        {
            var club = anchors[index]; var manager = Manager(index); var reviewer = Reviewer(club.CampusCode);
            for (var scenario = 0; scenario < 2; scenario++)
            {
                var title = $"[Demo] {(scenario == 0 ? "Ngày hội trải nghiệm và tuyển cộng tác viên" : "Đề án chuyên đề đang xét duyệt")} · {club.Code}";
                var actions = new[] {
                    new ActivityActionInput("host", "Điều phối nội dung và dẫn chương trình", 60, "Organizer", "ACTIVITY", null, 5, 1, 1, 5, "WORK_REVIEW", "FULL_ONLY", "ADDITIVE"),
                    new ActivityActionInput("media", "Thiết kế và truyền thông", 25, "Contributor", "ACTIVITY", null, 10, 1, 1, 10, "WORK_REVIEW", "FULL_ONLY", "ADDITIVE"),
                    new ActivityActionInput("support", "Hỗ trợ hậu cần và đón tiếp", 10, "Participant", "ACTIVITY", null, 15, 1, 1, 15, "ATTENDANCE", "FULL_ONLY", "ADDITIVE") };
                var input = new ActivityApplicationInput(club.Id, semester.SemesterCode, title,
                    "[Demo] Hoạt động thực hành có vị trí điều phối, truyền thông và hậu cần. Chấm điểm hành động giữ đúng trạng thái chờ chính sách của sản phẩm.",
                    "Kết nối thành viên mới và phát triển kỹ năng tổ chức sự kiện.", club.CampusCode + " · Sảnh Innovation",
                    options.At(7 + index, 14), options.At(7 + index, 17), "Campus", true, actions,
                    new(index % 3 == 0 ? "PRIVATE" : "PUBLIC", index % 3 == 0 ? "CLUB_MEMBERS" : "SCHOOL_STUDENTS",
                        [new("host", 3), new("media", 6), new("support", 10)]), IsInternal: index % 3 == 0);
                var created = await CallAsync("POST", "/api/activity-applications", manager, input, $"app-{index}-{scenario}-create");
                var id = created.GetProperty("id").GetInt32();
                if (scenario == 1 && index % 4 == 0) continue;
                await CallAsync("POST", $"/api/activity-applications/{id}/submit", manager, new { expectedVersion = 1 }, $"app-{index}-{scenario}-submit");
                if (scenario == 1 && index % 4 == 1) continue;
                var decision = scenario == 0 ? "APPROVED" : index % 4 == 2 ? "REVISION_REQUESTED" : "REJECTED";
                var reviewed = await CallAsync("POST", $"/api/activity-applications/{id}/review", reviewer,
                    new { expectedVersion = 2, decision, reason = "[Demo] Kết quả xét duyệt đề án mô phỏng: đối chiếu kế hoạch, nguồn lực và lịch tổ chức." }, $"app-{index}-{scenario}-review");
                if (scenario != 0) continue;
                var activityId = reviewed.GetProperty("activityId").GetInt32();
                await CallAsync("PATCH", $"/api/activities/{activityId}/publication", manager,
                    new { isPublished = true, expectedVersion = 1 }, $"app-{index}-publish");
                var execution = await activities.Set<ActivityExecution>().AsNoTracking().SingleAsync(e => e.ActivityId == activityId);
                await CallAsync("PATCH", $"/api/activities/{activityId}/recruitment/status", manager,
                    new { expectedVersion = execution.Version, status = "OPEN" }, $"app-{index}-recruitment-open");
                var rules = await activities.Set<ActivityActionRule>().AsNoTracking().Where(r => r.ActivityId == activityId).OrderBy(r => r.Id).ToArrayAsync();
                var members = await MembersAsync(club);
                for (var member = 0; member < 5; member++)
                {
                    execution = await activities.Set<ActivityExecution>().AsNoTracking().SingleAsync(e => e.ActivityId == activityId);
                    await CallAsync("POST", $"/api/activities/{activityId}/assignments", manager,
                        new { expectedVersion = execution.Version, userId = members[member].Id, actionRuleId = rules[member % 3].Id,
                            assignmentKind = member == 0 ? "HOST" : "STAFF" }, $"app-{index}-assignment-{member}");
                }
            }
        }
        journal.Checkpoints["applications"] = "committed"; journal.Write(options.JournalPath);
    }

    private async Task<Guid> EvidenceAsync(User author, string key, int document)
    {
        var cacheKey = "evidence:" + key;
        if (journal.Checkpoints.TryGetValue(cacheKey, out var stored)) return Guid.Parse(stored);
        for (var attempt = 0; attempt < 20; attempt++)
        {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(options.AssetDirectory, "documents", $"guide-{document:00}.pdf")));
        file.Headers.ContentType = new("application/pdf"); form.Add(file, "file", $"Minh-chung-demo-{key}.pdf");
        using var request = new HttpRequestMessage(HttpMethod.Post, options.ApiBaseUrl + "/api/contributions/evidence") { Content = form };
        request.Headers.Authorization = new("Bearer", await TokenAsync(author));
        request.Headers.Add("Idempotency-Key", FullDemoOptions.StableId("evidence:" + key).ToString());
        using var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            Console.WriteLine("Evidence upload rate limit respected; waiting 30 seconds before retrying.");
            await Task.Delay(TimeSpan.FromSeconds(30)); continue;
        }
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Evidence upload failed: {(int)response.StatusCode} {text}");
        var id = JsonSerializer.Deserialize<JsonElement>(text).GetProperty("evidenceId").GetGuid();
        journal.Checkpoints[cacheKey] = id.ToString(); journal.Write(options.JournalPath); return id;
        }
        throw new InvalidOperationException("Evidence upload rate limit did not clear.");
    }

    private async Task SeedCanonicalExperienceAsync()
    {
        if (journal.Checkpoints.GetValueOrDefault("experience") == "committed") return;
        for (var index = 0; index < anchors.Count; index++)
        {
            var club = anchors[index]; var manager = Manager(index);
            var grant = await admin.VerificationAssignments.AsNoTracking().AnyAsync(g => g.UserId == manager.Id && g.ClubId == club.Id && !g.IsRevoked);
            if (!grant) await CallAsync("POST", "/api/verification-assignments/", users["admin01"],
                new CreateVerificationAssignmentRequest(manager.Id, "CLUB", club.Id,
                    ["contribution.review", "attendance.verify"], options.At(-80), semester.EndDate!.Value.AddDays(30),
                    "[FullDemo v2] Phân quyền xác minh cho người quản lý tài khoản mô phỏng."), $"grant-{index}");
            var members = await MembersAsync(club);
            for (var kind = 0; kind < 2; kind++)
            {
                var title = ActivityKey(club, kind);
                var activity = await activities.Activities.AsNoTracking().SingleAsync(a => a.ClubId == club.Id && a.Title == title);
                var session = await activities.Sessions.AsNoTracking().Where(s => s.ActivityId == activity.Id).OrderBy(s => s.StartTimeUtc).FirstAsync();
                for (var authorIndex = 0; authorIndex < 4; authorIndex++)
                {
                    var key = $"contribution-{index}-{kind}-{authorIndex}"; var author = members[authorIndex];
                    var evidence = await EvidenceAsync(author, key, index + 1);
                    var attributes = new ContributionAttributes(activity.EffortTier, author.Username == "fulldemo.student001" && kind == 1 ? "Leader" : new[] { "Participant", "Contributor", "Product", "Leader" }[authorIndex],
                        activity.Scale, authorIndex == 2 ? "RealWorldOutput" : "None", Weights(club.Category));
                    var submitted = await CallAsync("POST", $"/api/activities/{activity.Id}/contributions/", author,
                        new SubmitClubContributionRequest(session.Id, "PARTICIPATION", attributes,
                            "[Demo] Đã tham gia buổi hoạt động, hoàn thiện phần việc nhóm và đính kèm minh chứng mô phỏng.", [evidence], activity.Version), key + "-submit");
                    var scenario = kind * 4 + authorIndex;
                    if (scenario == 5) continue;
                    var decision = scenario < 5 ? "APPROVE" : scenario == 6 ? "REQUEST_REVISION" : "REJECT";
                    await CallAsync("POST", $"/api/contributions/{submitted.GetProperty("id").GetGuid()}/reviews", manager,
                        new ReviewContributionRequest(decision, "[Demo] Đối chiếu minh chứng và phần việc theo kịch bản trình diễn.",
                            submitted.GetProperty("version").GetGuid(), decision == "APPROVE" ? attributes : null), key + "-review");
                }
                // The existing student's identity and password remain untouched.
                // Only an assigned manager verifies their seeded attendance via
                // the same endpoint used by the UI; the worker owns ledger effects.
                if (index < 3 && club.CampusCode == protectedUser.CampusCode)
                {
                    var attendance = await activities.ActivityAttendances.AsNoTracking().SingleAsync(a => a.SessionId == session.Id && a.UserId == protectedUser.Id);
                    if (attendance.VerificationStatus != "VERIFIED")
                    {
                        var membership = await clubs.ClubMemberships.AsNoTracking().SingleAsync(m => m.ClubId == club.Id && m.UserId == protectedUser.Id);
                        await CallAsync("PUT", $"/api/clubs/{club.Id}/activities/{activity.Id}/attendance", manager,
                            new BulkUpdateAttendanceRequest([new(membership.Id, "Present", "[Demo] Xác minh bản ghi tham dự mô phỏng.", attendance.Version)],
                                session.Id, session.Version, "[FullDemo v2] Xác minh dữ liệu demo qua luồng chính thức."), $"attendance-main-{index}-{kind}");
                    }
                }
            }
        }
        journal.Checkpoints["experience"] = "committed"; journal.Write(options.JournalPath);
    }
}
