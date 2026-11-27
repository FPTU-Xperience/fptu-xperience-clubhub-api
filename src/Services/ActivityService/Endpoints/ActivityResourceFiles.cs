using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityActionHelpers;

namespace ActivityService.Endpoints;

public static partial class ActivityResourceEndpoints
{
    private const long MaxFileBytes = 100 * 1024 * 1024;
    private static async Task<IResult> Upload(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var scope = await Access(activityId, db, http, ct); Management(scope);
        Require(http.Request.HasFormContentType, 415, "MULTIPART_REQUIRED", "Upload one file with its kind, title and visibility.");
        Require(http.Request.ContentLength is null || http.Request.ContentLength <= MaxFileBytes + 65536, 413, "FILE_TOO_LARGE", "File upload exceeds 100 MB.");
        var limit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = MaxFileBytes + 65536;
        var form = await http.Request.ReadFormAsync(ct);
        Require(form.Files.Count == 1, 400, "ONE_FILE_REQUIRED", "Upload one resource at a time.");
        var file = form.Files[0]; var kind = form["kind"].ToString();
        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        Require(name.Length is > 0 and <= 200 && !name.Any(char.IsControl), 400, "INVALID_FILE_NAME", "Use a file name of 1–200 characters.");
        var mime = FileType(name, kind, file.Length);
        Require(file.ContentType is "" or "application/octet-stream" || file.ContentType == mime, 400, "FILE_TYPE_MISMATCH", "File extension and content type must agree.");
        using var memory = new MemoryStream(); await file.CopyToAsync(memory, ct);
        var bytes = memory.ToArray(); ValidateFile(bytes, mime);
        var title = Title(form["title"].ToString()); var visibility = Visibility(form["visibility"].ToString());
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        return await ApiOperations.ExecuteAsync(http, db, new { kind, name, mime, title, visibility, hash }, async () =>
        {
            var current = await Access(activityId, db, http, ct); Management(current); await Writable(current, db, ct);
            var rows = db.Set<ActivityResource>().Where(x => x.ActivityId == activityId && x.DetachedAtUtc == null);
            Require(await rows.CountAsync(ct) < 100 && (await rows.SumAsync(x => x.SizeBytes ?? 0, ct)) + bytes.Length <= 500L * 1024 * 1024,
                409, "RESOURCE_QUOTA_EXCEEDED", "An activity supports 100 resources and 500 MB of attached files.");
            var row = new ActivityResource
            {
                ActivityId = activityId, Kind = kind, Title = title, Visibility = visibility,
                Provider = "PRIVATE_FILE", FileName = name, MimeType = mime, SizeBytes = bytes.Length, Sha256 = hash,
                Position = Math.Min(9999, (await rows.MaxAsync(x => (int?)x.Position, ct) ?? -1) + 1),
                UploadedByUserId = http.User.GetUserId(), CreatedAtUtc = Now(http), UpdatedAtUtc = Now(http)
            };
            row.Url = $"/api/activities/{activityId}/resources/{row.Id}/download";
            db.Add(row); db.Add(new ActivityResourceFile { ResourceId = row.Id, Bytes = bytes });
            Audit(db, row, "UPLOAD", null, null, http);
            return Results.Created(row.Url, Response(row, true));
        }, ct);
    }

    private static async Task<IResult> Download(int activityId, Guid resourceId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var scope = await Access(activityId, db, http, ct);
        var row = await Find(activityId, resourceId, db, ct);
        Require(row.DetachedAtUtc is null && (scope.CanReadClub || row.Visibility == "PUBLIC"), 403, "RESOURCE_ACCESS_DENIED", "Current resource access is required.");
        var file = await db.Set<ActivityResourceFile>().AsNoTracking().SingleOrDefaultAsync(x => x.ResourceId == resourceId, ct);
        Require(file is not null, 404, "RESOURCE_FILE_NOT_FOUND", "The file is not available.");
        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        return Results.File(file!.Bytes, row.MimeType ?? "application/octet-stream", row.FileName, enableRangeProcessing: true);
    }

    private static string FileType(string name, string kind, long size)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var type = extension switch
        {
            ".pdf" => "application/pdf", ".doc" => "application/msword", ".xls" => "application/vnd.ms-excel",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp",
            ".mp4" => "video/mp4", ".webm" => "video/webm", _ => ""
        };
        var document = extension is ".pdf" or ".doc" or ".xls" or ".docx" or ".xlsx";
        Require(type.Length > 0 && (document ? kind == "DOCUMENT" : kind == "MEDIA"), 400, "INVALID_RESOURCE_KIND", "Choose a supported document, image or video.");
        var max = document ? 20L * 1024 * 1024 : type.StartsWith("image/") ? 5L * 1024 * 1024 : MaxFileBytes;
        Require(size > 0 && size <= max, 413, "FILE_TOO_LARGE", "Documents support 20 MB, images 5 MB and videos 100 MB.");
        return type;
    }

    private static void ValidateFile(byte[] bytes, string mime)
    {
        var data = bytes.AsSpan();
        var valid = mime switch
        {
            "application/pdf" => data.StartsWith("%PDF-"u8),
            "image/png" => data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => data.StartsWith(new byte[] { 255, 216, 255 }),
            "image/webp" => bytes.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8),
            "video/mp4" => bytes.Length >= 12 && data.Slice(4, 4).SequenceEqual("ftyp"u8),
            "video/webm" => data.StartsWith(new byte[] { 26, 69, 223, 163 }),
            "application/msword" or "application/vnd.ms-excel" => data.StartsWith(new byte[] { 208, 207, 17, 224, 161, 177, 26, 225 }),
            _ => data.StartsWith("PK\u0003\u0004"u8)
        };
        Require(valid, 400, "INVALID_FILE_SIGNATURE", "File contents must match the declared type.");
        if (mime.Contains("openxmlformats"))
        {
            try
            {
                using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                Require(zip.Entries.Count <= 2000 && zip.Entries.Sum(x => x.Length) <= 100L * 1024 * 1024
                    && zip.GetEntry("[Content_Types].xml") is not null
                    && zip.GetEntry(mime.EndsWith("document") ? "word/document.xml" : "xl/workbook.xml") is not null
                    && !zip.Entries.Any(x => x.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase) || x.FullName.Contains("/embeddings/", StringComparison.OrdinalIgnoreCase)),
                    400, "UNSAFE_OFFICE_FILE", "Use a valid document without macros or embedded files.");
            }
            catch (InvalidDataException) { Require(false, 400, "INVALID_OFFICE_FILE", "The document archive is invalid."); }
        }
        if (mime == "application/pdf")
        {
            var text = Encoding.Latin1.GetString(bytes);
            Require(!new[] { "/JavaScript", "/JS", "/Launch", "/EmbeddedFile" }.Any(text.Contains),
                400, "ACTIVE_PDF_DENIED", "Use a PDF without active scripts or embedded files.");
        }
    }
}
