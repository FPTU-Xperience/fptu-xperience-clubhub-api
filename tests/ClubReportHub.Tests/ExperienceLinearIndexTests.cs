using ClubReportHub.Shared.Experience;
using Xunit;

namespace ClubReportHub.Tests;

public sealed class ExperienceLinearIndexTests
{
    [Theory]
    [InlineData(-100, 1.0)]
    [InlineData(0, 1.0)]
    [InlineData(500, 1.15)]
    [InlineData(1000, 1.3)]
    [InlineData(5000, 1.3)]
    public void LinearPlusOneUsesNetRawRatioAndCapsAtThirtyPercent(int plusOneRaw, double expected)
    {
        var raw = RadarPillars.AllSeven.ToDictionary(p => p, _ => 0m);
        var tau = RadarPillars.AllSeven.ToDictionary(p => p, _ => 1000m);
        raw[RadarPillars.PlusOne] = plusOneRaw;
        var result = ExperienceIndexCalculator.ComputeRadar(raw, tau, indexFormulaVersion: ExperienceIndexCalculator.LinearIndexFormula);
        Assert.Equal((decimal)expected, result.M); Assert.Equal(0m, result.D); Assert.Equal(0m, result.J); Assert.Equal(0m, result.ERI);
    }

    [Fact]
    public void RegisteredWorkedExamplesUseSixAxesAndPreserveLegacyFormula()
    {
        var raw = RadarPillars.AllSeven.ToDictionary(p => p, _ => 0m);
        var tau = RadarPillars.AllSeven.ToDictionary(p => p, _ => 1000m);
        raw[RadarPillars.CoreSix[0]] = (decimal)(-1000d * Math.Log(.2));
        raw[RadarPillars.PlusOne] = 1000m;
        var oneAxis = ExperienceIndexCalculator.ComputeRadar(raw, tau, indexFormulaVersion: ExperienceIndexCalculator.LinearIndexFormula);
        Assert.Equal(1.3m, oneAxis.M); Assert.Equal(0m, oneAxis.J); Assert.Equal(8.6667m, oneAxis.ERI);
        foreach (var pillar in RadarPillars.CoreSix) raw[pillar] = raw[RadarPillars.CoreSix[0]];
        var sixAxes = ExperienceIndexCalculator.ComputeRadar(raw, tau, indexFormulaVersion: ExperienceIndexCalculator.LinearIndexFormula);
        Assert.Equal(1m, Math.Round(sixAxes.J, 10)); Assert.Equal(104m, Math.Round(sixAxes.ERI, 6));
        var legacy = ExperienceIndexCalculator.ComputeRadar(raw, tau);
        Assert.Equal(1.1896m, legacy.M); Assert.NotEqual(sixAxes.ERI, legacy.ERI);
    }

    [Fact]
    public void LinearPolicyRejectsMissingOrNonpositiveThresholdAndUnknownFormula()
    {
        var raw = new Dictionary<string, decimal>();
        var tau = RadarPillars.AllSeven.ToDictionary(p => p, _ => 1000m);
        tau[RadarPillars.PlusOne] = 0;
        Assert.Throws<ArgumentException>(() => ExperienceIndexCalculator.ComputeRadar(raw, tau, indexFormulaVersion: ExperienceIndexCalculator.LinearIndexFormula));
        tau.Remove(RadarPillars.PlusOne);
        Assert.Throws<ArgumentException>(() => ExperienceIndexCalculator.ComputeRadar(raw, tau, indexFormulaVersion: ExperienceIndexCalculator.LinearIndexFormula));
        Assert.Throws<ArgumentException>(() => ExperienceIndexCalculator.ComputeRadar(raw, tau, indexFormulaVersion: "unknown"));
    }
}
