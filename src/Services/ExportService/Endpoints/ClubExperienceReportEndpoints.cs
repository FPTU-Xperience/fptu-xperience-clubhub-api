using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using ExportService.Data;
using ExportService.Models;
using Microsoft.EntityFrameworkCore;

namespace ExportService.Endpoints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateClubExperienceReportRequest(string SemesterId, Guid? ExpectedPlanVersion, string ExpectedLedgerRevision);

public static class ClubExperienceReportEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static void MapClubExperienceReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/clubs/{clubId:int}/experience-reports", CreateAsync).RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Club experience PDF");
        app.MapGet("/api/club-experience-reports/{id:guid}", GetAsync).RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Club experience PDF");
        app.MapGet("/api/club-experience-reports/{id:guid}/download", DownloadAsync).RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Club experience PDF");
        app.MapGet("/api/clubs/{clubId:int}/experience-reports/{id:guid}/download", async (int clubId, Guid id, ExportDbContext db,
            ClubAccessClient access, HttpContext http, CancellationToken ct) =>
        {
            if (!await db.ClubExperienceReports.AnyAsync(x => x.Id == id && x.ClubId == clubId, ct)) return Results.NotFound();
            return await DownloadAsync(id, db, access, http, ct);
        }).RequireAuthorization(AuthPolicies.BusinessAccess).WithTags("Club experience PDF");
    }
    private static async Task<bool> AllowedAsync(int clubId, ClubAccessClient access, HttpContext http, CancellationToken ct) =>
        (await access.GetMyAccessAsync(http.GetBearerToken(), ct, true)).Any(x => x.ClubId == clubId && x.CanManage);

    private static async Task<IResult> CreateAsync(int clubId, CreateClubExperienceReportRequest input, ExportDbContext db,
        HttpContext http, ClubAccessClient access, IHttpClientFactory clients, CancellationToken ct)
    {
        if (!await AllowedAsync(clubId, access, http, ct)) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(input.SemesterId) || input.SemesterId.Length > 40) return ApiOperations.Error(400, "SEMESTER_REQUIRED", "Provide a semester.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            ClubExperienceStatistics? snapshot;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"api/clubs/{clubId}/experience-statistics?semesterId={Uri.EscapeDataString(input.SemesterId)}");
                if (AuthenticationHeaderValue.TryParse(http.Request.Headers.Authorization, out var auth)) request.Headers.Authorization = auth;
                using var response = await clients.CreateClient("ExperienceAdmin").SendAsync(request, ct);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return Results.NotFound();
                response.EnsureSuccessStatusCode(); snapshot = await response.Content.ReadFromJsonAsync<ClubExperienceStatistics>(ct);
            }
            catch (HttpRequestException) { return ApiOperations.Error(503, "EXPERIENCE_STATISTICS_UNAVAILABLE", "Statistics snapshot is unavailable."); }
            if (snapshot is null || snapshot.ClubId != clubId || snapshot.SchemaVersion != 1) return ApiOperations.Error(503, "INVALID_STATISTICS_SNAPSHOT", "Statistics snapshot is invalid.");
            if (input.ExpectedPlanVersion != snapshot.PlanVersion || input.ExpectedLedgerRevision != snapshot.LedgerRevision)
                return ApiOperations.Error(409, "STALE_EXPERIENCE_SNAPSHOT", "Reload the plan and statistics before requesting a PDF.");
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            var row = new ClubExperienceReport
            {
                ClubId = clubId,
                SemesterId = snapshot.SemesterId,
                CreatedBy = http.User.GetUserId(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                SnapshotJson = json,
                SnapshotHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            };
            db.ClubExperienceReports.Add(row); await db.SaveChangesAsync(ct);
            return Results.Accepted($"/api/club-experience-reports/{row.Id}", Response(row));
        }, ct);
    }
    private static object Response(ClubExperienceReport row)
    {
        var snapshot = JsonSerializer.Deserialize<ClubExperienceStatistics>(row.SnapshotJson, JsonOptions)!;
        return new
        {
            reportId = row.Id,
            row.ClubId,
            row.SemesterId,
            row.Status,
            row.CreatedAtUtc,
            snapshotVersion = 1,
            row.SnapshotHash,
            row.FailureCode,
            asOfPlanVersion = snapshot.PlanVersion,
            snapshot.LedgerRevision,
            asOf = snapshot.GeneratedAt,
            statusUrl = $"/api/club-experience-reports/{row.Id}",
            downloadUrl = row.Status == "Completed" ? $"/api/clubs/{row.ClubId}/experience-reports/{row.Id}/download" : null
        };
    }
    private static async Task<IResult> GetAsync(Guid id, ExportDbContext db, ClubAccessClient access, HttpContext http, CancellationToken ct)
    {
        var row = await db.ClubExperienceReports.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return Results.NotFound();
        if (!await AllowedAsync(row.ClubId, access, http, ct)) return Results.Forbid();
        return Results.Ok(Response(row));
    }
    private static async Task<IResult> DownloadAsync(Guid id, ExportDbContext db, ClubAccessClient access, HttpContext http, CancellationToken ct)
    {
        var row = await db.ClubExperienceReports.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return Results.NotFound();
        if (!await AllowedAsync(row.ClubId, access, http, ct)) return Results.Forbid();
        if (row.Status != "Completed" || row.PdfBytes is null) return ApiOperations.Error(409, "REPORT_NOT_READY", "Wait for the report to complete.");
        http.Response.Headers.CacheControl = "private, no-store";
        return Results.File(row.PdfBytes, "application/pdf", $"club-{row.ClubId}-{row.SemesterId}-experience-{row.Id}.pdf");
    }
}
