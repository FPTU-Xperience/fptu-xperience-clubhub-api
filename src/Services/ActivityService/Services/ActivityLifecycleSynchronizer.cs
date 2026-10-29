using System.Data;
using System.Text.Json;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Services;

public sealed class ActivityLifecycleSynchronizer(ActivityDbContext db)
{
    public async Task<int> SynchronizeAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        // Bound each pass, and consider session dates even if the parent range is stale.
        var ids = await db.Activities.AsNoTracking()
            .Where(a => a.Status == ActivityStatuses.Scheduled
                && (a.PublicationStatus == "LEGACY" || a.PublicationStatus == "PUBLISHED"
                    || a.PublicationStatus == "CLOSED"
                    || db.Set<ActivityExecution>().Any(e => e.ActivityId == a.Id && e.IsPublished))
                && (db.Sessions.Any(s => s.ActivityId == a.Id && s.Status == "SCHEDULED"
                        && s.EndTimeUtc <= now && s.ClosesAt <= now)
                    || a.EndTimeUtc <= now && !db.Sessions.Any(s => s.ActivityId == a.Id
                        && s.Status != "CANCELLED" && (s.EndTimeUtc > now || s.ClosesAt > now))))
            .OrderBy(a => a.Id).Select(a => a.Id).Take(100).ToArrayAsync(ct);
        var changed = 0;
        foreach (var id in ids)
        {
            // Serialize with rescheduling/cancellation, including legacy writes which use
            // UpdatedAt rather than the Version concurrency token. Save audit + outbox atomically.
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var activity = await db.Activities.SingleAsync(a => a.Id == id, ct);
            var sessions = await db.Sessions.Where(s => s.ActivityId == id).ToListAsync(ct);
            if (activity.Status != ActivityStatuses.Scheduled) { db.ChangeTracker.Clear(); continue; }
            // Recheck publication after acquiring the transaction locks.
            var published = activity.PublicationStatus is "LEGACY" or "PUBLISHED" or "CLOSED"
                || await db.Set<ActivityExecution>().AnyAsync(e => e.ActivityId == id && e.IsPublished, ct);
            if (!published) { db.ChangeTracker.Clear(); continue; }
            foreach (var session in sessions.Where(s => s.Status == "SCHEDULED"
                && s.EndTimeUtc <= now && s.ClosesAt <= now))
            {
                session.Status = "CLOSED";
                session.Version = Guid.NewGuid();
                session.UpdatedAtUtc = now;
                Record(activity, session.Id, "SCHEDULED", "CLOSED", session.Version, now);
                changed++;
            }
            if (activity.EndTimeUtc <= now && sessions.All(s => s.Status == "CANCELLED"
                || s.Status == "CLOSED" && s.EndTimeUtc <= now && s.ClosesAt <= now))
            {
                activity.Status = ActivityStatuses.Completed;
                // Publication is an independent visibility decision; retain LEGACY/DRAFT
                // for adapters and only transition the discovery publication lifecycle.
                if (activity.PublicationStatus is "PUBLISHED" or "CLOSED") activity.PublicationStatus = "COMPLETED";
                activity.Version = Guid.NewGuid();
                activity.UpdatedAtUtc = now;
                Record(activity, null, ActivityStatuses.Scheduled, ActivityStatuses.Completed, activity.Version, now);
                changed++;
            }
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
        return changed;
    }

    private void Record(ClubActivity activity, int? sessionId, string before, string after, Guid version, DateTimeOffset now)
    {
        const string reason = "Scheduled time and attendance window have ended.";
        var snapshot = new { previousStatus = before, status = after, actor = "SYSTEM", asOf = now };
        db.SessionAudits.Add(new ActivitySessionAudit
        {
            ActivityId = activity.Id,
            SessionId = sessionId,
            ActorId = 0,
            Action = sessionId.HasValue ? "AUTO_CLOSE" : "AUTO_COMPLETE",
            Reason = reason,
            Version = version,
            CreatedAtUtc = now,
            SnapshotJson = JsonSerializer.Serialize(snapshot)
        });
        db.AddOutboxMessage(new ActivityLifecycleChangedEvent(Guid.NewGuid(), now, activity.Id,
            activity.ClubId, sessionId, before, after, version, reason), ActivityLifecycleChangedEvent.RoutingKey);
    }
}
