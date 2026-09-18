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
