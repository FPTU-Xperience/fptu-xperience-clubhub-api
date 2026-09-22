using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.Text.Json;

namespace ActivityService.Data;

public sealed class ActivityDbContextFactory : IDesignTimeDbContextFactory<ActivityDbContext>
{
    public ActivityDbContext CreateDbContext(string[] args)
    {
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        string? connectionString = null;

        if (File.Exists(configPath))
        {
            var json = JsonDocument.Parse(File.ReadAllText(configPath));
            connectionString = json.RootElement
                .GetProperty("ConnectionStrings")
                .GetProperty("DefaultConnection")
                .GetString();
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Server=localhost;Database=ClubReportHub_Activity;Trusted_Connection=True;TrustServerCertificate=True";
        }

        var options = new DbContextOptionsBuilder<ActivityDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new ActivityDbContext(options);
    }
}
