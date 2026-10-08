using System.Text.Json.Serialization;
using AdminService.Data;
using ClubReportHub.Shared.Experience;

namespace AdminService.Contracts;

public sealed record CapabilityWeightItem(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("weight")] decimal Weight,
    [property: JsonPropertyName("label")] string? Label = null,
    [property: JsonPropertyName("color")] string? Color = null);

public sealed record CreateBenchmarkConfigRequest(
    [property: JsonPropertyName("semesterCode")] string? SemesterCode = null,
    [property: JsonPropertyName("academicYear")] string? AcademicYear = null,
    [property: JsonPropertyName("isActive")] bool IsActive = true,
    [property: JsonPropertyName("tauAcademic")] decimal TauAcademic = 1000m,
    [property: JsonPropertyName("tauResearch")] decimal TauResearch = 1000m,
    [property: JsonPropertyName("tauGlobal")] decimal TauGlobal = 1000m,
    [property: JsonPropertyName("tauCultureSports")] decimal TauCultureSports = 1000m,
    [property: JsonPropertyName("tauCommunity")] decimal TauCommunity = 1000m,
    [property: JsonPropertyName("tauEntrepreneurship")] decimal TauEntrepreneurship = 1000m,
    [property: JsonPropertyName("tauRealWorldWork")] decimal TauRealWorldWork = 1000m,
    [property: JsonPropertyName("thresholdStarter")] decimal ThresholdStarter = 20m,
    [property: JsonPropertyName("thresholdPractitioner")] decimal ThresholdPractitioner = 50m,
    [property: JsonPropertyName("thresholdLeader")] decimal ThresholdLeader = 80m,
    [property: JsonPropertyName("minPillarScoreAllRounder")] decimal MinPillarScoreAllRounder = 20m,
    [property: JsonPropertyName("minJAllRounder")] decimal MinJAllRounder = 0.90m,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<CapabilityWeightItem>? Capabilities = null,
    [property: JsonPropertyName("bonusCap")] decimal? BonusCap = null,
    [property: JsonPropertyName("effectiveDate")] string? EffectiveDate = null)
{
    public (decimal TauAcademic, decimal TauResearch, decimal TauGlobal, decimal TauCultureSports, decimal TauCommunity, decimal TauEntrepreneurship, decimal TauRealWorldWork) ResolveTaus()
    {
        if (Capabilities is { Count: > 0 })
        {
            var dict = Capabilities.ToDictionary(x => x.Key.Trim().ToUpperInvariant(), x => x.Weight);
            var tauAcad = dict.TryGetValue("ACAD", out var acad) && acad > 0 ? Math.Round(acad * 50m, 2) : (TauAcademic > 0 ? TauAcademic : 1000m);
            var tauProf = dict.TryGetValue("PROF", out var prof) && prof > 0 ? Math.Round(prof * 50m, 2) : (TauEntrepreneurship > 0 ? TauEntrepreneurship : 1000m);
            var tauComm = dict.TryGetValue("COMM", out var comm) && comm > 0 ? Math.Round(comm * 50m, 2) : (TauCommunity > 0 ? TauCommunity : 1000m);
            var tauPhys = dict.TryGetValue("PHYS", out var phys) && phys > 0 ? Math.Round(phys * 50m, 2) : (TauCultureSports > 0 ? TauCultureSports : 1000m);
            var tauGlob = dict.TryGetValue("GLOB", out var glob) && glob > 0 ? Math.Round(glob * 50m, 2) : (TauGlobal > 0 ? TauGlobal : 1000m);
            var tauEthi = dict.TryGetValue("ETHI", out var ethi) && ethi > 0 ? Math.Round(ethi * 50m, 2) : (TauResearch > 0 ? TauResearch : 1000m);
            var tauReal = TauRealWorldWork > 0 ? TauRealWorldWork : tauProf;

            return (tauAcad, tauEthi, tauGlob, tauPhys, tauComm, tauProf, tauReal);
        }

        return (
            TauAcademic > 0 ? TauAcademic : 1000m,
            TauResearch > 0 ? TauResearch : 1000m,
            TauGlobal > 0 ? TauGlobal : 1000m,
            TauCultureSports > 0 ? TauCultureSports : 1000m,
            TauCommunity > 0 ? TauCommunity : 1000m,
            TauEntrepreneurship > 0 ? TauEntrepreneurship : 1000m,
            TauRealWorldWork > 0 ? TauRealWorldWork : 1000m);
    }

    public string ResolveAcademicYear()
    {
        if (!string.IsNullOrWhiteSpace(AcademicYear)) return AcademicYear.Trim();
        if (!string.IsNullOrWhiteSpace(SemesterCode))
        {
            var match = System.Text.RegularExpressions.Regex.Match(SemesterCode, @"\d{4}");
            if (match.Success && int.TryParse(match.Value, out var year))
            {
                return $"{year}-{year + 1}";
            }
            var shortMatch = System.Text.RegularExpressions.Regex.Match(SemesterCode, @"\d{2}");
            if (shortMatch.Success && int.TryParse(shortMatch.Value, out var shortYear))
            {
                var fullYear = 2000 + shortYear;
                return $"{fullYear}-{fullYear + 1}";
            }
        }
        return "2026-2027";
    }
}

public sealed record UpdateBenchmarkConfigRequest(
    [property: JsonPropertyName("isActive")] bool IsActive = true,
    [property: JsonPropertyName("tauAcademic")] decimal TauAcademic = 1000m,
    [property: JsonPropertyName("tauResearch")] decimal TauResearch = 1000m,
    [property: JsonPropertyName("tauGlobal")] decimal TauGlobal = 1000m,
    [property: JsonPropertyName("tauCultureSports")] decimal TauCultureSports = 1000m,
    [property: JsonPropertyName("tauCommunity")] decimal TauCommunity = 1000m,
    [property: JsonPropertyName("tauEntrepreneurship")] decimal TauEntrepreneurship = 1000m,
    [property: JsonPropertyName("tauRealWorldWork")] decimal TauRealWorldWork = 1000m,
    [property: JsonPropertyName("thresholdStarter")] decimal ThresholdStarter = 20m,
    [property: JsonPropertyName("thresholdPractitioner")] decimal ThresholdPractitioner = 50m,
    [property: JsonPropertyName("thresholdLeader")] decimal ThresholdLeader = 80m,
    [property: JsonPropertyName("minPillarScoreAllRounder")] decimal MinPillarScoreAllRounder = 20m,
    [property: JsonPropertyName("minJAllRounder")] decimal MinJAllRounder = 0.90m,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<CapabilityWeightItem>? Capabilities = null,
    [property: JsonPropertyName("bonusCap")] decimal? BonusCap = null,
    [property: JsonPropertyName("effectiveDate")] string? EffectiveDate = null)
{
    public (decimal TauAcademic, decimal TauResearch, decimal TauGlobal, decimal TauCultureSports, decimal TauCommunity, decimal TauEntrepreneurship, decimal TauRealWorldWork) ResolveTaus()
    {
        if (Capabilities is { Count: > 0 })
        {
            var dict = Capabilities.ToDictionary(x => x.Key.Trim().ToUpperInvariant(), x => x.Weight);
            var tauAcad = dict.TryGetValue("ACAD", out var acad) && acad > 0 ? Math.Round(acad * 50m, 2) : (TauAcademic > 0 ? TauAcademic : 1000m);
            var tauProf = dict.TryGetValue("PROF", out var prof) && prof > 0 ? Math.Round(prof * 50m, 2) : (TauEntrepreneurship > 0 ? TauEntrepreneurship : 1000m);
            var tauComm = dict.TryGetValue("COMM", out var comm) && comm > 0 ? Math.Round(comm * 50m, 2) : (TauCommunity > 0 ? TauCommunity : 1000m);
            var tauPhys = dict.TryGetValue("PHYS", out var phys) && phys > 0 ? Math.Round(phys * 50m, 2) : (TauCultureSports > 0 ? TauCultureSports : 1000m);
            var tauGlob = dict.TryGetValue("GLOB", out var glob) && glob > 0 ? Math.Round(glob * 50m, 2) : (TauGlobal > 0 ? TauGlobal : 1000m);
            var tauEthi = dict.TryGetValue("ETHI", out var ethi) && ethi > 0 ? Math.Round(ethi * 50m, 2) : (TauResearch > 0 ? TauResearch : 1000m);
            var tauReal = TauRealWorldWork > 0 ? TauRealWorldWork : tauProf;

            return (tauAcad, tauEthi, tauGlob, tauPhys, tauComm, tauProf, tauReal);
        }

        return (
            TauAcademic > 0 ? TauAcademic : 1000m,
            TauResearch > 0 ? TauResearch : 1000m,
            TauGlobal > 0 ? TauGlobal : 1000m,
            TauCultureSports > 0 ? TauCultureSports : 1000m,
            TauCommunity > 0 ? TauCommunity : 1000m,
            TauEntrepreneurship > 0 ? TauEntrepreneurship : 1000m,
            TauRealWorldWork > 0 ? TauRealWorldWork : 1000m);
    }
}

public sealed record BenchmarkConfigResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("semesterCode")] string SemesterCode,
    [property: JsonPropertyName("academicYear")] string AcademicYear,
    [property: JsonPropertyName("isActive")] bool IsActive,
    [property: JsonPropertyName("isLocked")] bool IsLocked,
    [property: JsonPropertyName("tauAcademic")] decimal TauAcademic,
    [property: JsonPropertyName("tauResearch")] decimal TauResearch,
    [property: JsonPropertyName("tauGlobal")] decimal TauGlobal,
    [property: JsonPropertyName("tauCultureSports")] decimal TauCultureSports,
    [property: JsonPropertyName("tauCommunity")] decimal TauCommunity,
    [property: JsonPropertyName("tauEntrepreneurship")] decimal TauEntrepreneurship,
    [property: JsonPropertyName("tauRealWorldWork")] decimal TauRealWorldWork,
    [property: JsonPropertyName("thresholdStarter")] decimal ThresholdStarter,
    [property: JsonPropertyName("thresholdPractitioner")] decimal ThresholdPractitioner,
    [property: JsonPropertyName("thresholdLeader")] decimal ThresholdLeader,
    [property: JsonPropertyName("minPillarScoreAllRounder")] decimal MinPillarScoreAllRounder,
    [property: JsonPropertyName("minJAllRounder")] decimal MinJAllRounder,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("createdByName")] string? CreatedByName,
    [property: JsonPropertyName("lockedAtUtc")] DateTimeOffset? LockedAtUtc,
    [property: JsonPropertyName("lockedByName")] string? LockedByName,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<CapabilityWeightItem>? Capabilities = null,
    [property: JsonPropertyName("bonusCap")] decimal BonusCap = 500m,
    [property: JsonPropertyName("status")] string Status = "ACTIVE",
    [property: JsonPropertyName("effectiveDate")] string? EffectiveDate = null)
{
    public static BenchmarkConfigResponse From(SemesterBenchmarkConfig item)
    {
        var tauSum = item.TauAcademic + item.TauResearch + item.TauGlobal +
                     item.TauCultureSports + item.TauCommunity + item.TauEntrepreneurship;
        if (tauSum <= 0m) tauSum = 6000m;

        var caps = new List<CapabilityWeightItem>
        {
            new("ACAD", Math.Round(item.TauAcademic / tauSum * 100m, 1), "Học thuật", "#5b8dd9"),
            new("PROF", Math.Round(item.TauEntrepreneurship / tauSum * 100m, 1), "Kỹ năng nghề nghiệp", "#7c6fcd"),
            new("COMM", Math.Round(item.TauCommunity / tauSum * 100m, 1), "Cộng đồng", "#3fa87b"),
            new("PHYS", Math.Round(item.TauCultureSports / tauSum * 100m, 1), "Thể chất", "#d97b3e"),
            new("GLOB", Math.Round(item.TauGlobal / tauSum * 100m, 1), "Công dân toàn cầu", "#c85fa8"),
            new("ETHI", Math.Round(item.TauResearch / tauSum * 100m, 1), "Đạo đức & Kỹ năng sống", "#8a6f3f")
        };

        var status = item.IsLocked ? "LOCKED" : (item.IsActive ? "ACTIVE" : "DRAFT");
        var effectiveDate = item.CreatedAtUtc.ToString("yyyy-MM-dd");

        return new(
            item.Id,
            item.SemesterCode,
            item.AcademicYear,
            item.IsActive,
            item.IsLocked,
            item.TauAcademic,
            item.TauResearch,
            item.TauGlobal,
            item.TauCultureSports,
            item.TauCommunity,
            item.TauEntrepreneurship,
            item.TauRealWorldWork,
            item.ThresholdStarter,
            item.ThresholdPractitioner,
            item.ThresholdLeader,
            item.MinPillarScoreAllRounder,
            item.MinJAllRounder,
            item.CreatedAtUtc,
            item.CreatedByName,
            item.LockedAtUtc,
            item.LockedByName,
            caps,
            500m,
            status,
            effectiveDate);
    }
}

public sealed record PillarRadarItemResponse(
    string Pillar,
    decimal RawPointsSum,
    decimal Tau,
    decimal SaturatedScore,
    string MasteryTier,
    bool HasLeaderProof);

public sealed record StudentRadarResponse(
    int StudentId,
    string StudentName,
    string StudentEmail,
    string SemesterCode,
    decimal D,
    decimal J,
    decimal M,
    decimal ERI,
    string ProfileTitle,
    int ApprovedDeclarationsCount,
    IReadOnlyList<PillarRadarItemResponse> Pillars);

public sealed record PillarOverviewResponse(
    string Pillar,
    string PillarName,
    string Description,
    decimal Tau,
    decimal TotalRawPoints,
    decimal AverageRawPoints,
    decimal AverageSaturatedScore,
    int StudentCount,
    int LeaderCount,
    IReadOnlyDictionary<string, int> MasteryTierCounts);

public sealed record ProfileTitleStatResponse(
    string Title,
    int Count,
    decimal Percentage);

public sealed record CampusComparisonItemResponse(
    string CampusCode,
    string CampusName,
    int TotalStudents,
    int TotalApprovedDeclarations,
    decimal AverageD,
    decimal AverageJ,
    decimal AverageM,
    decimal AverageERI,
    string TopStrengthPillar);

public sealed record CampusRadarOverviewResponse(
    string Scope,
    string CampusCode,
    string CampusName,
    string SemesterCode,
    string AcademicYear,
    int TotalStudents,
    int TotalApprovedDeclarations,
    decimal TotalRawPointsAwarded,
    decimal AverageD,
    decimal AverageJ,
    decimal AverageM,
    decimal AverageERI,
    IReadOnlyList<PillarOverviewResponse> Pillars,
    IReadOnlyList<ProfileTitleStatResponse> ProfileTitlesDistribution,
    IReadOnlyList<CampusComparisonItemResponse>? CampusesComparison);

