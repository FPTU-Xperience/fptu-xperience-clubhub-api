using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Messaging;
using ClubService.Models;
using FinanceService.Models;
using ReportService.Models;
using ReportService.Services;

namespace DemoDataSeeder;

public sealed record DemoUserSpec(
    string Key,
    string Username,
    string FullName,
    string Role,
    DateOnly DateOfBirth,
    string Gender,
    string PhoneNumber,
    string Address,
    string PersonalInfo,
    string Goals,
    string Hobbies,
    string Skills,
    string Expectations,
    string Contributions);

public sealed record DemoClubSpec(
    string Code,
    string Name,
    string Category,
    string Description,
    string ContactEmail,
    string ContactPhone,
    string ManagerUserKey);

public sealed record DemoMembershipSpec(
    string ClubCode,
    string UserKey,
    string ClubRole,
    string Status,
    string RequestMessage,
    string? ReviewNote,
    int RequestedDaysBeforeReference);

public sealed record DemoClubApplicationSpec(
    string Code,
    string Name,
    string Category,
    string RequesterUserKey,
    string Status,
    string? CreatedClubCode,
    string? ReviewNote);

public sealed record DemoAttendanceSpec(
    string UserKey,
    DateOnly Date,
    string Status,
    string? Note = null);

public sealed record DemoActivitySpec(
    string Key,
    string ClubCode,
    string Title,
    string Description,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset EndTimeUtc,
    IReadOnlyCollection<int> MeetingDays,
    string Location,
    string Status,
    string CreatedByUserKey,
    IReadOnlyCollection<string> ParticipantUserKeys,
    IReadOnlyCollection<DemoAttendanceSpec> Attendances,
    string? SourceReportKey = null,
    int? SourceReportDetailSortOrder = null);

public sealed record DemoReportDetailSpec(
    string ActivityName,
    DateOnly ActivityDate,
    string Description,
    int ParticipantCount,
    string Outcome,
    string ActivityType,
    string Location,
    string? PartnerUnit,
    string Objective,
    int? TargetParticipantCount,
    decimal? BudgetSpent,
    int SortOrder);

public sealed record DemoFeedbackSpec(
    string ReviewerUserKey,
    string Decision,
    string Message,
    int DaysAfterCreation);

public sealed record DemoReportSpec(
    string Key,
    string ClubCode,
    string Period,
    string ReportType,
    string Tag,
    string Status,
    string CreatedByUserKey,
    DateOnly DueDate,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? ReviewedAtUtc,
    string? ReviewedByUserKey,
    string ExecutiveSummary,
    string Achievements,
    string Challenges,
    string Recommendations,
    string NextPeriodPlan,
    IReadOnlyCollection<DemoReportDetailSpec> Details,
    IReadOnlyCollection<DemoFeedbackSpec> Feedback);

public sealed record DemoSettlementSpec(
    decimal TotalSpent,
    string ReceiptUrl,
    string Status,
    DateTimeOffset SubmittedAtUtc,
    string? ReviewedByUserKey,
    DateTimeOffset? ReviewedAtUtc,
    string? ReviewNote);

public sealed record DemoBudgetProposalSpec(
    string Key,
    string ClubCode,
    string Title,
    string Description,
    decimal RequestedAmount,
    decimal? ApprovedAmount,
    string Status,
    string ProposedByUserKey,
    DateTimeOffset ProposedAtUtc,
    string? ManagerReviewedByUserKey,
    DateTimeOffset? ManagerReviewedAtUtc,
    string? ManagerReviewNote,
    string? ReviewedByUserKey,
    DateTimeOffset? ReviewedAtUtc,
    string? ReviewNote,
    string? SourceReportKey,
    string? ActivityKey,
    DemoSettlementSpec? Settlement);

public sealed record DemoNotificationSpec(
    string? RecipientUserKey,
    string? RecipientRole,
    string EventType,
    string Title,
    string Message,
    bool IsRead,
    DateTimeOffset CreatedAtUtc);

public sealed class DemoCatalog
{
    private DemoCatalog() { }

    public required DateOnly ReferenceDate { get; init; }
    public required IReadOnlyList<DemoUserSpec> Users { get; init; }
    public required IReadOnlyList<DemoClubSpec> Clubs { get; init; }
    public required IReadOnlyList<DemoMembershipSpec> Memberships { get; init; }
    public required IReadOnlyList<DemoClubApplicationSpec> ClubApplications { get; init; }
    public required IReadOnlyList<DemoActivitySpec> Activities { get; init; }
    public required IReadOnlyList<DemoReportSpec> Reports { get; init; }
    public required IReadOnlyList<DemoBudgetProposalSpec> BudgetProposals { get; init; }
    public required IReadOnlyList<DemoNotificationSpec> Notifications { get; init; }

    public static DemoCatalog Build(
        DateOnly referenceDate,
        DemoIdentityOverrides? identityOverrides = null)
    {
        var configuredIdentities = identityOverrides ?? DemoIdentityOverrides.Default;
        var year = referenceDate.Year;
        var summerPeriod = $"SUMMER{year}";
        var fallPeriod = $"FALL{year}";
        var springPeriod = $"SPRING{year + 1}";

        DateTimeOffset At(DateOnly date, int hour, int minute = 0) =>
            new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(7))
                .ToUniversalTime();

        DemoAttendanceSpec Attendance(string user, DateOnly date, string status, string? note = null) =>
            new(user, date, status, note);

        var users = new List<DemoUserSpec>
        {
            new("admin", "admin@fpt.edu.vn", "Nguyễn Thu Hà", AuthRoles.Admin,
                new DateOnly(1990, 3, 18), ClubGenders.Female, "0901000001", "Quận Cầu Giấy, Hà Nội",
                "Chuyên viên phụ trách hệ thống câu lạc bộ sinh viên.", "Vận hành dữ liệu minh bạch và đúng quy trình.",
                "Đọc sách, chạy bộ", "Quản trị hệ thống, kiểm soát quy trình", "Các CLB báo cáo đúng hạn.",
                "Phê duyệt và hỗ trợ xử lý các vướng mắc liên CLB."),

            new("manager-tech", "manager.tech@fpt.edu.vn", "Trần Minh Quân", AuthRoles.ClubManager,
                new DateOnly(1998, 8, 12), ClubGenders.Male, "0902000001", "Quận Nam Từ Liêm, Hà Nội",
                "Cựu sinh viên ngành Kỹ thuật phần mềm, phụ trách định hướng chuyên môn.", "Xây dựng cộng đồng lập trình thực chiến.",
                "Lập trình, bóng đá", "C#, DevOps, quản lý dự án", "Thành viên chủ động chia sẻ kiến thức.",
                "Kết nối mentor và doanh nghiệp cho các hoạt động chuyên môn."),
            new("manager-ai", "manager.ai@fpt.edu.vn", "Lê Khánh Linh", AuthRoles.ClubManager,
                new DateOnly(1997, 11, 3), ClubGenders.Female, "0902000002", "Quận Thanh Xuân, Hà Nội",
                "Kỹ sư dữ liệu, mentor các dự án AI dành cho sinh viên.", "Đưa AI vào các bài toán thực tế trong trường.",
                "Nghiên cứu, nhiếp ảnh", "Python, machine learning, thuyết trình", "Dự án có dữ liệu và tiêu chí đo lường rõ ràng.",
                "Xây dựng chương trình học và phản biện sản phẩm cho thành viên."),
            new("manager-vol", "manager.volunteer@fpt.edu.vn", "Phạm Hoàng Nam", AuthRoles.ClubManager,
                new DateOnly(1996, 5, 27), ClubGenders.Male, "0902000003", "Quận Hà Đông, Hà Nội",
                "Điều phối viên hoạt động cộng đồng và phát triển bền vững.", "Tạo hoạt động thiện nguyện an toàn, có tác động đo được.",
                "Du lịch, công tác xã hội", "Điều phối sự kiện, làm việc đối tác", "Mọi chương trình có kế hoạch và báo cáo tác động.",
                "Kết nối địa phương, nhà tài trợ và đội ngũ tình nguyện viên."),

            new("tech-treasurer", "se170001@fpt.edu.vn", "Nguyễn Hải Đăng", AuthRoles.ClubMember,
                new DateOnly(2004, 2, 15), ClubGenders.Male, "0911000001", "Ký túc xá Hòa Lạc, Hà Nội",
                "Sinh viên Kỹ thuật phần mềm khóa 17.", "Rèn kỹ năng quản lý tài chính và tổ chức sự kiện công nghệ.",
                "Backend, cờ vua", "C#, SQL, lập dự toán", "Được tham gia dự án có mentor.",
                "Theo dõi ngân sách và minh bạch chứng từ cho CLB."),
            new("tech-02", "se170002@fpt.edu.vn", "Trần Gia Hân", AuthRoles.ClubMember,
                new DateOnly(2004, 7, 21), ClubGenders.Female, "0911000002", "Thạch Thất, Hà Nội",
                "Sinh viên yêu thích phát triển sản phẩm web.", "Hoàn thiện một sản phẩm có người dùng thật.",
                "UI/UX, âm nhạc", "React, Figma, làm việc nhóm", "Có workshop đều đặn và bài tập thực hành.",
                "Thiết kế giao diện và hỗ trợ truyền thông sự kiện."),
            new("tech-03", "se170003@fpt.edu.vn", "Lê Đức Anh", AuthRoles.ClubMember,
                new DateOnly(2003, 12, 9), ClubGenders.Male, "0911000003", "Quốc Oai, Hà Nội",
                "Sinh viên định hướng nền tảng dữ liệu và cloud.", "Nâng cao kỹ năng triển khai hệ thống.",
                "Cloud, cầu lông", "Docker, Linux, Python", "Được làm dự án liên CLB.",
                "Hỗ trợ hạ tầng workshop và chia sẻ kiến thức cloud."),
            new("tech-04", "se170004@fpt.edu.vn", "Phạm Ngọc Mai", AuthRoles.ClubMember,
                new DateOnly(2004, 9, 30), ClubGenders.Female, "0911000004", "Sơn Tây, Hà Nội",
                "Sinh viên năm hai đang tìm môi trường học lập trình.", "Củng cố nền tảng và tham gia dự án đầu tiên.",
                "Đọc truyện, thiết kế", "HTML, CSS cơ bản", "Được hướng dẫn lộ trình phù hợp.",
                "Sẵn sàng hỗ trợ hậu cần và nội dung truyền thông."),

            new("ai-treasurer", "ai170001@fpt.edu.vn", "Võ Minh Khang", AuthRoles.ClubMember,
                new DateOnly(2004, 1, 8), ClubGenders.Male, "0912000001", "Ký túc xá Hòa Lạc, Hà Nội",
                "Sinh viên chuyên ngành Trí tuệ nhân tạo.", "Quản lý tốt ngân sách cho các cuộc thi dữ liệu.",
                "Kaggle, bóng rổ", "Python, phân tích dữ liệu, Excel", "Có dữ liệu thật để thực hành.",
                "Lập ngân sách, tổng hợp hóa đơn và theo dõi chi phí."),
            new("ai-02", "ai170002@fpt.edu.vn", "Nguyễn Thảo Vy", AuthRoles.ClubMember,
                new DateOnly(2004, 6, 17), ClubGenders.Female, "0912000002", "Ba Vì, Hà Nội",
                "Sinh viên quan tâm NLP và ứng dụng AI vì cộng đồng.", "Xây dựng chatbot hỗ trợ sinh viên.",
                "Viết lách, thiện nguyện", "Python, NLP, truyền thông", "Được phản biện sản phẩm định kỳ.",
                "Phụ trách nội dung và kết nối hoạt động AI với CLB thiện nguyện."),
            new("ai-03", "ai170003@fpt.edu.vn", "Đỗ Quốc Bảo", AuthRoles.ClubMember,
                new DateOnly(2003, 10, 5), ClubGenders.Male, "0912000003", "Đan Phượng, Hà Nội",
                "Sinh viên theo hướng computer vision.", "Hoàn thiện portfolio dự án AI.",
                "Nhiếp ảnh, robotics", "PyTorch, xử lý ảnh", "Có nhóm nghiên cứu nhỏ theo chủ đề.",
                "Hỗ trợ kỹ thuật cho các buổi lab và cuộc thi."),
            new("ai-04", "ai170004@fpt.edu.vn", "Bùi Khánh An", AuthRoles.ClubMember,
                new DateOnly(2004, 4, 24), ClubGenders.Female, "0912000004", "Hoài Đức, Hà Nội",
                "Sinh viên mới tìm hiểu khoa học dữ liệu.", "Học quy trình làm dự án dữ liệu từ đầu.",
                "Nhiếp ảnh, đọc sách", "Excel, Python cơ bản", "Có tài liệu nhập môn và người hướng dẫn.",
                "Hỗ trợ ghi hình và tổng hợp tài liệu sau sự kiện."),

            new("vol-treasurer", "ss170001@fpt.edu.vn", "Trương Hoài Phương", AuthRoles.ClubMember,
                new DateOnly(2004, 3, 11), ClubGenders.Female, "0913000001", "Ký túc xá Hòa Lạc, Hà Nội",
                "Sinh viên Quản trị kinh doanh yêu thích hoạt động xã hội.", "Quản lý nguồn quỹ thiện nguyện minh bạch.",
                "Gây quỹ, nấu ăn", "Lập ngân sách, giao tiếp đối tác", "Các khoản thu chi có chứng từ rõ ràng.",
                "Theo dõi quỹ, công khai chi phí và làm báo cáo quyết toán."),
            new("vol-02", "ss170002@fpt.edu.vn", "Huỳnh Gia Bảo", AuthRoles.ClubMember,
                new DateOnly(2003, 8, 28), ClubGenders.Male, "0913000002", "Thạch Thất, Hà Nội",
                "Sinh viên năng động, có kinh nghiệm tổ chức sự kiện.", "Phát triển kỹ năng điều phối đội nhóm.",
                "Chạy bộ, du lịch", "Hậu cần, dẫn chương trình", "Được tham gia hoạt động cộng đồng đều đặn.",
                "Phụ trách vận chuyển và phân công tình nguyện viên."),
            new("vol-03", "ss170003@fpt.edu.vn", "Nguyễn Nhật Hạ", AuthRoles.ClubMember,
                new DateOnly(2004, 12, 2), ClubGenders.Female, "0913000003", "Quận Hà Đông, Hà Nội",
                "Sinh viên Thiết kế đồ họa quan tâm truyền thông xã hội.", "Tạo nội dung lan tỏa giá trị tích cực.",
                "Vẽ, quay phim", "Thiết kế, dựng video", "Được chủ động thực hiện chiến dịch truyền thông.",
                "Thiết kế ấn phẩm và ghi lại minh chứng hoạt động."),
            new("vol-04", "ss170004@fpt.edu.vn", "Phan Minh Châu", AuthRoles.ClubMember,
                new DateOnly(2004, 5, 19), ClubGenders.Other, "0913000004", "Quận Bắc Từ Liêm, Hà Nội",
                "Sinh viên yêu môi trường và các dự án phát triển bền vững.", "Tổ chức chiến dịch giảm rác nhựa trong trường.",
                "Trồng cây, tái chế", "Lập kế hoạch, khảo sát", "Hoạt động có số liệu tác động cụ thể.",
                "Theo dõi số liệu và viết báo cáo tác động môi trường.")
        };

        // The three test actors can be mapped to real Google addresses without
        // breaking business references because all cross-service records use
        // the stable catalog key, not the login e-mail.
        users = users.Select(user => user.Key switch
        {
            "admin" => user with { Username = configuredIdentities.AdminEmail },
            "manager-tech" => user with { Username = configuredIdentities.ClubManagerEmail },
            "tech-02" => user with { Username = configuredIdentities.StudentEmail },
            _ => user
        }).ToList();

        var clubs = new List<DemoClubSpec>
        {
            new("FPT-TECH", "Câu lạc bộ Công nghệ FPT", ClubCategories.Technology,
                "Cộng đồng sinh viên phát triển phần mềm, DevOps và sản phẩm số thông qua workshop, dự án và cuộc thi thực chiến.",
                "techclub@fpt.edu.vn", "02473001866", "manager-tech"),
            new("FPT-AI", "Câu lạc bộ AI & Data", ClubCategories.Academic,
                "Nơi sinh viên học và ứng dụng khoa học dữ liệu, machine learning và trí tuệ nhân tạo vào các bài toán thực tế.",
                "aiclub@fpt.edu.vn", "02473001867", "manager-ai"),
            new("FPT-VOL", "Câu lạc bộ Tình nguyện Cóc Xanh", ClubCategories.Volunteer,
                "Kết nối sinh viên với các hoạt động cộng đồng, bảo vệ môi trường và chương trình thiện nguyện có tác động bền vững.",
                "cocxanh@fpt.edu.vn", "02473001868", "manager-vol")
        };

        var memberships = new List<DemoMembershipSpec>
        {
            new("FPT-TECH", "manager-tech", ClubMemberRoles.Member, ClubMembershipStatuses.Approved, "Tham gia với vai trò phụ trách định hướng và vận hành CLB.", "Hồ sơ quản lý CLB đã được xác nhận.", 225),
            new("FPT-TECH", "tech-treasurer", ClubMemberRoles.Treasurer, ClubMembershipStatuses.Approved, "Mong muốn phụ trách tài chính và tham gia phát triển backend.", "Kinh nghiệm và cam kết phù hợp vị trí thủ quỹ.", 190),
            new("FPT-TECH", "tech-02", ClubMemberRoles.Member, ClubMembershipStatuses.Approved, "Muốn tham gia nhóm phát triển sản phẩm web.", "Hồ sơ rõ ràng, phù hợp định hướng CLB.", 175),
            new("FPT-TECH", "tech-03", ClubMemberRoles.Member, ClubMembershipStatuses.Approved, "Muốn học DevOps và hỗ trợ hạ tầng cho workshop.", "Đã hoàn thành buổi phỏng vấn đầu vào.", 160),
            new("FPT-TECH", "tech-04", ClubMemberRoles.Member, ClubMembershipStatuses.Pending, "Mong muốn được học lập trình và hỗ trợ truyền thông.", null, 4),

            new("FPT-AI", "manager-ai", ClubMemberRoles.Member, ClubMembershipStatuses.Approved, "Tham gia với vai trò mentor và quản lý học thuật.", "Hồ sơ quản lý CLB đã được xác nhận.", 225),
            new("FPT-AI", "ai-treasurer", ClubMemberRoles.Treasurer, ClubMembershipStatuses.Approved, "Muốn phụ trách dự toán cho các cuộc thi dữ liệu.", "Đã kiểm tra kỹ năng lập ngân sách.", 185),
            new("FPT-AI", "ai-02", ClubMemberRoles.Member, ClubMembershipStatuses.Approved, "Quan tâm NLP và muốn làm chatbot hỗ trợ sinh viên.", "Phù hợp nhóm NLP ứng dụng.", 168),
            new("FPT-AI", "ai-03", ClubMemberRoles.Member, ClubMembershipStatuses.Approved, "Muốn tham gia nhóm computer vision.", "Phù hợp nhóm xử lý ảnh.", 150),
            new("FPT-AI", "ai-04", ClubMemberRoles.Member, ClubMembershipStatuses.Rejected, "Muốn tìm hiểu khoa học dữ liệu và hỗ trợ ghi hình.", "Hồ sơ chưa thể hiện đủ thời gian cam kết; có thể đăng ký lại kỳ sau.", 25),
            new("FPT-AI", "tech-03", ClubMemberRoles.Member, ClubMembershipStatuses.Approved, "Tham gia nhóm MLOps liên CLB.", "Chấp thuận thành viên liên CLB cho nhóm MLOps.", 120),

            new("FPT-VOL", "manager-vol", ClubMemberRoles.Member, ClubMembershipStatuses.Approved, "Tham gia với vai trò điều phối viên trưởng.", "Hồ sơ quản lý CLB đã được xác nhận.", 225),
            new("FPT-VOL", "vol-treasurer", ClubMemberRoles.Treasurer, ClubMembershipStatuses.Approved, "Mong muốn quản lý quỹ và chứng từ thiện nguyện.", "Có kinh nghiệm lập ngân sách và cam kết minh bạch.", 180),
            new("FPT-VOL", "vol-02", ClubMemberRoles.Member, ClubMembershipStatuses.Approved, "Muốn tham gia đội hậu cần và điều phối.", "Đã tham gia buổi định hướng an toàn.", 165),
            new("FPT-VOL", "vol-03", ClubMemberRoles.Member, ClubMembershipStatuses.Approved, "Muốn phụ trách thiết kế và truyền thông.", "Portfolio phù hợp nhu cầu CLB.", 145),
            new("FPT-VOL", "vol-04", ClubMemberRoles.Member, ClubMembershipStatuses.Approved, "Muốn triển khai chiến dịch giảm rác nhựa.", "Ý tưởng có mục tiêu và chỉ số rõ ràng.", 130),
            new("FPT-VOL", "ai-02", ClubMemberRoles.Member, ClubMembershipStatuses.Approved, "Muốn ứng dụng NLP cho dự án cộng đồng.", "Chấp thuận thành viên liên CLB cho dự án chatbot.", 95)
        };

        var clubApplications = new List<DemoClubApplicationSpec>
        {
            new("FPT-VOL", "Câu lạc bộ Tình nguyện Cóc Xanh", ClubCategories.Volunteer,
                "manager-vol", ClubApplicationStatuses.Approved, "FPT-VOL",
                "Kế hoạch hoạt động, nhân sự sáng lập và cam kết báo cáo đáp ứng yêu cầu."),
            new("FPT-ROBOT", "Câu lạc bộ Robotics", ClubCategories.Technology,
                "tech-04", ClubApplicationStatuses.Submitted, null, null),
            new("FPT-PHOTO", "Câu lạc bộ Nhiếp ảnh", ClubCategories.Arts,
                "ai-04", ClubApplicationStatuses.NeedsRevision, null,
                "Cần bổ sung cố vấn chuyên môn, lịch sinh hoạt và phương án quản lý thiết bị.")
        };

        var techHackathonDate = referenceDate.AddDays(-76);
        var techGitDates = new[] { referenceDate.AddDays(-58), referenceDate.AddDays(-51), referenceDate.AddDays(-44), referenceDate.AddDays(-37) };
        var aiBootcampDates = new[] { referenceDate.AddDays(-70), referenceDate.AddDays(-63), referenceDate.AddDays(-56), referenceDate.AddDays(-49) };
        var aiChallengeDate = referenceDate.AddDays(-28);
        var greenDates = new[] { referenceDate.AddDays(-62), referenceDate.AddDays(-34), referenceDate.AddDays(-13) };
        var bloodDate = referenceDate.AddDays(-21);

        var activities = new List<DemoActivitySpec>
        {
            new("tech-hackathon", "FPT-TECH", $"FPT CodeFest {year}",
                "Hackathon 24 giờ xây dựng giải pháp số hỗ trợ đời sống sinh viên, có mentor kỹ thuật và vòng trình bày sản phẩm.",
                At(techHackathonDate, 1), At(techHackathonDate.AddDays(1), 1), [], "Innovation Space - Hòa Lạc",
                ActivityStatuses.Completed, "manager-tech", ["manager-tech", "tech-treasurer", "tech-02", "tech-03"],
                [
                    Attendance("manager-tech", techHackathonDate, AttendanceStatuses.Present),
                    Attendance("tech-treasurer", techHackathonDate, AttendanceStatuses.Present),
                    Attendance("tech-02", techHackathonDate, AttendanceStatuses.Late, "Đến muộn 12 phút do xe buýt."),
                    Attendance("tech-03", techHackathonDate, AttendanceStatuses.Present)
                ]),
            new("tech-git-workshop", "FPT-TECH", "Chuỗi workshop Git, CI/CD và Code Review",
                "Bốn buổi thực hành quản lý mã nguồn, pull request, pipeline kiểm thử và quy trình review trong nhóm.",
                At(techGitDates[0], 12, 30), At(techGitDates[^1], 15, 30), [7], "Phòng AL-203",
                ActivityStatuses.Completed, "manager-tech", ["manager-tech", "tech-treasurer", "tech-02", "tech-03"],
                techGitDates.SelectMany((date, index) => new[]
                {
                    Attendance("manager-tech", date, AttendanceStatuses.Present),
                    Attendance("tech-treasurer", date, AttendanceStatuses.Present),
                    Attendance("tech-02", date, index == 2 ? AttendanceStatuses.Excused : AttendanceStatuses.Present, index == 2 ? "Xin phép do lịch thi." : null),
                    Attendance("tech-03", date, index == 0 ? AttendanceStatuses.Late : AttendanceStatuses.Present, index == 0 ? "Đến muộn 8 phút." : null)
                }).ToArray()),
            new("tech-cloud-seminar", "FPT-TECH", "Seminar Cloud-native trên Azure",
                "Chia sẻ kiến trúc microservices, container, giám sát và triển khai ứng dụng lên môi trường cloud.",
                At(referenceDate.AddDays(22), 7, 30), At(referenceDate.AddDays(22), 10, 30), [], "Hội trường Beta",
                ActivityStatuses.Scheduled, "manager-tech", ["manager-tech", "tech-treasurer", "tech-02", "tech-03"], []),
            new("tech-weekly-lab", "FPT-TECH", "Software Engineering Weekly Lab",
                "Buổi lab hằng tuần để thành viên hoàn thiện sản phẩm, xử lý issue và demo tiến độ theo sprint.",
                At(referenceDate.AddDays(4), 12, 30), At(referenceDate.AddDays(95), 15, 30), [3], "Phòng DE-301",
                ActivityStatuses.Scheduled, "manager-tech", ["manager-tech", "tech-treasurer", "tech-02", "tech-03"], []),

            new("ai-bootcamp", "FPT-AI", "Machine Learning Foundation Bootcamp",
                "Chuỗi bốn buổi từ làm sạch dữ liệu, huấn luyện mô hình đến đánh giá và trình bày kết quả.",
                At(aiBootcampDates[0], 12, 30), At(aiBootcampDates[^1], 15, 30), [7], "AI Lab - Delta Building",
                ActivityStatuses.Completed, "manager-ai", ["manager-ai", "ai-treasurer", "ai-02", "ai-03", "tech-03"],
                aiBootcampDates.SelectMany((date, index) => new[]
                {
                    Attendance("manager-ai", date, AttendanceStatuses.Present),
                    Attendance("ai-treasurer", date, AttendanceStatuses.Present),
                    Attendance("ai-02", date, index == 1 ? AttendanceStatuses.Late : AttendanceStatuses.Present, index == 1 ? "Đến muộn 10 phút." : null),
                    Attendance("ai-03", date, index == 3 ? AttendanceStatuses.Absent : AttendanceStatuses.Present, index == 3 ? "Vắng không báo trước." : null),
                    Attendance("tech-03", date, index == 2 ? AttendanceStatuses.Excused : AttendanceStatuses.Present, index == 2 ? "Trùng lịch workshop chuyên ngành." : null)
                }).ToArray()),
            new("ai-data-challenge", "FPT-AI", $"Campus Data Challenge {year}",
                "Cuộc thi phân tích dữ liệu sử dụng bộ dữ liệu ẩn danh về trải nghiệm học tập để đề xuất giải pháp cải thiện dịch vụ sinh viên.",
                At(aiChallengeDate, 1), At(aiChallengeDate, 11), [], "Hội trường Gamma",
                ActivityStatuses.Completed, "manager-ai", ["manager-ai", "ai-treasurer", "ai-02", "ai-03", "tech-03"],
                [
                    Attendance("manager-ai", aiChallengeDate, AttendanceStatuses.Present),
                    Attendance("ai-treasurer", aiChallengeDate, AttendanceStatuses.Present),
                    Attendance("ai-02", aiChallengeDate, AttendanceStatuses.Present),
                    Attendance("ai-03", aiChallengeDate, AttendanceStatuses.Late, "Đến sau phần khai mạc 15 phút."),
                    Attendance("tech-03", aiChallengeDate, AttendanceStatuses.Present)
                ]),
            new("ai-ethics", "FPT-AI", "AI Ethics & Responsible Data Seminar",
                "Thảo luận về quyền riêng tư, thiên lệch dữ liệu và cách xây dựng sản phẩm AI có trách nhiệm.",
                At(referenceDate.AddDays(14), 8), At(referenceDate.AddDays(14), 10, 30), [], "Phòng AL-101",
                ActivityStatuses.Scheduled, "admin", ["manager-ai", "ai-treasurer", "ai-02", "ai-03", "tech-03"], [],
                "DEMO-AI-FALL-FUTURE", 1),
            new("ai-project-lab", "FPT-AI", "Applied AI Project Lab",
                "Buổi lab dự kiến dành cho các nhóm demo mô hình và nhận phản biện từ mentor.",
                At(referenceDate.AddDays(-8), 12, 30), At(referenceDate.AddDays(-8), 15), [], "AI Lab - Delta Building",
                ActivityStatuses.Cancelled, "manager-ai", ["ai-treasurer", "ai-02", "ai-03"], []),

            new("vol-green-sunday", "FPT-VOL", "Chủ nhật Xanh - Campus không rác nhựa",
                "Ba đợt phân loại rác, thu gom pin cũ và truyền thông giảm nhựa dùng một lần tại khuôn viên trường.",
                At(greenDates[0], 0, 30), At(greenDates[^1], 4), [7], "Khuôn viên FPTU Hòa Lạc",
                ActivityStatuses.Completed, "manager-vol", ["manager-vol", "vol-treasurer", "vol-02", "vol-03", "vol-04", "ai-02"],
                greenDates.SelectMany((date, index) => new[]
                {
                    Attendance("manager-vol", date, AttendanceStatuses.Present),
                    Attendance("vol-treasurer", date, AttendanceStatuses.Present),
                    Attendance("vol-02", date, AttendanceStatuses.Present),
                    Attendance("vol-03", date, index == 1 ? AttendanceStatuses.Excused : AttendanceStatuses.Present, index == 1 ? "Xin phép do lịch bảo vệ đồ án." : null),
                    Attendance("vol-04", date, AttendanceStatuses.Present),
                    Attendance("ai-02", date, index == 2 ? AttendanceStatuses.Late : AttendanceStatuses.Present, index == 2 ? "Đến muộn 7 phút." : null)
                }).ToArray()),
            new("vol-blood-donation", "FPT-VOL", $"Ngày hội Hiến máu Cóc Hồng {year}",
                "Phối hợp cùng Viện Huyết học tổ chức hiến máu, hướng dẫn đăng ký và chăm sóc người tham gia sau hiến.",
                At(bloodDate, 1), At(bloodDate, 9), [], "Sảnh Alpha",
                ActivityStatuses.Completed, "manager-vol", ["manager-vol", "vol-treasurer", "vol-02", "vol-03", "vol-04", "ai-02"],
                [
                    Attendance("manager-vol", bloodDate, AttendanceStatuses.Present),
                    Attendance("vol-treasurer", bloodDate, AttendanceStatuses.Present),
                    Attendance("vol-02", bloodDate, AttendanceStatuses.Present),
                    Attendance("vol-03", bloodDate, AttendanceStatuses.Present),
                    Attendance("vol-04", bloodDate, AttendanceStatuses.Present),
                    Attendance("ai-02", bloodDate, AttendanceStatuses.Excused, "Không đủ điều kiện sức khỏe để hiến máu nhưng có hỗ trợ truyền thông.")
                ]),
            new("vol-mid-autumn", "FPT-VOL", "Trung thu sẻ chia tại Ba Vì",
                "Chuẩn bị học phẩm, trò chơi và chương trình giao lưu cho trẻ em tại điểm trường vùng cao.",
                At(referenceDate.AddDays(17), 0), At(referenceDate.AddDays(17), 10), [], "Điểm trường Suối Hai, Ba Vì",
                ActivityStatuses.Scheduled, "manager-vol", ["manager-vol", "vol-treasurer", "vol-02", "vol-03", "vol-04", "ai-02"], []),
            new("vol-winter", "FPT-VOL", $"Áo ấm mùa đông {year}",
                "Chương trình gây quỹ, phân loại quà tặng và trao áo ấm cho học sinh tại huyện vùng cao.",
                At(referenceDate.AddDays(88), 0), At(referenceDate.AddDays(89), 10), [], "Huyện Mù Cang Chải, Yên Bái",
                ActivityStatuses.Scheduled, "manager-vol", ["manager-vol", "vol-treasurer", "vol-02", "vol-03", "vol-04"], [])
        };

        DemoReportDetailSpec Detail(
            string name, DateOnly date, string description, int participants, string outcome,
            string type, string location, string? partner, string objective, int? target,
            decimal? budget, int order) =>
            new(name, date, description, participants, outcome, type, location, partner, objective, target, budget, order);

        var reports = new List<DemoReportSpec>
        {
            new("DEMO-TECH-SUMMER-ACTIVITY", "FPT-TECH", summerPeriod, "ACTIVITY_REPORT", "Activity report", ReportStatuses.Approved,
                "manager-tech", new DateOnly(year, 8, 15), At(referenceDate.AddDays(-34), 3), At(referenceDate.AddDays(-32), 4), At(referenceDate.AddDays(-29), 3), "admin",
                "CLB Công nghệ hoàn thành CodeFest và chuỗi workshop Git/CI/CD với tỷ lệ tham gia ổn định, sản phẩm đầu ra đáp ứng mục tiêu học kỳ.",
                "Bốn nhóm hoàn thiện prototype; 92% lượt điểm danh hợp lệ; hai thành viên mới đảm nhiệm được quy trình CI/CD.",
                "Một số nhóm thiếu thời gian kiểm thử và tài liệu triển khai ban đầu chưa đồng đều.",
                "Chuẩn hóa checklist review, bổ sung buổi kiểm thử hiệu năng và phân công mentor theo nhóm.",
                "Tổ chức seminar cloud-native và duy trì Weekly Lab theo sprint hai tuần.",
                [
                    Detail($"FPT CodeFest {year}", techHackathonDate, "Hackathon 24 giờ theo bốn nhóm sản phẩm.", 38, "04 prototype, 01 sản phẩm được chọn tiếp tục ươm tạo.", "Competition", "Innovation Space - Hòa Lạc", "FPT Software", "Ứng dụng kỹ năng phát triển sản phẩm trong thời gian giới hạn.", 40, 21_450_000m, 1),
                    Detail("Chuỗi workshop Git, CI/CD và Code Review", techGitDates[0], "Bốn buổi thực hành theo repository mẫu.", 31, "27 thành viên hoàn thành pipeline và bài code review cuối khóa.", "Workshop", "Phòng AL-203", null, "Chuẩn hóa quy trình cộng tác mã nguồn.", 30, 3_200_000m, 2)
                ],
                [new("admin", "Approved", "Báo cáo có số liệu, minh chứng và kế hoạch cải tiến rõ ràng. Phê duyệt.", 5)]),

            new("DEMO-TECH-SUMMER-FINANCE", "FPT-TECH", summerPeriod, "FINANCIAL_REPORT", "Financial report", ReportStatuses.Approved,
                "tech-treasurer", new DateOnly(year, 8, 15), At(referenceDate.AddDays(-33), 2), At(referenceDate.AddDays(-31), 2), At(referenceDate.AddDays(-27), 2), "admin",
                "Tổng hợp thu chi mùa hè của CLB Công nghệ, tập trung vào CodeFest và chuỗi workshop chuyên môn.",
                "Chi thực tế thấp hơn dự toán 550.000 đồng; chứng từ được đối soát đầy đủ.",
                "Một hóa đơn in ấn nộp chậm hai ngày làm kéo dài thời gian quyết toán.",
                "Yêu cầu trưởng nhóm nộp chứng từ trong vòng 48 giờ sau mỗi hoạt động.",
                "Lập quỹ dự phòng 5% và số hóa checklist chứng từ cho học kỳ tiếp theo.",
                [Detail("Đối soát ngân sách hoạt động mùa hè", referenceDate.AddDays(-30), "Đối chiếu dự toán, hóa đơn và giao dịch của hai hoạt động.", 4, "Toàn bộ khoản chi có chứng từ; không phát sinh khoản ngoài kế hoạch.", "Finance", "Văn phòng CLB", null, "Hoàn tất quyết toán đúng quy định.", 4, 24_650_000m, 1)],
                [
                    new("manager-tech", "ManagerApproved", "Đã đối chiếu chứng từ gốc và số dư. Chuyển quản trị viên phê duyệt.", 3),
                    new("admin", "Approved", "Số liệu khớp với đề xuất và quyết toán. Phê duyệt báo cáo tài chính.", 6)
                ]),

            new("DEMO-TECH-FALL-FUTURE", "FPT-TECH", fallPeriod, FutureEventReportRules.ReportType, "Future event", ReportStatuses.AwaitingFinance,
                "manager-tech", new DateOnly(year, 11, 30), At(referenceDate.AddDays(-3), 3), At(referenceDate.AddDays(-2), 3), null, null,
                "Đề xuất tổ chức ngày hội Cloud-native giúp sinh viên thực hành triển khai microservices và quan sát hệ thống.",
                "Đã xác nhận diễn giả, nội dung kỹ thuật và phương án phòng lab.",
                "Ngân sách cloud credit và thiết bị mạng đang chờ phê duyệt cuối.",
                "Ưu tiên tài trợ cloud credit; có phương án lab cục bộ nếu nhà tài trợ phản hồi chậm.",
                "Sau sự kiện sẽ mở nhóm dự án cloud và đánh giá qua bài lab thực tế.",
                [Detail("Cloud-native Day", referenceDate.AddDays(45), "Một ngày gồm keynote, hai lab triển khai và phiên demo observability.", 0, "Kỳ vọng 60 sinh viên hoàn thành ít nhất một lab.", "FutureEvent", "Hội trường Beta và phòng Lab 202", "Microsoft Learn Student Ambassadors", "Thực hành triển khai và vận hành microservices.", 60, null, 1)], []),

            new("DEMO-TECH-FALL-DRAFT", "FPT-TECH", fallPeriod, "OPERATIONS_REPORT", "Operations", ReportStatuses.Draft,
                "manager-tech", new DateOnly(year, 11, 30), At(referenceDate.AddDays(-1), 7), null, null, null,
                "Bản nháp rà soát công tác vận hành đầu học kỳ của CLB Công nghệ.",
                "Đã phân nhóm chuyên môn và công bố lịch Weekly Lab.",
                "Danh sách mentor cho nhóm mobile chưa hoàn tất.",
                "Mời thêm mentor và chốt tiêu chí đánh giá sprint.",
                "Hoàn thiện cơ cấu nhóm, lịch workshop và bộ chỉ số tham gia trước cuối tháng.",
                [Detail("Khởi động Software Engineering Weekly Lab", referenceDate.AddDays(4), "Chuẩn bị backlog và phân nhóm dự án.", 0, "Kỳ vọng bốn nhóm có backlog và người phụ trách.", "Operations", "Phòng DE-301", null, "Ổn định nhịp sinh hoạt đầu kỳ.", 32, null, 1)], []),

            new("DEMO-AI-SUMMER-ACTIVITY", "FPT-AI", summerPeriod, "ACTIVITY_REPORT", "Activity report", ReportStatuses.Approved,
                "manager-ai", new DateOnly(year, 8, 15), At(referenceDate.AddDays(-26), 2), At(referenceDate.AddDays(-24), 2), At(referenceDate.AddDays(-20), 2), "admin",
                "Bootcamp Machine Learning và Campus Data Challenge tạo được lộ trình học liên tục từ nền tảng đến sản phẩm dữ liệu.",
                "85% học viên hoàn thành notebook; năm nhóm nộp dashboard và ba nhóm trình bày mô hình.",
                "Chất lượng dữ liệu đầu vào chưa đồng nhất; một số thành viên còn yếu kỹ năng trình bày.",
                "Thêm buổi data quality và yêu cầu mỗi nhóm có một phiên rehearsal.",
                "Mở Applied AI Project Lab và seminar AI có trách nhiệm.",
                [
                    Detail("Machine Learning Foundation Bootcamp", aiBootcampDates[0], "Bốn buổi học và thực hành theo notebook.", 42, "36 học viên hoàn thành bài cuối khóa.", "Training", "AI Lab - Delta Building", null, "Nắm quy trình xây dựng mô hình cơ bản.", 45, 11_800_000m, 1),
                    Detail($"Campus Data Challenge {year}", aiChallengeDate, "Cuộc thi phân tích dữ liệu trải nghiệm học tập đã ẩn danh.", 35, "05 dashboard và 03 mô hình dự báo được trình bày.", "Competition", "Hội trường Gamma", "Phòng Công tác sinh viên", "Đề xuất cải thiện dịch vụ dựa trên dữ liệu.", 40, 8_600_000m, 2)
                ],
                [new("admin", "Approved", "Báo cáo rõ kết quả học tập, số người tham gia và bài học cải tiến. Phê duyệt.", 6)]),

            new("DEMO-AI-SUMMER-FINANCE", "FPT-AI", summerPeriod, "FINANCIAL_REPORT", "Financial report", ReportStatuses.Rejected,
                "ai-treasurer", new DateOnly(year, 8, 15), At(referenceDate.AddDays(-23), 3), At(referenceDate.AddDays(-21), 3), At(referenceDate.AddDays(-18), 3), "manager-ai",
                "Báo cáo chi phí sơ bộ cho Bootcamp và Data Challenge.",
                "Đã tập hợp phần lớn hóa đơn thuê thiết bị và in ấn.",
                "Thiếu biên bản bàn giao giải thưởng và hai hóa đơn chưa ghi đúng đơn vị.",
                "Bổ sung biên bản, điều chỉnh hóa đơn và nộp lại bản đối soát.",
                "Hoàn thiện hồ sơ chứng từ trước khi lập ngân sách sự kiện tiếp theo.",
                [Detail("Đối soát chi phí Bootcamp và Data Challenge", referenceDate.AddDays(-20), "Tổng hợp chứng từ của hai hoạt động học thuật.", 3, "Còn ba chứng từ cần bổ sung hoặc điều chỉnh.", "Finance", "Văn phòng CLB", null, "Hoàn thiện hồ sơ quyết toán.", 3, 20_400_000m, 1)],
                [new("manager-ai", "Rejected", "Chưa thể chuyển phê duyệt cuối: thiếu biên bản bàn giao giải thưởng và hóa đơn hợp lệ.", 5)]),

            new("DEMO-AI-FALL-FUTURE", "FPT-AI", fallPeriod, FutureEventReportRules.ReportType, "Future event", ReportStatuses.Approved,
                "manager-ai", new DateOnly(year, 11, 30), At(referenceDate.AddDays(-15), 2), At(referenceDate.AddDays(-13), 2), At(referenceDate.AddDays(-9), 2), "admin",
                "Đề xuất seminar AI Ethics nhằm trang bị kiến thức về quyền riêng tư, thiên lệch và quản trị dữ liệu.",
                "Đã chốt diễn giả, phòng học và bộ tình huống thảo luận.",
                "Cần giới hạn số người để đảm bảo chất lượng thảo luận nhóm.",
                "Mở đăng ký sớm, ưu tiên thành viên đang thực hiện dự án AI.",
                "Tổng hợp bộ nguyên tắc responsible AI dùng chung cho các dự án CLB.",
                [Detail("AI Ethics & Responsible Data Seminar", referenceDate.AddDays(14), "Seminar kết hợp tình huống thực tế và thảo luận nhóm.", 0, "Kỳ vọng 45 người tham dự và hoàn thành bài đánh giá tình huống.", "FutureEvent", "Phòng AL-101", "FPT Smart Cloud", "Nâng cao nhận thức về AI có trách nhiệm.", 45, null, 1)],
                [new("admin", "Approved", "Kế hoạch, ngân sách và phương án bảo vệ dữ liệu phù hợp. Phê duyệt tổ chức.", 6)]),

            new("DEMO-AI-FALL-REVIEW", "FPT-AI", fallPeriod, "RESEARCH_REPORT", "Research", ReportStatuses.UnderReview,
                "manager-ai", new DateOnly(year, 11, 30), At(referenceDate.AddDays(-7), 2), At(referenceDate.AddDays(-5), 2), At(referenceDate.AddDays(-3), 2), "admin",
                "Báo cáo tiến độ ba nhóm nghiên cứu ứng dụng NLP, computer vision và MLOps trong tám tuần đầu học kỳ.",
                "Hai nhóm có baseline và bộ dữ liệu đã làm sạch; nhóm MLOps hoàn thành pipeline thử nghiệm.",
                "Nhóm NLP còn thiếu tiêu chí đánh giá chất lượng câu trả lời.",
                "Bổ sung rubric đánh giá, kiểm thử chéo và nhật ký thí nghiệm.",
                "Demo nội bộ cuối tháng và chọn một dự án tham dự cuộc thi nghiên cứu sinh viên.",
                [
                    Detail("NLP Student Support Bot", referenceDate.AddDays(-6), "Xây dựng baseline chatbot hỏi đáp quy chế học vụ.", 8, "Hoàn thành bộ 320 câu hỏi đã ẩn danh và baseline retrieval.", "Research", "AI Lab", "Phòng Công tác sinh viên", "Đánh giá khả năng hỗ trợ câu hỏi thường gặp.", 8, 2_400_000m, 1),
                    Detail("MLOps Shared Pipeline", referenceDate.AddDays(-4), "Chuẩn hóa pipeline huấn luyện và theo dõi thí nghiệm.", 6, "Pipeline chạy được với hai bộ dữ liệu mẫu.", "Research", "Cloud Lab", "CLB Công nghệ FPT", "Giảm thời gian thiết lập môi trường cho nhóm dự án.", 6, 1_800_000m, 2)
                ],
                [new("admin", "UnderReview", "Đã tiếp nhận; cần đối chiếu thêm tiêu chí đánh giá của nhóm NLP trước quyết định cuối.", 4)]),

            new("DEMO-VOL-SUMMER-ACTIVITY", "FPT-VOL", summerPeriod, "ACTIVITY_REPORT", "Activity report", ReportStatuses.Approved,
                "manager-vol", new DateOnly(year, 8, 15), At(referenceDate.AddDays(-17), 2), At(referenceDate.AddDays(-15), 2), At(referenceDate.AddDays(-11), 2), "admin",
                "Chuỗi Chủ nhật Xanh và Ngày hội hiến máu đạt mục tiêu về tình nguyện viên, khối lượng thu gom và số đơn vị máu tiếp nhận.",
                "Thu gom 186 kg vật liệu tái chế, 412 viên pin cũ và tiếp nhận 126 đơn vị máu.",
                "Khâu phân luồng đầu giờ hiến máu còn ùn tắc trong khoảng 20 phút.",
                "Bổ sung bàn check-in, chia ca tình nguyện viên và diễn tập luồng trước sự kiện.",
                "Triển khai Trung thu sẻ chia và chiến dịch Áo ấm mùa đông theo bộ checklist an toàn mới.",
                [
                    Detail("Chủ nhật Xanh - Campus không rác nhựa", greenDates[0], "Ba đợt thu gom và truyền thông phân loại rác.", 54, "186 kg tái chế và 412 viên pin được chuyển tới đơn vị xử lý.", "Community", "Khuôn viên FPTU Hòa Lạc", "Green Life", "Giảm rác nhựa và nâng cao thói quen phân loại.", 50, 4_350_000m, 1),
                    Detail($"Ngày hội Hiến máu Cóc Hồng {year}", bloodDate, "Tổ chức đăng ký, sàng lọc và hiến máu trong khuôn viên.", 168, "126 đơn vị máu được tiếp nhận an toàn.", "Community", "Sảnh Alpha", "Viện Huyết học - Truyền máu Trung ương", "Bổ sung nguồn máu và lan tỏa tinh thần sẻ chia.", 150, 8_720_000m, 2)
                ],
                [new("admin", "Approved", "Số liệu tác động và đối tác xác nhận đầy đủ. Phê duyệt báo cáo hoạt động.", 6)]),

            new("DEMO-VOL-SUMMER-FINANCE", "FPT-VOL", summerPeriod, "FINANCIAL_REPORT", "Financial report", ReportStatuses.Approved,
                "vol-treasurer", new DateOnly(year, 8, 15), At(referenceDate.AddDays(-16), 3), At(referenceDate.AddDays(-14), 3), At(referenceDate.AddDays(-10), 3), "admin",
                "Báo cáo tài chính cho Chủ nhật Xanh và Ngày hội Hiến máu, bao gồm nguồn tài trợ hiện vật và chi phí vận hành.",
                "Quyết toán thấp hơn ngân sách được duyệt; toàn bộ khoản chi tiền mặt có hóa đơn hoặc biên nhận.",
                "Việc định giá tài trợ hiện vật mất thêm thời gian xác nhận từ đối tác.",
                "Thống nhất biểu mẫu xác nhận tài trợ hiện vật ngay khi tiếp nhận.",
                "Áp dụng mã khoản mục và lưu chứng từ theo từng hoạt động trên kho dùng chung.",
                [Detail("Quyết toán hoạt động cộng đồng mùa hè", referenceDate.AddDays(-12), "Đối soát chi phí và tài trợ hiện vật của hai chương trình.", 5, "Số liệu khớp với biên bản bàn giao và giao dịch.", "Finance", "Văn phòng CLB", null, "Minh bạch nguồn quỹ và hoàn tất quyết toán.", 5, 13_070_000m, 1)],
                [
                    new("manager-vol", "ManagerApproved", "Đã xác nhận chi phí và tài trợ hiện vật với đối tác.", 3),
                    new("admin", "Approved", "Chứng từ đầy đủ, số dư và giao dịch khớp. Phê duyệt.", 6)
                ]),

            new("DEMO-VOL-FALL-SUBMITTED", "FPT-VOL", fallPeriod, "FINANCIAL_REPORT", "Financial report", ReportStatuses.Submitted,
                "vol-treasurer", new DateOnly(year, 11, 30), At(referenceDate.AddDays(-4), 3), At(referenceDate.AddDays(-2), 3), null, null,
                "Báo cáo dự toán sơ bộ cho Trung thu sẻ chia và chiến dịch Áo ấm mùa đông.",
                "Đã có ba báo giá, danh sách hạng mục và cam kết tài trợ vận chuyển.",
                "Số lượng quà cuối cùng phụ thuộc khảo sát tại địa phương.",
                "Chốt danh sách thụ hưởng trước thời hạn mua hàng bảy ngày.",
                "Hoàn thiện dự toán, ký cam kết đối tác và chuẩn bị mẫu quyết toán.",
                [Detail("Rà soát dự toán chương trình cuối năm", referenceDate.AddDays(-3), "Tổng hợp báo giá học phẩm, áo ấm và vận chuyển.", 4, "Dự toán sơ bộ và ba phương án quy mô đã hoàn thành.", "Finance", "Văn phòng CLB", "Đoàn xã Suối Giàng", "Chuẩn bị ngân sách có phương án dự phòng.", 4, null, 1)], []),

            new("DEMO-VOL-FALL-REJECTED", "FPT-VOL", fallPeriod, "PARTNERSHIP_REPORT", "Partnership", ReportStatuses.Rejected,
                "manager-vol", new DateOnly(year, 11, 30), At(referenceDate.AddDays(-12), 4), At(referenceDate.AddDays(-10), 4), At(referenceDate.AddDays(-8), 4), "admin",
                "Đề xuất hợp tác truyền thông với một nhà tài trợ cho chuỗi hoạt động cuối năm.",
                "Đối tác cam kết hỗ trợ hiện vật và vận chuyển.",
                "Điều khoản sử dụng hình ảnh người thụ hưởng chưa nêu rõ phạm vi và thời hạn.",
                "Đàm phán lại điều khoản hình ảnh, quyền riêng tư và quy trình đồng thuận.",
                "Chỉ ký biên bản hợp tác sau khi điều khoản bảo vệ người thụ hưởng được hoàn thiện.",
                [Detail("Đánh giá đề xuất đối tác cuối năm", referenceDate.AddDays(-11), "Rà soát quyền lợi tài trợ và phương án truyền thông.", 6, "Phát hiện điều khoản hình ảnh cần sửa trước khi hợp tác.", "Partnership", "Phòng họp Alpha", "Nhà tài trợ tiềm năng", "Đảm bảo hợp tác phù hợp nguyên tắc bảo vệ người thụ hưởng.", 6, null, 1)],
                [new("admin", "Rejected", "Từ chối phiên bản hiện tại; cần bổ sung cơ chế đồng thuận và giới hạn sử dụng hình ảnh.", 4)])
        };

        var budgets = new List<DemoBudgetProposalSpec>
        {
            new("budget-tech-cloud", "FPT-TECH", "Ngân sách Cloud-native Day",
                "Cloud credit, thiết bị mạng thực hành, tài liệu và hỗ trợ diễn giả cho 60 người tham dự.",
                18_500_000m, null, FinanceStatuses.ManagerApproved, "tech-treasurer", At(referenceDate.AddDays(-2), 4),
                "manager-tech", At(referenceDate.AddDays(-1), 3), "Hạng mục phù hợp kế hoạch; đề nghị ưu tiên tài trợ cloud credit.",
                null, null, null, "DEMO-TECH-FALL-FUTURE", null, null),
            new("budget-ai-ethics", "FPT-AI", "Ngân sách seminar AI Ethics",
                "Chi phí diễn giả, tài liệu tình huống, tea-break và ghi hình nội bộ cho seminar responsible AI.",
                12_000_000m, 11_200_000m, FinanceStatuses.Approved, "ai-treasurer", At(referenceDate.AddDays(-12), 3),
                "manager-ai", At(referenceDate.AddDays(-11), 3), "Nội dung cần thiết; đã tối ưu tea-break theo số lượng đăng ký.",
                "admin", At(referenceDate.AddDays(-9), 1), "Phê duyệt 11.200.000 đồng theo báo giá đã điều chỉnh.",
                "DEMO-AI-FALL-FUTURE", "ai-ethics", null),
            new("budget-tech-codefest", "FPT-TECH", $"Ngân sách FPT CodeFest {year}",
                "Giải thưởng, ăn nhẹ, vật tư trình bày và chi phí vận hành hackathon 24 giờ.",
                22_000_000m, 22_000_000m, FinanceStatuses.Settled, "tech-treasurer", At(referenceDate.AddDays(-92), 3),
                "manager-tech", At(referenceDate.AddDays(-90), 3), "Dự toán theo bốn nhóm và 40 người tham gia.",
                "admin", At(referenceDate.AddDays(-87), 2), "Phê duyệt đầy đủ theo kế hoạch hoạt động.",
                null, "tech-hackathon",
                new DemoSettlementSpec(21_450_000m, "https://demo.clubreporthub.vn/receipts/codefest-settlement.pdf", FinanceStatuses.Approved,
                    At(referenceDate.AddDays(-72), 3), "admin", At(referenceDate.AddDays(-69), 2), "Chứng từ hợp lệ; hoàn trả 550.000 đồng ngân sách dư.")),
            new("budget-ai-bootcamp", "FPT-AI", "Ngân sách ML Foundation Bootcamp",
                "Phí mentor, cloud notebook, tài liệu và tea-break cho bốn buổi học.",
                15_000_000m, null, FinanceStatuses.Rejected, "ai-treasurer", At(referenceDate.AddDays(-85), 2),
                "manager-ai", At(referenceDate.AddDays(-83), 2), "Nội dung phù hợp nhưng cần tách rõ chi phí mentor và cloud.",
                "admin", At(referenceDate.AddDays(-81), 2), "Chưa đủ ba báo giá và chưa nêu định mức cloud credit; vui lòng lập lại đề xuất.",
                null, "ai-bootcamp", null),
            new("budget-ai-data-challenge", "FPT-AI", $"Ngân sách Campus Data Challenge {year}",
                "Giải thưởng, thuê thiết bị trình chiếu, in bộ dữ liệu hướng dẫn và tea-break cho ngày thi.",
                9_500_000m, 9_200_000m, FinanceStatuses.Settled, "ai-treasurer", At(referenceDate.AddDays(-45), 2),
                "manager-ai", At(referenceDate.AddDays(-43), 2), "Dự toán phù hợp quy mô năm nhóm; giảm hạng mục in ấn không cần thiết.",
                "admin", At(referenceDate.AddDays(-41), 2), "Phê duyệt 9.200.000 đồng theo dự toán đã tối ưu.",
                null, "ai-data-challenge",
                new DemoSettlementSpec(8_600_000m, "https://demo.clubreporthub.vn/receipts/data-challenge-settlement.pdf", FinanceStatuses.Approved,
                    At(referenceDate.AddDays(-25), 2), "admin", At(referenceDate.AddDays(-23), 2), "Chứng từ hợp lệ; hoàn trả 600.000 đồng ngân sách dư.")),
            new("budget-vol-blood", "FPT-VOL", $"Ngân sách Ngày hội Hiến máu Cóc Hồng {year}",
                "Vật tư check-in, nước uống, suất ăn tình nguyện viên, in ấn hướng dẫn và y tế dự phòng.",
                9_000_000m, 9_000_000m, FinanceStatuses.Settled, "vol-treasurer", At(referenceDate.AddDays(-40), 3),
                "manager-vol", At(referenceDate.AddDays(-38), 3), "Dự toán bám sát quy mô 150 người đăng ký.",
                "admin", At(referenceDate.AddDays(-36), 3), "Phê duyệt theo xác nhận phối hợp của Viện Huyết học.",
                null, "vol-blood-donation",
                new DemoSettlementSpec(8_720_000m, "https://demo.clubreporthub.vn/receipts/blood-donation-settlement.pdf", FinanceStatuses.Approved,
                    At(referenceDate.AddDays(-18), 3), "admin", At(referenceDate.AddDays(-15), 3), "Chứng từ đầy đủ; quyết toán thấp hơn 280.000 đồng.")),
            new("budget-vol-winter", "FPT-VOL", $"Ngân sách Áo ấm mùa đông {year}",
                "Mua áo ấm, học phẩm, đóng gói và hỗ trợ vận chuyển tới điểm trường vùng cao.",
                14_000_000m, null, FinanceStatuses.Submitted, "vol-treasurer", At(referenceDate.AddDays(-1), 4),
                null, null, null, null, null, null, null, "vol-winter", null)
        };

        var notifications = new List<DemoNotificationSpec>
        {
            new(null, AuthRoles.Admin, EventRoutingKeys.ReportSubmitted, "Báo cáo mới chờ phê duyệt", "CLB AI & Data đã gửi báo cáo tiến độ nghiên cứu học kỳ hiện tại.", false, At(referenceDate.AddDays(-5), 2)),
            new(null, AuthRoles.Admin, EventRoutingKeys.BudgetProposalSubmitted, "Đề xuất ngân sách chờ duyệt cuối", "Ngân sách Cloud-native Day đã được quản lý CLB duyệt và chuyển tới quản trị viên.", false, At(referenceDate.AddDays(-1), 3)),
            new(null, AuthRoles.Admin, EventRoutingKeys.ReportDeadlineReminder, "Nhắc hạn báo cáo học kỳ", $"Hạn nộp báo cáo {fallPeriod} là 30/11/{year}; hiện còn báo cáo ở trạng thái nháp hoặc chưa hoàn tất.", true, At(referenceDate.AddDays(-6), 1)),
            new("manager-tech", null, EventRoutingKeys.BudgetProposalSubmitted, "Ngân sách Cloud-native Day đã qua bước quản lý", "Đề xuất đã được chuyển tới quản trị viên để phê duyệt cuối.", true, At(referenceDate.AddDays(-1), 3, 5)),
            new("manager-tech", null, EventRoutingKeys.ReportApproved, "Báo cáo hoạt động mùa hè đã được duyệt", "Báo cáo CodeFest và chuỗi workshop đã hoàn tất quy trình phê duyệt.", true, At(referenceDate.AddDays(-29), 3)),
            new("manager-ai", null, EventRoutingKeys.ReportApproved, "Kế hoạch seminar AI Ethics đã được duyệt", "Hoạt động đã được xuất bản vào lịch CLB với ngân sách 11.200.000 đồng.", false, At(referenceDate.AddDays(-9), 2)),
            new("manager-ai", null, EventRoutingKeys.ReportRejected, "Báo cáo tài chính cần bổ sung", "Thiếu biên bản bàn giao giải thưởng và hóa đơn hợp lệ; vui lòng hướng dẫn thủ quỹ cập nhật.", false, At(referenceDate.AddDays(-18), 3)),
            new("manager-vol", null, EventRoutingKeys.ReportApproved, "Báo cáo tác động mùa hè đã được duyệt", "Số liệu Chủ nhật Xanh và Ngày hội hiến máu đã được xác nhận.", true, At(referenceDate.AddDays(-11), 2)),
            new("manager-vol", null, EventRoutingKeys.BudgetProposalSubmitted, "Đề xuất Áo ấm mùa đông chờ bạn duyệt", "Thủ quỹ đã gửi đề xuất 14.000.000 đồng; cần kiểm tra báo giá và số lượng thụ hưởng.", false, At(referenceDate.AddDays(-1), 4)),
            new("tech-treasurer", null, EventRoutingKeys.BudgetApproved, "Quyết toán CodeFest đã hoàn tất", "Quyết toán 21.450.000 đồng được duyệt; ngân sách dư 550.000 đồng.", true, At(referenceDate.AddDays(-69), 2)),
            new("tech-treasurer", null, EventRoutingKeys.BudgetProposalSubmitted, "Đề xuất Cloud-native Day đã được quản lý duyệt", "Đề xuất đang chờ quản trị viên phê duyệt cuối.", false, At(referenceDate.AddDays(-1), 3)),
            new("tech-02", null, EventRoutingKeys.ActivityCreated, "Mở đăng ký seminar Cloud-native", "Seminar diễn ra tại Hội trường Beta; bạn đã có tên trong danh sách tham gia.", false, At(referenceDate.AddDays(-2), 8)),
            new("tech-03", null, EventRoutingKeys.ActivityCreated, "Lịch Weekly Lab đã được cập nhật", "Weekly Lab bắt đầu tuần tới tại phòng DE-301 và lặp lại vào thứ Tư.", true, At(referenceDate.AddDays(-3), 8)),
            new("tech-04", null, "membership.submitted", "Đơn gia nhập đang được xem xét", "CLB Công nghệ đã nhận hồ sơ của bạn; quản lý sẽ phản hồi sau khi hoàn tất phỏng vấn.", false, At(referenceDate.AddDays(-4), 8)),
            new("ai-treasurer", null, EventRoutingKeys.BudgetApproved, "Ngân sách seminar AI Ethics được duyệt", "Mức duyệt cuối là 11.200.000 đồng; vui lòng theo dõi chứng từ theo từng hạng mục.", true, At(referenceDate.AddDays(-9), 2)),
            new("ai-02", null, EventRoutingKeys.ActivityCreated, "Bạn đã được thêm vào seminar AI Ethics", "Sự kiện diễn ra tại phòng AL-101; vui lòng hoàn thành tài liệu đọc trước sự kiện.", false, At(referenceDate.AddDays(-8), 8)),
            new("ai-04", null, "membership.rejected", "Kết quả hồ sơ CLB AI & Data", "Hồ sơ chưa đáp ứng thời gian cam kết; bạn có thể cập nhật và đăng ký lại ở kỳ tiếp theo.", true, At(referenceDate.AddDays(-23), 8)),
            new("vol-treasurer", null, EventRoutingKeys.BudgetProposalSubmitted, "Đề xuất Áo ấm mùa đông đã được gửi", "Đề xuất đang chờ quản lý CLB kiểm tra trước khi chuyển phê duyệt cuối.", false, At(referenceDate.AddDays(-1), 4)),
            new("vol-03", null, EventRoutingKeys.ActivityCreated, "Phân công truyền thông Trung thu sẻ chia", "Bạn phụ trách bộ nhận diện, ảnh minh chứng và tổng hợp nội dung sau chương trình.", false, At(referenceDate.AddDays(-3), 9)),
            new("vol-04", null, EventRoutingKeys.ActivityCreated, "Đăng ký Chủ nhật Xanh đợt mới", "Kế hoạch phân loại rác học kỳ mới đã mở; vui lòng xác nhận ca tham gia.", true, At(referenceDate.AddDays(-7), 9)),
            new("ai-02", null, "club.cross-membership.approved", "Đã duyệt thành viên liên CLB", "Bạn được tham gia dự án chatbot cộng đồng của CLB Tình nguyện Cóc Xanh.", true, At(referenceDate.AddDays(-94), 8))
        };

        return new DemoCatalog
        {
            ReferenceDate = referenceDate,
            Users = users,
            Clubs = clubs,
            Memberships = memberships,
            ClubApplications = clubApplications,
            Activities = activities,
            Reports = reports,
            BudgetProposals = budgets,
            Notifications = notifications
        };
    }

    public IReadOnlyList<string> ValidateDefinition()
    {
        var errors = new List<string>();
        var allowedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AuthRoles.Admin,
            AuthRoles.ClubManager,
            AuthRoles.ClubMember
        };

        EnsureUnique(Users.Select(x => x.Key), "user key", errors);
        EnsureUnique(Users.Select(x => x.Username), "username", errors);
        EnsureUnique(Clubs.Select(x => x.Code), "club code", errors);
        EnsureUnique(Activities.Select(x => x.Key), "activity key", errors);
        EnsureUnique(Reports.Select(x => x.Key), "report seed key", errors);
        EnsureUnique(BudgetProposals.Select(x => x.Key), "budget key", errors);

        var users = Users.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var clubs = Clubs.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var activities = Activities.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var reports = Reports.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var user in Users.Where(x => !allowedRoles.Contains(x.Role)))
        {
            errors.Add($"User '{user.Key}' has out-of-scope role '{user.Role}'.");
        }

        foreach (var club in Clubs)
        {
            if (!users.TryGetValue(club.ManagerUserKey, out var manager)
                || manager.Role != AuthRoles.ClubManager)
            {
                errors.Add($"Club '{club.Code}' does not reference a CLUB_MANAGER user.");
            }
        }

        foreach (var membership in Memberships)
        {
            if (!clubs.ContainsKey(membership.ClubCode))
                errors.Add($"Membership references unknown club '{membership.ClubCode}'.");
            if (!users.ContainsKey(membership.UserKey))
                errors.Add($"Membership references unknown user '{membership.UserKey}'.");
        }

        EnsureUnique(
            Memberships.Select(x => $"{x.ClubCode}|{x.UserKey}"),
            "club membership",
            errors);

        var approvedMemberships = Memberships
            .Where(x => x.Status == ClubMembershipStatuses.Approved)
            .Select(x => $"{x.ClubCode}|{x.UserKey}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var activity in Activities)
        {
            if (!clubs.ContainsKey(activity.ClubCode))
                errors.Add($"Activity '{activity.Key}' references unknown club '{activity.ClubCode}'.");
            if (!users.ContainsKey(activity.CreatedByUserKey))
                errors.Add($"Activity '{activity.Key}' references unknown creator '{activity.CreatedByUserKey}'.");
            if (activity.EndTimeUtc <= activity.StartTimeUtc)
                errors.Add($"Activity '{activity.Key}' has an invalid time range.");
            foreach (var participant in activity.ParticipantUserKeys)
            {
                if (!approvedMemberships.Contains($"{activity.ClubCode}|{participant}"))
                    errors.Add($"Activity '{activity.Key}' contains non-approved member '{participant}'.");
            }
            foreach (var attendance in activity.Attendances)
            {
                if (!activity.ParticipantUserKeys.Contains(attendance.UserKey, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"Attendance in '{activity.Key}' references non-participant '{attendance.UserKey}'.");
            }
            EnsureUnique(
                activity.Attendances.Select(x => $"{x.UserKey}|{x.Date:yyyy-MM-dd}"),
                $"attendance row in activity '{activity.Key}'",
                errors);

            var vietnamStartDate = DateOnly.FromDateTime(
                activity.StartTimeUtc.ToOffset(TimeSpan.FromHours(7)).DateTime);
            var vietnamEndDate = DateOnly.FromDateTime(
                activity.EndTimeUtc.ToOffset(TimeSpan.FromHours(7)).DateTime);
            if (activity.Attendances.Any(x => x.Date < vietnamStartDate || x.Date > vietnamEndDate))
                errors.Add($"Activity '{activity.Key}' has attendance outside its date range.");
            if (activity.Status == ActivityStatuses.Completed && activity.Attendances.Count == 0)
                errors.Add($"Completed activity '{activity.Key}' has no attendance history.");
            if (activity.Status == ActivityStatuses.Scheduled && activity.Attendances.Count > 0)
                errors.Add($"Scheduled activity '{activity.Key}' already has attendance history.");
            if (activity.SourceReportKey is not null && !reports.ContainsKey(activity.SourceReportKey))
                errors.Add($"Activity '{activity.Key}' references unknown report '{activity.SourceReportKey}'.");
        }

        foreach (var report in Reports)
        {
            if (!clubs.ContainsKey(report.ClubCode))
                errors.Add($"Report '{report.Key}' references unknown club '{report.ClubCode}'.");
            if (!users.ContainsKey(report.CreatedByUserKey))
                errors.Add($"Report '{report.Key}' references unknown creator '{report.CreatedByUserKey}'.");
            if (report.Details.Count == 0)
                errors.Add($"Report '{report.Key}' has no detail rows.");
            if (report.Details.Any(x => x.ParticipantCount < 0 || x.BudgetSpent < 0))
                errors.Add($"Report '{report.Key}' contains negative metrics.");
            if (report.SubmittedAtUtc.HasValue && report.SubmittedAtUtc < report.CreatedAtUtc)
                errors.Add($"Report '{report.Key}' was submitted before it was created.");
            if (report.ReviewedAtUtc.HasValue
                && (!report.SubmittedAtUtc.HasValue || report.ReviewedAtUtc < report.SubmittedAtUtc))
                errors.Add($"Report '{report.Key}' has an invalid review timestamp.");
            if (report.Feedback.Any(x => x.DaysAfterCreation < 0))
                errors.Add($"Report '{report.Key}' has feedback before creation.");
            if (report.ReportType == FutureEventReportRules.ReportType
                && (report.Details.Count != 1 || report.Details.Single().ActivityDate <= ReferenceDate))
                errors.Add($"Future-event report '{report.Key}' must contain one future activity.");
        }

        foreach (var budget in BudgetProposals)
        {
            if (!clubs.ContainsKey(budget.ClubCode))
                errors.Add($"Budget '{budget.Key}' references unknown club '{budget.ClubCode}'.");
            if (!users.ContainsKey(budget.ProposedByUserKey))
                errors.Add($"Budget '{budget.Key}' references unknown proposer '{budget.ProposedByUserKey}'.");
            if (budget.SourceReportKey is not null && !reports.ContainsKey(budget.SourceReportKey))
                errors.Add($"Budget '{budget.Key}' references unknown report '{budget.SourceReportKey}'.");
            if (budget.ActivityKey is not null && !activities.ContainsKey(budget.ActivityKey))
                errors.Add($"Budget '{budget.Key}' references unknown activity '{budget.ActivityKey}'.");
            if (budget.RequestedAmount <= 0 || budget.ApprovedAmount < 0)
                errors.Add($"Budget '{budget.Key}' contains invalid amounts.");
            if (budget.ApprovedAmount > budget.RequestedAmount)
                errors.Add($"Budget '{budget.Key}' approves more than requested.");

            var isApprovedTreasurer = Memberships.Any(x =>
                x.ClubCode == budget.ClubCode
                && x.UserKey == budget.ProposedByUserKey
                && x.ClubRole == ClubMemberRoles.Treasurer
                && x.Status == ClubMembershipStatuses.Approved);
            if (!isApprovedTreasurer)
                errors.Add($"Budget '{budget.Key}' was not proposed by an approved club treasurer.");

            if (budget.ManagerReviewedAtUtc.HasValue
                && budget.ManagerReviewedAtUtc < budget.ProposedAtUtc)
                errors.Add($"Budget '{budget.Key}' was manager-reviewed before submission.");
            if (budget.ReviewedAtUtc.HasValue
                && budget.ReviewedAtUtc < (budget.ManagerReviewedAtUtc ?? budget.ProposedAtUtc))
                errors.Add($"Budget '{budget.Key}' has an invalid final-review timestamp.");
            if (budget.Settlement is not null)
            {
                if (budget.Settlement.SubmittedAtUtc < (budget.ReviewedAtUtc ?? budget.ProposedAtUtc))
                    errors.Add($"Budget '{budget.Key}' was settled before approval.");
                if (budget.Settlement.ReviewedAtUtc.HasValue
                    && budget.Settlement.ReviewedAtUtc < budget.Settlement.SubmittedAtUtc)
                    errors.Add($"Budget '{budget.Key}' has an invalid settlement-review timestamp.");
                if (budget.ActivityKey is not null
                    && activities.TryGetValue(budget.ActivityKey, out var activity)
                    && budget.Settlement.SubmittedAtUtc < activity.EndTimeUtc)
                    errors.Add($"Budget '{budget.Key}' was settled before its activity ended.");
            }
        }

        foreach (var notification in Notifications)
        {
            if (notification.RecipientUserKey is not null && !users.ContainsKey(notification.RecipientUserKey))
                errors.Add($"Notification '{notification.Title}' references unknown user '{notification.RecipientUserKey}'.");
            if (notification.RecipientUserKey is null && notification.RecipientRole is null)
                errors.Add($"Notification '{notification.Title}' has no recipient.");
            if (notification.RecipientUserKey is not null && notification.RecipientRole is not null)
                errors.Add($"Notification '{notification.Title}' has both a user and role recipient.");
        }

        if (Users.Count != 16) errors.Add($"Expected 16 demo users, found {Users.Count}.");
        if (Users.Count(x => x.Role == AuthRoles.Admin) != 1) errors.Add("Expected exactly one ADMIN account.");
        if (Users.Count(x => x.Role == AuthRoles.ClubManager) != 3) errors.Add("Expected exactly three CLUB_MANAGER accounts.");
        if (Users.Count(x => x.Role == AuthRoles.ClubMember) != 12) errors.Add("Expected exactly twelve STUDENT accounts.");
        if (Clubs.Count != 3) errors.Add($"Expected 3 demo clubs, found {Clubs.Count}.");
        if (Activities.Count != 12) errors.Add($"Expected 12 demo activities, found {Activities.Count}.");
        if (Reports.Count != 12) errors.Add($"Expected 12 demo reports, found {Reports.Count}.");

        return errors;
    }

    private static void EnsureUnique(IEnumerable<string> values, string label, ICollection<string> errors)
    {
        foreach (var duplicate in values
                     .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                     .Where(x => x.Count() > 1)
                     .Select(x => x.Key))
        {
            errors.Add($"Duplicate {label}: '{duplicate}'.");
        }
    }
}
