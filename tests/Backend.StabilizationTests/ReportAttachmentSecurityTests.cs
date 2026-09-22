using ReportService.Attachments;
using ReportService.Extensions;
using ReportService.Models;
using Xunit;

namespace Backend.StabilizationTests;

public sealed class ReportAttachmentSecurityTests
{
    private static readonly string TestStorageRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test_attachments"));

    [Fact]
    public void IsPathUnderRoot_ValidSubpath_ReturnsTrue()
    {
        var validFilePath = Path.Combine(TestStorageRoot, "10", "20260921-testfile.pdf");

        var result = ReportAttachmentPolicy.IsPathUnderRoot(validFilePath, TestStorageRoot);

        Assert.True(result);
    }

    [Theory]
    [InlineData("../../appsettings.json")]
    [InlineData("..\\..\\appsettings.json")]
    [InlineData("../../../../Windows/win.ini")]
    [InlineData("..\\..\\..\\..\\Windows\\win.ini")]
    [InlineData("../../etc/passwd")]
    public void IsPathUnderRoot_DirectoryTraversal_ReturnsFalse(string traversalPath)
    {
        var candidatePath = Path.Combine(TestStorageRoot, "10", traversalPath);

        var result = ReportAttachmentPolicy.IsPathUnderRoot(candidatePath, TestStorageRoot);

        Assert.False(result);
    }

    [Fact]
    public void IsPathUnderRoot_PrefixCollisionSiblingDirectory_ReturnsFalse()
    {
        var siblingRoot = TestStorageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "_sibling";
        var candidatePath = Path.Combine(siblingRoot, "secret.txt");

        var result = ReportAttachmentPolicy.IsPathUnderRoot(candidatePath, TestStorageRoot);

        Assert.False(result);
    }

    [Theory]
    [InlineData(null, "some/root")]
    [InlineData("some/path", null)]
    [InlineData("", "some/root")]
    [InlineData("   ", "some/root")]
    public void IsPathUnderRoot_NullOrWhitespace_ReturnsFalse(string? candidate, string? root)
    {
        var result = ReportAttachmentPolicy.IsPathUnderRoot(candidate!, root!);

        Assert.False(result);
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("C:\\appsettings.Production.json")]
    [InlineData("/etc/shadow")]
    [InlineData("/var/run/secrets")]
    public void IsPathUnderRoot_ArbitraryAbsoluteSystemPath_ReturnsFalse(string arbitrarySystemPath)
    {
        var result = ReportAttachmentPolicy.IsPathUnderRoot(arbitrarySystemPath, TestStorageRoot);

        Assert.False(result);
    }

    [Fact]
    public void GetSafeFileName_SanitizesTraversalAndControlCharacters()
    {
        const string maliciousFileName = "..\\..\\evil/payload\0.pdf";

        var safeName = ReportAttachmentPolicy.GetSafeFileName(maliciousFileName);

        Assert.DoesNotContain("..", safeName);
        Assert.DoesNotContain("/", safeName);
        Assert.DoesNotContain("\\", safeName);
        Assert.EndsWith(".pdf", safeName);
    }

    [Fact]
    public void ReportMappers_MasksPhysicalStoragePathInResponse()
    {
        var report = new Report
        {
            Id = 123,
            ClubId = 1,
            Period = "2026-Q3",
            CreatedByUserId = 456,
            Attachments =
            [
                new ReportAttachment
                {
                    Id = 1,
                    ReportId = 123,
                    FileName = "evidence.pdf",
                    ContentType = "application/pdf",
                    SizeBytes = 1024,
                    StoragePath = "C:\\secret\\internal\\path\\evidence.pdf",
                    UploadedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        };

        var response = ReportMappers.ToResponse(report);

        Assert.NotNull(response.Attachments);
        var attachment = Assert.Single(response.Attachments);
        Assert.Equal(1, attachment.Id);
        Assert.Equal("evidence.pdf", attachment.FileName);
        Assert.Equal(string.Empty, attachment.StoragePath);
        Assert.DoesNotContain("C:\\secret", attachment.StoragePath);
    }
}
