using System.Security.Claims;

namespace ClubReportHub.Shared.Auth;

public interface IUserSecurityStampValidator
{
    Task<bool> ValidateSecurityStampAsync(int userId, ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
