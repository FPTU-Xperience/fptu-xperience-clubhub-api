using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClubReportHub.Shared.Auth;
using Microsoft.Extensions.Options;
using Xunit;

namespace ClubReportHub.Tests;

public class CampusCodesTests
{
    [Theory]
    [InlineData("Hà Nội", "HAN")]
    [InlineData("Hòa Lạc", "HAN")]
    [InlineData("HN", "HAN")]
    [InlineData("HAN", "HAN")]
    [InlineData("Hồ Chí Minh", "HCM")]
    [InlineData("HCM", "HCM")]
    [InlineData("SG", "HCM")]
    [InlineData("Đà Nẵng", "DAN")]
    [InlineData("DAN", "DAN")]
    [InlineData("DN", "DAN")]
    [InlineData("Cần Thơ", "CAN")]
    [InlineData("CAN", "CAN")]
    [InlineData("CT", "CAN")]
    [InlineData("Quy Nhơn", "QNH")]
    [InlineData("QNH", "QNH")]
    [InlineData("QN", "QNH")]
    [InlineData("GLOBAL", "GLOBAL")]
    [InlineData("Toàn trường", "GLOBAL")]
    [InlineData(null, "HAN")]
    [InlineData("", "HAN")]
    public void Normalize_ReturnsStandardCampusCode(string? input, string expected)
    {
        var result = CampusCodes.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("HE170001", "HAN")]
    [InlineData("he180002@fpt.edu.vn", "HAN")]
    [InlineData("ha160003", "HAN")]
    [InlineData("SE170001", "HCM")]
    [InlineData("se170002@fpt.edu.vn", "HCM")]
    [InlineData("sa180003", "HCM")]
    [InlineData("ia170004", "HCM")]
    [InlineData("DE170001", "DAN")]
    [InlineData("de180002@fpt.edu.vn", "DAN")]
    [InlineData("CE170001", "CAN")]
    [InlineData("ce180002@fpt.edu.vn", "CAN")]
    [InlineData("QE170001", "QNH")]
    [InlineData("qe180002@fpt.edu.vn", "QNH")]
    [InlineData("ctsv.hcm@fpt.edu.vn", "HCM")]
    [InlineData("ctsv.dn@fpt.edu.vn", "DAN")]
    [InlineData("ctsv.ct@fpt.edu.vn", "CAN")]
    [InlineData("ctsv.qn@fpt.edu.vn", "QNH")]
    [InlineData("ctsv.hn@fpt.edu.vn", "HAN")]
    public void InferFromStudentCodeOrEmail_CorrectlyDetectsCampus(string identifier, string expected)
    {
        var result = CampusCodes.InferFromStudentCodeOrEmail(identifier);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void JwtTokenFactory_EmitsCampusClaim()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "ClubReportHub",
            Audience = "ClubReportHubClient",
            SigningKey = "SuperSecretKeyForIntegrationTesting1234567890!",
            ExpirationMinutes = 60
        });

        var factory = new JwtTokenFactory(options);
        var tokenResult = factory.CreateToken(
            userId: 42,
            username: "se170001@fpt.edu.vn",
            fullName: "Nguyễn Hải Đăng",
            roles: [AuthRoles.ClubMember],
            securityVersion: 1,
            campusCode: CampusCodes.HoChiMinh);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenResult.AccessToken);

        var campusClaim = jwt.Claims.FirstOrDefault(c => c.Type == "campus");
        Assert.NotNull(campusClaim);
        Assert.Equal(CampusCodes.HoChiMinh, campusClaim.Value);
    }

    [Fact]
    public void ClaimsPrincipalExtensions_GetCampus_ReturnsClaimOrFallback()
    {
        var identityWithCampus = new ClaimsIdentity([new Claim("campus", CampusCodes.Danang)]);
        var principalWithCampus = new ClaimsPrincipal(identityWithCampus);
        Assert.Equal(CampusCodes.Danang, principalWithCampus.GetCampus());

        var identityWithoutCampus = new ClaimsIdentity();
        var principalWithoutCampus = new ClaimsPrincipal(identityWithoutCampus);
        Assert.Equal(CampusCodes.Hanoi, principalWithoutCampus.GetCampus());
    }
}
