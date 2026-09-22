using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using ReportService.Data;
using ReportService.Models;

namespace ReportService.Jobs;

public sealed class ReportDeadlineJobs(ReportDbContext db, IEventBus eventBus, ILogger<ReportDeadlineJobs> logger)
{
    public async Task PublishDailyReminderAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var reminders = await db.ReportingDeadlines
            .Where(x => x.IsActive && x.DueDate >= today && x.DueDate <= today.AddDays(3))
            .ToListAsync(cancellationToken);

        foreach (var deadline in reminders)
        {
            await PublishReminderForPeriod(deadline.Period, deadline.DueDate, cancellationToken);
        }
    }

    public async Task PublishMissingReportCheckAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var overdue = await db.ReportingDeadlines
            .Where(x => x.IsActive && x.DueDate < today)
            .OrderByDescending(x => x.DueDate)
            .Take(3)
            .ToListAsync(cancellationToken);

        foreach (var deadline in overdue)
        {
            await PublishReminderForPeriod(deadline.Period, deadline.DueDate, cancellationToken);
        }
    }

    private async Task PublishReminderForPeriod(string period, DateOnly dueDate, CancellationToken cancellationToken)
    {
        var submittedClubIds = (await db.Reports
            .Where(x => x.Period == period && x.Status != ReportStatuses.Draft)
            .Select(x => x.ClubId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        // DATA-F07: Calculate truly missing clubs (all known clubs that have not submitted a non-draft report for this period)
        var allKnownClubIds = await db.Reports
            .Select(x => x.ClubId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var missingClubIds = allKnownClubIds
            .Where(clubId => !submittedClubIds.Contains(clubId))
            .ToList();

        await eventBus.PublishAsync(new ReportDeadlineReminderEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            period,
            dueDate,
            missingClubIds.ToArray()), EventRoutingKeys.ReportDeadlineReminder, cancellationToken);

        logger.LogInformation("Published deadline reminder for {Period} due {DueDate} with {MissingCount} clubs that need reminder",
            period, dueDate, missingClubIds.Count);
    }
}
