namespace AuthService.Services;

public sealed record GoogleIdentity(
    string Subject,
    string Email,
    bool EmailVerified,
    string? HostedDomain);

public interface IGoogleIdTokenValidator
{
    Task<GoogleIdentity?> ValidateAsync(string credential, CancellationToken cancellationToken = default);
}
