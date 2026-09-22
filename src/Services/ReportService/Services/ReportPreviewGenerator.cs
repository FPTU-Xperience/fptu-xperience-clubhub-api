using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Configuration;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ReportService.Models;

namespace ReportService.Services;

public static class ReportPreviewGenerator
{
    static ReportPreviewGenerator()
    {
        try
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }
        catch
        {
            // Ignore licensing errors if already initialized
        }
    }

    public static async Task GeneratePreviewAsync(
        ReportUploadedFile uploadedFile,
        IConfiguration config,
        CancellationToken cancellationToken = default)
    {
        if (uploadedFile is null || string.IsNullOrWhiteSpace(uploadedFile.StoragePath))
            return;

        var previewsDir = Path.GetFullPath(config["Uploads:PreviewStoragePath"]
            ?? Path.Combine(AppContext.BaseDirectory, "report-previews"));
        Directory.CreateDirectory(previewsDir);

        var ext = (uploadedFile.FileExtension ?? Path.GetExtension(uploadedFile.OriginalFileName)).ToLowerInvariant();

        if (ext == ".pdf")
        {
            var previewFileName = $"preview-{uploadedFile.StoredFileName}";
            var previewPath = Path.Combine(previewsDir, previewFileName);
            if (File.Exists(uploadedFile.StoragePath))
            {
                if (!string.Equals(uploadedFile.StoragePath, previewPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(uploadedFile.StoragePath, previewPath, overwrite: true);
                }
                uploadedFile.PreviewFileName = previewFileName;
                uploadedFile.PreviewStoragePath = previewPath;
                uploadedFile.PreviewContentType = "application/pdf";
                uploadedFile.PreviewStatus = "Available";
                uploadedFile.PreviewErrorMessage = null;
                uploadedFile.PreviewGeneratedAtUtc = DateTimeOffset.UtcNow;
            }
            else
            {
                uploadedFile.PreviewStatus = "Failed";
                uploadedFile.PreviewErrorMessage = "Không tìm thấy tệp PDF gốc trên máy chủ.";
            }
            return;
        }

        if (ext == ".docx")
        {
            string? previewPath = null;
            try
            {
                if (!File.Exists(uploadedFile.StoragePath))
                {
                    uploadedFile.PreviewStatus = "Failed";
                    uploadedFile.PreviewErrorMessage = "Không tìm thấy tệp DOCX gốc trên máy chủ.";
                    return;
                }

                var baseName = Path.GetFileNameWithoutExtension(uploadedFile.StoredFileName);
                var previewFileName = $"preview-{baseName}.pdf";
                previewPath = Path.Combine(previewsDir, previewFileName);

                const int MaxPreviewElements = 200;
                var elements = new List<(string Type, string Text)>();

                // SEC-F14: Stream directly from FileStream instead of buffering entire byte array into memory
                await using (var fileStream = new FileStream(uploadedFile.StoragePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
                using (var docx = WordprocessingDocument.Open(fileStream, false))
                {
                    var body = docx.MainDocumentPart?.Document?.Body;

                    if (body != null)
                    {
                        foreach (var element in body.ChildElements)
                        {
                            if (elements.Count >= MaxPreviewElements)
                            {
                                elements.Add(("Paragraph", "[Bản xem trước bị giới hạn ở 200 mục đầu tiên để tối ưu hóa bộ nhớ và hiệu năng...]"));
                                break;
                            }

                            if (element is Paragraph p)
                            {
                                var text = p.InnerText?.Trim();
                                if (!string.IsNullOrEmpty(text))
                                {
                                    var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                                    var isHeading = styleId != null && styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase);
                                    elements.Add((isHeading ? "Heading" : "Paragraph", text));
                                }
                            }
                            else if (element is Table t)
                            {
                                var rows = t.Elements<TableRow>().Select(r =>
                                    string.Join(" | ", r.Elements<TableCell>().Select(c => c.InnerText?.Trim() ?? ""))
                                ).Where(rText => !string.IsNullOrWhiteSpace(rText));

                                foreach (var rText in rows)
                                {
                                    if (elements.Count >= MaxPreviewElements)
                                    {
                                        elements.Add(("Paragraph", "[Bản xem trước bị giới hạn ở 200 mục đầu tiên để tối ưu hóa bộ nhớ và hiệu năng...]"));
                                        break;
                                    }
                                    elements.Add(("TableRow", rText));
                                }
                            }
                        }
                    }
                }

                if (elements.Count == 0)
                {
                    elements.Add(("Paragraph", $"[Document: {uploadedFile.OriginalFileName}]"));
                }

                // Generate PDF using QuestPDF
                QuestPDF.Fluent.Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Margin(36);
                        page.Size(PageSizes.A4);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Grey.Darken3));

                        page.Header().Text(uploadedFile.OriginalFileName)
                            .SemiBold().FontSize(12).FontColor(Colors.Blue.Darken2);

                        page.Content().PaddingVertical(10).Column(col =>
                        {
                            col.Spacing(8);
                            foreach (var item in elements)
                            {
                                if (item.Type == "Heading")
                                {
                                    col.Item().Text(item.Text).Bold().FontSize(14).FontColor(Colors.Blue.Darken3);
                                }
                                else if (item.Type == "TableRow")
                                {
                                    col.Item().Background(Colors.Grey.Lighten4).Padding(4).Text(item.Text).FontSize(10);
                                }
                                else
                                {
                                    col.Item().Text(item.Text).FontSize(11);
                                }
                            }
                        });

                        page.Footer().AlignCenter().Text(x =>
                        {
                            x.Span("Page ");
                            x.CurrentPageNumber();
                        });
                    });
                }).GeneratePdf(previewPath);

                uploadedFile.PreviewFileName = previewFileName;
                uploadedFile.PreviewStoragePath = previewPath;
                uploadedFile.PreviewContentType = "application/pdf";
                uploadedFile.PreviewStatus = "Available";
                uploadedFile.PreviewErrorMessage = null;
                uploadedFile.PreviewGeneratedAtUtc = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(previewPath) && File.Exists(previewPath))
                {
                    try
                    {
                        File.Delete(previewPath);
                    }
                    catch
                    {
                        // Ignore deletion failure during rollback
                    }
                }
                uploadedFile.PreviewStatus = "Failed";
                uploadedFile.PreviewErrorMessage = ex.Message;
            }
            return;
        }

        uploadedFile.PreviewStatus = "Unsupported";
        uploadedFile.PreviewErrorMessage = "Không hỗ trợ xem trước định dạng tệp này.";
    }
}
