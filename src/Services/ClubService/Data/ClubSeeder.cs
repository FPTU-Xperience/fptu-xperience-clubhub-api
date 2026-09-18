using Microsoft.Extensions.Logging;

namespace ClubService.Data;

public static class ClubSeeder
{
    public static Task SeedAsync(ClubDbContext db, ILogger? logger = null)
    {
        // Demo content is owned by the explicit, opt-in DemoDataSeeder tool.
        // Service startup must never rewrite user-managed club records.
        logger?.LogInformation(
            "ClubSeeder: No static seed data needed; clubs are managed through application workflows");
        return Task.CompletedTask;
    }
}
