namespace AdminService.Security;

public interface ICurrentActor
{
    string? SubjectId { get; }
    int? UserId { get; }
    string? Email { get; }
    IReadOnlyCollection<string> Roles { get; }
    bool IsAuthenticated { get; }
}
