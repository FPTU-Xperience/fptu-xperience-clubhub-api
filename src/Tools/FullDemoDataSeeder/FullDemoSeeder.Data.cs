using System.Security.Cryptography;
using System.Text.Json;
using ActivityService.Models;
using AuthService.Models;
using ClubReportHub.Shared.Experience;
using ClubService.Models;
using FinanceService.Models;
using Microsoft.EntityFrameworkCore;
using NotificationService.Models;
using ReportService.Models;

namespace FullDemoDataSeeder;

public sealed partial class FullDemoSeeder
{
    private ExperiencePolicyReference periodPolicy = null!;
    private static IReadOnlyList<PillarWeight> Weights(string category) => category switch
    {
        "ACADEMIC" => [new("Academic", .7m), new("Research", .3m)],
        "ARTS" => [new("CultureSports", .7m), new("Global", .3m)],
        "SPORTS" => [new("CultureSports", .8m), new("Community", .2m)],
        "TECHNOLOGY" => [new("Academic", .5m), new("Entrepreneurship", .5m)],
        "VOLUNTEER" => [new("Community", .7m), new("Global", .3m)],
        _ => [new("Entrepreneurship", .6m), new("Community", .4m)]
    };
    private static string[] Themes(string category) => category switch
    {
        "ACADEMIC" => ["Tư duy phản biện và nghiên cứu", "Ngày hội chia sẻ đề tài", "Nhóm học tập chuyên ngành", "Giao lưu học thuật liên trường"],
        "ARTS" => ["Kể chuyện bằng nhiếp ảnh", "Đêm nhạc kết nối sắc màu", "Xưởng sáng tạo cuối tuần", "Triển lãm hành trình tuổi trẻ"],
        "SPORTS" => ["Kỹ thuật và tinh thần đồng đội", "Giải đấu giao hữu mùa thu", "Thử thách vận động 21 ngày", "Ngày hội thể thao sinh viên"],
        "TECHNOLOGY" => ["Xây dựng ứng dụng AI đầu tiên", "Hackathon giải pháp xanh", "Phòng lab phát triển sản phẩm", "Gặp gỡ chuyên gia công nghệ"],
        "VOLUNTEER" => ["Cuối tuần xanh trong khuôn viên", "Chuyến đi sẻ chia tri thức", "Chiến dịch giảm rác nhựa", "Kết nối cộng đồng địa phương"],
        _ => ["Kỹ năng lập kế hoạch dự án", "Thử thách ý tưởng khởi nghiệp", "Câu chuyện giao lưu đa văn hóa", "Ngày hội định hướng nghề nghiệp"]
    };
    private string ActivityKey(Club club, int kind) => $"[Demo] {Themes(club.Category)[kind]} · {club.Code}";

    private async Task<List<User>> MembersAsync(Club club)
    {
        var ids = await clubs.ClubMemberships.AsNoTracking().Where(m => m.ClubId == club.Id && m.Status == "Approved")
            .Select(m => m.UserId).ToArrayAsync();
        return users.Values.Where(u => ids.Contains(u.Id) && u.Username.StartsWith("fulldemo.student")).OrderBy(u => u.Username).Take(9).ToList();
    }

    private async Task SeedActivitiesAsync()
    {
        for (var index = 0; index < anchors.Count; index++)
        {
            var club = anchors[index]; var manager = Manager(index); var members = await MembersAsync(club);
            clubStudents[club.Id] = members;
            if (index < 3 && club.CampusCode == protectedUser.CampusCode) members = [.. members, protectedUser];
            for (var kind = 0; kind < 4; kind++)
            {
                var title = ActivityKey(club, kind);
                if (await activities.Activities.AnyAsync(a => a.ClubId == club.Id && a.Title == title)) continue;
                var cancelled = kind == 3 && index % 4 == 3;
                var offset = kind switch { 0 => -28, 1 => -14, 2 => -1, _ => 7 + index % 4 };
                var start = options.At(offset, kind == 2 ? 18 : 9);
                var sessionCount = kind == 3 ? 1 : 2;
                var end = start.AddDays(sessionCount == 2 ? kind == 2 ? 2 : 7 : 0).AddHours(2);
                var weights = Weights(club.Category);
                var row = new ClubActivity { ClubId = club.Id, ClubName = club.Name, Title = title,
                    Description = $"{Themes(club.Category)[kind]} do {club.Name} tổ chức. Phần hướng dẫn, làm việc nhóm và tổng kết sản phẩm. [Dữ liệu mô phỏng FullDemo v2]",
                    Location = kind >= 2 ? "Trực tuyến · Phòng sinh hoạt ClubHub" : $"{club.CampusCode} · Không gian sinh hoạt mô phỏng",
                    StartTimeUtc = start, EndTimeUtc = end, SemesterId = semester.SemesterCode,
                    Status = cancelled ? "Cancelled" : kind < 2 ? "Completed" : "Scheduled",
                    PublicationStatus = cancelled ? "CANCELLED" : kind < 2 ? "CLOSED" : "PUBLISHED", IsInternal = kind % 2 == 0,
                    DiscoveryVisibility = kind % 2 == 0 ? "CLUB_ONLY" : "CAMPUS_PUBLIC",
                    Eligibility = kind % 2 == 0 ? "CLUB_MEMBER" : "CAMPUS_PUBLIC", Capacity = kind == 3 && index % 4 == 0 ? members.Count : 30 + index * 3,
                    RegisteredCount = members.Count, RegistrationOpensAt = start.AddDays(-14), RegistrationClosesAt = start.AddHours(-1),
                    EffortTier = kind == 1 ? index < 3 ? "T5" : index % 3 == 0 ? "T4" : "T3" : "T2",
                    Scale = kind == 1 ? index < 3 ? "National" : "Campus" : "Club",
                    PillarWeightsJson = JsonSerializer.Serialize(weights, Json), PolicyVersion = periodPolicy.PolicyVersion,
                    CoverImageUrl = options.AssetBaseUrl + $"/activities/e{index % 12 + 1:00}.png",
                    EvidenceRequirements = "Nộp ghi chú hoặc sản phẩm nhóm; minh chứng trong bộ demo đều là dữ liệu mô phỏng.",
                    CreatedByUserId = manager.Id, CreatedAtUtc = options.At(-40), UpdatedAtUtc = options.At(kind < 2 ? -5 : -1) };
                activities.Activities.Add(row); await journal.SaveAsync("Activity", activities);
                var attributes = new ContributionAttributes(row.EffortTier, "Participant", row.Scale, "None", weights);
                row.ScoringSnapshotJson = JsonSerializer.Serialize(new ExperienceSourceSnapshot(row.Id, club.Id, 0, semester.SemesterCode,
                    title, start, end, row.Version, Guid.Empty, cancelled ? "CANCELLED" : "SCHEDULED", true, attributes, periodPolicy), Json);
                foreach (var user in members)
                    activities.ActivityParticipants.Add(new() { ActivityId = row.Id, UserId = user.Id, FullName = user.FullName,
                        AttendanceStatus = kind < 2 ? "Present" : "Registered", RegisteredAtUtc = start.AddDays(-7) });
                for (var sessionIndex = 0; sessionIndex < sessionCount; sessionIndex++)
                {
                    var live = kind == 2 && sessionIndex == 1;
                    var time = live ? options.At(0, 0) : start.AddDays(sessionIndex == 0 ? 0 : 7);
                    var sessionEnd = live ? options.At(0, 23).AddMinutes(59) : time.AddHours(2);
                    var session = new ActivitySession { ActivityId = row.Id, SeriesId = FullDemoOptions.StableId($"series:{club.Code}:{kind}"),
                        SemesterId = semester.SemesterCode, StartTimeUtc = time, EndTimeUtc = sessionEnd, Location = row.Location,
                        Mode = kind >= 2 ? "ONLINE" : "ONSITE",
                        Latitude = kind >= 2 ? null : club.CampusCode == "HAN" ? 21d : club.CampusCode == "HCM" ? 10.8d : 16.1d,
                        Longitude = kind >= 2 ? null : club.CampusCode == "HAN" ? 105.8d : club.CampusCode == "HCM" ? 106.7d : 108.2d,
                        RadiusMeters = kind >= 2 ? null : 200d,
                        Status = cancelled ? "CANCELLED" : sessionEnd < DateTimeOffset.UtcNow ? "CLOSED" : "SCHEDULED",
                        OpensAt = time, ClosesAt = sessionEnd, ManualConfirmationEnabled = true,
                        ScoringSnapshotJson = row.ScoringSnapshotJson, CreatedByUserId = manager.Id, CreatedAtUtc = options.At(-40), UpdatedAtUtc = options.At(-1) };
                    activities.Sessions.Add(session); await journal.SaveAsync("Activity", activities);
                    if (time < DateTimeOffset.UtcNow && kind != 3 && !live)
                        foreach (var user in members.Where(u => u.Id == protectedUser.Id || members.IndexOf(u) < 8))
                            activities.ActivityAttendances.Add(new() { ActivityId = row.Id, SessionId = session.Id, UserId = user.Id, FullName = user.FullName,
                                AttendanceDate = DateOnly.FromDateTime(time.ToOffset(TimeSpan.FromHours(7)).DateTime),
                                Status = "Present", VerificationStatus = "UNVERIFIED", RecognitionStatus = "NOT_REQUESTED",
                                Note = "[Demo] Lịch sử tham dự mô phỏng; yêu cầu xác minh là tình huống tương tác riêng.",
                                CheckedInAtUtc = time.AddMinutes(5), CheckedInByUserId = manager.Id, CreatedAtUtc = time.AddMinutes(5), UpdatedAtUtc = time.AddHours(2) });
                }
                if (kind < 3)
                    activities.Set<ActivityResource>().Add(new() { Id = FullDemoOptions.StableId($"resource:{club.Code}:{kind}"), ActivityId = row.Id,
                        Kind = "LINK", Provider = "EXTERNAL", Title = kind == 0 ? "Cẩm nang tham gia và agenda" : "Tài liệu tổng kết hoạt động",
                        Visibility = kind == 1 ? "PUBLIC" : "CLUB", Url = options.AssetBaseUrl + $"/documents/guide-{index + 1:00}.pdf",
                        Position = 0, UploadedByUserId = manager.Id, CreatedAtUtc = options.At(-35), UpdatedAtUtc = options.At(-35) });
            }
        }
    }

    private async Task SeedReportsAsync()
    {
        foreach (var period in await admin.Semesters.AsNoTracking().ToArrayAsync())
            if (!await reports.ReportingDeadlines.AnyAsync(d => d.Period == period.SemesterCode))
                reports.ReportingDeadlines.Add(new() { Period = period.SemesterCode, IsActive = period.IsActive,
                    DueDate = DateOnly.FromDateTime(period.EndDate!.Value.DateTime).AddDays(7) });
        for (var index = 0; index < anchors.Count; index++)
        {
            var club = anchors[index]; var manager = Manager(index); var reviewer = Reviewer(club.CampusCode);
            for (var kind = 0; kind < (index < 4 ? 4 : 3); kind++)
            {
                var key = $"FULLDEMO_V2:{club.Code}:REPORT:{kind}";
                if (await reports.Reports.AnyAsync(r => r.SeedKey == key)) continue;
                var status = kind == 0 ? "Approved" : kind == 1 ? index % 3 == 0 ? "Rejected" : index % 3 == 1 ? "UnderReview" : "Submitted"
                    : kind == 3 ? index % 2 == 0 ? "Draft" : "AwaitingFinance"
                    : new[] { "Approved", "Rejected", "Submitted", "Draft" }[index % 4];
                var financial = kind == 2 && index % 2 == 1;
                var authorId = financial ? await clubs.ClubMemberships.AsNoTracking()
                    .Where(m => m.ClubId == club.Id && m.Role == "TREASURER" && m.Status == "Approved").Select(m => m.UserId).SingleAsync() : manager.Id;
                var author = financial ? users.Values.Single(u => u.Id == authorId) : manager;
                var reviewed = status is "Approved" or "Rejected";
                var createdAt = options.At(kind == 0 ? -40 : kind is 1 or 3 ? -12 : -6);
                var reviewedAt = options.At(kind == 0 ? -36 : -3);
                var historicalActivity = kind == 0 ? await activities.Activities.AsNoTracking().SingleAsync(a => a.ClubId == club.Id && a.Title == ActivityKey(club, 0)) : null;
                var row = new Report { SeedKey = key, ClubId = club.Id, ClubName = club.Name, Period = semester.SemesterCode,
                    ReportType = kind != 2 ? "FUTURE_EVENT" : financial ? "MONTHLY" : "CLUB_ACTIVITY",
                    Tag = kind != 2 ? "Future event" : financial ? "Financial report" : "Activity report", Status = status, CreatedByUserId = author.Id,
                    DueDate = options.ReferenceDate.AddDays(kind == 1 ? 14 : -2), CreatedAtUtc = createdAt, UpdatedAtUtc = reviewed ? reviewedAt : options.At(-2),
                    SubmittedAtUtc = status != "Draft" ? options.At(kind == 0 ? -38 : kind == 1 ? -10 : -5) : null,
                    ReviewedAtUtc = reviewed ? reviewedAt : null, ReviewedByUserId = reviewed ? reviewer.Id : null,
                    PublishedActivityId = historicalActivity?.Id, Version = kind + 1,
                    ExecutiveSummary = kind is 1 or 3 ? "[Demo] Kế hoạch tổ chức sự kiện sắp tới, có dự toán và phân công nhân sự để tiếp tục thao tác xét duyệt."
                        : $"[Demo] Tổng kết {Themes(club.Category)[kind]}: hoàn thành nội dung chuyên môn và tạo không gian trao đổi cho thành viên.",
                    Achievements = "Hoàn thành 2 buổi sinh hoạt, 8 sản phẩm nhóm; ghi nhận phản hồi tích cực về nội dung thực hành.",
                    Challenges = "Một số thành viên trùng lịch học; cần thêm thời gian cho phần hỏi đáp và chuẩn bị hậu cần.",
                    Recommendations = "Công bố tài liệu sớm, chia nhóm theo kỹ năng và khảo sát lịch trước khi mở đăng ký.",
                    NextPeriodPlan = "Mở buổi chia sẻ chuyên sâu, tuyển cộng tác viên và hoàn thiện bộ tài liệu hướng dẫn thành viên mới." };
                row.Details.Add(new() { ActivityName = Themes(club.Category)[kind], ActivityDate = kind == 0
                        ? DateOnly.FromDateTime(historicalActivity!.StartTimeUtc.ToOffset(TimeSpan.FromHours(7)).DateTime)
                        : options.ReferenceDate.AddDays(kind == 1 ? 13 : kind == 3 ? 18 : -14),
                    Description = "Hướng dẫn chuyên môn, làm việc nhóm và trình bày kết quả. Đây là hồ sơ mô phỏng cho buổi demo.",
                    Objective = "Phát triển năng lực hợp tác, kỹ năng chuyên môn và tinh thần đóng góp.", ParticipantCount = kind is 1 or 3 ? 0 : 8 + index,
                    TargetParticipantCount = 20, Outcome = kind is 1 or 3 ? "Chờ triển khai theo kế hoạch và ngân sách được duyệt." : "Có sản phẩm thực hành, biên bản tổng kết và kế hoạch cải tiến.",
                    Location = club.CampusCode + " · Nhà Alpha", ActivityType = kind != 2 ? "FutureEvent" : club.Category, BudgetSpent = kind is 1 or 3 ? null : 850000 + index * 50000,
                    EvidenceUrl = options.AssetBaseUrl + $"/documents/guide-{index + 1:00}.pdf", SortOrder = 1 });
                reports.Reports.Add(row); await journal.SaveAsync("Report", reports);
                var bytes = await File.ReadAllBytesAsync(Path.Combine(options.AssetDirectory, "documents", $"guide-{index + 1:00}.pdf"));
                var path = $"/app/report-uploads/full-demo-v2/report-{index + 1:00}-{kind}.pdf";
                reports.Set<ReportUploadedFile>().Add(new() { ReportId = row.Id, OriginalFileName = $"Bao-cao-{club.Code}-{kind + 1}.pdf",
                    StoredFileName = Path.GetFileName(path), ContentType = "application/pdf", FileExtension = ".pdf", SizeBytes = bytes.Length,
                    StoragePath = path, Checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), UploadedByUserId = author.Id,
                    UploadedAtUtc = createdAt, PreviewStatus = "Available", PreviewContentType = "application/pdf", PreviewStoragePath = path,
                    PreviewFileName = Path.GetFileName(path), PreviewGeneratedAtUtc = createdAt });
                reports.Set<ReportAttachment>().Add(new() { ReportId = row.Id, ReportDetailId = row.Details.Single().Id,
                    FileName = $"Bien-ban-{club.Code}.pdf", ContentType = "application/pdf", SizeBytes = bytes.Length,
                    StoragePath = $"/app/attachments/full-demo-v2/guide-{index + 1:00}-{kind}.pdf", UploadedAtUtc = createdAt });
                reports.AuditLogs.Add(new() { ReportId = row.Id, ActorUserId = author.Id, Action = "DemoFixtureCreated",
                    Description = "[FullDemo v2] Hồ sơ mẫu tĩnh; không đại diện xét duyệt thực tế.", CreatedAtUtc = createdAt });
                if (reviewed)
                    reports.ReportFeedback.Add(new() { ReportId = row.Id, ReviewerUserId = reviewer.Id, ReviewerName = reviewer.FullName,
                        Decision = status, Message = status == "Approved" ? "[Demo] Hồ sơ minh họa đã đầy đủ nội dung." : "[Demo] Bổ sung đánh giá đầu ra và phương án tài chính.", CreatedAtUtc = reviewedAt });
            }
        }
    }

    private async Task SeedReportActivityLinksAsync()
    {
        foreach (var club in anchors)
        {
            var report = await reports.Reports.AsNoTracking().Include(r => r.Details).SingleAsync(r => r.SeedKey == $"FULLDEMO_V2:{club.Code}:REPORT:0");
            var activity = await activities.Activities.SingleAsync(a => a.Id == report.PublishedActivityId);
            activity.SourceReportId = report.Id; activity.SourceReportDetailId = report.Details.Single().Id;
            activity.CreatedAtUtc = options.At(-36);
        }
    }

    private async Task SeedFinanceAsync()
    {
        for (var index = 0; index < anchors.Count; index++)
        {
            var club = anchors[index]; var manager = Manager(index); var reviewer = Reviewer(club.CampusCode);
            var treasurerId = await clubs.ClubMemberships.Where(m => m.ClubId == club.Id && m.Role == "TREASURER" && m.Status == "Approved").Select(m => m.UserId).SingleAsync();
            for (var kind = 0; kind < 2; kind++)
            {
                var title = $"[Demo] Dự toán {Themes(club.Category)[kind]} · {club.Code}";
                if (await finance.BudgetProposals.AnyAsync(b => b.ClubId == club.Id && b.Title == title)) continue;
                var source = await reports.Reports.AsNoTracking().SingleAsync(r => r.SeedKey == $"FULLDEMO_V2:{club.Code}:REPORT:{kind}");
                var status = kind == 0 ? index % 3 == 0 ? "Approved" : "Settled" : index % 3 == 0 ? "Rejected" : index % 3 == 1 ? "ManagerApproved" : "Submitted";
                var requested = 2000000m + index * 250000m;
                var proposal = new BudgetProposal { ClubId = club.Id, ClubName = club.Name, SourceReportId = source.Id, Title = title,
                    Description = "[Demo] Vật tư hoạt động, in tài liệu, nước uống và hỗ trợ hậu cần. Đính kèm biên bản mẫu có thể tải được.",
                    RequestedAmount = requested, ApprovedAmount = kind == 0 ? requested - 200000 : null,
                    ActivityId = kind == 0 ? source.PublishedActivityId : null,
                    ProposedByUserId = treasurerId, ProposedAtUtc = options.At(kind == 0 ? -39 : -3), Status = status,
                    ManagerReviewedByUserId = status != "Submitted" ? manager.Id : null, ManagerReviewedAtUtc = status != "Submitted" ? options.At(kind == 0 ? -38 : -2) : null,
                    ManagerReviewNote = status != "Submitted" ? "[Demo] Đã đối chiếu kế hoạch hoạt động." : null,
                    ReviewedByUserId = status is "Approved" or "Settled" or "Rejected" ? reviewer.Id : null,
                    ReviewedAtUtc = status is "Approved" or "Settled" or "Rejected" ? options.At(kind == 0 ? -37 : -1) : null,
                    ReviewNote = status == "Rejected" ? "[Demo] Cần chi tiết chi phí từng hạng mục." : status is "Approved" or "Settled" ? "[Demo] Phê duyệt dự toán minh họa." : null,
                    Version = status == "Settled" ? 4 : status == "Approved" ? 3 : 1 };
                finance.BudgetProposals.Add(proposal); await journal.SaveAsync("Finance", finance);
                if (kind == 0)
                {
                    var spent = requested - 350000;
                    finance.Settlements.Add(new() { BudgetProposalId = proposal.Id, TotalSpent = spent,
                        ReceiptUrl = options.AssetBaseUrl + $"/documents/guide-{index + 1:00}.pdf", Status = index % 3 == 0 ? "Submitted" : "Approved",
                        SubmittedAtUtc = options.At(-4), ReviewedByUserId = index % 3 == 0 ? null : reviewer.Id,
                        ReviewedAtUtc = index % 3 == 0 ? null : options.At(-2), ReviewNote = index % 3 == 0 ? null : "[Demo] Đã đối chiếu chứng từ mẫu." });
                    finance.FinanceTransactions.Add(new() { ClubId = club.Id, Amount = requested - 200000, Type = TransactionTypes.BudgetApproved,
                        ReferenceId = proposal.Id, Description = "[Demo] Phân bổ ngân sách minh họa.", TransactionDateUtc = options.At(-37) });
                    finance.FinanceTransactions.Add(new() { ClubId = club.Id, Amount = spent, Type = TransactionTypes.SettlementSubmitted,
                        ReferenceId = proposal.Id, Description = "[Demo] Chi phí hoạt động theo quyết toán mẫu.", TransactionDateUtc = options.At(-4) });
                    if (index % 3 != 0)
                        finance.FinanceTransactions.Add(new() { ClubId = club.Id, Amount = spent, Type = TransactionTypes.SettlementApproved,
                            ReferenceId = proposal.Id, Description = "[Demo] Phê duyệt quyết toán minh họa.", TransactionDateUtc = options.At(-2) });
                }
            }
        }
    }

    private async Task SeedFinanceReportLinksAsync()
    {
        var ids = anchors.Select(c => c.Id).ToArray();
        foreach (var proposal in await finance.BudgetProposals.AsNoTracking().Where(b => ids.Contains(b.ClubId)
            && b.Title.StartsWith("[Demo]") && b.SourceReportId != null).ToListAsync())
        {
            var report = await reports.Reports.SingleAsync(r => r.Id == proposal.SourceReportId && r.SeedKey != null && r.SeedKey.StartsWith("FULLDEMO_V2:"));
            if (proposal.Status == "Rejected")
            {
                report.BudgetProposalId = null; report.BudgetRequestedAmount = null; report.BudgetApprovedAmount = null;
                report.BudgetDescription = null; report.FinanceSubmittedAtUtc = null; report.Status = "Rejected";
            }
            else
            {
                report.BudgetProposalId = proposal.Id; report.BudgetRequestedAmount = proposal.RequestedAmount;
                report.BudgetApprovedAmount = proposal.ApprovedAmount; report.BudgetDescription = proposal.Description;
                report.FinanceSubmittedAtUtc = proposal.ProposedAtUtc;
                if (report.PublishedActivityId is null) report.Status = proposal.Status == "ManagerApproved" ? "UnderReview" : "Submitted";
            }
        }
    }

    private async Task SeedNotificationsAsync()
    {
        var recipients = users.Values.OrderBy(u => u.Username).Append(protectedUser).ToArray();
        for (var index = 0; index < recipients.Length; index++)
        {
            var user = recipients[index];
            var title = $"[Demo] {(index % 3 == 0 ? "Lịch sinh hoạt mới" : index % 3 == 1 ? "Hồ sơ đang chờ phản hồi" : "Tài liệu và cơ hội tham gia")}";
            if (await notifications.Notifications.AnyAsync(n => n.RecipientUserId == user.Id && n.Title == title)) continue;
            notifications.Notifications.Add(new() { RecipientUserId = user.Id, EventType = "FULLDEMO_V2",
                Title = title, Message = "Bộ dữ liệu mô phỏng đã sẵn sàng. Mở trang hoạt động, câu lạc bộ hoặc hồ sơ để xem thông tin chi tiết.",
                IsRead = index % 4 == 0, CreatedAtUtc = options.At(-(index % 7), 8 + index % 10) });
        }
    }
}
