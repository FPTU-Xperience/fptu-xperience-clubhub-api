using System.Text.Json;
using AdminService.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Services;

internal static class DiscoveryTagAffinity
{
    internal static async Task<Dictionary<string, double>> ReadAsync(AdminDbContext db, int userId, DateTime asOf, CancellationToken ct)
    {
        var cutoff = asOf.AddDays(-30);
        var rows = await db.DiscoveryInteractions.AsNoTracking().Where(x => x.UserId == userId
                && x.OccurredAtUtc >= cutoff && x.OccurredAtUtc <= asOf)
            .OrderByDescending(x => x.OccurredAtUtc).ThenBy(x => x.EventId).Take(500).ToArrayAsync(ct);

        return rows.GroupBy(x => (x.EntityKind, x.EntityId, Day: x.OccurredAtUtc.Date))
            .Select(group => group.OrderByDescending(x => x.Action == "click").ThenByDescending(x => x.OccurredAtUtc).First())
            .SelectMany(x => (JsonSerializer.Deserialize<string[]>(x.TagIdsJson) ?? [])
                .Where(DiscoveryTags.IsAffinityTag)
                .Select(tag => (Tag: tag, Weight: x.Action == "click" ? 3 : 1)))
            .GroupBy(x => x.Tag, StringComparer.Ordinal)
            .Select(group => (Tag: group.Key, Weight: group.Sum(x => x.Weight)))
            .OrderByDescending(x => x.Weight).ThenBy(x => x.Tag, StringComparer.Ordinal)
            .Take(100)
            .ToDictionary(x => x.Tag, x => Math.Min(x.Weight / 6.0, 1), StringComparer.Ordinal);
    }
}
