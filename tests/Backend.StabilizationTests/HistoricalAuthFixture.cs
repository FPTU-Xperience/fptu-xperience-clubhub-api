using AuthService.Data;
using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.StabilizationTests;

internal static class HistoricalAuthFixture
{
    // A pre-outbox schema must be seeded using historical SQL, not the current
    // context's SaveChanges hooks. Production writes always require the outbox.
    internal static async Task SeedTrackedUsersAsync(AuthDbContext db)
    {
        var pending = db.ChangeTracker.Entries().Where(x => x.State == EntityState.Added).ToArray();
        Assert.All(pending, x => Assert.IsType<User>(x.Entity));
        var users = pending.Select(x => (User)x.Entity).ToArray();
        db.ChangeTracker.Clear();
        foreach (var user in users)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [Users] ([Id],[Username],[FullName],[Email],[CampusCode],[IsActive],[IsLocked],[SecurityVersion],[CreatedAtUtc]) VALUES ({user.Id},{user.Username},{user.FullName},{user.Email},{user.CampusCode},{user.IsActive},{user.IsLocked},{user.SecurityVersion},{user.CreatedAtUtc})");
    }
}
