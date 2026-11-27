using AdminService.Data;
using ClubReportHub.Shared.Experience;
using System.Security.Cryptography;
using System.Text.Json;

namespace AdminService.Contracts;

public sealed record SubmitDeclarationRequest(
    string Title,
    string Category,
    string OrganizationSource,
    bool IsOutsideClub,
    int? ClubId,
    string EvidenceUrl,
    string EvidenceDescription,
    string? RoleProposed,
    DateOnly StartDate,
    DateOnly EndDate,
    string? CampusCode = null, string? SemesterId = null, DateTimeOffset? OccurredAt = null,
    ContributionAttributes? ProposedAttributes = null, IReadOnlyList<Guid>? EvidenceRefs = null,
    Guid? ExpectedVersion = null, Guid? PolicyVersion = null, int? ReferringClubId = null);

public sealed record ReviewDeclarationRequest(
    string Decision,
    string? ReviewNote,
    string? FinalCategory = null,
    string? Tier = null,
    string? Role = null,
    string? Scale = null,
    string? BonusResult = null, Guid? ExpectedVersion = null, string? Reason = null,
    ContributionAttributes? VerifiedAttributes = null);

public sealed record UpgradeDeclarationRequest(string ExpectedLegacyVersion, SubmitDeclarationRequest Submission);

public sealed record DeclarationResponse(
    Guid Id,
    int StudentId,
    string StudentName,
    string StudentEmail,
    string Title,
    string Category,
    string? FinalCategory,
    string OrganizationSource,
    bool IsOutsideClub,
    int? ClubId,
    string EvidenceUrl,
    string EvidenceDescription,
    string? RoleProposed,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReviewedAtUtc,
    int? ReviewedByUserId,
    string? ReviewedByName,
    string? ReviewNote,
    string? Tier,
    string? Role,
    string? Scale,
    string? BonusResult,
    decimal? RawPoints,
    string CampusCode = ClubReportHub.Shared.Auth.CampusCodes.Hanoi)
{
    public bool IsAuthoritative => false;
    public string SourceNamespace => "LEGACY_DECLARATION";
    public string LegacyVersion { get; init; } = "";
    public bool CanUpgradeLegacy => IsOutsideClub && ClubId is null && Status is "Submitted" or "RevisionRequested";
    public static string Fingerprint(SelfDeclaration item) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(item)));
    public static DeclarationResponse From(SelfDeclaration item) =>
        new(
            item.Id,
            item.StudentId,
            item.StudentName,
            item.StudentEmail,
            item.Title,
            item.Category,
            item.FinalCategory ?? item.Category,
            item.OrganizationSource,
            item.IsOutsideClub,
            item.ClubId,
            item.EvidenceUrl,
            item.EvidenceDescription,
            item.RoleProposed,
            item.StartDate,
            item.EndDate,
            item.Status,
            item.CreatedAtUtc,
            item.ReviewedAtUtc,
            item.ReviewedByUserId,
            item.ReviewedByName,
            item.ReviewNote,
            item.Tier,
            item.Role,
            item.Scale,
            item.BonusResult,
            item.RawPoints,
            item.CampusCode)
        { LegacyVersion = Fingerprint(item) };
}
