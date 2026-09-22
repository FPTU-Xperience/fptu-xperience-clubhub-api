namespace ClubReportHub.Shared.Security;

public static class ContentDispositionSanitizer
{
    private static readonly HashSet<char> InvalidFileNameChars =
    [
        '\"', '\'', '\\', '/', ':', '*', '?', '<', '>', '|', '\0'
    ];

    public static string SanitizeFileName(string? rawFileName, string fallback = "download")
    {
        if (string.IsNullOrWhiteSpace(rawFileName))
        {
            return fallback;
        }

        // 1. Strip directory paths and path traversal
        var fileName = Path.GetFileName(rawFileName.Trim());

        // 2. Remove CRLF to prevent HTTP response splitting / header injection
        fileName = fileName.Replace("\r", string.Empty).Replace("\n", string.Empty);

        // 3. Replace control characters and unsafe characters with underscore
        var chars = fileName.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (char.IsControl(c) || InvalidFileNameChars.Contains(c))
            {
                chars[i] = '_';
            }
        }

        var sanitized = new string(chars).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}
