using ClubReportHub.Shared.Cors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Backend.StabilizationTests;

public sealed class CorsOriginConfigurationTests
{
    [Fact]
    public void ProductionKeepsConfiguredPrimaryOrigin()
    {
        var origins = Resolve(
            Environments.Production,
            ("Cors:AllowedOrigins:0", "https://frontend.example"));

        Assert.Equal(["https://frontend.example"], origins);
    }

    [Fact]
    public void ProductionCombinesConfiguredOriginsWithoutDevelopmentDefaults()
    {
        var origins = Resolve(
            Environments.Production,
            ("Cors:AllowedOrigins:0", "https://fptux-legacy-ui.pages.dev"),
            ("Cors:AdditionalOrigins", "http://127.0.0.1:5174,http://localhost:5174"));

        Assert.Equal(
            [
                "https://fptux-legacy-ui.pages.dev",
                "http://127.0.0.1:5174",
                "http://localhost:5174"
            ],
            origins);
    }

    [Fact]
    public void ParsesCommaSeparatedAdditionalOrigins()
    {
        var origins = Resolve(
            Environments.Production,
            ("Cors:AdditionalOrigins", "http://127.0.0.1:5174,http://localhost:5174"));

        Assert.Equal(
            ["http://127.0.0.1:5174", "http://localhost:5174"],
            origins);
    }

    [Fact]
    public void ParsesSemicolonSeparatedAdditionalOrigins()
    {
        var origins = Resolve(
            Environments.Production,
            ("Cors:AdditionalOrigins", "https://one.example;https://two.example"));

        Assert.Equal(["https://one.example", "https://two.example"], origins);
    }

    [Fact]
    public void TrimsWhitespace()
    {
        var origins = Resolve(
            Environments.Production,
            ("Cors:AdditionalOrigins", "  https://one.example  ,  https://two.example  "));

        Assert.Equal(["https://one.example", "https://two.example"], origins);
    }

    [Fact]
    public void RemovesTrailingSlashes()
    {
        var origins = Resolve(
            Environments.Production,
            ("Cors:AllowedOrigins:0", "https://frontend.example/"),
            ("Cors:AdditionalOrigins", "http://localhost:5174///"));

        Assert.Equal(["https://frontend.example", "http://localhost:5174"], origins);
    }

    [Fact]
    public void RemovesDuplicatesCaseInsensitively()
    {
        var origins = Resolve(
            Environments.Production,
            ("Cors:AllowedOrigins:0", "https://Frontend.Example/"),
            ("Cors:AdditionalOrigins", "https://frontend.example"));

        Assert.Equal(["https://Frontend.Example"], origins);
    }

    [Fact]
    public void IgnoresEmptyAdditionalOrigins()
    {
        var origins = Resolve(
            Environments.Production,
            ("Cors:AllowedOrigins:0", "https://frontend.example"),
            ("Cors:AdditionalOrigins", " , ;  ; "));

        Assert.Equal(["https://frontend.example"], origins);
    }

    [Fact]
    public void DevelopmentIncludesIntendedLocalhostOrigins()
    {
        var origins = Resolve(Environments.Development);

        Assert.Contains("http://localhost:3000", origins);
        Assert.Contains("http://localhost:3001", origins);
        Assert.Contains("http://localhost:5173", origins);
        Assert.Contains("http://127.0.0.1:3000", origins);
        Assert.Contains("http://127.0.0.1:5173", origins);
    }

    [Fact]
    public void ProductionDoesNotIncludeAutomaticDevelopmentOrigins()
    {
        var origins = Resolve(Environments.Production);

        Assert.DoesNotContain("http://localhost:3000", origins);
        Assert.DoesNotContain("http://localhost:5173", origins);
        Assert.DoesNotContain("http://127.0.0.1:5173", origins);
    }

    [Fact]
    public void ProductionDoesNotIncludeUnconfiguredOrigin()
    {
        var origins = Resolve(
            Environments.Production,
            ("Cors:AllowedOrigins:0", "https://frontend.example"),
            ("Cors:AdditionalOrigins", "http://localhost:5174"));

        Assert.DoesNotContain("https://evil.example", origins);
    }

    [Theory]
    [InlineData("https://f790bd35.fptux-clubhub-ui.pages.dev", true)]
    [InlineData("https://preview-123.fptux-clubhub-ui.pages.dev", true)]
    [InlineData("https://fptux-clubhub-ui.pages.dev", true)]
    [InlineData("https://unapproved-preview.pages.dev", false)]
    [InlineData("https://evil-fptux-clubhub-ui.pages.dev", false)]
    [InlineData("http://f790bd35.fptux-clubhub-ui.pages.dev", false)]
    [InlineData("invalid-uri", false)]
    [InlineData("", false)]
    public void IsOriginAllowed_EvaluatesWildcardAndExactOriginsCorrectly(string origin, bool expected)
    {
        string[] allowed =
        [
            "https://fptux-clubhub-ui.pages.dev",
            "https://*.fptux-clubhub-ui.pages.dev",
            "https://fptux-legacy-ui.pages.dev"
        ];

        var result = CorsOriginConfiguration.IsOriginAllowed(origin, allowed);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("http://localhost:5173", true)]
    [InlineData("http://localhost:3000", true)]
    [InlineData("http://localhost:4173", true)]
    [InlineData("http://localhost:5174", true)]
    [InlineData("http://127.0.0.1:5173", true)]
    [InlineData("http://127.0.0.1:3000", true)]
    [InlineData("http://127.0.0.1:8080", true)]
    [InlineData("https://localhost:3000", false)]
    [InlineData("http://localhost.evil.com:5173", false)]
    [InlineData("http://evil-localhost:5173", false)]
    public void IsOriginAllowed_EvaluatesLocalhostWildcardCorrectly(string origin, bool expected)
    {
        string[] allowed =
        [
            "http://localhost:*",
            "http://127.0.0.1:*"
        ];

        var result = CorsOriginConfiguration.IsOriginAllowed(origin, allowed);
        Assert.Equal(expected, result);
    }

    private static string[] Resolve(
        string environmentName,
        params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => pair.Value))
            .Build();

        return CorsOriginConfiguration.ResolveAllowedOrigins(
            configuration,
            new TestHostEnvironment(environmentName));
    }
}
