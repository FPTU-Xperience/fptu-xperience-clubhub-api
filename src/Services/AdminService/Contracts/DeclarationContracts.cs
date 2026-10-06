using AdminService.Data;

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
    DateOnly EndDate);

public sealed record ReviewDeclarationRequest(
    string Decision,
    string? ReviewNote,
    string? FinalCategory = null,
    string? Tier = null,
    string? Role = null,
    string? Scale = null,
    string? BonusResult = null);

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
    decimal? RawPoints)
{
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
            item.RawPoints);
}
