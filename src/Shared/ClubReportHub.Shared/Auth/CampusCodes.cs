namespace ClubReportHub.Shared.Auth;

/// <summary>
/// Chuẩn hóa mã và tên 5 cơ sở của Trường Đại học FPT:
/// 1. Hà Nội (Hòa Lạc) - HAN
/// 2. TP. Hồ Chí Minh - HCM
/// 3. Đà Nẵng - DAN
/// 4. Cần Thơ - CAN
/// 5. Quy Nhơn - QNH
/// 6. Toàn trường (Global / Hệ thống chung) - GLOBAL
///
/// Lưu ý: Tên và mã cơ sở là thuộc tính độc lập, không gắn hoặc ràng buộc
/// với bất kỳ tiền tố mã số sinh viên (MSSV) nào.
/// </summary>
public static class CampusCodes
{
    public const string Hanoi = "HAN";
    public const string HoChiMinh = "HCM";
    public const string Danang = "DAN";
    public const string CanTho = "CAN";
    public const string QuyNhon = "QNH";
    public const string Global = "GLOBAL";

    public static readonly string[] FiveCampuses = [Hanoi, HoChiMinh, Danang, CanTho, QuyNhon];
    public static readonly string[] All = [Hanoi, HoChiMinh, Danang, CanTho, QuyNhon, Global];

    public static bool IsValid(string? code) =>
        !string.IsNullOrWhiteSpace(code) && All.Contains(code.Trim().ToUpperInvariant());

    public static string Normalize(string? code, string defaultCampus = Hanoi)
    {
        if (string.IsNullOrWhiteSpace(code)) return defaultCampus;
        var upper = code.Trim().ToUpperInvariant();
        return upper switch
        {
            "HAN" or "HN" or "HOALAC" or "HÒA LẠC" or "HÀ NỘI" or "HANOI" => Hanoi,
            "HCM" or "HỒ CHÍ MINH" or "HOCHIMINH" or "HO CHI MINH" or "SG" or "SAIGON" => HoChiMinh,
            "DAN" or "DN" or "ĐÀ NẴNG" or "DANANG" or "DA NANG" => Danang,
            "CAN" or "CT" or "CẦN THƠ" or "CANTHO" or "CAN THO" => CanTho,
            "QNH" or "QN" or "QUY NHƠN" or "QUYNHON" or "QUY NHON" => QuyNhon,
            "GLOBAL" or "ALL" or "TOÀN TRƯỜNG" or "TOAN TRUONG" or "SYSTEM" => Global,
            _ => defaultCampus
        };
    }

    public static string GetDisplayName(string? code)
    {
        var normalized = Normalize(code);
        return normalized switch
        {
            Hanoi => "Hà Nội (Hòa Lạc)",
            HoChiMinh => "TP. Hồ Chí Minh",
            Danang => "Đà Nẵng",
            CanTho => "Cần Thơ",
            QuyNhon => "Quy Nhơn",
            Global => "Toàn trường",
            _ => "Hà Nội"
        };
    }
}
