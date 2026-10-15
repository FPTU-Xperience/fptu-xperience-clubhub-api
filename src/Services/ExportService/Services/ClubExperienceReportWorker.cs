using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClubReportHub.Shared.Experience;
using ExportService.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ExportService.Services;

public sealed class ClubExperienceReportWorker(IServiceScopeFactory scopes, ILogger<ClubExperienceReportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
                var now = DateTimeOffset.UtcNow;
                var ids = await db.ClubExperienceReports.AsNoTracking().Where(x => x.Status == "Pending" || x.Status == "Processing" && x.LeaseUntil < now)
                    .OrderBy(x => x.CreatedAtUtc).Take(10).Select(x => x.Id).ToListAsync(stoppingToken);
                foreach (var id in ids)
                {
                    var version = Guid.NewGuid();
                    if (await db.ClubExperienceReports.Where(x => x.Id == id && (x.Status == "Pending" || x.Status == "Processing" && x.LeaseUntil < now))
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Processing").SetProperty(x => x.Version, version)
                            .SetProperty(x => x.LeaseUntil, now.AddMinutes(2)), stoppingToken) == 0) continue;
                    var row = await db.ClubExperienceReports.SingleAsync(x => x.Id == id, stoppingToken);
                    try
                    {
                        if (row.SnapshotHash != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.SnapshotJson))))
                            throw new InvalidOperationException("Experience report snapshot integrity failed.");
                        var snapshot = JsonSerializer.Deserialize<ClubExperienceStatistics>(row.SnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                        row.PdfBytes = Generate(snapshot, row.SnapshotHash); row.Status = "Completed";
                    }
                    catch (Exception exception) { logger.LogError(exception, "Experience PDF {ReportId} failed", id); row.Status = "Failed"; row.FailureCode = "PDF_GENERATION_FAILED"; }
                    row.Version = Guid.NewGuid(); row.LeaseUntil = null;
                    try { await db.SaveChangesAsync(stoppingToken); } catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Experience PDF queue processing failed; persisted jobs will retry"); }
            await Task.Delay(2000, stoppingToken);
        }
    }

    public static byte[] Generate(ClubExperienceStatistics snapshot, string hash)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4); page.Margin(32); page.DefaultTextStyle(style => style.FontSize(10));
            page.Header().Text("FPTU Xperience - Club experience report").FontSize(18).SemiBold();
            page.Content().PaddingVertical(14).Column(column =>
            {
                column.Spacing(8); column.Item().Text($"Club: {snapshot.ClubName} (#{snapshot.ClubId})");
                column.Item().Text($"Semester: {snapshot.SemesterId} | Snapshot: {snapshot.GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}");
                column.Item().Text($"Completed activities: {snapshot.Activities.CompletedActivityCount}; completed sessions: {snapshot.Activities.CompletedSessionCount}");
                column.Item().Text($"Verified participants: {snapshot.Activities.UniqueVerifiedParticipantCount}; first participation: {snapshot.FirstTimeParticipantCount}");
                column.Item().Text($"Pending recognition: {snapshot.Activities.PendingRecognitionCount}. Points use the verified net ledger.");
                column.Item().Text("Net points by pillar").SemiBold();
                foreach (var pillar in snapshot.NetPointsByPillar) column.Item().Text($"{pillar.Key}: {pillar.Value:0.####}");
                column.Item().Text("Effective roles").SemiBold();
                foreach (var role in snapshot.EffectiveRoleCounts) column.Item().Text($"{role.Key}: {role.Value}");
                column.Item().Text("Semester targets").SemiBold();
                foreach (var target in snapshot.Progress) column.Item().Text($"{target.Key}: {target.Value.Actual}/{target.Value.Target}; progress {(target.Value.Ratio.HasValue ? target.Value.Ratio.Value.ToString("P2") : "N/A")}");
                column.Item().Text($"Schema: {snapshot.SchemaVersion} | Policy versions: {string.Join(", ", snapshot.PolicyVersions)}").FontSize(8);
                column.Item().Text($"Snapshot SHA-256: {hash}").FontSize(8);
            });
            page.Footer().AlignCenter().Text(text => { text.Span("Page "); text.CurrentPageNumber(); });
        })).GeneratePdf();
    }
}
