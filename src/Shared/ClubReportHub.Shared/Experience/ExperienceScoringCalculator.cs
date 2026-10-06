namespace ClubReportHub.Shared.Experience;

/// <summary>
/// Implements the official FPTU Xperience Contribution Scoring formula from Appendix A2:
/// P = B * min(R * Q, 8) * (1 + K)
/// </summary>
public static class ExperienceScoringCalculator
{
    public static decimal GetTierBasePoints(string tier) => tier?.Trim().ToUpperInvariant() switch
    {
        EffortTiers.T1 => 10m,
        EffortTiers.T2 => 25m,
        EffortTiers.T3 => 60m,
        EffortTiers.T4 => 150m,
        EffortTiers.T5 => 400m,
        _ => throw new ArgumentException($"Invalid tier '{tier}'. Expected T1, T2, T3, T4, or T5.")
    };

    public static decimal GetRoleMultiplier(string role)
    {
        var normalized = Normalize(role, ContributionRoles.All);
        return normalized switch
        {
            ContributionRoles.Participant => 1.0m,
            ContributionRoles.Contributor => 1.6m,
            ContributionRoles.Product => 2.2m,
            ContributionRoles.Organizer => 3.0m,
            ContributionRoles.Leader => 4.0m,
            _ => throw new ArgumentException($"Invalid contribution role '{role}'.")
        };
    }

    public static decimal GetScaleMultiplier(string scale)
    {
        var normalized = Normalize(scale, ActivityScales.All);
        return normalized switch
        {
            ActivityScales.Club => 1.0m,
            ActivityScales.Campus => 1.3m,
            ActivityScales.FptEducation => 1.6m,
            ActivityScales.National => 2.2m,
            ActivityScales.International => 3.0m,
            _ => throw new ArgumentException($"Invalid activity scale '{scale}'.")
        };
    }

    public static decimal GetBonusResultMultiplier(string? bonusResult)
    {
        if (string.IsNullOrWhiteSpace(bonusResult))
        {
            return 0.0m;
        }

        var normalized = Normalize(bonusResult, BonusResults.All);
        return normalized switch
        {
            BonusResults.None => 0.0m,
            BonusResults.Finalist => 0.3m,
            BonusResults.Prize => 0.6m,
            BonusResults.RealWorldOutput => 1.0m,
            _ => throw new ArgumentException($"Invalid bonus result '{bonusResult}'.")
        };
    }

    /// <summary>
    /// Calculates raw contribution points P = B * min(R * Q, 8) * (1 + K).
    /// Precision is kept up to 4 decimal places without premature rounding.
    /// </summary>
    public static decimal CalculateRawPoints(string tier, string role, string scale, string? bonusResult = null)
    {
        var b = GetTierBasePoints(tier);
        var r = GetRoleMultiplier(role);
        var q = GetScaleMultiplier(scale);
        var k = GetBonusResultMultiplier(bonusResult);

        var cappedRq = Math.Min(r * q, 8.0m);
        var rawPoints = b * cappedRq * (1.0m + k);

        return Math.Round(rawPoints, 4, MidpointRounding.AwayFromZero);
    }

    private static string Normalize(string value, IEnumerable<string> validValues)
    {
        var match = validValues.FirstOrDefault(v => string.Equals(v, value?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? value?.Trim() ?? string.Empty;
    }
}
