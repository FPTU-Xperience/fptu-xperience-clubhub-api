using System.Text.Json;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace ActivityService.Services;

internal static class ClosingReportXpGate
{
    internal const string Held = "AwaitingClosingReport";
    private const string Ineligible = "ClosingReportIneligible";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void HoldNewAttendanceEvents(ActivityDbContext db)
    {
        var pending = db.ChangeTracker.Entries<OutboxMessage>().Where(x => x.State == EntityState.Added
            && x.Entity.Status == OutboxMessageStatus.Pending && x.Entity.EventType == ExperienceRoutingKeys.AttendanceVerified)
            .Select(x => x.Entity).ToArray();
        foreach (var row in pending)
        {
            var message = Read(row);
            row.Status = Held;
            db.Add(new ActivityHeldXp { EventId = row.Id, ActivityId = message.Source.ActivityId });
        }
    }

    internal static async Task ReleaseAsync(ClubActivity activity, DateTimeOffset lockedAt,
        ActivityDbContext db, ClubExperienceAuthorityClient clubs, CancellationToken ct)
    {
        var roles = await db.Set<ActivityFinalRole>().AsNoTracking().Where(x => x.ActivityId == activity.Id)
            .ToDictionaryAsync(x => x.UserId, ct);
        var duties = QueueDuties(activity, lockedAt, roles.Values, db);
        var held = await (from link in db.Set<ActivityHeldXp>()
            join row in db.OutboxMessages on link.EventId equals row.Id
            where link.ActivityId == activity.Id && row.Status == Held
            select row).ToListAsync(ct);
        if (held.Count == 0) return;

        var events = held.Select(row => (Row: row, Message: Read(row))).ToArray();
        var attendanceIds = events.Select(x => x.Message.AttendanceId).Distinct().ToArray();
        var verified = await db.ActivityAttendances.AsNoTracking()
            .Where(x => x.ActivityId == activity.Id && attendanceIds.Contains(x.Id) && x.VerificationStatus == "VERIFIED")
            .Select(x => new { x.Id, x.UserId, x.SessionId }).ToDictionaryAsync(x => x.Id, ct);
        var members = new HashSet<int>();
        if (activity.ClubId > 0)
            foreach (var batch in events.Select(x => x.Message.UserId).Distinct().Chunk(200))
                members.UnionWith(await clubs.MembersAtAsync(activity.ClubId, activity.StartTimeUtc, batch, ct));

        foreach (var (row, message) in events)
        {
            var eligible = verified.TryGetValue(message.AttendanceId, out var attendance)
                && attendance.UserId == message.UserId && attendance.SessionId == message.Source.SessionId
                && message.Source.ClubId == activity.ClubId && message.OccurredAtUtc <= lockedAt
                && roles.TryGetValue(message.UserId, out var role) && role.ConfirmedAtUtc <= lockedAt
                && !duties.Contains(message.UserId);
            row.Status = eligible ? OutboxMessageStatus.Pending : Ineligible;
            if (eligible)
                row.Payload = JsonSerializer.Serialize(message with
                {
                    ClosingReportAuthorized = true,
                    ClosingReportReviewRole = members.Contains(message.UserId) || roles[message.UserId].Role != "PARTICIPANT"
                        ? roles[message.UserId].Role : null
                }, Json);
            row.ConcurrencyToken = Guid.NewGuid();
        }
    }

    private static HashSet<int> QueueDuties(ClubActivity activity, DateTimeOffset lockedAt, IEnumerable<ActivityFinalRole> roles, ActivityDbContext db)
    {
        var queued = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(activity.ScoringSnapshotJson)) return queued;
        var blueprint = JsonSerializer.Deserialize<ExperienceSourceSnapshot>(activity.ScoringSnapshotJson, Json);
        if (blueprint is null) return queued;
        var source = blueprint with
        {
            ActivityId = activity.Id, ClubId = activity.ClubId, ActivityVersion = activity.Version, SessionId = 0,
            StartTimeUtc = activity.StartTimeUtc, EndTimeUtc = activity.EndTimeUtc, Title = activity.Title, Status = "CLOSED"
        };
        foreach (var role in roles.Where(x => x.ConfirmedAtUtc <= lockedAt && x.Role is "STAFF" or "HOST"))
        {
            db.AddOutboxMessage(new ClosingDutyXpEvent(Guid.NewGuid(), lockedAt, role.UserId, role.Role, source),
                ExperienceRoutingKeys.ClosingDutyXp);
            queued.Add(role.UserId);
        }
        return queued;
    }

    private static AttendanceVerifiedEvent Read(OutboxMessage row) =>
        JsonSerializer.Deserialize<AttendanceVerifiedEvent>(row.Payload, Json)
        ?? throw new InvalidOperationException("An attendance XP event has no payload.");
}
