using AdminService.Data;
using ClubReportHub.Shared.Experience;

namespace AdminService.Contracts;

public sealed record CreateBenchmarkConfigRequest(
    string SemesterCode,
    string AcademicYear,
    bool IsActive = true,
    decimal TauAcademic = 1000m,
    decimal TauResearch = 1000m,
    decimal TauGlobal = 1000m,
    decimal TauCultureSports = 1000m,
    decimal TauCommunity = 1000m,
    decimal TauEntrepreneurship = 1000m,
    decimal TauRealWorldWork = 1000m,
    decimal ThresholdStarter = 20m,
    decimal ThresholdPractitioner = 50m,
    decimal ThresholdLeader = 80m,
    decimal MinPillarScoreAllRounder = 20m,
    decimal MinJAllRounder = 0.90m);

public sealed record UpdateBenchmarkConfigRequest(
    bool IsActive,
    decimal TauAcademic,
    decimal TauResearch,
    decimal TauGlobal,
    decimal TauCultureSports,
    decimal TauCommunity,
    decimal TauEntrepreneurship,
    decimal TauRealWorldWork,
    decimal ThresholdStarter = 20m,
    decimal ThresholdPractitioner = 50m,
    decimal ThresholdLeader = 80m,
    decimal MinPillarScoreAllRounder = 20m,
    decimal MinJAllRounder = 0.90m);

public sealed record BenchmarkConfigResponse(
    Guid Id,
    string SemesterCode,
    string AcademicYear,
    bool IsActive,
    bool IsLocked,
    decimal TauAcademic,
    decimal TauResearch,
    decimal TauGlobal,
    decimal TauCultureSports,
    decimal TauCommunity,
    decimal TauEntrepreneurship,
    decimal TauRealWorldWork,
    decimal ThresholdStarter,
    decimal ThresholdPractitioner,
    decimal ThresholdLeader,
    decimal MinPillarScoreAllRounder,
    decimal MinJAllRounder,
    DateTimeOffset CreatedAtUtc,
    string? CreatedByName,
    DateTimeOffset? LockedAtUtc,
    string? LockedByName)
{
    public static BenchmarkConfigResponse From(SemesterBenchmarkConfig item) =>
        new(
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
            item.LockedByName);
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
