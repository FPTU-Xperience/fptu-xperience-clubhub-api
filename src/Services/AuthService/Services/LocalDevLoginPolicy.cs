using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AuthService.Services;

public static class LocalDevLoginPolicy
{
    public static bool Allows(HttpRequest request, IHostEnvironment environment, IConfiguration configuration, string? email)
    {
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing") || environment.IsEnvironment("Test"))
            return true;

        var key = configuration["LocalDevLogin:AccessKey"];
        var supplied = request.Headers["X-Local-Dev-Login-Key"].ToString();
        if (string.IsNullOrEmpty(key) || key.Length < 32 || string.IsNullOrEmpty(supplied)
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            return false;

        if (!DateTimeOffset.TryParse(configuration["LocalDevLogin:ExpiresAtUtc"], CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var expiry) || expiry <= DateTimeOffset.UtcNow)
            return false;

        return (configuration["LocalDevLogin:AllowedEmails"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(email?.Trim() ?? "", StringComparer.OrdinalIgnoreCase);
    }
}
