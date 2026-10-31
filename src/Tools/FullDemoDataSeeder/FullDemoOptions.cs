using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FullDemoDataSeeder;

public sealed record FullDemoOptions(DateOnly ReferenceDate, string JournalPath, string ApiBaseUrl,
    string AssetDirectory, string Password, string ProtectedEmail, IReadOnlyDictionary<string, string> Connections)
{
    public const string Dataset = "FULLDEMO_V2";
    public string AssetBaseUrl => (Environment.GetEnvironmentVariable("FullDemo__AssetBaseUrl") ?? ApiBaseUrl + "/demo-assets").TrimEnd('/');
    public static readonly string[] Services = ["Auth", "Club", "Activity", "Admin", "Report", "Finance", "Notification", "Export"];
    public static FullDemoOptions Read()
    {
        string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException($"Missing {name}.");
        var date = Environment.GetEnvironmentVariable("FullDemo__ReferenceDate");
        var reference = date is null ? DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime)
            : DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (Environment.GetEnvironmentVariable("DemoData__ResetAll")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("FullDemo never permits ResetAll.");
        return new(reference, Path.GetFullPath(Required("FullDemo__JournalPath")),
            Required("FullDemo__ApiBaseUrl").TrimEnd('/'), Path.GetFullPath(Required("FullDemo__AssetDirectory")),
            Environment.GetEnvironmentVariable("FullDemo__Password") ?? "", "mr28042005@gmail.com",
            Services.ToDictionary(s => s, s => Required($"ConnectionStrings__{s}")));
    }

    public static Guid StableId(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{Dataset}:{key}"))[..16]);
    public DateTimeOffset At(int dayOffset, int hour = 9) => new(ReferenceDate.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(7));
}
