# Coherent Demo Dataset

This dataset is intended for a teacher or reviewer testing a deployed demo server. It is deliberately limited to three business actor types:

- `ADMIN`
- `CLUB_MANAGER`
- `CLUB_MEMBER` (shown to users as **Student**)

The authorization role definitions used by the source code are still retained, but no demo account is assigned `SYSTEM_ADMIN`, `STUDENT_AFFAIRS_ADMIN`, or global `TREASURER`. A student receives finance permissions through the `TREASURER` role of their **club membership**, while their global account role remains `CLUB_MEMBER`.

## Run on the demo server

1. Create the environment file and replace all default secrets:

   ```bash
   cp .env.example .env
   ```

2. Start the services so every service can create or migrate its own database:

   ```bash
   docker compose up -d --build
   ```

3. Build the one-shot seeder:

   ```bash
   docker compose --profile demo build demo-data-seeder
   ```

4. For an old demo database containing disposable test data, clear all rows and create a clean dataset:

   ```bash
   docker compose --profile demo run --rm -e DemoData__ResetAll=true demo-data-seeder
   ```

   `ResetAll=true` is destructive. It clears business data in the Auth, Club, Activity, Report, Finance, Notification, and Export databases and trims stale integration events from the configured Redis stream. Role definitions are retained. Use it only on the disposable demo server.

5. For later refreshes, run the idempotent mode. It updates the managed demo records without duplicating them:

   ```bash
   docker compose --profile demo run --rm demo-data-seeder
   ```

6. Check the last line. A successful run prints a summary beginning with `DEMO DATA READY`.

Validation without writes is also available:

```bash
docker compose --profile demo run --rm -e DemoData__ValidateOnly=true demo-data-seeder
```

## Dataset size

| Area | Seeded data |
|---|---:|
| Accounts | 16 |
| Actor types | 3 |
| Clubs | 3 |
| Memberships | 17 |
| Club creation applications | 3 |
| Activities | 12 |
| Attendance records | 69 |
| Reports | 12 |
| Reporting deadlines | 3 |
| Budget proposals | 7 |
| Settlements | 3 |
| Finance transactions | 10 |
| Notifications | 21 |

The default reference date is `2026-09-11`. Change `DEMO_REFERENCE_DATE` in `.env` when a different demonstration timeline is needed. Dates before and after that point are regenerated consistently.

## Google-only login accounts

There are no passwords, self-registration endpoint, or automatic account creation. Google verifies the browser identity, then the backend grants a JWT only if the verified e-mail is already an active, unlocked row in `ClubReportHub_Auth.dbo.Users` with exactly one enabled actor role: `ADMIN`, `CLUB_MANAGER`, or `CLUB_MEMBER`.

Before the teacher tests the server:

1. Create a **Web application OAuth client** in Google Cloud and add the deployed front-end origin to its authorized JavaScript origins.
2. Put that client ID in `GOOGLE_CLIENT_ID` in `.env`.
3. Replace the three e-mails below with real Google accounts controlled by the teacher/testers, then run the demo seeder. The default `@fpt.edu.vn` addresses are realistic sample roster data, not Google accounts anyone can sign into.

| Actor test path | `.env` key | Default sample e-mail |
|---|---|---|
| `ADMIN` | `DEMO_ADMIN_EMAIL` | `admin@fpt.edu.vn` |
| `CLUB_MANAGER` | `DEMO_CLUB_MANAGER_EMAIL` | `manager.tech@fpt.edu.vn` |
| `CLUB_MEMBER` (Student) | `DEMO_STUDENT_EMAIL` | `se170002@fpt.edu.vn` |

The first successful Google sign-in links the account's immutable Google `sub` identifier to the pre-approved user row. A different Google account cannot subsequently use that row, even if an e-mail conflict is attempted. When an administrator changes a user's e-mail, the prior link and refresh tokens are revoked so the new holder must authenticate through Google again.

See [GOOGLE_SIGN_IN.md](GOOGLE_SIGN_IN.md) for the exact Google Cloud, API, and front-end handoff steps.

### Admin

| Username / allow-listed Google e-mail | Name |
|---|---|
| `DEMO_ADMIN_EMAIL` (default `admin@fpt.edu.vn`) | Nguyễn Thu Hà |

The Admin account can perform both system administration and final business approval because the existing authorization policies include `ADMIN` in both paths.

### Club managers

| Username / allow-listed Google e-mail | Name | Managed club |
|---|---|---|
| `DEMO_CLUB_MANAGER_EMAIL` (default `manager.tech@fpt.edu.vn`) | Trần Minh Quân | Câu lạc bộ Công nghệ FPT |
| `manager.ai@fpt.edu.vn` | Lê Khánh Linh | Câu lạc bộ AI & Data |
| `manager.volunteer@fpt.edu.vn` | Phạm Hoàng Nam | Câu lạc bộ Tình nguyện Cóc Xanh |

Each manager owns exactly one club and also has an approved membership in that club, matching the normal manager-assignment workflow.

### Students

| Username | Name | Primary club | Membership state | Club responsibility |
|---|---|---|---|---|
| `se170001@fpt.edu.vn` | Nguyễn Hải Đăng | FPT-TECH | Approved | Treasurer |
| `DEMO_STUDENT_EMAIL` (default `se170002@fpt.edu.vn`) | Trần Gia Hân | FPT-TECH | Approved | Member |
| `se170003@fpt.edu.vn` | Lê Đức Anh | FPT-TECH | Approved | Member; also approved in FPT-AI |
| `se170004@fpt.edu.vn` | Phạm Ngọc Mai | FPT-TECH | Pending | Member applicant |
| `ai170001@fpt.edu.vn` | Võ Minh Khang | FPT-AI | Approved | Treasurer |
| `ai170002@fpt.edu.vn` | Nguyễn Thảo Vy | FPT-AI | Approved | Member; also approved in FPT-VOL |
| `ai170003@fpt.edu.vn` | Đỗ Quốc Bảo | FPT-AI | Approved | Member |
| `ai170004@fpt.edu.vn` | Bùi Khánh An | FPT-AI | Rejected | Member applicant |
| `ss170001@fpt.edu.vn` | Trương Hoài Phương | FPT-VOL | Approved | Treasurer |
| `ss170002@fpt.edu.vn` | Huỳnh Gia Bảo | FPT-VOL | Approved | Member |
| `ss170003@fpt.edu.vn` | Nguyễn Nhật Hạ | FPT-VOL | Approved | Member |
| `ss170004@fpt.edu.vn` | Phan Minh Châu | FPT-VOL | Approved | Member |

The table is a pre-approved roster. Profile forms include realistic dates of birth, phone numbers, addresses, skills, goals, expectations, contribution statements, review notes, and request dates. To make another sample student sign in, an Admin must update that row's e-mail to the student's real Google e-mail through `PUT /api/users/{id}`.

## Recommended test paths

### 1. Admin overview and final approval

- Sign in with the Google account configured in `DEMO_ADMIN_EMAIL`.
- Review all three clubs, their managers, membership states, report deadlines, and KPI results.
- Open the FPT-TECH future-event report. Its budget is manager-approved and waiting for Admin final approval.
- Final approval should create the activity through `POST /api/activities/from-approved-report`; the endpoint is idempotent, so a retry cannot create a duplicate activity for the same report.

### 2. Club manager workflow

- Sign in with the Google account configured in `DEMO_CLUB_MANAGER_EMAIL`.
- Review the pending application from Phạm Ngọc Mai.
- Inspect completed activities and attendance history for CodeFest and the Git/CI/CD workshop series.
- Review the Cloud-native Day budget before it moves to Admin approval.
- Compare approved, draft, and awaiting-finance reports in the same club.

### 3. Student and club-treasurer workflow

- Use an allow-listed Google account with the `tech-treasurer` roster row to test student access with club-level finance permission.
- Create or inspect a financial report and budget proposal for FPT-TECH.
- Sign in with the Google account configured in `DEMO_STUDENT_EMAIL` to verify that an ordinary student can view approved club content but cannot manage finance.
- Compare activity statistics and attendance history between students with Present, Late, Excused, and Absent records.

### 4. Rejection and correction paths

- The AI financial report is rejected because supporting documents are incomplete.
- The Volunteer partnership report is rejected because image-consent terms are unclear.
- The AI membership application for Bùi Khánh An is rejected with a realistic review note.
- The Photography club application is in `NeedsRevision`, while the Robotics application is still `Submitted`.

## Integrity rules enforced by the seeder

- No seed record uses a hard-coded database identity ID.
- User IDs are resolved by username; club IDs are resolved by club code; report IDs are resolved by `SeedKey`.
- Every activity participant has an approved membership in that activity's club.
- Every attendance row references an activity participant.
- Published activities and budget proposals reference valid seeded reports.
- Every seeded report has a club name, due date, narrative sections, and at least one detail row.
- Dates follow a valid order: created → submitted → manager/final review → settlement.
- Running the non-reset mode repeatedly does not duplicate managed records.
