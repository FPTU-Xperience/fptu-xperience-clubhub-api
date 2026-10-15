using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ActivityService.Contracts;
using ActivityService.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace ActivityService.Services;

public static class AttendanceChallenge
{
    public static bool Configured(IConfiguration configuration) =>
        Encoding.UTF8.GetByteCount(configuration["Attendance:SigningKey"] ?? string.Empty) >= 32;

    public static string Issue(ActivitySession session, DateTimeOffset now, IConfiguration configuration)
    {
        if (!Configured(configuration)) throw new InvalidOperationException("Attendance signing key is not configured.");
        var text = FormattableString.Invariant($"attendance-v1|{session.ActivityId}|{session.Id}|{session.Version:N}|{now.ToUnixTimeSeconds() / 30}");
        var bytes = Encoding.UTF8.GetBytes(text);
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(configuration["Attendance:SigningKey"]!), bytes);
        return WebEncoders.Base64UrlEncode(bytes) + "." + WebEncoders.Base64UrlEncode(signature);
    }

    public static bool Validate(string? challenge, ActivitySession session, DateTimeOffset now, IConfiguration configuration)
    {
        if (!Configured(configuration) || string.IsNullOrWhiteSpace(challenge) || challenge.Length > 512) return false;
        try
        {
            var parts = challenge.Split('.');
            if (parts.Length != 2) return false;
            var bytes = WebEncoders.Base64UrlDecode(parts[0]);
            var actual = WebEncoders.Base64UrlDecode(parts[1]);
            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(configuration["Attendance:SigningKey"]!), bytes);
            if (actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual, expected)) return false;
            var fields = Encoding.UTF8.GetString(bytes).Split('|');
            return fields.Length == 5 && fields[0] == "attendance-v1"
                && fields[1] == session.ActivityId.ToString(CultureInfo.InvariantCulture)
                && fields[2] == session.Id.ToString(CultureInfo.InvariantCulture)
                && fields[3] == session.Version.ToString("N")
                && long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var slot)
                && slot == now.ToUnixTimeSeconds() / 30;
        }
        catch (FormatException) { return false; }
    }

    public static bool InsideGeofence(ActivitySession session, AttendanceLocation? location, IConfiguration configuration)
    {
        if (location is null || !double.IsFinite(location.Latitude) || !double.IsFinite(location.Longitude)
            || location.Latitude is < -90 or > 90 || location.Longitude is < -180 or > 180
            || !double.IsFinite(location.AccuracyMeters) || location.AccuracyMeters <= 0
            || location.AccuracyMeters > configuration.GetValue("Attendance:MaxAccuracyMeters", 50d)
            || !session.Latitude.HasValue || !session.Longitude.HasValue || !session.RadiusMeters.HasValue) return false;
        const double radians = Math.PI / 180;
        var dLat = (location.Latitude - session.Latitude.Value) * radians;
        var dLon = (location.Longitude - session.Longitude.Value) * radians;
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(session.Latitude.Value * radians)
            * Math.Cos(location.Latitude * radians) * Math.Pow(Math.Sin(dLon / 2), 2);
        var distance = 6371000d * 2 * Math.Atan2(Math.Sqrt(Math.Clamp(a, 0, 1)), Math.Sqrt(Math.Clamp(1 - a, 0, 1)));
        return distance <= session.RadiusMeters.Value;
    }
}
