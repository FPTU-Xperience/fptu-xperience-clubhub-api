using System.ComponentModel.DataAnnotations;

namespace AdminService.Contracts;

public sealed record SemesterResponse(
    Guid Id,
    string SemesterCode,
    string? Label,
    string AcademicYear,
    string? Start,
    string? End,
    int Threshold,
    int XpPerLevel,
    bool Rankings,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CreateSemesterRequest(
    [Required, MaxLength(30)] string SemesterCode,
    [MaxLength(100)] string? Label = null,
    [MaxLength(30)] string? AcademicYear = null,
    string? Start = null,
    string? End = null,
    int? Threshold = null,
    int? XpPerLevel = null,
    bool? Rankings = null,
    bool? IsActive = null);

public sealed record UpdateSemesterRequest(
    [MaxLength(100)] string? Label = null,
    [MaxLength(30)] string? AcademicYear = null,
    string? Start = null,
    string? End = null,
    int? Threshold = null,
    int? XpPerLevel = null,
    bool? Rankings = null,
    bool? IsActive = null);
