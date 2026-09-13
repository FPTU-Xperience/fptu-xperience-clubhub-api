using System.Text;
using DemoDataSeeder;

Console.OutputEncoding = Encoding.UTF8;

try
{
    var enabled = bool.TryParse(
        Environment.GetEnvironmentVariable("DemoData__Enabled"),
        out var enabledValue) && enabledValue;
    if (!enabled)
    {
        Console.Error.WriteLine(
            "Demo seeding is disabled. Set DemoData__Enabled=true to run this one-shot tool.");
        return 2;
    }

    var options = DemoSeederOptions.FromEnvironment();
    var seeder = new DemoDatasetSeeder(options);
    await seeder.RunAsync();
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"[DEMO SEED FAILED] {exception.Message}");
    Console.Error.WriteLine(exception);
    return 1;
}
