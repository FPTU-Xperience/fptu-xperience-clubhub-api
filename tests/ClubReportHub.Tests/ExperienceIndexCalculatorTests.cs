using ClubReportHub.Shared.Experience;
using Xunit;

namespace ClubReportHub.Tests;

public sealed class ExperienceIndexCalculatorTests
{
    [Fact]
    public void CalculateSaturatedScore_ZeroRaw_ReturnsZero()
    {
        var x = ExperienceIndexCalculator.CalculateSaturatedScore(0m, 1000m);
        Assert.Equal(0m, x);
    }

    [Fact]
    public void CalculateSaturatedScore_ValidValues_MatchesExponentialCurve()
    {
        // 100 * (1 - e^-1) ≈ 63.21205588
        var x = ExperienceIndexCalculator.CalculateSaturatedScore(1000m, 1000m);
        Assert.Equal(63.2121m, Math.Round(x, 4));
    }

    [Theory]
    [InlineData(0, false, ExperienceMasteryTiers.Unexplored)]
    [InlineData(10, false, ExperienceMasteryTiers.Starter)]
    [InlineData(20, false, ExperienceMasteryTiers.Practitioner)]
    [InlineData(49.9, false, ExperienceMasteryTiers.Practitioner)]
    [InlineData(50, false, ExperienceMasteryTiers.Contributor)]
    [InlineData(79.9, false, ExperienceMasteryTiers.Contributor)]
    [InlineData(80, false, ExperienceMasteryTiers.Contributor)] // Capped because hasLeaderProof is false!
    [InlineData(85, false, ExperienceMasteryTiers.Contributor)] // Capped because hasLeaderProof is false!
    [InlineData(80, true, ExperienceMasteryTiers.Leader)]      // Leader reached with leader proof!
    [InlineData(95, true, ExperienceMasteryTiers.Leader)]
    public void DetermineMasteryTier_FollowsAppendixA4ThresholdsAndCap(decimal score, bool hasLeaderProof, string expectedTier)
    {
        var tier = ExperienceIndexCalculator.DetermineMasteryTier(score, hasLeaderProof);
        Assert.Equal(expectedTier, tier);
    }

    [Fact]
    public void AppendixA5_Example1_SixEqualScores_NoPlusOne()
    {
        // For testing D, J, M, ERI directly with X = 50:
        // We find raw points such that 100 * (1 - e^(-raw/1000)) = 50
        // e^(-raw/1000) = 0.5 => raw = 1000 * ln(2) ≈ 693.14718056m
        var rawFor50 = (decimal)(1000.0 * Math.Log(2));

        var rawScores = new Dictionary<string, decimal>
        {
            [ExperienceCategories.Academic] = rawFor50,
            [ExperienceCategories.Research] = rawFor50,
            [ExperienceCategories.Global] = rawFor50,
            [ExperienceCategories.CultureSports] = rawFor50,
            [ExperienceCategories.Community] = rawFor50,
            [ExperienceCategories.Entrepreneurship] = rawFor50,
            [ExperienceCategories.RealWorldWork] = 0m
        };

        var tau = RadarPillars.AllSeven.ToDictionary(p => p, _ => 1000m);

        var result = ExperienceIndexCalculator.ComputeRadar(rawScores, tau);

        Assert.Equal(50.0m, result.D);
        Assert.Equal(1.0m, result.J);
        Assert.Equal(1.0m, result.M);
        Assert.Equal(50.0m, result.ERI);
        Assert.Equal(ExperienceTitles.WellRounded, result.ProfileTitle);
    }

    [Fact]
    public void AppendixA5_Example2_SinglePillarScore_NoPlusOne()
    {
        var rawFor50 = (decimal)(1000.0 * Math.Log(2));

        var rawScores = new Dictionary<string, decimal>
        {
            [ExperienceCategories.Academic] = rawFor50,
            [ExperienceCategories.Research] = 0m,
            [ExperienceCategories.Global] = 0m,
            [ExperienceCategories.CultureSports] = 0m,
            [ExperienceCategories.Community] = 0m,
            [ExperienceCategories.Entrepreneurship] = 0m,
            [ExperienceCategories.RealWorldWork] = 0m
        };

        var tau = RadarPillars.AllSeven.ToDictionary(p => p, _ => 1000m);

        var result = ExperienceIndexCalculator.ComputeRadar(rawScores, tau);

        // D ≈ 8.3333, J = 0, M = 1, ERI ≈ 4.1667
        Assert.Equal(8.3333m, result.D);
        Assert.Equal(0.0m, result.J);
        Assert.Equal(1.0m, result.M);
        Assert.Equal(4.1667m, result.ERI);
        Assert.Equal("Thế mạnh Academic", result.ProfileTitle);
    }

    [Fact]
    public void AppendixA5_Example3_SixEqualScores_WithPlusOneScore50()
    {
        var rawFor50 = (decimal)(1000.0 * Math.Log(2));

        var rawScores = new Dictionary<string, decimal>
        {
            [ExperienceCategories.Academic] = rawFor50,
            [ExperienceCategories.Research] = rawFor50,
            [ExperienceCategories.Global] = rawFor50,
            [ExperienceCategories.CultureSports] = rawFor50,
            [ExperienceCategories.Community] = rawFor50,
            [ExperienceCategories.Entrepreneurship] = rawFor50,
            [ExperienceCategories.RealWorldWork] = rawFor50
        };

        var tau = RadarPillars.AllSeven.ToDictionary(p => p, _ => 1000m);

        var result = ExperienceIndexCalculator.ComputeRadar(rawScores, tau);

        // M = 1.15, ERI = 57.5
        Assert.Equal(50.0m, result.D);
        Assert.Equal(1.0m, result.J);
        Assert.Equal(1.15m, result.M);
        Assert.Equal(57.5m, result.ERI);
        Assert.Equal(ExperienceTitles.WellRounded, result.ProfileTitle);
    }

    [Fact]
    public void ZeroContributions_ReturnsUnexploredAndZeroIndices()
    {
        var rawScores = RadarPillars.AllSeven.ToDictionary(p => p, _ => 0m);
        var tau = RadarPillars.AllSeven.ToDictionary(p => p, _ => 1000m);

        var result = ExperienceIndexCalculator.ComputeRadar(rawScores, tau);

        Assert.Equal(0m, result.D);
        Assert.Equal(0m, result.J);
        Assert.Equal(1.0m, result.M);
        Assert.Equal(0m, result.ERI);
        Assert.Equal(ExperienceTitles.Unexplored, result.ProfileTitle);
    }
}
