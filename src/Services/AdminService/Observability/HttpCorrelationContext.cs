using ClubReportHub.Shared.Tracing;

namespace AdminService.Observability;

internal sealed class HttpCorrelationContext(IHttpContextAccessor httpContextAccessor)
    : ICorrelationContext
{
    public string Id => httpContextAccessor.HttpContext.GetCorrelationId()
        ?? throw new InvalidOperationException("Correlation context is not available for this request.");
}
