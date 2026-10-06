using ClubReportHub.Shared.Experience;
using Xunit;

namespace ClubReportHub.Tests;

public sealed class ExperienceScoringCalculatorTests
{
    [Theory]
    [InlineData("T1", 10)]
    [InlineData("T2", 25)]
    [InlineData("T3", 60)]
    [InlineData("T4", 150)]
    [InlineData("T5", 400)]
    public void GetTierBasePoints_ReturnsExpectedValue(string tier, decimal expected)
    {
        var result = ExperienceScoringCalculator.GetTierBasePoints(tier);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Participant", 1.0)]
    [InlineData("Contributor", 1.6)]
    [InlineData("Product", 2.2)]
    [InlineData("Organizer", 3.0)]
    [InlineData("Leader", 4.0)]
    public void GetRoleMultiplier_ReturnsExpectedValue(string role, decimal expected)
    {
        var result = ExperienceScoringCalculator.GetRoleMultiplier(role);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Club", 1.0)]
    [InlineData("Campus", 1.3)]
    [InlineData("FptEducation", 1.6)]
    [InlineData("National", 2.2)]
    [InlineData("International", 3.0)]
    public void GetScaleMultiplier_ReturnsExpectedValue(string scale, decimal expected)
    {
        var result = ExperienceScoringCalculator.GetScaleMultiplier(scale);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("None", 0.0)]
    [InlineData(null, 0.0)]
    [InlineData("Finalist", 0.3)]
    [InlineData("Prize", 0.6)]
    [InlineData("RealWorldOutput", 1.0)]
    public void GetBonusResultMultiplier_ReturnsExpectedValue(string? bonus, decimal expected)
    {
        var result = ExperienceScoringCalculator.GetBonusResultMultiplier(bonus);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalculateRawPoints_ExampleA_NationalContestFinalist()
    {
        // Tier T4 (150) * Contributor (1.6) * National (2.2) * (1 + Finalist (0.3)) = 150 * 3.52 * 1.3 = 686.4
        var points = ExperienceScoringCalculator.CalculateRawPoints(
            EffortTiers.T4,
            ContributionRoles.Contributor,
            ActivityScales.National,
            BonusResults.Finalist);

        Assert.Equal(686.4m, points);
    }

    [Fact]
    public void CalculateRawPoints_ExampleB_CampusEventLeader()
    {
        // Tier T3 (60) * Leader (4.0) * Campus (1.3) * (1 + 0) = 60 * 5.2 = 312.0
        var points = ExperienceScoringCalculator.CalculateRawPoints(
            EffortTiers.T3,
            ContributionRoles.Leader,
            ActivityScales.Campus,
            BonusResults.None);

        Assert.Equal(312.0m, points);
    }

    [Fact]
    public void CalculateRawPoints_ExampleC_CappingRqAt8()
    {
        // Tier T5 (400) * Leader (4.0) * International (3.0) -> R * Q = 12 > 8 (capped at 8)
        // Bonus RealWorldOutput (1.0) -> (1 + 1) = 2
        // P = 400 * 8 * 2 = 6400.0
        var points = ExperienceScoringCalculator.CalculateRawPoints(
            EffortTiers.T5,
            ContributionRoles.Leader,
            ActivityScales.International,
            BonusResults.RealWorldOutput);

        Assert.Equal(6400.0m, points);
    }

    [Fact]
    public void CalculateRawPoints_InvalidTier_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            ExperienceScoringCalculator.CalculateRawPoints("InvalidTier", ContributionRoles.Participant, ActivityScales.Club));
    }
}
