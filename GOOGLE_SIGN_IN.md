# Google-only sign-in

This backend has one passwordless entry point:

```http
POST /api/auth/google
Content-Type: application/json

{ "credential": "GOOGLE_ID_TOKEN" }
```

The `credential` is the signed ID token returned by Google Identity Services. The backend validates its signature, issuer, audience, expiry, and `email_verified` claim using Google's OpenID Connect metadata. It then checks the verified e-mail against `ClubReportHub_Auth.dbo.Users`.

Access is granted only when the matching local row is active, unlocked, and has exactly one allowed demo actor role: `ADMIN`, `CLUB_MANAGER`, or `CLUB_MEMBER` (Student). It does not register a user at sign-in. On first successful login, the Google `sub` identifier is stored as `Users.GoogleSubject`; later, a different Google account cannot take over the local account.

## Server configuration

1. In Google Cloud Console, create an OAuth 2.0 **Web application** client.
2. Add each front-end URL users will open (for example `https://demo.example.edu.vn` and `http://localhost:5173`) under **Authorized JavaScript origins**.
3. Put the resulting client ID in `.env`:

   ```dotenv
   GOOGLE_CLIENT_ID=123456789012-xxxx.apps.googleusercontent.com
   # Optional: force an FPT Google Workspace account in addition to the DB list.
   GOOGLE_ALLOWED_HOSTED_DOMAIN=fpt.edu.vn
   ```

4. Add real Google e-mails to the local roster before anyone attempts login. For the three ready-made demo paths, set `DEMO_ADMIN_EMAIL`, `DEMO_CLUB_MANAGER_EMAIL`, and `DEMO_STUDENT_EMAIL`, then run the demo seeder. An authenticated Admin can add further entries with `POST /api/users`.
5. Redeploy the Auth service so migration `20260913090000_GoogleOnlyAuthentication` runs. It preserves users and business records, adds `GoogleSubject`, and removes legacy password hashes.

For Google’s current server-side ID-token validation requirements, see [Verify Google ID tokens on your server](https://developers.google.com/identity/gsi/web/guides/verify-google-id-token).

## Front-end handoff

This repository contains backend services only; there is no browser login page to modify. The front end should load Google Identity Services and submit its returned `credential` to the endpoint above. A minimal integration looks like this:

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
