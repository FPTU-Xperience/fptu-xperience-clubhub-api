using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Services;

/// <summary>
/// Validates Google ID tokens against Google's OpenID Connect metadata and
/// signing keys. The browser-provided e-mail is never trusted directly.
/// </summary>
public sealed class GoogleIdTokenValidator : IGoogleIdTokenValidator
{
    private static readonly string[] GoogleIssuers =
    [
        "https://accounts.google.com",
        "accounts.google.com"
    ];

    private readonly GoogleAuthenticationOptions _options;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configurationManager;
    // Preserve Google's standard claim names (`sub`, `email`, `hd`) instead
    // of letting the default handler remap them to .NET claim URIs.
    private readonly JwtSecurityTokenHandler _tokenHandler = new() { MapInboundClaims = false };

    public GoogleIdTokenValidator(IOptions<GoogleAuthenticationOptions> options)
    {
        _options = options.Value;
        _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            "https://accounts.google.com/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });
    }

    public async Task<GoogleIdentity?> ValidateAsync(
        string credential,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credential))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId))
        {
            throw new InvalidOperationException(
                "GoogleAuthentication:ClientId must be configured before Google sign-in can be used.");
        }

        try
        {
            var configuration = await _configurationManager.GetConfigurationAsync(cancellationToken);
            return ToIdentity(ValidateToken(credential, configuration));
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            // Google can rotate signing keys. Refresh discovery metadata once
            // before treating the otherwise-valid credential as invalid.
            _configurationManager.RequestRefresh();
            try
            {
                var configuration = await _configurationManager.GetConfigurationAsync(cancellationToken);
                return ToIdentity(ValidateToken(credential, configuration));
            }
            catch (SecurityTokenException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private ClaimsPrincipal ValidateToken(
        string credential,
        OpenIdConnectConfiguration configuration)
    {
        return _tokenHandler.ValidateToken(
            credential,
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = configuration.SigningKeys,
                ValidateIssuer = true,
                ValidIssuers = GoogleIssuers,
                ValidateAudience = true,
                ValidAudience = _options.ClientId,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            },
            out _);
    }

    private GoogleIdentity? ToIdentity(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirst("sub")?.Value?.Trim();
        var email = principal.FindFirst("email")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var emailVerified = bool.TryParse(
            principal.FindFirst("email_verified")?.Value,
            out var verified) && verified;
        var hostedDomain = principal.FindFirst("hd")?.Value?.Trim();

        if (!string.IsNullOrWhiteSpace(_options.AllowedHostedDomain) &&
            !string.Equals(
                hostedDomain,
                _options.AllowedHostedDomain.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new GoogleIdentity(subject, email, emailVerified, hostedDomain);
    }
}
