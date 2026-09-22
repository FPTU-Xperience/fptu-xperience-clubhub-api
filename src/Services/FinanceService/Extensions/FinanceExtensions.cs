using System.Security.Claims;
using ClubReportHub.Shared.Auth;
using FinanceService.Clients;

namespace FinanceService.Extensions;

public static class FinanceExtensions
{
    public static bool IsFinanceReviewer(this ClaimsPrincipal user) =>
        user.IsInRole(AuthRoles.Admin)
        || user.IsInRole(AuthRoles.StudentAffairsAdmin);

    public static bool IsCombinedReportWorkflow(this HttpContext httpContext, IConfiguration? config = null)
    {
        var hasHeader = string.Equals(
            httpContext.Request.Headers["X-Combined-Report-Workflow"].ToString(),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!hasHeader)
        {
            return false;
        }

        var expectedSecret = config?["InternalServiceAuth:Secret"]
            ?? config?["Security:InternalWorkflowToken"]
            ?? "clubhub-internal-workflow-shared-secret";
        var receivedSecret = httpContext.Request.Headers["X-Internal-Workflow-Token"].ToString();

        if (string.IsNullOrEmpty(receivedSecret))
        {
            return false;
        }

        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expectedSecret);
        var receivedBytes = System.Text.Encoding.UTF8.GetBytes(receivedSecret);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expectedBytes, receivedBytes);
    }

    public static async Task<HashSet<int>> GetFinanceClubIdsAsync(
        this ClubAccessClient clubAccess,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await clubAccess.GetMyAccessAsync(httpContext.GetBearerToken(), cancellationToken);
        return access.Where(x => x.CanManageFinance || x.CanManage).Select(x => x.ClubId).ToHashSet();
    }

    public static async Task<bool> CanAccessFinanceClubAsync(
        this ClubAccessClient clubAccess,
        int clubId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await clubAccess.GetMyAccessAsync(httpContext.GetBearerToken(), cancellationToken);
        return access.Any(x => x.ClubId == clubId && (x.CanManageFinance || x.CanManage));
    }

    public static async Task<bool> CanManageFinanceClubAsync(
        this ClubAccessClient clubAccess,
        int clubId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await clubAccess.GetMyAccessAsync(httpContext.GetBearerToken(), cancellationToken);
        return access.Any(x => x.ClubId == clubId && x.CanManageFinance);
    }
}
