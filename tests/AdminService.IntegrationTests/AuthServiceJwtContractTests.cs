using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using Microsoft.IdentityModel.Tokens;

namespace AdminService.IntegrationTests;

public sealed class AuthServiceJwtContractTests(JwtContractApiFactory factory)
    : IClassFixture<JwtContractApiFactory>
{
    private const string SigningKey = "test-only-signing-key-at-least-32-characters";

    [Fact]
    public async Task AuthServiceContractToken_IsAcceptedAndNormalizesIntegerUserId()
    {
        using var client = CreateClient(CreateToken("101", AuthRoles.Admin));
        var response = await client.GetAsync("/api/v1/admin/me");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("101", json.GetProperty("subjectId").GetString());
        Assert.Equal(101, json.GetProperty("userId").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("email").ValueKind);
        Assert.Contains(AuthRoles.Admin, json.GetProperty("roles").EnumerateArray()
            .Select(role => role.GetString()));
    }

    [Theory]
    [InlineData("not-an-integer", "ClubReportHub")]
    [InlineData("101", "untrusted-issuer")]
    public async Task TokenOutsideAuthServiceContract_IsRejected(string subject, string issuer)
    {
        using var client = CreateClient(CreateToken(subject, AuthRoles.Admin, issuer));
        var response = await client.GetAsync("/api/v1/admin/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient CreateClient(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string CreateToken(
        string subject,
        string role,
        string issuer = "ClubReportHub")
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, subject),
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim(ClaimTypes.Name, "Contract Test Actor"),
            new Claim("username", "contract-test"),
            new Claim(ClaimTypes.Role, role)
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer,
            "ClubReportHub.Client",
            claims,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(5),
            credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
