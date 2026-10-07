namespace ClubReportHub.Shared.Auth;

/// <summary>
/// Chuẩn hóa mã cơ sở (Campus Codes) của Trường Đại học FPT:
/// 1. Hà Nội (Hòa Lạc) - HAN
/// 2. TP. Hồ Chí Minh - HCM
/// 3. Đà Nẵng - DAN
/// 4. Cần Thơ - CAN
/// 5. Quy Nhơn - QNH
/// 6. Toàn trường (Global / Hệ thống chung) - GLOBAL
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

    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return Hanoi;
        var upper = code.Trim().ToUpperInvariant();
        return upper switch
        {
            "HAN" or "HN" or "HOALAC" or "HÒA LẠC" or "HÀ NỘI" or "HANOI" => Hanoi,
            "HCM" or "HỒ CHÍ MINH" or "HOCHIMINH" or "HO CHI MINH" or "SG" or "SAIGON" => HoChiMinh,
            "DAN" or "DN" or "ĐÀ NẴNG" or "DANANG" or "DA NANG" => Danang,
            "CAN" or "CT" or "CẦN THƠ" or "CANTHO" or "CAN THO" => CanTho,
            "QNH" or "QN" or "QUY NHƠN" or "QUYNHON" or "QUY NHON" => QuyNhon,
            "GLOBAL" or "ALL" or "TOÀN TRƯỜNG" or "TOAN TRUONG" or "SYSTEM" => Global,
            _ => Hanoi
        };
    }

    public static string InferFromStudentCodeOrEmail(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return Hanoi;
        var trimmed = identifier.Trim().ToUpperInvariant();

        // Check explicit campus domain/token: ctsv.hn@fpt.edu.vn, ctsv.hcm@fpt.edu.vn,...
        if (trimmed.Contains(".HN@") || trimmed.Contains("_HN@") || trimmed.Contains("-HN@") || trimmed.StartsWith("CTSV.HN") || trimmed.StartsWith("CTSV_HN"))
            return Hanoi;
        if (trimmed.Contains(".HCM@") || trimmed.Contains("_HCM@") || trimmed.Contains("-HCM@") || trimmed.StartsWith("CTSV.HCM") || trimmed.StartsWith("CTSV_HCM"))
            return HoChiMinh;
        if (trimmed.Contains(".DN@") || trimmed.Contains("_DN@") || trimmed.Contains("-DN@") || trimmed.StartsWith("CTSV.DN") || trimmed.StartsWith("CTSV_DN") || trimmed.Contains(".DAN@"))
            return Danang;
        if (trimmed.Contains(".CT@") || trimmed.Contains("_CT@") || trimmed.Contains("-CT@") || trimmed.StartsWith("CTSV.CT") || trimmed.StartsWith("CTSV_CT") || trimmed.Contains(".CAN@"))
            return CanTho;
        if (trimmed.Contains(".QN@") || trimmed.Contains("_QN@") || trimmed.Contains("-QN@") || trimmed.StartsWith("CTSV.QN") || trimmed.StartsWith("CTSV_QN") || trimmed.Contains(".QNH@"))
            return QuyNhon;

        // Strip email domain if present to inspect username / MSSV
        var atIndex = trimmed.IndexOf('@');
        var username = atIndex > 0 ? trimmed[..atIndex] : trimmed;

        // FPT Student Code prefix detection
        // Hanoi: HE, HA, HS, HF
        if (username.StartsWith("HE") || username.StartsWith("HA") || username.StartsWith("HS") || username.StartsWith("HF"))
            return Hanoi;

        // Ho Chi Minh: SE, SS, SA, IA, IB
        if (username.StartsWith("SE") || username.StartsWith("SS") || username.StartsWith("SA") || username.StartsWith("IA") || username.StartsWith("IB"))
            return HoChiMinh;

        // Danang: DE, DS, DA
        if (username.StartsWith("DE") || username.StartsWith("DS") || username.StartsWith("DA"))
            return Danang;

        // Can Tho: CE, CS
        if (username.StartsWith("CE") || username.StartsWith("CS"))
            return CanTho;

        // Quy Nhon: QE, QS
        if (username.StartsWith("QE") || username.StartsWith("QS"))
            return QuyNhon;

        return Hanoi;
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
