using ClubService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClubService.Data;

public static class ClubSeeder
{
    public static async Task<int> SeedAsync(ClubDbContext db, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        if (!await db.ClubCategories.AnyAsync(cancellationToken))
        {
            var defaultCategories = new List<ClubCategory>
            {
                new() { Code = "TECHNOLOGY", Name = "Công nghệ", Description = "Câu lạc bộ công nghệ, kỹ thuật và phần mềm" },
                new() { Code = "ARTS", Name = "Nghệ thuật", Description = "Câu lạc bộ nghệ thuật, âm nhạc, biểu diễn và thiết kế" },
                new() { Code = "SPORTS", Name = "Thể thao", Description = "Câu lạc bộ thể thao, rèn luyện thể chất và võ thuật" },
                new() { Code = "VOLUNTEER", Name = "Tình nguyện", Description = "Câu lạc bộ tình nguyện, công tác xã hội và cộng đồng" },
                new() { Code = "ACADEMIC", Name = "Học thuật", Description = "Câu lạc bộ học thuật, ngoại ngữ và kỹ năng" },
                new() { Code = "BUSINESS", Name = "Kinh doanh", Description = "Câu lạc bộ kinh doanh, tài chính và khởi nghiệp" },
                new() { Code = "OTHER", Name = "Khác", Description = "Các câu lạc bộ sở thích và hoạt động chung khác" }
            };
            db.ClubCategories.AddRange(defaultCategories);
            await db.SaveChangesAsync(cancellationToken);
            logger?.LogInformation("ClubSeeder: Seeded {Count} default club categories.", defaultCategories.Count);
        }

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
