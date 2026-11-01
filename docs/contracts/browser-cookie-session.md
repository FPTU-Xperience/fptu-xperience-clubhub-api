# Browser cookie session: ClubHub is the sign-in entry point

ClubHub and Admin independently call Auth to inspect the same browser cookie.
Neither UI navigates to the other UI to probe local storage. An anonymous ClubHub
visitor stays on ClubHub. Admin redirects to ClubHub only after Auth confirms no
session exists; an Auth outage shows a retry screen instead of a redirect.

## Auth contract

- Google/email sign-in from an exact trusted UI origin sets a host-only HttpOnly
  session cookie on the API host. Production uses `__Host-fptux-session`, Secure,
  Path=/, with no Domain attribute. Local HTTP development uses `fptux-session`.
- The signed cookie identifies a user, security version, refresh family, random
  browser session ID and fixed expiry. It is not accepted as an API access token.
  Its signing key is purpose-separated from JWT access tokens using HKDF-SHA256.
- `GET /api/auth/session` returns 200 with `{ user, browserSessionId }` or 204 if
  absent, expired, locked, revoked or invalid. It never rotates refresh tokens.
- `POST /api/auth/session` restores an app-specific access/refresh branch in the
  same live refresh family, returning `{ session: AuthResponse, browserSessionId }`.
  Restore uses the existing family lock to serialize with logout.
- Both calls require an exact allowed Origin and `X-FPTUX-Session: 1`. Responses
  are `no-store`. Requests use `credentials: include`; CORS permits credentials
  only for configured frontend origins.
- Each UI checks on startup, visible-tab focus and every 15 seconds. The cookie
  is authoritative. Stale local credentials are cleared if Auth confirms absence;
  transient errors do not expire a retained session. In-flight checks cannot
  overwrite a new login or revive a signed-out session.
- Logout revokes the family, changes the account security version, and expires
  the matching user's cookie. The other UI observes revocation on its next check.
- Sign-in verifies cookie persistence before accepting local credentials. If a
  browser blocks the cookie, it shows an error instead of a redirect loop.

## Deployment and domains

Deploy Auth first, then **both** frontend revisions together. Existing browser
sessions without the new cookie must sign in once again through ClubHub. Existing
token login/refresh endpoints and legacy PKCE endpoints remain compatible; neither
new frontend imports the old redirect-based discovery implementation.

For reliable SSO, use HTTPS origins under one registrable domain, for example
`club.example.edu`, `admin.example.edu`, and `api.example.edu`. The cookie remains
host-only on `api.example.edu`; both UIs send it directly to that host.

Set `CLUBHUB_ORIGIN` and `ADMIN_ORIGIN` in the API deployment environment, and add
both exact origins to `FRONTEND_ORIGIN` / `CORS_ADDITIONAL_ORIGINS` for Gateway and
Auth. Set each frontend's `VITE_API_BASE_URL` to the same HTTPS API and Admin's
`VITE_CLUBHUB_APP_URL` to the ClubHub `/v2` URL. `BROWSER_SESSION_SAME_SITE=Lax` is
appropriate when all three hosts are same-site. Default `None` supports cross-site
requests where the browser permits third-party cookies, always with Secure.

Separate `*.pages.dev` sites and an unrelated API domain cannot provide reliable
cookie SSO in browsers that block third-party cookies. SameSite=None and CORS do
not override that browser policy. Do not set Domain=pages.dev, use partitioned
cookies as a shared-login substitute, or reintroduce navigation probes.

References:
- https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Set-Cookie
- https://developer.mozilla.org/en-US/docs/Web/HTTP/Guides/CORS
- https://developers.cloudflare.com/pages/configuration/custom-domains/
