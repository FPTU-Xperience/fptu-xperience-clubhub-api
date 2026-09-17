namespace AdminService.Errors;

public static class ApiErrorCodes
{
    public const string Validation = "VALIDATION_ERROR";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "RESOURCE_NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string BusinessRule = "BUSINESS_RULE_VIOLATION";
    public const string DependencyFailure = "DEPENDENCY_FAILURE";
    public const string Internal = "INTERNAL_ERROR";
}

public sealed record ErrorDetail(string? Field, string Message);

public abstract class ApplicationExceptionBase(
    string message,
    string code,
    int statusCode,
    IReadOnlyCollection<ErrorDetail>? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyCollection<ErrorDetail> Details { get; } = details ?? [];
}

public sealed class RequestValidationException(
    IReadOnlyCollection<ErrorDetail> details,
    string message = "Request validation failed.")
    : ApplicationExceptionBase(message, ApiErrorCodes.Validation, 400, details);

public sealed class ResourceNotFoundException(string message = "The requested resource was not found.")
    : ApplicationExceptionBase(message, ApiErrorCodes.NotFound, 404);

public sealed class ResourceConflictException(string message)
    : ApplicationExceptionBase(message, ApiErrorCodes.Conflict, 409);

public sealed class BusinessRuleException(string message)
    : ApplicationExceptionBase(message, ApiErrorCodes.BusinessRule, 422);

public sealed class OperationForbiddenException(
    string message = "You do not have permission to perform this operation.")
    : ApplicationExceptionBase(message, ApiErrorCodes.Forbidden, 403);

public sealed class DependencyFailureException(
    string message = "A required dependency is temporarily unavailable.")
    : ApplicationExceptionBase(message, ApiErrorCodes.DependencyFailure, 502);
