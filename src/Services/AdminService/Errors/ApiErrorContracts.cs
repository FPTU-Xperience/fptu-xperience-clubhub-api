namespace AdminService.Errors;

public sealed record ApiError(
    string Code,
    string Message,
    IReadOnlyCollection<ErrorDetail> Details,
    string CorrelationId);

public sealed record ApiErrorEnvelope(ApiError Error);
