using AuthService.Models;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AuthService.Data;

public static class AuthSeeder
{
    public static async Task SeedAsync(AuthDbContext db, IConfiguration? configuration = null)
    {
        await EnsureRoleAsync(db, AuthRoles.Admin);
        await EnsureRoleAsync(db, AuthRoles.SystemAdmin);
        await EnsureRoleAsync(db, AuthRoles.StudentAffairsAdmin);
        await EnsureRoleAsync(db, AuthRoles.ClubManager);
        await EnsureRoleAsync(db, AuthRoles.Treasurer);
        await EnsureRoleAsync(db, AuthRoles.ClubMember);
        await db.SaveChangesAsync();

        // The application still keeps every role definition because authorization policies
        // reference them. Demo accounts, however, are intentionally limited to ADMIN,
        // CLUB_MANAGER and CLUB_MEMBER. Managers and students are created by DemoDataSeeder.
        var adminEmail = GetOptionalConfiguration(
            configuration,
            "BootstrapAdmin:Email",
            "admin@fpt.edu.vn");
        var adminFullName = GetOptionalConfiguration(
            configuration,
            "BootstrapAdmin:FullName",
            "Nguyễn Thu Hà");
        ValidateBootstrapAdmin(adminEmail, adminFullName);

        await EnsureUserAsync(
            db,
            username: adminEmail,
            fullName: adminFullName,
            email: adminEmail,
            roles: [AuthRoles.Admin]);

        if (configuration is not null)
        {
            var additionalEmails = configuration.GetSection("PreApprovedAdmins").Get<string[]>()
                ?? (Environment.GetEnvironmentVariable("PRE_APPROVED_ADMINS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) 
                    ?? ["namthse173516@fpt.edu.vn", "namhoang20032710@gmail.com"]);

            foreach (var extraEmail in additionalEmails)
            {
                var trimmed = extraEmail.Trim();
                await EnsureUserAsync(
                    db,
                    username: trimmed,
                    fullName: trimmed.StartsWith("namth", StringComparison.OrdinalIgnoreCase) ? "Hoàng Nam" : "Nam Hoàng",
                    email: trimmed,
                    roles: [AuthRoles.Admin]);
            }
        }

        await db.SaveChangesAsync();
    }

    private static string GetOptionalConfiguration(
        IConfiguration? configuration,
        string configKey,
        string fallback)
    {
        var value = configuration?[configKey];
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
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
        var existing = await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.Username == username);
        if (existing is not null)
        {
            existing.FullName = fullName;
            existing.Email = email;
            existing.IsActive = true;
            existing.IsLocked = false;

            var existingRoleNames = existing.UserRoles.Select(x => x.Role.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unexpectedRoles = existing.UserRoles
                .Where(userRole => !roles.Contains(userRole.Role.Name, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (unexpectedRoles.Length > 0)
            {
                db.UserRoles.RemoveRange(unexpectedRoles);
            }

            var missingRoleNames = roles.Where(role => !existingRoleNames.Contains(role)).ToArray();
            if (missingRoleNames.Length > 0)
            {
                var missingRoles = await db.Roles.Where(x => missingRoleNames.Contains(x.Name)).ToListAsync();
                foreach (var role in missingRoles)
                {
                    db.UserRoles.Add(new UserRole { UserId = existing.Id, RoleId = role.Id });
                }
            }

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
