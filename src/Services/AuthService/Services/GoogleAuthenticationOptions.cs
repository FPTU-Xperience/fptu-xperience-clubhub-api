namespace AuthService.Services;

public sealed class GoogleAuthenticationOptions
{
    public const string SectionName = "GoogleAuthentication";

    /// <summary>
    /// OAuth 2.0 Web client ID registered for the browser application.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Optional Google Workspace domain. Leave blank to rely solely on the
    /// database allow-list; when set, the verified Google <c>hd</c> claim must
    /// match this value.
    /// </summary>
    public string? AllowedHostedDomain { get; set; }
}
