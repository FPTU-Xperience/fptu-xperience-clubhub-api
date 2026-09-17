namespace AdminService.Observability;

public interface ICorrelationContext
{
    string Id { get; }
}
