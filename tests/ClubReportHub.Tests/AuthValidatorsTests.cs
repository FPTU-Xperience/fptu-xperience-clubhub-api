using AuthService.Contracts;
using AuthService.Validators;
using Xunit;

namespace ClubReportHub.Tests;

public class AuthValidatorsTests
{
    [Fact]
    public void ValidateGoogleLogin_ValidCredential_ReturnsSuccess()
    {
        var request = new GoogleLoginRequest("valid-google-id-token");

        var result = AuthValidators.ValidateGoogleLogin(request);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateGoogleLogin_MissingCredential_ReturnsFailure(string? credential)
    {
        var request = new GoogleLoginRequest(credential!);

        var result = AuthValidators.ValidateGoogleLogin(request);

        Assert.False(result.IsValid);
        Assert.Contains("Google credential is required.", result.Errors);
    }

    [Fact]
    public void ValidateGoogleLogin_CredentialMaxLength_ReturnsSuccess()
    {
        var credential = new string('a', 8192);
        var request = new GoogleLoginRequest(credential);

        var result = AuthValidators.ValidateGoogleLogin(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateGoogleLogin_CredentialExceedsMaxLength_ReturnsFailure()
    {
        var credential = new string('a', 8193);
        var request = new GoogleLoginRequest(credential);

        var result = AuthValidators.ValidateGoogleLogin(request);

        Assert.False(result.IsValid);
        Assert.Contains("Google credential is too long.", result.Errors);
    }

    [Fact]
    public void ValidateCreateUser_ValidRequest_ReturnsSuccess()
    {
        var request = new CreateUserRequest(
            "john.doe",
            "John Doe",
            "john.doe@fpt.edu.vn",
            new List<string> { "CLUB_MANAGER" });

        var result = AuthValidators.ValidateCreateUser(request);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateCreateUser_MissingRequiredFields_ReturnsMultipleErrors()
    {
        var request = new CreateUserRequest(
            "",
            "  ",
            "",
            new List<string>());

        var result = AuthValidators.ValidateCreateUser(request);

        Assert.False(result.IsValid);
        Assert.Contains("Username is required.", result.Errors);
        Assert.Contains("Full name is required.", result.Errors);
        Assert.Contains("Email is required.", result.Errors);
        Assert.Contains("Exactly one actor role is required.", result.Errors);
    }

    [Fact]
    public void ValidateCreateUser_NullRoles_ReturnsError()
    {
        var request = new CreateUserRequest(
            "john.doe",
            "John Doe",
            "john.doe@fpt.edu.vn",
            null!);

        var result = AuthValidators.ValidateCreateUser(request);

        Assert.False(result.IsValid);
        Assert.Contains("Exactly one actor role is required.", result.Errors);
    }

    [Fact]
    public void ValidateCreateUser_InvalidEmailFormat_ReturnsError()
    {
        var request = new CreateUserRequest(
            "john.doe",
            "John Doe",
            "not-a-valid-email",
            new List<string> { "ADMIN" });

        var result = AuthValidators.ValidateCreateUser(request);

        Assert.False(result.IsValid);
        Assert.Contains("Email address is invalid.", result.Errors);
    }

    [Fact]
    public void ValidateCreateUser_FieldsAtMaxLength_ReturnsSuccess()
    {
        var username = new string('u', 100);
        var fullName = new string('f', 200);
        var localPart = new string('a', 200 - "@fpt.edu.vn".Length);
        var email = $"{localPart}@fpt.edu.vn";

        var request = new CreateUserRequest(
            username,
            fullName,
            email,
            new List<string> { "ADMIN" });

        var result = AuthValidators.ValidateCreateUser(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateCreateUser_FieldsExceedMaxLength_ReturnsErrors()
    {
        var username = new string('u', 101);
        var fullName = new string('f', 201);
        var localPart = new string('a', 201 - "@fpt.edu.vn".Length);
        var email = $"{localPart}@fpt.edu.vn";

        var request = new CreateUserRequest(
            username,
            fullName,
            email,
            new List<string> { "ADMIN" });

        var result = AuthValidators.ValidateCreateUser(request);

        Assert.False(result.IsValid);
        Assert.Contains("Username must not exceed 100 characters.", result.Errors);
        Assert.Contains("Full name must not exceed 200 characters.", result.Errors);
        Assert.Contains("Email must not exceed 200 characters.", result.Errors);
    }

    [Fact]
    public void ValidateUpdateUser_ValidRequest_ReturnsSuccess()
    {
        var request = new UpdateUserRequest(
            "Jane Doe",
            "jane.doe@fpt.edu.vn",
            true,
            new List<string> { "CLUB_MEMBER" });

        var result = AuthValidators.ValidateUpdateUser(request);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateUpdateUser_MissingRequiredFields_ReturnsErrors()
    {
        var request = new UpdateUserRequest(
            "",
            "",
            true,
            new List<string>());

        var result = AuthValidators.ValidateUpdateUser(request);

        Assert.False(result.IsValid);
        Assert.Contains("Full name is required.", result.Errors);
        Assert.Contains("Email is required.", result.Errors);
        Assert.Contains("Exactly one actor role is required.", result.Errors);
    }

    [Fact]
    public void ValidateUpdateUser_InvalidEmailFormat_ReturnsError()
    {
        var request = new UpdateUserRequest(
            "Jane Doe",
            "invalid-email-format",
            true,
            new List<string> { "CLUB_MEMBER" });

        var result = AuthValidators.ValidateUpdateUser(request);

        Assert.False(result.IsValid);
        Assert.Contains("Email address is invalid.", result.Errors);
    }

    [Fact]
    public void ValidateUpdateUser_FieldsExceedMaxLength_ReturnsErrors()
    {
        var fullName = new string('f', 201);
        var localPart = new string('a', 201 - "@fpt.edu.vn".Length);
        var email = $"{localPart}@fpt.edu.vn";

        var request = new UpdateUserRequest(
            fullName,
            email,
            true,
            new List<string> { "CLUB_MEMBER" });

        var result = AuthValidators.ValidateUpdateUser(request);

        Assert.False(result.IsValid);
        Assert.Contains("Full name must not exceed 200 characters.", result.Errors);
        Assert.Contains("Email must not exceed 200 characters.", result.Errors);
    }
}
