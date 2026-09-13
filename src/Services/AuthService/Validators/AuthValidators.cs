using AuthService.Contracts;

namespace AuthService.Validators;

public static class AuthValidators
{
    public static ValidationResult ValidateGoogleLogin(GoogleLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Credential))
        {
            return ValidationResult.Failure("Google credential is required.");
        }

        if (request.Credential.Length > 8192)
        {
            return ValidationResult.Failure("Google credential is too long.");
        }

        return ValidationResult.Success();
    }

    public static ValidationResult ValidateCreateUser(CreateUserRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Username))
            errors.Add("Username is required.");
        if (string.IsNullOrWhiteSpace(request.FullName))
            errors.Add("Full name is required.");
        if (string.IsNullOrWhiteSpace(request.Email))
            errors.Add("Email is required.");
        if (request.Roles is null || request.Roles.Count == 0)
            errors.Add("Exactly one actor role is required.");

        if (request.Username?.Length > 100)
            errors.Add("Username must not exceed 100 characters.");
        if (request.FullName?.Length > 200)
            errors.Add("Full name must not exceed 200 characters.");
        if (request.Email?.Length > 200)
            errors.Add("Email must not exceed 200 characters.");

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            if (!System.Net.Mail.MailAddress.TryCreate(request.Email, out _))
            {
                errors.Add("Email address is invalid.");
            }
        }

        return errors.Count > 0
            ? ValidationResult.Failure(errors)
            : ValidationResult.Success();
    }

    public static ValidationResult ValidateUpdateUser(UpdateUserRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.FullName))
            errors.Add("Full name is required.");
        if (string.IsNullOrWhiteSpace(request.Email))
            errors.Add("Email is required.");

        if (request.FullName?.Length > 200)
            errors.Add("Full name must not exceed 200 characters.");
        if (request.Email?.Length > 200)
            errors.Add("Email must not exceed 200 characters.");
        if (request.Roles is null || request.Roles.Count == 0)
            errors.Add("Exactly one actor role is required.");

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            if (!System.Net.Mail.MailAddress.TryCreate(request.Email, out _))
            {
                errors.Add("Email address is invalid.");
            }
        }

        return errors.Count > 0
            ? ValidationResult.Failure(errors)
            : ValidationResult.Success();
    }
}

public class ValidationResult
{
    public bool IsValid { get; private set; }
    public IReadOnlyList<string> Errors { get; private set; } = [];

    private ValidationResult() { }

    public static ValidationResult Success() => new() { IsValid = true };

    public static ValidationResult Failure(string error) =>
        new() { IsValid = false, Errors = [error] };

    public static ValidationResult Failure(IEnumerable<string> errors) =>
        new() { IsValid = false, Errors = errors.ToList() };
}
