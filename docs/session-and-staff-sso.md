# Admin / ClubHub sessions and CTSV SSO

Current authentication contract: [Email/password login and shared sessions](email-password-login.md).
The historical dev-login instructions below are superseded: passwordless dev-login and its temporary proxy key were removed.

Implementation: 2026-10-22. Source changes require deployment; browser fixtures are not live OAuth or production acceptance.

## Browser behavior

- Admin and ClubHub use the same session-client implementation. Both refresh after an authenticated 401, share one refresh per tab, and serialize rotation across same-origin tabs with Web Locks where available.
- Retry runs once. A retry returning 400/403/500 does not clear the session. Network/5xx refresh failures preserve credentials. A rejected refresh or repeated authenticated 401 clears them.
- An atomic `fptux:session:v1` localStorage record drives same-origin login/logout. Existing legacy tokens migrate on refresh; raw legacy keys cannot resurrect a cleared session.
- AuthContext revalidates account identity against the API after login/account switching. Results from an old session cannot restore its identity after logout.
- Every logout and definitive session expiry goes to ClubHub `/v2`, including Admin and tabs receiving sign-out. The initiating tab completes its server revocation attempt before navigating. The old ClubHub `/login` route redirects to `/v2`.
- Offline logout clears the browser session; another origin cannot confirm server revocation until the server is reachable.

## Cross-origin CTSV SSO

Production origins are exactly `https://fptux-admin-ui.pages.dev` and `https://fptux-clubhub-ui.pages.dev`; API is `https://clubreporthub.duckdns.org`. No shared-domain cookie or third-party storage is required.

An app without a local session generates a random state and PKCE verifier in sessionStorage, then navigates to its configured peer. That peer uses its own local session to request an opaque code, and immediately returns to the requesting app. The requester checks state, redeems the code with its verifier and restores the original local route. Access and refresh tokens never appear in URLs. Callback parameters are removed from browser history before the app renders, and both pages use `Referrer-Policy: no-referrer` through their HTML meta tag.

Only an active, unlocked `STUDENT_AFFAIRS_ADMIN` can issue or redeem a ticket. An already signed-in student/admin account is not overwritten by SSO. Anonymous discovery returns to login without a redirect loop. Returning to an anonymous tab after leaving it immediately checks for a new peer login; redundant initial probes have a 30-second cooldown.

Codes expire in one minute. The database stores only their SHA-256 hashes, target origin, PKCE challenge, user security version and browser-session family. Redemption consumes the code atomically inside a serializable transaction, rechecks the account and family, and creates a separate refresh chain in that same family. Rotating one app's token does not revoke the other app's token. Logout revokes the entire shared family; pending tickets and cached rotations cannot restore it.

The other origin observes logout on focus/visibility or a 15-second status check while visible. Background tabs check when visible again. This is not a WebSocket push guarantee. Existing unrelated login families retain the backend's prior security-version/refresh behavior; this is not a new logout-all-devices feature.

## API contract

All SSO responses are `Cache-Control: no-store`.

| Endpoint | Authentication / body | Result |
| --- | --- | --- |
| `POST /api/auth/sso/tickets` | CTSV bearer; `{refreshToken,targetOrigin,codeChallenge}` | `{code,expiresAtUtc}`; exact source/target origin pair required |
| `POST /api/auth/sso/redeem` | Anonymous; `{code,codeVerifier}`; Origin must match target | Normal `AuthResponse`; 401 for expired, used, wrong-verifier, wrong-target or revoked tickets |
| `POST /api/auth/sso/status` | Anonymous proof of refresh-token possession; `{refreshToken}`; trusted Origin | 204 for active staff session; 401 if revoked/expired/account no longer allowed; does not rotate |

The existing gateway `/api/auth/{**catch-all}` route forwards these endpoints. Ticket/redeem use AuthService's refresh rate limiter. CORS allows the Admin production origin in AuthService and gateway. Compose's AuthService allow-list explicitly includes Admin; gateway already lists it at index 15.

## Configuration and rollout

1. Deploy AuthService/API first. Startup migrations add `StaffSsoTickets` and the refresh-family index through `20261022090000_StaffSsoTickets`. Back up and apply the migration under the normal deployment procedure. The application startup already applies migrations.
2. Defaults accept only the two production origins above. Optional `Sso__AdminOrigin` and `Sso__ClubHubOrigin` replace them. Development defaults are `http://localhost:5174` and `http://localhost:5173`; use those hosts consistently, or override both SSO and CORS origins. Custom QA ports require matching overrides.
3. Build/deploy Admin with `VITE_API_BASE_URL`, `VITE_CLUBHUB_APP_URL=https://fptux-clubhub-ui.pages.dev/v2`, `VITE_STAFF_SSO_ENABLED=true` and the existing Google client ID.
4. Build/deploy ClubHub with the same API, `VITE_ADMIN_APP_URL=https://fptux-admin-ui.pages.dev`, `VITE_STAFF_SSO_ENABLED=true` and its existing Google client ID. Workflow defaults already set the production peer URLs. The dev auth bypass skips SSO.
5. Verify Google CTSV login in each direction, existing-tab login/logout, expiry, blocked third-party cookies, original-route restoration and server revocation on the real domains. Non-staff accounts must not acquire an SSO session. No deployment was performed by this change.

`VITE_STAFF_SSO_ENABLED=false` disables cross-origin discovery/polling for isolated UI fixtures; token refresh, same-origin synchronization and `/v2` logout redirects remain enabled.

## Verification commands

Both frontend repositories: `npm run test:session` and `npm run build`. Admin's complete unit suite: `npm test`.

Backend: `dotnet test tests/Backend.StabilizationTests/Backend.StabilizationTests.csproj --filter FullyQualifiedName~RefreshTokenSecurityTests`.

Admin `scripts/session-sso-smoke.mjs` runs real UI entry points in Chrome against simulated API responses. Start isolated Vite servers on ports 5274/5273, override both peer URLs and API URL, and set `PLAYWRIGHT_MODULE` if Playwright is supplied externally. It checks SSO in both directions, login/logout in already-open tabs, cross-origin revocation detection and the common `/v2` redirect.

The GitNexus native runtime failed on this Windows host; the user explicitly authorized direct source review and tests in place of its pre-edit impact gate. No graph impact claim is made.

### Recorded checks (2026-10-22)

- Admin unit tests: 81 passed. ClubHub session/SSO tests: 27 passed. Both production builds passed (existing bundle-size warnings).
- Backend: 26 focused tests passed through a temporary runner linking the repository's complete `RefreshTokenSecurityTests` sources and referencing the real AuthService/Shared projects. This uses ASP.NET TestServer/SQLite, including simultaneous code redemption, both SSO directions, replay, locked/deactivated users, family revocation and cached-rotation rejection. A SQL Server model/snapshot comparison also passed without a database connection. This is not the complete stabilization suite or a SQL Server integration run.
- Chrome fixture: both SSO directions; login/logout in already-open tabs; logout and timeout from both app origins ending at ClubHub `/v2` passed. API responses are simulated in this browser check.
- The broader Admin UI smoke route sweep passed before its workflow checks stopped at the XP history text assertion `Đang xem 1 dòng · 5 điểm thô đề xuất` in `scripts/workflow-ui-checks.mjs`. That XP assertion remains unresolved; it is not counted as a successful complete UI smoke.
- No production deploy, live Google login, production migration or remote push was performed.

## Local frontend login against the deployed API

Local ClubHub 5173 and Admin 5174 continue to use the production API and real database. Student Google login calls the production Google endpoint directly. Email dev-login uses only the local Vite proxy at `/api/auth/dev-login`, enabled with `VITE_LOCAL_DEV_LOGIN=true` and a non-VITE `LOCAL_DEV_LOGIN_KEY` in git-ignored `.env.development.local`. The proxy requires a loopback connection and its exact local browser Origin, then attaches `X-Local-Dev-Login-Key` server-side. The key must never be placed in a VITE variable or browser bundle.

Production AuthService accepts this email login only when `LOCAL_DEV_LOGIN_KEY` is at least 32 characters, matches the request key in constant time, `LOCAL_DEV_LOGIN_EMAILS` contains the email, and `LOCAL_DEV_LOGIN_EXPIRES_AT` is a future UTC timestamp. Missing, invalid, or expired configuration returns 403 with `code: dev_login_unavailable`, rather than disguising a configured route as 404. Existing active/unlocked account and actor-policy checks still apply. Deployment obtains the key from the production environment secret and the allow-list/expiry from production environment variables. Removing the secret or expiring the window disables this login method; existing sessions follow normal revocation/expiry rules.

`LOCAL_SSO_ENABLED=true` additionally permits exactly `http://localhost:5174` and `http://localhost:5173` as a separate SSO pair. Production-to-production SSO remains available; ticket issuance between a production origin and a local origin is rejected. The endpoint still requires a valid CTSV session, source refresh ownership, and PKCE. No API Testing service or synthetic user is needed for this flow.

This section documents the source change; deployment and browser verification must be recorded separately.

## Login-loop correction (2026-10-26)

`AddClubAccessClient` sets `SizeLimit = 10000` on the shared memory cache. Auth security stamps, remote security stamps and cached refresh rotation responses must each specify `Size = 1`, consistent with the club-access entry-count budget. Without this, a new token is rejected when the Auth security-stamp lookup throws; refresh commits its database rotation and then throws while caching its response, leaving the client with the old revoked token. The observed sequence is `/api/users/me` 401, `/api/auth/refresh` 500, then refresh 401. The browser's refresh-on-401 behavior is reactive, not an immediate-expiry policy.

Regression fixtures now register the production club-access cache configuration. Coverage includes new email login → identity → refresh → identity, duplicate rotation inside grace, revoked security versions in a bounded remote cache, and existing two-site SSO/revocation tests. No tokens or production secrets are required by these tests.

Google sign-in accepts verified `fpt.edu.vn` and `gmail.com` email domains. Explicitly allowed Gmail accounts do not require the Workspace `hd` claim, which personal Google accounts do not have; the configured school hosted-domain restriction still applies to school accounts. Signature, issuer, audience, lifetime, verified email, pre-existing account, account status and role checks remain required. Accepting a domain does not create or enable an account.
