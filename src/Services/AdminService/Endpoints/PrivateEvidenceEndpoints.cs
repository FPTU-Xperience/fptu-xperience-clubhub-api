using AdminService.Data;
using AdminService.Security;
using AdminService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Endpoints;

public static class PrivateEvidenceEndpoints
{
    public static void MapPrivateEvidenceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/contributions/evidence", async (HttpContext http, AdminDbContext db, CancellationToken ct) =>
        {
            if (!http.Request.HasFormContentType || http.Request.ContentLength is > 10_600_000)
                return ApiOperations.Error(400, "INVALID_EVIDENCE", "Upload one PDF, PNG or JPEG file of at most 10 MB.");
            var form = await http.Request.ReadFormAsync(ct);
            if (form.Files.Count != 1) return ApiOperations.Error(400, "INVALID_EVIDENCE", "Upload one evidence file.");
            var file = form.Files[0];
            if (file.Length is <= 0 or > 10_485_760 || file.ContentType is not ("application/pdf" or "image/png" or "image/jpeg"))
                return ApiOperations.Error(400, "INVALID_EVIDENCE", "Unsupported file type or size.");
            using var stream = new MemoryStream(); await file.CopyToAsync(stream, ct);
            var bytes = stream.ToArray();
            var valid = file.ContentType switch
            {
                "application/pdf" => bytes.AsSpan().StartsWith("%PDF-"u8),
                "image/png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                _ => bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255
            };
            if (!valid) return ApiOperations.Error(400, "INVALID_FILE_SIGNATURE", "File contents do not match the declared type.");
            var name = Path.GetFileName(file.FileName).Replace('\r', '_').Replace('\n', '_');
            if (name.Length is 0 or > 200) return ApiOperations.Error(400, "INVALID_FILE_NAME", "Invalid file name.");
            return await ApiOperations.ExecuteAsync(http, db, new
            {
                name,
                file.ContentType,
                sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))
            }, async () =>
            {
                var row = new PrivateContributionEvidence
                {
                    OwnerUserId = http.User.GetUserId(),
                    Name = name,
                    ContentType = file.ContentType,
                    SizeBytes = bytes.Length,
                    Bytes = bytes,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                db.ContributionEvidence.Add(row); await db.SaveChangesAsync(ct);
                return Results.Created($"/api/contributions/evidence/{row.Id}", new { evidenceId = row.Id, attachmentId = row.Id, row.Name, row.ContentType, row.SizeBytes, row.CreatedAtUtc });
            }, ct);
        }).RequireAuthorization(AdminPolicies.AnyActor).DisableAntiforgery().WithTags("Private contribution evidence");
        Func<Guid, AdminDbContext, HttpContext, CancellationToken, Task<IResult>> download = async (id, db, http, ct) =>
        {
            var file = await db.ContributionEvidence.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (file is null) return Results.NotFound();
            var actor = http.User.GetUserId(); var allowed = actor == file.OwnerUserId;
            if (!allowed)
            {
                // Evidence is readable only through a currently assigned source.
                var needle = id.ToString();
                var sources = await db.Contributions.AsNoTracking().Where(x => x.EvidenceRefsJson.Contains(needle)
                    || db.ContributionRevisions.Any(r => r.ContributionId == x.Id && r.EvidenceRefsJson.Contains(needle))
                    || db.ContributionUpgradeRequests.Any(r => r.ContributionId == x.Id && r.EvidenceRefsJson.Contains(needle))).ToListAsync(ct);
                foreach (var source in sources)
                    if (await ExperienceAuthorization.FindAsync(db, actor, source.SourceType, source.ClubId, "contribution.review", ct) is not null) { allowed = true; break; }
                if (!allowed)
                    foreach (var request in await db.ManualAttendanceRequests.AsNoTracking().Where(x => x.EvidenceRefsJson.Contains(needle)).ToListAsync(ct))
                        if (await ExperienceAuthorization.FindAsync(db, actor, "CLUB", request.ClubId, "attendance.verify", ct) is not null) { allowed = true; break; }
            }
            if (!allowed) return Results.Forbid();
            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(file.Bytes, file.ContentType, file.Name);
        };
        app.MapGet("/api/contributions/evidence/{id:guid}", download).RequireAuthorization(AdminPolicies.AnyActor).WithTags("Private contribution evidence");
        app.MapGet("/api/contributions/evidence/{id:guid}/download", download).RequireAuthorization(AdminPolicies.AnyActor).WithTags("Private contribution evidence");
    }
}
