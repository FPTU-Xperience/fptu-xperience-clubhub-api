using System.Text;
using FullDemoDataSeeder;

Console.OutputEncoding = Encoding.UTF8;
try
{
    var command = args.FirstOrDefault() ?? "plan";
    if (command is not ("plan" or "seed" or "verify"))
        throw new ArgumentException("Use plan (read only), seed, or verify.");
    await using var demo = new FullDemoSeeder(FullDemoOptions.Read());
    await demo.RunAsync(command);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FullDemo v2 failed: {exception.GetBaseException().Message}");
    return 1;
}
