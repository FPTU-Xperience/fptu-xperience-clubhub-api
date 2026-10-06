namespace ClubReportHub.Shared.Experience;

public sealed record PillarAccumulation(
    string Pillar,
    decimal RawPointsSum,
    decimal Tau,
    decimal SaturatedScore,
    string MasteryTier,
    bool HasLeaderProof);

public sealed record RadarIndexResult(
    IReadOnlyDictionary<string, PillarAccumulation> Pillars,
    decimal D,
    decimal J,
    decimal M,
    decimal ERI,
    string ProfileTitle);

/// <summary>
/// Implements Appendix A3 & A4: 6+1 Saturated Radar, D, J, M, ERI and Mastery Tiers/Titles.
/// </summary>
public static class ExperienceIndexCalculator
{
    private const double Ln6 = 1.791759469228055; // Math.Log(6)

    /// <summary>
    /// Calculates Saturated Score: X_j = 100 * (1 - e^(-S_j / tau_j))
    /// </summary>
    public static decimal CalculateSaturatedScore(decimal rawSum, decimal tau)
    {
        if (rawSum <= 0m || tau <= 0m)
        {
            return 0m;
        }

        var s = (double)rawSum;
        var t = (double)tau;
        var x = 100.0 * (1.0 - Math.Exp(-s / t));
        if (x < 0.0) x = 0.0;
        if (x > 100.0) x = 100.0;

        return (decimal)x;
    }

    /// <summary>
    /// Determines the 5-tier mastery level:
    /// Unexplored: X = 0
    /// Starter: 0 &lt; X &lt; 20
    /// Practitioner: 20 &lt;= X &lt; 50
    /// Contributor: 50 &lt;= X &lt; 80
    /// Leader: X &gt;= 80 AND hasLeaderProof (otherwise capped at Contributor)
    /// </summary>
    public static string DetermineMasteryTier(decimal saturatedScore, bool hasLeaderProof)
    {
        if (saturatedScore <= 0m)
        {
            return ExperienceMasteryTiers.Unexplored;
        }
        if (saturatedScore < 20m)
        {
            return ExperienceMasteryTiers.Starter;
        }
        if (saturatedScore < 50m)
        {
            return ExperienceMasteryTiers.Practitioner;
        }
        if (saturatedScore < 80m)
        {
            return ExperienceMasteryTiers.Contributor;
        }

        // X >= 80: Requires leader proof to reach Leader, otherwise capped at Contributor (Appendix A4)
        return hasLeaderProof
            ? ExperienceMasteryTiers.Leader
            : ExperienceMasteryTiers.Contributor;
    }

    /// <summary>
    /// Calculates D, J, M, ERI, and determines Profile Title according to Appendix A3 &amp; A4.
    /// </summary>
    public static RadarIndexResult ComputeRadar(
        IReadOnlyDictionary<string, decimal> rawScoresByPillar,
        IReadOnlyDictionary<string, decimal> tauByPillar,
        ISet<string>? pillarsWithLeaderProof = null)
    {
        var pillars = new Dictionary<string, PillarAccumulation>(StringComparer.OrdinalIgnoreCase);

        foreach (var pillar in RadarPillars.AllSeven)
        {
            var raw = rawScoresByPillar.TryGetValue(pillar, out var rVal) ? rVal : 0m;
            var tau = tauByPillar.TryGetValue(pillar, out var tVal) && tVal > 0m ? tVal : 1000m;
            var sat = CalculateSaturatedScore(raw, tau);
            var hasLeader = pillarsWithLeaderProof?.Contains(pillar) ?? false;
            var tier = DetermineMasteryTier(sat, hasLeader);

            pillars[pillar] = new PillarAccumulation(pillar, raw, tau, sat, tier, hasLeader);
        }

        // Core 6 pillars for D and J
        var coreSatScores = RadarPillars.CoreSix
            .Select(p => pillars[p].SaturatedScore)
            .ToArray();

        var coreSum = coreSatScores.Sum();
        decimal d = coreSum / 6.0m;

        decimal j = 0m;
        if (coreSum > 0m)
        {
            double entropy = 0.0;
            var totalDouble = (double)coreSum;
            foreach (var s in coreSatScores)
            {
                var p = (double)s / totalDouble;
                if (p > 0.0)
                {
                    entropy -= p * Math.Log(p);
                }
            }
            var jVal = entropy / Ln6;
            if (jVal < 0.0) jVal = 0.0;
            if (jVal > 1.0) jVal = 1.0;
            j = (decimal)jVal;
        }

        // PlusOne (+1) pillar for M
        var plusOneSat = pillars[RadarPillars.PlusOne].SaturatedScore;
        decimal m = 1.0m + 0.30m * (plusOneSat / 100.0m);

        // ERI = D * (0.5 + 0.5 * J) * M
        decimal eri;
        if (d <= 0m)
        {
            eri = 0m;
        }
        else
        {
            eri = d * (0.5m + 0.5m * j) * m;
            if (eri > 130m) eri = 130m;
        }

        // Determine Profile Title
        string profileTitle;
        if (d <= 0m && plusOneSat <= 0m)
        {
            profileTitle = ExperienceTitles.Unexplored;
        }
        else if (coreSatScores.All(s => s >= 20m) && j >= 0.90m)
        {
            profileTitle = ExperienceTitles.WellRounded;
        }
        else
        {
            // Highest score among 6 pillars, tie-break by order in CoreSix
            string topPillar = RadarPillars.CoreSix[0];
            decimal maxScore = -1m;
            foreach (var pName in RadarPillars.CoreSix)
            {
                var s = pillars[pName].SaturatedScore;
                if (s > maxScore)
                {
                    maxScore = s;
                    topPillar = pName;
                }
            }

            profileTitle = maxScore > 0m
                ? ExperienceTitles.StrengthOf(topPillar)
                : ExperienceTitles.Unexplored;
        }

        return new RadarIndexResult(
            Pillars: pillars,
            D: Math.Round(d, 4, MidpointRounding.AwayFromZero),
            J: Math.Round(j, 4, MidpointRounding.AwayFromZero),
            M: Math.Round(m, 4, MidpointRounding.AwayFromZero),
            ERI: Math.Round(eri, 4, MidpointRounding.AwayFromZero),
            ProfileTitle: profileTitle);
    }
}
