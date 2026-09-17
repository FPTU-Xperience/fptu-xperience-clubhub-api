namespace AdminService.Security;

public static class AdminPolicies
{
    public const string SystemAdminOnly = "AdminService.SystemAdminOnly";
    public const string StudentAffairsOnly = "AdminService.StudentAffairsOnly";
    public const string BackofficeUser = "AdminService.BackofficeUser";
}
