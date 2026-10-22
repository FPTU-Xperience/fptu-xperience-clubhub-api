using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using static ActivityService.Endpoints.ActivityActionHelpers;

namespace ActivityService.Endpoints;

internal static class ActivityResourceLinkPolicy
{
    private static readonly HashSet<string> YouTubeHosts = new(StringComparer.Ordinal)
        { "youtube.com", "www.youtube.com", "m.youtube.com", "youtu.be", "youtube-nocookie.com", "www.youtube-nocookie.com" };
    private static readonly HashSet<string> ExternalHosts = new(StringComparer.Ordinal)
        { "docs.google.com", "drive.google.com", "forms.gle", "onedrive.live.com", "1drv.ms", "github.com", "www.figma.com", "www.canva.com" };

    // Exact trusted hosts only. No server-side URL fetch or iframe HTML is accepted.
    internal static (string Url, string Provider) Normalize(string? input)
    {
        var text = input?.Trim();
        Require(text is { Length: > 0 and <= 2000 } && !text.Any(char.IsControl)
            && Uri.TryCreate(text, UriKind.Absolute, out var parsed) && parsed.IsWellFormedOriginalString(),
            400, "INVALID_RESOURCE_URL", "Provide a supported HTTPS resource link.");
        var uri = new Uri(text!);
        var host = uri.IdnHost.ToLowerInvariant();
        Require(uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo)
            && !IPAddress.TryParse(host, out _) && (YouTubeHosts.Contains(host) || ExternalHosts.Contains(host)),
            400, "UNSUPPORTED_RESOURCE_HOST", "Use an exact supported HTTPS host without credentials or custom ports.");
        if (!YouTubeHosts.Contains(host)) return (uri.AbsoluteUri, "EXTERNAL");
        string? video = null;
        var path = uri.AbsolutePath;
        if (host == "youtu.be") video = path.TrimStart('/');
        else if (path == "/watch" && QueryHelpers.ParseQuery(uri.Query).TryGetValue("v", out var values) && values.Count == 1) video = values[0];
        else if (path.StartsWith("/embed/", StringComparison.Ordinal)) video = path[7..];
        else if (path.StartsWith("/shorts/", StringComparison.Ordinal) && host is not ("youtube-nocookie.com" or "www.youtube-nocookie.com")) video = path[8..];
        Require(video is not null && Regex.IsMatch(video, "^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant),
            400, "INVALID_VIDEO_LINK", "Provide a supported YouTube video link.");
        return ($"https://www.youtube-nocookie.com/embed/{video}", "YOUTUBE");
    }
}
