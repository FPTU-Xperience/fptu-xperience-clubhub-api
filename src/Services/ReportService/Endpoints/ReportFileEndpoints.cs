using System.Security.Claims;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReportService.Attachments;
using ReportService.Clients;
using ReportService.Contracts;
using ReportService.Data;
using ReportService.Extensions;
using ReportService.Models;
using ReportService.Services;

namespace ReportService.Endpoints;

public static class ReportFileEndpoints
{
    public static void MapReportFileEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/upload", UploadReportFile).DisableAntiforgery();
        group.MapGet("/{reportId:int}/uploaded-file", GetUploadedFile);
        group.MapGet("/{reportId:int}/uploaded-file/preview", GetUploadedFilePreview);
        group.MapGet("/{reportId:int}/uploaded-file/download", DownloadUploadedFile);
        group.MapPut("/{reportId:int}/uploaded-file", ReplaceUploadedFile).DisableAntiforgery();
        group.MapDelete("/{reportId:int}/uploaded-file", DeleteUploadedFile);

        group.MapPost("/{id:int}/attachments", AddAttachmentMetadata);
        group.MapPost("/{id:int}/attachments/upload", UploadAttachment)
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery();
        group.MapGet("/{id:int}/attachments/{attachmentId:int}/download", DownloadAttachment);
    }

    private static async Task<IResult> UploadReportFile(
        HttpContext httpContext,
        IConfiguration config,
        ReportDbContext db,
        ClaimsPrincipal user,
        ClubAccessClient clubAccess,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Request.HasFormContentType)
        {
            return Results.BadRequest(new { message = "Request content type must be multipart/form-data." });
        }

        var form = await httpContext.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null)
        {
            return Results.BadRequest(new { message = "Vui lòng chọn tệp báo cáo để tải lên." });
        }

        if (!int.TryParse(form["clubId"], out var clubId) || clubId <= 0)
        {
            return Results.BadRequest(new { message = "ClubId không hợp lệ." });
        }

        var period = form["period"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(period))
        {
            return Results.BadRequest(new { message = "Kỳ báo cáo là bắt buộc." });
        }

        var reportType = form["reportType"].ToString().Trim();
        var note = form["note"].ToString().Trim();

        var validation = ReportExtensions.ValidateUploadedReportFile(file);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new { message = validation.ErrorMessage });
        }

        var tag = ReportExtensions.NormalizeReportTag(reportType, reportType);
        var normReportType = ReportExtensions.NormalizeReportType(reportType, tag);
        var authorAccess = await ReportExtensions.GetAuthorAccessAsync(
            clubId,
            tag,
            normReportType,
            clubAccess,
            httpContext,
            cancellationToken);

        if (authorAccess is null)
        {
            return Results.Forbid();
        }

        if (await db.Reports.AnyAsync(x => x.ClubId == clubId && x.Period == period && x.Tag == tag, cancellationToken))
        {
            return Results.Conflict(new { message = "A report already exists for this club, period, and tag." });
        }

        var deadline = await db.ReportingDeadlines.FirstOrDefaultAsync(x => x.Period == period, cancellationToken);
        var dueDate = deadline?.DueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14));

        (string StoredFileName, string StoragePath, string Checksum, long SizeBytes, string OriginalFileName) savedFile;
        try
        {
            savedFile = await ReportExtensions.SaveUploadedReportFileAsync(file, clubId, config, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }

        var report = new Report
        {
            ClubId = clubId,
            ClubName = authorAccess.ClubName,
            Period = period,
            ReportType = normReportType,
            Tag = tag,
            DueDate = dueDate,
            CreatedByUserId = user.GetUserId(),
            ContentSource = ReportContentSources.UploadedFile,
            Status = ReportStatuses.Draft,
            ExecutiveSummary = string.IsNullOrWhiteSpace(note) ? null : note
        };

        var uploadedFile = new ReportUploadedFile
        {
            OriginalFileName = savedFile.OriginalFileName,
            StoredFileName = savedFile.StoredFileName,
            ContentType = validation.ContentType,
            FileExtension = Path.GetExtension(savedFile.OriginalFileName).ToLowerInvariant(),
            SizeBytes = savedFile.SizeBytes,
            StoragePath = savedFile.StoragePath,
            Checksum = savedFile.Checksum,
            UploadedByUserId = user.GetUserId(),
            UploadedAtUtc = DateTimeOffset.UtcNow,
            IsActive = true,
            Report = report
        };

        await ReportPreviewGenerator.GeneratePreviewAsync(uploadedFile, config, cancellationToken);
        report.UploadedFile = uploadedFile;
        db.Reports.Add(report);
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            AuditHelper.AddAudit(db, report.Id, "Upload", user.GetUserId(), "Report file uploaded as draft.");
            await db.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            DeleteFileIfPresent(uploadedFile.StoragePath);
            DeleteFileIfPresent(uploadedFile.PreviewStoragePath);
            return Results.Conflict(new { message = "A report already exists for this club, period, and tag." });
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            DeleteFileIfPresent(uploadedFile.StoragePath);
            DeleteFileIfPresent(uploadedFile.PreviewStoragePath);
            throw;
        }

        return Results.Created($"/api/reports/{report.Id}", ReportMappers.ToResponse(report));
    }

    private static void DeleteFileIfPresent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Database rollback is authoritative; stale files can be removed by storage maintenance.
        }
    }

    private static async Task<IResult> GetUploadedFile(
        int reportId,
        ReportDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        CancellationToken cancellationToken)
    {
        var report = await db.Reports
            .Include(x => x.UploadedFile)
            .FirstOrDefaultAsync(x => x.Id == reportId, cancellationToken);

        if (report is null)
        {
            return Results.NotFound(new { message = "Không tìm thấy báo cáo." });
        }

        if (!await ReportExtensions.CanReadReportAsync(report, user, clubAccess, httpContext, cancellationToken))
        {
            return Results.Forbid();
        }

        if (report.UploadedFile is null || !report.UploadedFile.IsActive)
        {
            return Results.NotFound(new { message = "Báo cáo này không có file đính kèm hoặc file đã bị xóa." });
        }

        var isDownloadAvailable = File.Exists(report.UploadedFile.StoragePath);
        var isPreviewAvailable = !string.IsNullOrEmpty(report.UploadedFile.PreviewStoragePath) && File.Exists(report.UploadedFile.StoragePath);

        return Results.Ok(new ReportUploadedFileResponse(
            report.UploadedFile.Id,
            report.UploadedFile.OriginalFileName,
            report.UploadedFile.ContentType,
            report.UploadedFile.FileExtension,
            report.UploadedFile.SizeBytes,
            report.UploadedFile.UploadedAtUtc,
            report.UploadedFile.UploadedByUserId,
            isDownloadAvailable,
            report.UploadedFile.PreviewStatus ?? "Available",
            isPreviewAvailable,
            report.UploadedFile.PreviewErrorMessage));
    }

    private static async Task<IResult> GetUploadedFilePreview(
        int reportId,
        ReportDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        IConfiguration config,
        CancellationToken cancellationToken)
    {
        var report = await db.Reports
            .Include(x => x.UploadedFile)
            .FirstOrDefaultAsync(x => x.Id == reportId, cancellationToken);

        if (report is null)
        {
            return Results.NotFound(new { message = "Không tìm thấy báo cáo." });
        }

        if (!await ReportExtensions.CanReadReportAsync(report, user, clubAccess, httpContext, cancellationToken))
        {
            return Results.Forbid();
        }

        if (report.UploadedFile is null || !report.UploadedFile.IsActive || report.UploadedFile.ReportId != report.Id)
        {
            return Results.NotFound(new { message = "Báo cáo này không có file đính kèm." });
        }

        var uploadedFile = report.UploadedFile;

        if (uploadedFile.PreviewStatus is null || uploadedFile.PreviewStatus == "None" || string.IsNullOrEmpty(uploadedFile.PreviewStoragePath) || !File.Exists(uploadedFile.PreviewStoragePath))
        {
            await ReportPreviewGenerator.GeneratePreviewAsync(uploadedFile, config, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (uploadedFile.PreviewStatus == "Pending")
        {
            return Results.Conflict(new { message = "Quá trình tạo bản xem trước đang được xử lý." });
        }

        if (uploadedFile.PreviewStatus == "Failed")
        {
            return Results.BadRequest(new { message = uploadedFile.PreviewErrorMessage ?? "Không thể tạo bản xem trước cho file này." });
        }

        if (uploadedFile.PreviewStatus == "Unsupported" || string.IsNullOrEmpty(uploadedFile.PreviewStoragePath) || !File.Exists(uploadedFile.PreviewStoragePath))
        {
            return Results.BadRequest(new { message = "Định dạng file không hỗ trợ xem trước trực tiếp." });
        }

        var previewPath = Path.GetFullPath(uploadedFile.PreviewStoragePath);
        var previewsDir = Path.GetFullPath(config["Uploads:PreviewStoragePath"]
            ?? Path.Combine(AppContext.BaseDirectory, "report-previews"));
        var uploadsDir = Path.GetFullPath(config["Uploads:StoragePath"]
            ?? Path.Combine(AppContext.BaseDirectory, "report-uploads"));

        if (!previewPath.StartsWith(previewsDir, StringComparison.OrdinalIgnoreCase) &&
            !previewPath.StartsWith(uploadsDir, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = "Đường dẫn file xem trước không hợp lệ." });
        }

        var contentType = uploadedFile.PreviewContentType ?? "application/pdf";
        var rawFileName = Path.GetFileName(previewPath);
        var fileName = ContentDispositionSanitizer.SanitizeFileName(rawFileName, "preview.pdf");

        httpContext.Response.Headers.Append("Content-Disposition", $"inline; filename=\"{fileName}\"");
        return Results.File(previewPath, contentType, enableRangeProcessing: true);
    }

    private static async Task<IResult> DownloadUploadedFile(
        int reportId,
        ReportDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        IConfiguration config,
        CancellationToken cancellationToken)
    {
        var report = await db.Reports
            .Include(x => x.UploadedFile)
            .FirstOrDefaultAsync(x => x.Id == reportId, cancellationToken);

        if (report is null)
        {
            return Results.NotFound(new { message = "Không tìm thấy báo cáo." });
        }

        if (!await ReportExtensions.CanReadReportAsync(report, user, clubAccess, httpContext, cancellationToken))
        {
            return Results.Forbid();
        }

        if (report.UploadedFile is null || !report.UploadedFile.IsActive)
        {
            return Results.NotFound(new { message = "Báo cáo này không có file đính kèm." });
        }

        var storageDir = Path.GetFullPath(config["Uploads:StoragePath"] ?? "report-uploads");
        var normalizedReportPath = Path.GetFullPath(report.UploadedFile.StoragePath);
        if (!ReportAttachmentPolicy.IsPathUnderRoot(normalizedReportPath, storageDir))
        {
            return Results.BadRequest(new { message = "Invalid report file path." });
        }

        if (!File.Exists(normalizedReportPath))
        {
            return Results.NotFound(new { message = "Tệp tin vật lý không còn tồn tại trên máy chủ." });
        }

        var safeFileName = ContentDispositionSanitizer.SanitizeFileName(
            report.UploadedFile.OriginalFileName, "report_document");

        return Results.File(
            normalizedReportPath,
            report.UploadedFile.ContentType,
            safeFileName,
            enableRangeProcessing: true);
    }

    private static async Task<IResult> ReplaceUploadedFile(
        int reportId,
        HttpContext httpContext,
        IConfiguration config,
        ReportDbContext db,
        ClaimsPrincipal user,
        ClubAccessClient clubAccess,
        CancellationToken cancellationToken)
    {
        var report = await db.Reports
            .Include(x => x.UploadedFile)
            .Include(x => x.Details)
            .Include(x => x.Attachments)
            .Include(x => x.Feedback)
            .FirstOrDefaultAsync(x => x.Id == reportId, cancellationToken);

        if (report is null)
        {
            return Results.NotFound(new { message = "Không tìm thấy báo cáo." });
        }

        if (!await ReportExtensions.CanAuthorReportsAsync(report.ClubId, report.Tag, report.ReportType, clubAccess, httpContext, cancellationToken))
        {
            return Results.Forbid();
        }

        if (report.Status is not (ReportStatuses.Draft or ReportStatuses.Rejected))
        {
            return Results.BadRequest(new { message = "Chỉ có thể thay đổi tệp báo cáo khi ở trạng thái Nháp hoặc Thất bại/Yêu cầu sửa." });
        }

        if (!httpContext.Request.HasFormContentType)
        {
            return Results.BadRequest(new { message = "Request content type must be multipart/form-data." });
        }

        var form = await httpContext.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null)
        {
            return Results.BadRequest(new { message = "Vui lòng chọn tệp báo cáo mới." });
        }

        var validation = ReportExtensions.ValidateUploadedReportFile(file);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new { message = validation.ErrorMessage });
        }

        (string StoredFileName, string StoragePath, string Checksum, long SizeBytes, string OriginalFileName) savedFile;
        try
        {
            savedFile = await ReportExtensions.SaveUploadedReportFileAsync(file, report.ClubId, config, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }

        if (report.UploadedFile is not null)
        {
            report.UploadedFile.IsActive = false;
        }

        var newUploadedFile = new ReportUploadedFile
        {
            ReportId = report.Id,
            OriginalFileName = savedFile.OriginalFileName,
            StoredFileName = savedFile.StoredFileName,
            ContentType = validation.ContentType,
            FileExtension = Path.GetExtension(savedFile.OriginalFileName).ToLowerInvariant(),
            SizeBytes = savedFile.SizeBytes,
            StoragePath = savedFile.StoragePath,
            Checksum = savedFile.Checksum,
            UploadedByUserId = user.GetUserId(),
            UploadedAtUtc = DateTimeOffset.UtcNow,
            IsActive = true
        };

        report.UploadedFile = newUploadedFile;
        report.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AuditHelper.AddAudit(db, report.Id, "ReplaceUploadedFile", user.GetUserId(), "Uploaded report file replaced.");
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(ReportMappers.ToResponse(report));
    }

    private static async Task<IResult> DeleteUploadedFile(
        int reportId,
        ReportDbContext db,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        CancellationToken cancellationToken)
    {
        var report = await db.Reports
            .Include(x => x.UploadedFile)
            .Include(x => x.Details)
            .Include(x => x.Attachments)
            .Include(x => x.Feedback)
            .FirstOrDefaultAsync(x => x.Id == reportId, cancellationToken);

        if (report is null)
        {
            return Results.NotFound(new { message = "Không tìm thấy báo cáo." });
        }

        if (!await ReportExtensions.CanAuthorReportsAsync(report.ClubId, report.Tag, report.ReportType, clubAccess, httpContext, cancellationToken))
        {
            return Results.Forbid();
        }

        if (report.Status is not (ReportStatuses.Draft or ReportStatuses.Rejected))
        {
            return Results.BadRequest(new { message = "Chỉ có thể xóa tệp báo cáo khi ở trạng thái Nháp hoặc Thất bại." });
        }

        if (report.UploadedFile is not null)
        {
            report.UploadedFile.IsActive = false;
            report.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AuditHelper.AddAudit(db, report.Id, "DeleteUploadedFile", user.GetUserId(), "Uploaded report file deleted.");
            await db.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(ReportMappers.ToResponse(report));
    }

    private static async Task<IResult> AddAttachmentMetadata(
        int id,
        AddAttachmentRequest request,
        ReportDbContext db,
        IOptions<ReportAttachmentOptions> attachmentOptions,
        IWebHostEnvironment environment,
        ClaimsPrincipal user,
        ClubAccessClient clubAccess,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var report = await db.Reports.Include(x => x.Details).Include(x => x.Attachments).Include(x => x.Feedback).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (report is null)
        {
            return Results.NotFound();
        }

        if (report.CreatedByUserId != user.GetUserId()
            || !await ReportExtensions.CanAuthorReportsAsync(
                report.ClubId,
                report.Tag,
                report.ReportType,
                clubAccess,
                httpContext,
                cancellationToken))
        {
            return Results.Forbid();
        }

        if (report.Status is not (ReportStatuses.Draft or ReportStatuses.Rejected))
        {
            return Results.BadRequest(new { message = "Attachments can only be changed on draft or rejected reports." });
        }

        var validation = ReportAttachmentPolicy.Validate(request.FileName, request.ContentType, request.SizeBytes, attachmentOptions.Value);
        if (!validation.Succeeded)
        {
            return Results.BadRequest(new { message = validation.ErrorMessage });
        }

        if (request.ReportDetailId.HasValue && report.Details.All(x => x.Id != request.ReportDetailId.Value))
        {
            return Results.BadRequest(new { message = "Report detail does not belong to this report." });
        }

        var safeName = ReportAttachmentPolicy.GetSafeFileName(request.FileName);
        var storageRoot = ReportAttachmentPolicy.ResolveStorageRoot(attachmentOptions.Value.StoragePath, environment.ContentRootPath);
        var reportFolder = Path.Combine(storageRoot, report.Id.ToString());

        var normalizedFolder = Path.GetFullPath(reportFolder);
        if (!ReportAttachmentPolicy.IsPathUnderRoot(normalizedFolder, storageRoot))
        {
            return Results.BadRequest(new { message = "Invalid storage path." });
        }

        Directory.CreateDirectory(reportFolder);

        var storedFileName = ReportAttachmentPolicy.CreateStoredFileName(safeName);
        var safeFilePath = Path.Combine(reportFolder, storedFileName);

        var normalizedFilePath = Path.GetFullPath(safeFilePath);
        if (!ReportAttachmentPolicy.IsPathUnderRoot(normalizedFilePath, storageRoot))
        {
            return Results.BadRequest(new { message = "Invalid file path." });
        }

        report.Attachments.Add(new ReportAttachment
        {
            ReportDetailId = request.ReportDetailId,
            FileName = safeName,
            ContentType = request.ContentType,
            SizeBytes = request.SizeBytes,
            StoragePath = normalizedFilePath
        });
        report.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AuditHelper.AddAudit(db, report.Id, "Attachment", user.GetUserId(), $"Attachment metadata added: {safeName}");
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ReportMappers.ToResponse(report));
    }

    private static async Task<IResult> UploadAttachment(
        int id,
        IFormFile? file,
        [FromForm] int? reportDetailId,
        ReportDbContext db,
        IOptions<ReportAttachmentOptions> attachmentOptions,
        IWebHostEnvironment environment,
        ClaimsPrincipal user,
        ClubAccessClient clubAccess,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var report = await db.Reports
            .Include(x => x.Details)
            .Include(x => x.Attachments)
            .Include(x => x.Feedback)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (report is null)
        {
            return Results.NotFound();
        }

        if (report.CreatedByUserId != user.GetUserId()
            || !await ReportExtensions.CanAuthorReportsAsync(
                report.ClubId,
                report.Tag,
                report.ReportType,
                clubAccess,
                httpContext,
                cancellationToken))
        {
            return Results.Forbid();
        }

        if (report.Status is not (ReportStatuses.Draft or ReportStatuses.Rejected))
        {
            return Results.BadRequest(new { message = "Attachments can only be changed on draft or rejected reports." });
        }

        if (reportDetailId.HasValue && report.Details.All(x => x.Id != reportDetailId.Value))
        {
            return Results.BadRequest(new { message = "Report detail does not belong to this report." });
        }

        if (file is null)
        {
            return Results.BadRequest(new { message = "Evidence file is required." });
        }

        var validation = ReportAttachmentPolicy.Validate(file.FileName, file.ContentType, file.Length, attachmentOptions.Value);
        if (!validation.Succeeded)
        {
            return Results.BadRequest(new { message = validation.ErrorMessage });
        }

        var safeName = ReportAttachmentPolicy.GetSafeFileName(file.FileName);
        var storageRoot = ReportAttachmentPolicy.ResolveStorageRoot(attachmentOptions.Value.StoragePath, environment.ContentRootPath);
        var reportFolder = Path.Combine(storageRoot, report.Id.ToString());

        var normalizedFolder = Path.GetFullPath(reportFolder);
        if (!normalizedFolder.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = "Invalid storage path." });
        }

        Directory.CreateDirectory(reportFolder);

        var storedFileName = ReportAttachmentPolicy.CreateStoredFileName(safeName);
        var filePath = Path.Combine(reportFolder, storedFileName);

        var normalizedFilePath = Path.GetFullPath(filePath);
        if (!normalizedFilePath.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = "Invalid file path." });
        }

        await using (var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        report.Attachments.Add(new ReportAttachment
        {
            ReportDetailId = reportDetailId,
            FileName = safeName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            StoragePath = filePath
        });
        report.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AuditHelper.AddAudit(db, report.Id, "AttachmentUpload", user.GetUserId(), $"Evidence uploaded: {safeName}");
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ReportMappers.ToResponse(report));
    }

    private static async Task<IResult> DownloadAttachment(
        int id,
        int attachmentId,
        ReportDbContext db,
        IOptions<ReportAttachmentOptions> attachmentOptions,
        IWebHostEnvironment environment,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ClubAccessClient clubAccess,
        CancellationToken cancellationToken)
    {
        var report = await db.Reports
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (report is null)
        {
            return Results.NotFound();
        }

        if (!await ReportExtensions.CanReadReportAsync(report, user, clubAccess, httpContext, cancellationToken))
        {
            return Results.Forbid();
        }

        var attachment = await db.ReportAttachments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ReportId == id && x.Id == attachmentId, cancellationToken);
        if (attachment is null || string.IsNullOrWhiteSpace(attachment.StoragePath))
        {
            return Results.NotFound(new { message = "Attachment file is not available." });
        }

        var storageRoot = ReportAttachmentPolicy.ResolveStorageRoot(attachmentOptions.Value.StoragePath, environment.ContentRootPath);
        var normalizedAttachmentPath = Path.GetFullPath(attachment.StoragePath);
        if (!ReportAttachmentPolicy.IsPathUnderRoot(normalizedAttachmentPath, storageRoot))
        {
            return Results.BadRequest(new { message = "Invalid attachment path." });
        }

        if (!File.Exists(normalizedAttachmentPath))
        {
            return Results.NotFound(new { message = "Attachment file is not available." });
        }

        var safeAttachmentName = ContentDispositionSanitizer.SanitizeFileName(
            attachment.FileName, "attachment");

        return Results.File(normalizedAttachmentPath, attachment.ContentType, safeAttachmentName);
    }
}
