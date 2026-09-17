using System.Security.Claims;
using System.Text.Encodings.Web;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdminService.IntegrationTests;

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AdminServiceTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail("Malformed authorization header."));
        }

        var token = authorization["Bearer ".Length..];
        var identity = token switch
        {
            "admin" => CreateIdentity("101", "admin@fpt.edu.vn", AuthRoles.Admin),
            "student-affairs" => CreateIdentity(
                "202", "ctsv@fpt.edu.vn", AuthRoles.StudentAffairsAdmin),
            _ => null
        };
        if (identity is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired test token."));
        }

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    private static ClaimsIdentity CreateIdentity(string subjectId, string email, string role) =>
        new(
        [
            new Claim(ClaimTypes.NameIdentifier, subjectId),
            new Claim("sub", subjectId),
            new Claim(ClaimTypes.Email, email),
            new Claim("email", email),
            new Claim(ClaimTypes.Role, role)
        ],
        SchemeName);
}
