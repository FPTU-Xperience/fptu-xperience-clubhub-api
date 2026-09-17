using System.Globalization;
using System.Security.Claims;

namespace AdminService.Security;

internal sealed class HttpCurrentActor(IHttpContextAccessor httpContextAccessor) : ICurrentActor
{
    private ClaimsPrincipal Principal =>
        httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public string? SubjectId => Principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Principal.FindFirstValue("sub");

    public int? UserId => int.TryParse(
        SubjectId,
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out var userId) && userId > 0
            ? userId
            : null;

    public string? Email => Principal.FindFirstValue(ClaimTypes.Email)
        ?? Principal.FindFirstValue("email");

    public IReadOnlyCollection<string> Roles => Principal.FindAll(ClaimTypes.Role)
        .Select(claim => claim.Value)
        .Where(role => !string.IsNullOrWhiteSpace(role))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;
}
