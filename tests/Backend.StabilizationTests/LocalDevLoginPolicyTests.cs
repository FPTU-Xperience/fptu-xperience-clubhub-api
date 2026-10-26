using AuthService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Backend.StabilizationTests;

public sealed class LocalDevLoginPolicyTests
{
    private const string Key = "test-only-local-proxy-secret-at-least-32-chars";

    [Theory]
    [InlineData("", "ctsv.an@fpt.edu.vn", 1, false)]
    [InlineData("wrong-key", "ctsv.an@fpt.edu.vn", 1, false)]
    [InlineData(Key, "cuongnkce161131@fpt.edu.vn", 1, false)]
    [InlineData(Key, "ctsv.an@fpt.edu.vn", -1, false)]
    [InlineData(Key, "CTSV.AN@fpt.edu.vn", 1, true)]
    public void ProductionRequiresKeyAllowedEmailAndUnexpiredWindow(string supplied, string email, int hours, bool allowed)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LocalDevLogin:AccessKey"] = Key,
            ["LocalDevLogin:AllowedEmails"] = "ctsv.an@fpt.edu.vn",
            ["LocalDevLogin:ExpiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(hours).ToString("O")
        }).Build();
        var request = new DefaultHttpContext().Request;
        request.Headers["Origin"] = "http://localhost:5174";
        request.Headers["X-Local-Dev-Login-Key"] = supplied;
        Assert.Equal(allowed, LocalDevLoginPolicy.Allows(request, new TestHostEnvironment(Environments.Production), configuration, email));
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("Production", false)]
    public void DefaultProductionRemainsClosed(string environment, bool allowed)
    {
        Assert.Equal(allowed, LocalDevLoginPolicy.Allows(new DefaultHttpContext().Request,
            new TestHostEnvironment(environment), new ConfigurationBuilder().Build(), "ctsv.an@fpt.edu.vn"));
    }
}
