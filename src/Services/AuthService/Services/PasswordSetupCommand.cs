using AuthService.Data;
using AuthService.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AuthService.Services;

// Operator-only command inside the API container. Never reads a password from arguments/logs.
public static class PasswordSetupCommand
{
    public static async Task Run(IServiceProvider services, string email)
    {
        if (Console.IsInputRedirected) throw new InvalidOperationException("Use an interactive terminal to enter the password securely.");
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail)
            ?? throw new InvalidOperationException("Account does not exist. No account was created.");
        Console.Write("New password (12–128 characters): ");
        var password = ReadPassword();
        Console.Write("Confirm password: ");
        if (password != ReadPassword()) throw new InvalidOperationException("Passwords do not match.");
        await EmailLoginEndpoints.SetPassword(user, password, db,
            scope.ServiceProvider.GetRequiredService<RefreshTokenService>(), scope.ServiceProvider.GetRequiredService<IMemoryCache>());
        Console.WriteLine("Password updated. Previous sessions revoked; account roles/status unchanged.");
    }

    private static string ReadPassword()
    {
        var value = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; }
            else if (!char.IsControl(key.KeyChar) && value.Length < 129) value.Append(key.KeyChar);
        }
    }
}
