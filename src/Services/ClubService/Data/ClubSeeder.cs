using ClubService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClubService.Data;

public static class ClubSeeder
{
    public static async Task<int> SeedAsync(ClubDbContext db, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        var existingList = await db.Clubs
            .IgnoreQueryFilters()
            .Select(c => c.Code)
            .ToListAsync(cancellationToken);
        var existingCodes = new HashSet<string>(existingList, StringComparer.OrdinalIgnoreCase);

        var addedClubs = new List<Club>();
        foreach (var def in FptuClubRegistry.Clubs)
        {
            if (existingCodes.Contains(def.Code))
            {
                continue;
            }

            addedClubs.Add(new Club
            {
                Code = def.Code,
                Name = def.Name,
                Category = def.Category,
                CampusCode = def.CampusCode,
                Description = def.Description,
                ContactEmail = def.ContactEmail,
                ContactPhone = def.ContactPhone,
                ScheduleLabel = "Sinh hoạt định kỳ",
                IsRecruiting = true,
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            existingCodes.Add(def.Code);
        }

        if (addedClubs.Count > 0)
        {
            db.Clubs.AddRange(addedClubs);
            await db.SaveChangesAsync(cancellationToken);
            logger?.LogInformation("ClubSeeder: Seeded {Count} official FPT University clubs.", addedClubs.Count);
        }
        else
        {
            logger?.LogInformation("ClubSeeder: All {Count} official FPT University clubs already exist in database.", FptuClubRegistry.Clubs.Count);
        }

        return addedClubs.Count;
    }
}
