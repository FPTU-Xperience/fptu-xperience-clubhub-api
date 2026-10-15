using System.Globalization;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClubReportHub.Shared.Auth;

// A privileged write must not use the gateway's permissive cached validator.
// Services independently revalidate account state for direct calls as well.
public sealed class CurrentAccountValidator(HttpClient client) : IUserSecurityStampValidator
{
    public async Task<bool> ValidateSecurityStampAsync(int userId, ClaimsPrincipal principal, CancellationToken ct = default)
    {
        try
        {
            using var response = await client.GetAsync($"api/auth/user-status/{userId}", ct);
            if (!response.IsSuccessStatusCode) return false;
            var status = await response.Content.ReadFromJsonAsync<RemoteUserStatus>(ct);
            var version = int.TryParse(principal.FindFirst("ver")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 1;
            return status is not null && status.IsActive && !status.IsLocked && version == status.SecurityVersion
                && principal.FindAll(ClaimTypes.Role).All(x => status.Roles.Contains(x.Value, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { return false; }
    }
}
public static class CurrentAccountValidationExtensions
{
    public static IServiceCollection AddCurrentAccountValidation(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsEnvironment("Test") || environment.IsEnvironment("Testing")) return services;
        services.AddHttpClient<IUserSecurityStampValidator, CurrentAccountValidator>(client =>
        {
            client.BaseAddress = new Uri(configuration["Services:AuthService:BaseUrl"] ?? "http://auth-service:8080/");
            client.Timeout = TimeSpan.FromSeconds(5);
        }); return services;
    }
}
