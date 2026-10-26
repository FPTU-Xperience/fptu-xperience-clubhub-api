using System.Reflection;
using System.Security.Claims;
using AuthService.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace ClubReportHub.Tests;

public sealed class GoogleDomainPolicyTests
{
    [Theory]
    [InlineData("student@fpt.edu.vn", "fpt.edu.vn", true)]
    [InlineData("member@gmail.com", null, true)]
    [InlineData("MEMBER@GMAIL.COM", null, true)]
    [InlineData("student@fpt.edu.vn", null, false)]
    [InlineData("student@fpt.edu.vn", "evil.example", false)]
    [InlineData("member@gmail.com.evil.example", "fpt.edu.vn", false)]
    [InlineData("member@other.example", "fpt.edu.vn", false)]
    public void SchoolAndConsumerAccountsFollowVerifiedDomainPolicy(string email, string? hostedDomain, bool allowed)
    {
        var validator = new GoogleIdTokenValidator(Options.Create(new GoogleAuthenticationOptions
        {
            ClientId = "test-client",
            AllowedEmailDomains = "fpt.edu.vn,gmail.com",
            AllowedHostedDomain = "fpt.edu.vn"
        }));
        var claims = new List<Claim> { new("sub", "verified-subject"), new("email", email), new("email_verified", "true") };
        if (hostedDomain is not null) claims.Add(new Claim("hd", hostedDomain));
        // Exercise the claim policy after signature/audience/lifetime verification;
        // login still separately enforces the existing active-account roster.
        var identity = typeof(GoogleIdTokenValidator).GetMethod("ToIdentity", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(validator, [new ClaimsPrincipal(new ClaimsIdentity(claims))]);
        Assert.Equal(allowed, identity is not null);
    }
}
