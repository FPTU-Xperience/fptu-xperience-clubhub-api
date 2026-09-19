using AuthService.Models;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AuthService.Data;

public static class AuthSeeder
{
    public static async Task SeedAsync(
        AuthDbContext db,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        await EnsureRoleAsync(db, AuthRoles.Admin);
        await EnsureRoleAsync(db, AuthRoles.SystemAdmin);
        await EnsureRoleAsync(db, AuthRoles.StudentAffairsAdmin);
        await EnsureRoleAsync(db, AuthRoles.ClubManager);
        await EnsureRoleAsync(db, AuthRoles.Treasurer);
        await EnsureRoleAsync(db, AuthRoles.ClubMember);
        await db.SaveChangesAsync();

        // Bootstrap identities are opt-in in every environment. In particular,
        // Production never falls back to a personal or demo administrator.
        var adminEmail = configuration["BootstrapAdmin:Email"]?.Trim();
        if (!string.IsNullOrWhiteSpace(adminEmail))
        {
            var fullName = configuration["BootstrapAdmin:FullName"]?.Trim();
            await EnsureBootstrapAdminAsync(db, adminEmail, fullName);
        }

        var additionalEmails = configuration.GetSection("PreApprovedAdmins").Get<string[]>()
            ?? (Environment.GetEnvironmentVariable("PRE_APPROVED_ADMINS")?.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? []);

        foreach (var extraEmail in additionalEmails
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await EnsureBootstrapAdminAsync(db, extraEmail, fullName: null);
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureBootstrapAdminAsync(
        AuthDbContext db,
        string email,
        string? fullName)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var normalizedFullName = string.IsNullOrWhiteSpace(fullName)
            ? normalizedEmail
            : fullName.Trim();

        ValidateBootstrapAdmin(normalizedEmail, normalizedFullName);
        await EnsureUserAsync(
            db,
            username: normalizedEmail,
            fullName: normalizedFullName,
            email: normalizedEmail,
            roles: [AuthRoles.Admin]);
    }

    private static void ValidateBootstrapAdmin(string email, string fullName)
    {
        if (email.Length > 100 || !System.Net.Mail.MailAddress.TryCreate(email, out _))
        {
            throw new InvalidOperationException(
                "BootstrapAdmin:Email must be a valid e-mail address up to 100 characters.");
        }

        if (fullName.Length > 200)
        {
            throw new InvalidOperationException(
                "BootstrapAdmin:FullName must not exceed 200 characters.");
        }
    }

    private static async Task EnsureRoleAsync(AuthDbContext db, string roleName)
    {
        if (!await db.Roles.AnyAsync(x => x.Name == roleName))
        {
            db.Roles.Add(new Role { Name = roleName });
        }
    }

    private static async Task EnsureUserAsync(
        AuthDbContext db,
        string username,
        string fullName,
        string email,
        IReadOnlyCollection<string> roles)
    {
        var existing = await db.Users
            .FirstOrDefaultAsync(x => x.Username == username || x.Email == email);
        if (existing is not null)
        {
            // Startup seeding must not undo an operator's decision to disable,
            // lock, rename, or change the roles of an existing account.
            return;
        }

        var user = new User
        {
            Username = username,
            FullName = fullName,
            Email = email,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var roleEntities = await db.Roles.Where(x => roles.Contains(x.Name)).ToListAsync();
        foreach (var role in roleEntities)
        {
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        }
    }
}
