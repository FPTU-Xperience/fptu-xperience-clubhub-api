namespace ClubReportHub.Shared.Experience;

public static class ExperienceCategories
{
    public const string Academic = "Academic";
    public const string Research = "Research";
    public const string Global = "Global";
    public const string CultureSports = "CultureSports";
    public const string Community = "Community";
    public const string Entrepreneurship = "Entrepreneurship";
    public const string RealWorldWork = "RealWorldWork";

    public static readonly string[] All =
    [
        Academic,
        Research,
        Global,
        CultureSports,
        Community,
        Entrepreneurship,
        RealWorldWork
    ];

    public static bool IsValid(string? category) =>
        !string.IsNullOrWhiteSpace(category) &&
        All.Contains(category.Trim(), StringComparer.OrdinalIgnoreCase);
}

public static class EffortTiers
{
    public const string T1 = "T1";
    public const string T2 = "T2";
    public const string T3 = "T3";
    public const string T4 = "T4";
    public const string T5 = "T5";

    public static readonly string[] All = [T1, T2, T3, T4, T5];

    public static bool IsValid(string? tier) =>
        !string.IsNullOrWhiteSpace(tier) &&
        All.Contains(tier.Trim(), StringComparer.OrdinalIgnoreCase);
}

public static class ContributionRoles
{
    public const string Participant = "Participant";
    public const string Contributor = "Contributor";
    public const string Product = "Product";
    public const string Organizer = "Organizer";
    public const string Leader = "Leader";

    public static readonly string[] All = [Participant, Contributor, Product, Organizer, Leader];

    public static bool IsValid(string? role) =>
        !string.IsNullOrWhiteSpace(role) &&
        All.Contains(role.Trim(), StringComparer.OrdinalIgnoreCase);
}

public static class ActivityScales
{
    public const string Club = "Club";
    public const string Campus = "Campus";
    public const string FptEducation = "FptEducation";
    public const string National = "National";
    public const string International = "International";

    public static readonly string[] All = [Club, Campus, FptEducation, National, International];

    public static bool IsValid(string? scale) =>
        !string.IsNullOrWhiteSpace(scale) &&
        All.Contains(scale.Trim(), StringComparer.OrdinalIgnoreCase);
}

public static class BonusResults
{
    public const string None = "None";
    public const string Finalist = "Finalist";
    public const string Prize = "Prize";
    public const string RealWorldOutput = "RealWorldOutput";

    public static readonly string[] All = [None, Finalist, Prize, RealWorldOutput];

    public static bool IsValid(string? bonusResult) =>
        string.IsNullOrWhiteSpace(bonusResult) ||
        All.Contains(bonusResult.Trim(), StringComparer.OrdinalIgnoreCase);
}

public static class DeclarationStatuses
{
    public const string Submitted = "Submitted";
    public const string UnderReview = "UnderReview";
    public const string Approved = "Approved";
    public const string RevisionRequested = "RevisionRequested";
    public const string Rejected = "Rejected";

    public static readonly string[] All =
    [
        Submitted,
        UnderReview,
        Approved,
        RevisionRequested,
        Rejected
    ];

    public static bool IsValid(string? status) =>
        !string.IsNullOrWhiteSpace(status) &&
        All.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase);
}

public static class DeclarationDecisions
{
    public const string Approved = "Approved";
    public const string RevisionRequested = "RevisionRequested";
    public const string Rejected = "Rejected";

    public static readonly string[] All = [Approved, RevisionRequested, Rejected];

    public static bool IsValid(string? decision) =>
        !string.IsNullOrWhiteSpace(decision) &&
        All.Contains(decision.Trim(), StringComparer.OrdinalIgnoreCase);
}

public static class RadarPillars
{
    public const string PlusOne = ExperienceCategories.RealWorldWork;

    public static readonly string[] CoreSix =
    [
        ExperienceCategories.Academic,
        ExperienceCategories.Research,
        ExperienceCategories.Global,
        ExperienceCategories.CultureSports,
        ExperienceCategories.Community,
        ExperienceCategories.Entrepreneurship
    ];

    public static readonly string[] AllSeven =
    [
        .. CoreSix,
        PlusOne
    ];

    public static string GetVietnameseName(string pillar) => pillar switch
    {
        ExperienceCategories.Academic => "Học tập",
        ExperienceCategories.Research => "Nghiên cứu",
        ExperienceCategories.Global => "Quốc tế",
        ExperienceCategories.CultureSports => "Thể thao & văn hóa",
        ExperienceCategories.Community => "Cộng đồng",
        ExperienceCategories.Entrepreneurship => "Khởi nghiệp",
        ExperienceCategories.RealWorldWork => "Thực chiến dự án (+1)",
        _ => pillar
    };

    public static string GetDescription(string pillar) => pillar switch
    {
        ExperienceCategories.Academic => "Các hoạt động học thuật, chứng chỉ kỹ năng chuyên môn, hội thảo chuyên ngành và cuộc thi kiến thức.",
        ExperienceCategories.Research => "Đề tài nghiên cứu khoa học, bài báo công bố, giải thưởng học thuật và lab thực nghiệm.",
        ExperienceCategories.Global => "Giao lưu quốc tế, chương trình trao đổi sinh viên, hoạt động ngoại ngữ và trải nghiệm đa văn hóa.",
        ExperienceCategories.CultureSports => "Rèn luyện thể chất, thi đấu thể thao, hoạt động văn nghệ và phát triển bản thân.",
        ExperienceCategories.Community => "Hoạt động thiện nguyện, phục vụ cộng đồng, phát triển bền vững và trách nhiệm xã hội.",
        ExperienceCategories.Entrepreneurship => "Dự án khởi nghiệp, đổi mới sáng tạo, chuyển giao công nghệ và phát triển sản phẩm thực tế.",
        ExperienceCategories.RealWorldWork => "Dự án thực tế tại doanh nghiệp, làm việc chuyên nghiệp (On-the-job training) và hợp tác phát triển.",
        _ => string.Empty
    };
}

public static class ExperienceMasteryTiers
{
    public const string Unexplored = "Unexplored";
    public const string Starter = "Starter";
    public const string Practitioner = "Practitioner";
    public const string Contributor = "Contributor";
    public const string Leader = "Leader";

    public static readonly string[] All =
    [
        Unexplored,
        Starter,
        Practitioner,
        Contributor,
        Leader
    ];
}

public static class ExperienceTitles
{
    public const string WellRounded = "Người Toàn Diện";
    public const string Unexplored = "Chưa khám phá";

    public static string StrengthOf(string category) => $"Thế mạnh {category}";
}
