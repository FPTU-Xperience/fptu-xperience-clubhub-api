using AdminService.Auditing;
using AdminService.Contracts;
using AdminService.Data;
using AdminService.Security;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Endpoints;

public static class SemesterEndpoints
{
    public static IEndpointRouteBuilder MapSemesterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1/semesters").WithTags("Semesters");

        api.MapGet("/", GetSemestersAsync)
            .WithName("GetSemesters")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces<IReadOnlyList<SemesterResponse>>();

        api.MapGet("/active", GetActiveSemesterAsync)
            .WithName("GetActiveSemester")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces<SemesterResponse>();

        api.MapPost("/", CreateSemesterAsync)
            .WithName("CreateSemester")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces<SemesterResponse>(StatusCodes.Status201Created);

        api.MapPut("/{identifier}", UpdateSemesterAsync)
            .WithName("UpdateSemester")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces<SemesterResponse>();

        // Direct aliases for frontend convenience
        var direct = endpoints.MapGroup("/api/semesters").WithTags("Semesters Direct");

        direct.MapGet("/", GetSemestersAsync)
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .ExcludeFromDescription();

        direct.MapGet("/active", GetActiveSemesterAsync)
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .ExcludeFromDescription();

        direct.MapPost("/", CreateSemesterAsync)
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .ExcludeFromDescription();

        direct.MapPut("/{identifier}", UpdateSemesterAsync)
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<IResult> GetSemestersAsync(
        AdminDbContext db,
        CancellationToken cancellationToken)
    {
        var list = await db.Semesters
            .AsNoTracking()
            .OrderByDescending(x => x.StartDate ?? x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (list.Count == 0)
        {
            var defaults = new List<Semester>
            {
                new()
                {
                    SemesterCode = "FALL2026",
                    Label = "Fall 2026",
                    AcademicYear = "2026-2027",
                    StartDate = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
                    EndDate = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
                    Threshold = 200,
                    XpPerLevel = 100,
                    RankingsEnabled = true,
                    IsActive = true
                },
                new()
                {
                    SemesterCode = "SUMMER2026",
                    Label = "Summer 2026",
                    AcademicYear = "2025-2026",
                    StartDate = new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero),
                    EndDate = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero),
                    Threshold = 150,
                    XpPerLevel = 80,
                    RankingsEnabled = true,
                    IsActive = false
                },
                new()
                {
                    SemesterCode = "SPRING2026",
                    Label = "Spring 2026",
                    AcademicYear = "2025-2026",
                    StartDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                    EndDate = new DateTimeOffset(2026, 5, 14, 0, 0, 0, TimeSpan.Zero),
                    Threshold = 150,
                    XpPerLevel = 80,
                    RankingsEnabled = true,
                    IsActive = false
                }
            };

            db.Semesters.AddRange(defaults);
            await db.SaveChangesAsync(cancellationToken);
            list = defaults;
        }

        return Results.Ok(list.Select(ToResponse));
    }

    private static async Task<IResult> GetActiveSemesterAsync(
        AdminDbContext db,
        CancellationToken cancellationToken)
    {
        var active = await db.Semesters
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsActive, cancellationToken);

        if (active is null)
        {
            var all = await db.Semesters.AsNoTracking().ToListAsync(cancellationToken);
            if (all.Count == 0)
            {
                await GetSemestersAsync(db, cancellationToken);
                active = await db.Semesters.AsNoTracking().FirstOrDefaultAsync(x => x.IsActive, cancellationToken);
            }
            else
            {
                active = all.First();
            }
        }

        return active is not null
            ? Results.Ok(ToResponse(active))
            : Results.NotFound(new { message = "No active semester found." });
    }

    private static async Task<IResult> CreateSemesterAsync(
        CreateSemesterRequest request,
        AdminDbContext db,
        IAuditService auditService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SemesterCode))
        {
            return Results.BadRequest(new { message = "Semester code is required." });
        }

        var code = request.SemesterCode.Trim().ToUpperInvariant();
        if (await db.Semesters.AnyAsync(x => x.SemesterCode == code, cancellationToken))
        {
            return Results.Conflict(new { message = $"Semester '{code}' already exists." });
        }

        var startDate = ParseDate(request.Start);
        var endDate = ParseDate(request.End);
        if (startDate.HasValue && endDate.HasValue && startDate >= endDate)
        {
            return Results.BadRequest(new { message = "End date must be after start date." });
        }

        var isActive = request.IsActive ?? false;
        if (isActive)
        {
            await db.Semesters
                .Where(x => x.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), cancellationToken);
        }

        var academicYear = !string.IsNullOrWhiteSpace(request.AcademicYear)
            ? request.AcademicYear.Trim()
            : DeriveAcademicYear(code, startDate);

        var semester = new Semester
        {
            SemesterCode = code,
            Label = string.IsNullOrWhiteSpace(request.Label) ? FormatDefaultLabel(code) : request.Label.Trim(),
            AcademicYear = academicYear,
            StartDate = startDate,
            EndDate = endDate,
            Threshold = request.Threshold ?? 200,
            XpPerLevel = request.XpPerLevel ?? 100,
            RankingsEnabled = request.Rankings ?? true,
            IsActive = isActive,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        db.Semesters.Add(semester);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(new AuditWriteRequest(
            Action: "CREATE_SEMESTER",
            ResourceType: "Semester",
            ResourceId: semester.SemesterCode,
            Metadata: new Dictionary<string, string?>
            {
                ["SemesterCode"] = semester.SemesterCode,
                ["AcademicYear"] = semester.AcademicYear,
                ["IsActive"] = semester.IsActive.ToString()
            }), cancellationToken);

        return Results.Created($"/api/v1/semesters/{semester.Id}", ToResponse(semester));
    }

    private static async Task<IResult> UpdateSemesterAsync(
        string identifier,
        UpdateSemesterRequest request,
        AdminDbContext db,
        IAuditService auditService,
        CancellationToken cancellationToken)
    {
        Semester? semester = null;
        if (Guid.TryParse(identifier, out var guidId))
        {
            semester = await db.Semesters.FirstOrDefaultAsync(x => x.Id == guidId, cancellationToken);
        }
        if (semester is null)
        {
            semester = await db.Semesters.FirstOrDefaultAsync(
                x => x.SemesterCode.ToLower() == identifier.ToLower(),
                cancellationToken);
        }

        if (semester is null)
        {
            return Results.NotFound(new { message = $"Semester '{identifier}' not found." });
        }

        if (request.Label is not null)
        {
            semester.Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.AcademicYear))
        {
            semester.AcademicYear = request.AcademicYear.Trim();
        }
        if (request.Start is not null)
        {
            semester.StartDate = ParseDate(request.Start);
        }
        if (request.End is not null)
        {
            semester.EndDate = ParseDate(request.End);
        }
        if (request.Threshold.HasValue)
        {
            semester.Threshold = request.Threshold.Value;
        }
        if (request.XpPerLevel.HasValue)
        {
            semester.XpPerLevel = request.XpPerLevel.Value;
        }
        if (request.Rankings.HasValue)
        {
            semester.RankingsEnabled = request.Rankings.Value;
        }

        if (request.IsActive.HasValue && request.IsActive.Value && !semester.IsActive)
        {
            await db.Semesters
                .Where(x => x.Id != semester.Id && x.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), cancellationToken);
            semester.IsActive = true;
        }
        else if (request.IsActive.HasValue)
        {
            semester.IsActive = request.IsActive.Value;
        }

        semester.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(new AuditWriteRequest(
            Action: "UPDATE_SEMESTER",
            ResourceType: "Semester",
            ResourceId: semester.SemesterCode,
            Metadata: new Dictionary<string, string?>
            {
                ["SemesterCode"] = semester.SemesterCode,
                ["IsActive"] = semester.IsActive.ToString()
            }), cancellationToken);

        return Results.Ok(ToResponse(semester));
    }

    private static SemesterResponse ToResponse(Semester s) =>
        new(
            s.Id,
            s.SemesterCode,
            s.Label ?? s.SemesterCode,
            s.AcademicYear,
            s.StartDate?.ToString("yyyy-MM-dd"),
            s.EndDate?.ToString("yyyy-MM-dd"),
            s.Threshold,
            s.XpPerLevel,
            s.RankingsEnabled,
            s.IsActive,
            s.CreatedAtUtc,
            s.UpdatedAtUtc);

    private static DateTimeOffset? ParseDate(string? dateString)
    {
        if (string.IsNullOrWhiteSpace(dateString)) return null;
        if (DateTimeOffset.TryParse(dateString, out var dto)) return dto;
        if (DateTime.TryParse(dateString, out var dt)) return new DateTimeOffset(dt, TimeSpan.Zero);
        return null;
    }

    private static string FormatDefaultLabel(string code)
    {
        var upper = code.ToUpperInvariant();
        if (upper.StartsWith("FALL") && upper.Length >= 8)
            return $"Fall {upper[4..]}";
        if (upper.StartsWith("SUMMER") && upper.Length >= 10)
            return $"Summer {upper[6..]}";
        if (upper.StartsWith("SPRING") && upper.Length >= 10)
            return $"Spring {upper[6..]}";
        return code;
    }

    private static string DeriveAcademicYear(string code, DateTimeOffset? date)
    {
        if (date.HasValue)
        {
            var year = date.Value.Year;
            return $"{year}-{year + 1}";
        }
        return "2026-2027";
    }
}
