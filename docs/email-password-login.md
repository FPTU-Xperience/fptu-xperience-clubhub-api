# Email/password login and shared sessions

`POST /api/auth/email-login` accepts `{ "email": "…", "password": "…" }`.
Emails are trimmed and matched without case sensitivity; passwords are never trimmed.
Only an existing active, unlocked account with a valid actor role and password hash can log in.
The API uses ASP.NET Identity password hashing, a generic credentials error, and an email-login rate limit.
It never creates an account or changes roles during login.

Successful email and Google login return the same `AuthResponse`: access token, refresh token,
user summary and expiry timestamps. Both use the same refresh rotation, grace period,
security-version checks and logout revocation. There is no temporary email-login window,
proxy secret, email allow-list or `LOCAL_DEV_LOGIN_*` configuration.

The old `/api/auth/dev-login` and `/api/auth/test-login` routes are removed in all environments.
The existing `/api/auth/internal/login` route remains a compatibility alias for the same
password verification and session creation. Google login remains available.

## Existing accounts and initial passwords

Existing `PasswordHash` values are used unchanged. Accounts with no hash need an explicit password;
deployment never generates a default password or changes production credentials.
No database migration is needed because `Users.PasswordHash` already exists.

An operator with access to the deployed AuthService container can set a password interactively:

```sh
docker exec -it clubreporthub-auth dotnet AuthService.dll --set-password ctsv.an@fpt.edu.vn
docker exec -it clubreporthub-auth dotnet AuthService.dll --set-password admin@fpt.edu.vn
```

The command prompts twice with hidden input; passwords are not passed in arguments or printed.
It requires an existing account, hashes a 12–128 character password, revokes previous sessions,
and preserves account roles, active/locked status and Google linkage.
An authenticated administrator can also use `PUT /api/users/{id}/password` with `{ "password": "…" }`.
This endpoint requires the system-administration policy and also revokes previous sessions.

## SSO between Admin and ClubHub

Both `ADMIN` and `STUDENT_AFFAIRS_ADMIN` can use the existing one-time PKCE SSO ticket flow.
Each site receives a distinct refresh-token chain in one shared family; tokens never travel in URLs.
Logout revokes the family. Students cannot obtain staff SSO tickets, and SSO never grants new roles.
The issuer verifies the token owner, current account status, target origin and PKCE challenge.

Production origins remain the configured Admin and ClubHub pair. The exact localhost 5174/5173 pair
is enabled by default for local frontend development against the deployed API. Set
`LOCAL_SSO_ENABLED=false` to opt out. Tickets cannot cross between the local and production pairs.

## Frontend forms

Both sites submit email and password to the API directly over the configured API URL.
ClubHub initially shows Google and an expandable “Hoặc dùng email” control; its email form is collapsed.
Admin initially shows Google and exposes the email/password form through its Email option.

`VITE_LOGIN_ACCOUNT_SHORTCUTS=true` shows Admin and CTSV email shortcuts in both forms.
The default is false. Shortcuts fill only `admin@fpt.edu.vn` or `ctsv.an@fpt.edu.vn`, never passwords,
and do not submit the form. Set the flag in git-ignored `.env.development.local` for local development;
restart Vite after changing environment configuration. Production builds use the flag set at build time.

Source tests and isolated browser fixtures do not establish deployed authentication success.
Deployment and real-account password setup must be verified separately.
