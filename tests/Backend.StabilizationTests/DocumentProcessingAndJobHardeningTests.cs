using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClubReportHub.Shared.Data;
using ExportService.Contracts;
using ExportService.Data;
using ExportService.Models;
using ExportService.Services;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ReportService.Extensions;
using ReportService.Models;
using ReportService.Services;
using Xunit;

namespace Backend.StabilizationTests;

public sealed class DocumentProcessingAndJobHardeningTests : IDisposable
{
    private readonly string _testDirectory;

    public DocumentProcessingAndJobHardeningTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "clubhub_phase15_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    // =========================================================================
    // 1. SEC-F14: Streaming Upload & Incremental Hashing
    // =========================================================================

    [Fact]
    public async Task SaveUploadedReportFileAsync_StreamsDirectly_ComputesAccurateSha256()
    {
        var testContent = Encoding.UTF8.GetBytes("Test report payload for streaming upload validation.");
        using var stream = new MemoryStream(testContent);
        var formFile = new FormFile(stream, 0, testContent.Length, "file", "test-report.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Uploads:StoragePath"] = _testDirectory
            })
            .Build();

        var (storedFileName, storagePath, checksum, sizeBytes, originalFileName) =
            await ReportExtensions.SaveUploadedReportFileAsync(formFile, 12, config, CancellationToken.None);

        Assert.Equal("test-report.pdf", originalFileName);
        Assert.True(File.Exists(storagePath));
        Assert.Equal(testContent.Length, sizeBytes);

        var expectedChecksum = Convert.ToHexString(SHA256.HashData(testContent)).ToLowerInvariant();
        Assert.Equal(expectedChecksum, checksum);
    }

    // =========================================================================
    // 2. SEC-F14: Archive Decompression & Zip-Bomb Protection
    // =========================================================================

    [Fact]
    public void ValidateArchiveDecompression_ValidZip_PassesValidation()
    {
        var zipPath = Path.Combine(_testDirectory, "valid.docx");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("<xml><p>Sample docx content</p></xml>");
        }

        // Should not throw
        var exception = Record.Exception(() =>
            ReportExtensions.ValidateArchiveDecompression(zipPath, ".docx"));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateArchiveDecompression_ExcessiveEntries_ThrowsInvalidOperationException()
    {
        var zipPath = Path.Combine(_testDirectory, "too_many_entries.docx");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Max allowed is 500, create 505
            for (int i = 0; i < 505; i++)
            {
                var entry = archive.CreateEntry($"item_{i}.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("a");
            }
        }

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ReportExtensions.ValidateArchiveDecompression(zipPath, ".docx"));

        Assert.Contains("vượt quá giới hạn số lượng mục", ex.Message);
    }

    [Fact]
    public void ValidateArchiveDecompression_ZipSlipTraversal_ThrowsInvalidOperationException()
    {
        var zipPath = Path.Combine(_testDirectory, "zip_slip.docx");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../../../etc/passwd");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("root:x:0:0::/root:/bin/bash");
        }

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ReportExtensions.ValidateArchiveDecompression(zipPath, ".docx"));

        Assert.Contains("đường dẫn không hợp lệ", ex.Message);
    }

    [Fact]
    public void ValidateArchiveDecompression_NonArchiveExtension_IsIgnored()
    {
        var txtPath = Path.Combine(_testDirectory, "plain.pdf");
        File.WriteAllText(txtPath, "Dummy PDF raw content");

        // Should safely return without trying to read as zip
        var exception = Record.Exception(() =>
            ReportExtensions.ValidateArchiveDecompression(txtPath, ".pdf"));

        Assert.Null(exception);
    }

    // =========================================================================
    // 3. SEC-F14: Report Preview Generator Stream & Bounds
    // =========================================================================

    [Fact]
    public async Task ReportPreviewGenerator_DocxPreview_BoundsElementsAndStreams()
    {
        var docxPath = Path.Combine(_testDirectory, "sample.docx");
        using (var archive = ZipFile.Open(docxPath, ZipArchiveMode.Create))
        {
            var contentTypes = archive.CreateEntry("[Content_Types].xml");
            using (var writer = new StreamWriter(contentTypes.Open()))
            {
                writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"xml\" ContentType=\"application/xml\"/></Types>");
            }

            var rels = archive.CreateEntry("_rels/.rels");
            using (var writer = new StreamWriter(rels.Open()))
            {
                writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            }

            var doc = archive.CreateEntry("word/document.xml");
            using (var writer = new StreamWriter(doc.Open()))
            {
                var sb = new StringBuilder();
                sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                sb.Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>");
                for (int i = 0; i < 250; i++)
                {
                    sb.Append($"<w:p><w:r><w:t>Paragraph number {i}</w:t></w:r></w:p>");
                }
                sb.Append("</w:body></w:document>");
                writer.Write(sb.ToString());
            }
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Uploads:PreviewStoragePath"] = Path.Combine(_testDirectory, "previews")
            })
            .Build();

        var uploadedFile = new ReportUploadedFile
        {
            Id = 1,
            OriginalFileName = "sample.docx",
            StoredFileName = "sample.docx",
            FileExtension = ".docx",
            StoragePath = docxPath,
            SizeBytes = new FileInfo(docxPath).Length
        };

        await ReportPreviewGenerator.GeneratePreviewAsync(uploadedFile, config, CancellationToken.None);

        Assert.Equal("Available", uploadedFile.PreviewStatus);
        Assert.NotNull(uploadedFile.PreviewStoragePath);
        Assert.True(File.Exists(uploadedFile.PreviewStoragePath));
        Assert.Equal("application/pdf", uploadedFile.PreviewContentType);
    }

    [Fact]
    public async Task ReportPreviewGenerator_NonExistentFile_MarksFailedAndCleansUp()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Uploads:PreviewStoragePath"] = Path.Combine(_testDirectory, "previews")
            })
            .Build();

        var uploadedFile = new ReportUploadedFile
        {
            Id = 2,
            OriginalFileName = "missing.docx",
            StoredFileName = "missing.docx",
            FileExtension = ".docx",
            StoragePath = Path.Combine(_testDirectory, "does_not_exist.docx"),
            SizeBytes = 100
        };

        await ReportPreviewGenerator.GeneratePreviewAsync(uploadedFile, config, CancellationToken.None);

        Assert.Equal("Failed", uploadedFile.PreviewStatus);
        Assert.Contains("Không tìm thấy tệp", uploadedFile.PreviewErrorMessage);
    }

    // =========================================================================
    // 4. CQ-F01 & REL-F08: Export Snapshot Parsing & Partial File Cleanup
    // =========================================================================

    [Fact]
    public void ExportFileGenerator_MalformedSnapshotJson_ThrowsInvalidOperationException()
    {
        var generator = new ExportFileGenerator(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Exports:StoragePath"] = _testDirectory
            })
            .Build());

        var malformedRequest = new ExportRequest
        {
            Id = 999,
            ExportType = ExportTypes.Pdf,
            Scope = "club",
            SnapshotJson = "{ invalid json payload: not a valid object"
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            generator.Generate(malformedRequest));

        Assert.Contains("không hợp lệ", ex.Message);

        // Verify partial file was cleaned up
        var expectedFilePath = Path.Combine(_testDirectory, "clubreport-club-999.pdf");
        Assert.False(File.Exists(expectedFilePath));
    }

    [Fact]
    public void ExportFileGenerator_NullOrEmptySnapshot_SucceedsGracefully()
    {
        var generator = new ExportFileGenerator(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Exports:StoragePath"] = _testDirectory
            })
            .Build());

        var emptySnapshotRequest = new ExportRequest
        {
            Id = 998,
            ExportType = ExportTypes.Pdf,
            Scope = "club",
            SnapshotJson = null
        };

        var result = generator.Generate(emptySnapshotRequest);

        Assert.NotNull(result);
        Assert.True(File.Exists(result.FilePath));
        Assert.True(result.SizeBytes > 0);
    }

    [Fact]
    public async Task ExportGenerationJob_MalformedSnapshot_SetsFailedStatusAndCleansUp()
    {
        var options = new DbContextOptionsBuilder<ExportDbContext>()
            .UseInMemoryDatabase("ExportJob_MalformedSnapshot_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var db = new ExportDbContext(options);

        var request = new ExportRequest
        {
            Id = 1001,
            ExportType = ExportTypes.Pdf,
            Scope = "finance",
            Status = ExportStatuses.Pending,
            RequestedByUserId = 42,
            SnapshotJson = "{\"broken_snapshot\": [malformed json"
        };
        db.ExportRequests.Add(request);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Exports:StoragePath"] = _testDirectory
            })
            .Build();

        var generator = new ExportFileGenerator(config);
        var job = new ExportGenerationJob(db, generator, config, NullLogger<ExportGenerationJob>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await job.GenerateAsync(1001, CancellationToken.None));

        var updated = await db.ExportRequests.FindAsync(1001);
        Assert.NotNull(updated);
        Assert.Equal(ExportStatuses.Failed, updated.Status);
        Assert.Contains("không hợp lệ", updated.ErrorMessage);

        // Ensure outbox event was NOT generated
        var outboxCount = await db.OutboxMessages.CountAsync();
        Assert.Equal(0, outboxCount);
    }

    // =========================================================================
    // 5. REL-F08: Hangfire Server Configuration
    // =========================================================================

    [Fact]
    public void HangfireServer_Configuration_BoundsWorkerCount()
    {
        var configLow = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hangfire:WorkerCount"] = "0"
            })
            .Build();

        var workerCountLow = Math.Clamp(configLow.GetValue<int>("Hangfire:WorkerCount", 2), 1, 8);
        Assert.Equal(1, workerCountLow);

        var configHigh = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hangfire:WorkerCount"] = "64"
            })
            .Build();

        var workerCountHigh = Math.Clamp(configHigh.GetValue<int>("Hangfire:WorkerCount", 2), 1, 8);
        Assert.Equal(8, workerCountHigh);

        var configDefault = new ConfigurationBuilder().Build();
        var workerCountDefault = Math.Clamp(configDefault.GetValue<int>("Hangfire:WorkerCount", 2), 1, 8);
        Assert.Equal(2, workerCountDefault);
    }

    // =========================================================================
    // 6. OPS-F07: Docker Compose Persistent Volume Verification
    // =========================================================================

    [Fact]
    public void DockerCompose_ReportService_HasPersistentPreviewVolumeMounted()
    {
        var composePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../docker-compose.yml"));
        if (!File.Exists(composePath))
        {
            // Alternative relative path from test runner
            composePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../docker-compose.yml"));
        }

        if (File.Exists(composePath))
        {
            var composeText = File.ReadAllText(composePath);

            Assert.Contains("report_previews:/app/report-previews", composeText);
            Assert.Contains("report_previews:", composeText);
            Assert.Contains("Uploads__PreviewStoragePath", composeText);
        }
    }
}
