using System.Globalization;

namespace DemoDataSeeder;

public sealed record DemoIdentityOverrides(
    string AdminEmail,
    string ClubManagerEmail,
    string StudentEmail)
{
    public static DemoIdentityOverrides Default { get; } = new(
        "admin@fpt.edu.vn",
        "manager.tech@fpt.edu.vn",
        "se170002@fpt.edu.vn");
}

public sealed record DemoSeederOptions(
    bool Enabled,
    bool ResetAll,
    bool ValidateOnly,
    DateOnly ReferenceDate,
    TimeSpan DatabaseWaitTimeout,
    DemoIdentityOverrides IdentityOverrides,
    string AuthConnectionString,
    string ClubConnectionString,
    string ActivityConnectionString,
    string ReportConnectionString,
    string FinanceConnectionString,
    string NotificationConnectionString,
    string ExportConnectionString)
{
    public static DemoSeederOptions FromEnvironment()
    {
        var referenceDateValue = GetOptional("DemoData__ReferenceDate", "2026-09-11");
        if (!DateOnly.TryParseExact(
                referenceDateValue,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var referenceDate))
        {
            throw new InvalidOperationException(
                "DemoData__ReferenceDate must use yyyy-MM-dd format.");
        }

        var waitSecondsValue = GetOptional("DemoData__DatabaseWaitSeconds", "120");
        if (!int.TryParse(waitSecondsValue, out var waitSeconds) || waitSeconds is < 10 or > 600)
        {
            throw new InvalidOperationException(
                "DemoData__DatabaseWaitSeconds must be between 10 and 600.");
        }

        var options = new DemoSeederOptions(
            Enabled: GetBoolean("DemoData__Enabled"),
            ResetAll: GetBoolean("DemoData__ResetAll"),
            ValidateOnly: GetBoolean("DemoData__ValidateOnly"),
            ReferenceDate: referenceDate,
            DatabaseWaitTimeout: TimeSpan.FromSeconds(waitSeconds),
            IdentityOverrides: new DemoIdentityOverrides(
                GetOptional("DemoData__AdminEmail", DemoIdentityOverrides.Default.AdminEmail),
                GetOptional("DemoData__ClubManagerEmail", DemoIdentityOverrides.Default.ClubManagerEmail),
                GetOptional("DemoData__StudentEmail", DemoIdentityOverrides.Default.StudentEmail)),
            AuthConnectionString: GetRequired("ConnectionStrings__Auth"),
            ClubConnectionString: GetRequired("ConnectionStrings__Club"),
            ActivityConnectionString: GetRequired("ConnectionStrings__Activity"),
            ReportConnectionString: GetRequired("ConnectionStrings__Report"),
            FinanceConnectionString: GetRequired("ConnectionStrings__Finance"),
            NotificationConnectionString: GetRequired("ConnectionStrings__Notification"),
            ExportConnectionString: GetRequired("ConnectionStrings__Export"));

        ValidateIdentityOverrides(options.IdentityOverrides);

        if (options.ResetAll)
        {
            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                ?? "Development";

            if (string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(env, "Staging", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Destructive database reset (DemoData__ResetAll=true) is strictly forbidden in Production or Staging environments.");
            }

            var confirm = GetBoolean("DemoData__ConfirmDestructiveReset");
            if (!confirm)
            {
                throw new InvalidOperationException(
                    "Destructive database reset requires explicit confirmation. Set DemoData__ConfirmDestructiveReset=true along with DemoData__ResetAll=true.");
            }
        }

        return options;
    }

    private static bool GetBoolean(string key) =>
        bool.TryParse(Environment.GetEnvironmentVariable(key), out var value) && value;

    private static string GetRequired(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Required environment variable '{key}' is missing.")
            : value.Trim();
    }

    private static string GetOptional(string key, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static void ValidateIdentityOverrides(DemoIdentityOverrides overrides)
    {
        var entries = new[]
        {
            ("DemoData__AdminEmail", overrides.AdminEmail),
            ("DemoData__ClubManagerEmail", overrides.ClubManagerEmail),
            ("DemoData__StudentEmail", overrides.StudentEmail)
        };

        foreach (var (key, email) in entries)
        {
            // The demo uses the e-mail as Username as well, and Username is
            // limited to 100 characters by the Auth database schema.
            if (email.Length > 100 || !System.Net.Mail.MailAddress.TryCreate(email, out _))
            {
                throw new InvalidOperationException($"{key} must be a valid e-mail address up to 100 characters.");
            }
        }

        if (entries.Select(x => x.Item2).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
        {
            throw new InvalidOperationException(
                "DemoData Google login e-mails must be distinct so each actor test account is unambiguous.");
        }
    }
}
