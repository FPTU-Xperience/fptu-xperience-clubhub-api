# Google-only sign-in

This backend has one passwordless entry point:

```http
POST /api/auth/google
Content-Type: application/json

{ "credential": "GOOGLE_ID_TOKEN" }
```

The `credential` is the signed ID token returned by Google Identity Services. The backend validates its signature, issuer, audience, expiry, and `email_verified` claim using Google's OpenID Connect metadata. It then checks the verified e-mail against `ClubReportHub_Auth.dbo.Users`.

Access is granted only when the matching local row is active, unlocked, and has exactly one allowed actor role: `ADMIN`, `CLUB_MANAGER`, or `CLUB_MEMBER` (Student). `CLUB_MEMBER` accounts must also be present and active in the current semester roster. Admin and club-manager accounts are not subject to the student roster, so they can prepare or switch semesters. It does not register a user at sign-in. On first successful login, the Google `sub` identifier is stored as `Users.GoogleSubject`; later, a different Google account cannot take over the local account.

## Server configuration

1. In Google Cloud Console, create an OAuth 2.0 **Web application** client.
2. Add each front-end URL users will open (for example `https://demo.example.edu.vn` and `http://localhost:5173`) under **Authorized JavaScript origins**.
3. Put the resulting client ID in `.env`:

   ```dotenv
   GOOGLE_CLIENT_ID=123456789012-xxxx.apps.googleusercontent.com
   # Exact verified e-mail domains, comma-separated.
   GOOGLE_ALLOWED_EMAIL_DOMAINS=fpt.edu.vn
   # Optional: additionally require Google's Workspace `hd` claim.
   GOOGLE_ALLOWED_HOSTED_DOMAIN=fpt.edu.vn
   ```

4. Provision the application users as usual. For student accounts, the `Users.Email` value must match the Google e-mail and the account must have the `CLUB_MEMBER` role.
5. Redeploy the Auth service so migrations `20260913090000_GoogleOnlyAuthentication` and `20260914153544_ActiveSemesterRoster` run. The latter adds indexed `Semesters` and `SemesterStudents` tables.

## Semester roster management

The roster is stored in SQL Server, not in an environment variable or a frontend bundle. An Admin can manage it through these protected endpoints:

```http
POST /api/semesters
{ "code": "2026-FALL", "isCurrent": true }

PUT /api/semesters/{semesterId}/students
{ "emails": ["student1@fpt.edu.vn", "student2@fpt.edu.vn"] }

PUT /api/semesters/{semesterId}/current
```

`PUT /students` replaces the roster atomically. E-mails are trimmed, lower-cased, deduplicated, validated, and indexed. Login performs one indexed `EXISTS` query against the current semester; a 4,000-row roster is small for SQL Server and does not need an in-memory cache. The Google login burst limit is 1,200 requests per minute per source IP to accommodate a campus NAT/proxy; multi-instance deployments should also apply a distributed limit at the gateway or edge.

For Google’s current server-side ID-token validation requirements, see [Verify Google ID tokens on your server](https://developers.google.com/identity/gsi/web/guides/verify-google-id-token).

## Front-end handoff

The companion `fptu-xperience-clubhub-ui` repository now includes the browser login integration in `src/pages/LoginPage.jsx`. Set `VITE_GOOGLE_CLIENT_ID` in the front end to the same client ID configured as `GOOGLE_CLIENT_ID` for this API. The front end loads Google Identity Services and submits its returned `credential` to the endpoint above. A minimal integration looks like this:

```html
<script src="https://accounts.google.com/gsi/client" async></script>
<div id="g_id_onload"
     data-client_id="YOUR_GOOGLE_CLIENT_ID"
     data-callback="onGoogleCredential"></div>
<div class="g_id_signin" data-theme="outline" data-size="large"></div>

<script>
  async function onGoogleCredential(googleResponse) {
    const response = await fetch(`${API_BASE_URL}/api/auth/google`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ credential: googleResponse.credential })
    });

    if (response.status === 403) {
      throw new Error("Google e-mail is not in the approved roster.");
    }
    if (!response.ok) {
      throw new Error("Google identity could not be verified.");
    }

    const session = await response.json();
    // Store accessToken / refreshToken according to the front-end security policy.
    return session;
  }
</script>
```

Never send an e-mail address, role, or password as a replacement for `credential`; the API intentionally ignores those as authentication input.
