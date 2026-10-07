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

    [Fact]
    public void FiveCampuses_ContainsExactlyFiveCampuses()
    {
        Assert.Equal(5, CampusCodes.FiveCampuses.Length);
        Assert.Contains(CampusCodes.Hanoi, CampusCodes.FiveCampuses);
        Assert.Contains(CampusCodes.HoChiMinh, CampusCodes.FiveCampuses);
        Assert.Contains(CampusCodes.Danang, CampusCodes.FiveCampuses);
        Assert.Contains(CampusCodes.CanTho, CampusCodes.FiveCampuses);
        Assert.Contains(CampusCodes.QuyNhon, CampusCodes.FiveCampuses);
    }

    [Fact]
    public void All_ContainsSixCodesIncludingGlobal()
    {
        Assert.Equal(6, CampusCodes.All.Length);
        Assert.Contains(CampusCodes.Global, CampusCodes.All);
    }

    [Theory]
    [InlineData("HAN", true)]
    [InlineData("HCM", true)]
    [InlineData("DAN", true)]
    [InlineData("CAN", true)]
    [InlineData("QNH", true)]
    [InlineData("GLOBAL", true)]
    [InlineData("UNKNOWN", false)]
    [InlineData("HE170001", false)]
    [InlineData("SE180002", false)]
    [InlineData(null, false)]
    public void IsValid_ValidatesCampusCodesIndependentlyOfStudentIds(string? code, bool expected)
    {
        Assert.Equal(expected, CampusCodes.IsValid(code));
    }

    [Theory]
    [InlineData("HAN", "Hà Nội (Hòa Lạc)")]
    [InlineData("HCM", "TP. Hồ Chí Minh")]
    [InlineData("DAN", "Đà Nẵng")]
    [InlineData("CAN", "Cần Thơ")]
    [InlineData("QNH", "Quy Nhơn")]
    [InlineData("GLOBAL", "Toàn trường")]
    public void GetDisplayName_ReturnsExpectedCampusName(string code, string expected)
    {
        Assert.Equal(expected, CampusCodes.GetDisplayName(code));
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
