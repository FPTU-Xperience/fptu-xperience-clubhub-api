using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;
using ReportService.Clients;
using ReportService.Contracts;
using ReportService.Data;
using ReportService.Models;

namespace ReportService.Endpoints;

public static class DeadlineEndpoints
{
    public static void MapDeadlineEndpoints(this WebApplication app)
    {
        var deadlines = app.MapGroup("/api/deadlines")
            .WithTags("Deadlines")
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);

        deadlines.MapGet("/", GetDeadlines);
        deadlines.MapGet("/{period}", GetDeadline);
        deadlines.MapPost("/", CreateOrUpdateDeadline);
        deadlines.MapPut("/{period}", UpdateDeadline);
        deadlines.MapDelete("/{period}", DisableDeadline);

        app.MapGet("/api/reporting-deadlines", GetDeadlines)
            .WithTags("Deadlines")
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);

        // Manager-accessible deadline endpoint
        app.MapGet("/api/deadlines/me", GetMyDeadlines)
            .WithTags("Deadlines")
            .RequireAuthorization(AuthPolicies.BusinessAccess);
    }

    private static async Task<IResult> GetDeadlines(ReportDbContext db)
    {
        var deadlines = await db.ReportingDeadlines.OrderBy(x => x.Period).ToListAsync();
        return Results.Ok(deadlines);
    }

    private static async Task<IResult> GetDeadline(
        string period,
        ReportDbContext db,
        CancellationToken cancellationToken)
    {
        var normalizedPeriod = period.Trim();
        if (normalizedPeriod.Length is 0 or > 40)
        {
            return Results.BadRequest(new { message = "Period must contain between 1 and 40 characters." });
        }

        var deadline = await db.ReportingDeadlines.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Period == normalizedPeriod, cancellationToken);
        return deadline is null ? Results.NotFound() : Results.Ok(deadline);
    }

    private static async Task<IResult> UpdateDeadline(
        string period,
        UpdateDeadlineRequest request,
        ReportDbContext db,
        CancellationToken cancellationToken)
    {
        var normalizedPeriod = period.Trim();
        if (normalizedPeriod.Length is 0 or > 40)
        {
            return Results.BadRequest(new { message = "Period must contain between 1 and 40 characters." });
        }
        if (request.Period is not null
            && !string.Equals(request.Period.Trim(), normalizedPeriod, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { message = "Body period must match the route period. The period cannot be changed." });
        }
        if (request.DueDate is null || request.DueDate == DateOnly.MinValue)
        {
            return Results.BadRequest(new { message = "A valid dueDate is required (yyyy-MM-dd)." });
        }

        var deadline = await db.ReportingDeadlines
            .FirstOrDefaultAsync(x => x.Period == normalizedPeriod, cancellationToken);
        if (deadline is null)
        {
            return Results.NotFound();
        }

        deadline.DueDate = request.DueDate.Value;
        if (request.IsActive.HasValue)
        {
            deadline.IsActive = request.IsActive.Value;
        }
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(deadline);
    }

    private static async Task<IResult> DisableDeadline(
        string period,
        ReportDbContext db,
        CancellationToken cancellationToken)
    {
        var normalizedPeriod = period.Trim();
        if (normalizedPeriod.Length is 0 or > 40)
        {
            return Results.BadRequest(new { message = "Period must contain between 1 and 40 characters." });
        }

        var deadline = await db.ReportingDeadlines
            .FirstOrDefaultAsync(x => x.Period == normalizedPeriod, cancellationToken);
        if (deadline is null)
        {
            return Results.NotFound();
        }

        if (deadline.IsActive)
        {
            deadline.IsActive = false;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> CreateOrUpdateDeadline(DeadlineRequest request, ReportDbContext db)
    {
        var deadline = await db.ReportingDeadlines.FirstOrDefaultAsync(x => x.Period == request.Period);
        if (deadline is null)
        {
            deadline = new ReportingDeadline { Period = request.Period.Trim() };
            db.ReportingDeadlines.Add(deadline);
        }

        deadline.DueDate = request.DueDate;
        deadline.IsActive = request.IsActive;
        await db.SaveChangesAsync();
        return Results.Ok(deadline);
    }

    private static async Task<IResult> GetMyDeadlines(
        ReportDbContext db,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        CancellationToken cancellationToken)
    {
        var access = await clubAccess.GetMyAccessAsync(httpContext.GetBearerToken(), cancellationToken);
        if (!access.Any(x => x.IsManager))
        {
            return Results.Forbid();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = await db.ReportingDeadlines
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DueDate)
            .Select(x => new MyDeadlineResponse(
                x.Id,
                x.Period,
                x.DueDate,
                x.DueDate < today,
                x.DueDate.DayNumber - today.DayNumber))
            .ToListAsync(cancellationToken);
        return Results.Ok(rows);
    }
}
