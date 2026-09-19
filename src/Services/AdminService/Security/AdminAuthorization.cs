namespace AdminService.Security;

public static class AdminPolicies
{
    // This intentionally maps only to the current ADMIN role. Whether ADMIN
    // and SYSTEM_ADMIN should be consolidated remains a product decision.
    public const string AdminOnly = "AdminService.AdminOnly";
    public const string StudentAffairsOnly = "AdminService.StudentAffairsOnly";
    public const string BackofficeUser = "AdminService.BackofficeUser";
}
