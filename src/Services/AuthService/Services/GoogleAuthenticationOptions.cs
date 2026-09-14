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

    /// <summary>
    /// Optional comma-separated list of exact e-mail domains accepted by the
    /// application, for example <c>fpt.edu.vn</c>. This is checked against the
    /// verified e-mail claim, independently of the Google <c>hd</c> claim.
    /// </summary>
    public string? AllowedEmailDomains { get; set; }
}
