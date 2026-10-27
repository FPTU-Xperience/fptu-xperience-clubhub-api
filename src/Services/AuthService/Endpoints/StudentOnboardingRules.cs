namespace AuthService.Endpoints;

public static class StudentOnboardingRules
{
    private static readonly HashSet<string> Majors = new(StringComparer.Ordinal)
    {
        "Kỹ thuật phần mềm", "Trí tuệ nhân tạo", "An toàn thông tin", "Thiết kế mỹ thuật số",
        "Truyền thông đa phương tiện", "Kinh doanh quốc tế", "Digital Marketing", "Ngôn ngữ Anh", "Ngôn ngữ Nhật"
    };

    public static bool IsMajor(string? value) => value is not null && Majors.Contains(value.Trim());
}
