# Coherent Demo Dataset

This dataset is intended for a teacher or reviewer testing a deployed demo server. It is configured for four business actor types:

- `ADMIN`
- `STUDENT_AFFAIRS_ADMIN` (shown to users as **CTSV / Student Affairs**)
- `CLUB_MANAGER`
- `CLUB_MEMBER` (shown to users as **Student**)

The authorization role definitions used by the source code are retained, but no demo account is assigned legacy `SYSTEM_ADMIN` or global `TREASURER`. A student receives finance permissions through the `TREASURER` role of their **club membership**, while their global account role remains `CLUB_MEMBER`.

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
| Accounts | 17 |
| Actor types | 4 |
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

There are no passwords, self-registration endpoint, or automatic account creation. Google verifies the browser identity, then the backend grants a JWT only if the verified e-mail is already an active, unlocked row in `ClubReportHub_Auth.dbo.Users` with exactly one enabled actor role: `ADMIN`, `STUDENT_AFFAIRS_ADMIN`, `CLUB_MANAGER`, or `CLUB_MEMBER`.

Before the teacher tests the server:

1. Create a **Web application OAuth client** in Google Cloud and add the deployed front-end origin to its authorized JavaScript origins.
2. Put that client ID in `GOOGLE_CLIENT_ID` in `.env`.
3. Replace the four e-mails below with real Google accounts controlled by the teacher/testers, then run the demo seeder. The default `@fpt.edu.vn` addresses are realistic sample roster data, not Google accounts anyone can sign into.

| Actor test path | `.env` key | Default sample e-mail |
|---|---|---|
| `ADMIN` | `DEMO_ADMIN_EMAIL` | `admin@fpt.edu.vn` |
| `STUDENT_AFFAIRS_ADMIN` (CTSV) | `DEMO_CTSV_EMAIL` | `ctsv@fpt.edu.vn` |
| `CLUB_MANAGER` | `DEMO_CLUB_MANAGER_EMAIL` | `manager.tech@fpt.edu.vn` |
| `CLUB_MEMBER` (Student) | `DEMO_STUDENT_EMAIL` | `se170002@fpt.edu.vn` |

The first successful Google sign-in links the account's immutable Google `sub` identifier to the pre-approved user row. A different Google account cannot subsequently use that row, even if an e-mail conflict is attempted. When an administrator changes a user's e-mail, the prior link and refresh tokens are revoked so the new holder must authenticate through Google again.

See [GOOGLE_SIGN_IN.md](GOOGLE_SIGN_IN.md) for the exact Google Cloud, API, and front-end handoff steps.

### Admin

| Username / allow-listed Google e-mail | Name |
|---|---|
| `DEMO_ADMIN_EMAIL` (default `admin@fpt.edu.vn`) | Nguyễn Thu Hà |

The Admin account can perform system administration, account lifecycle, and configuration management.

### Student Affairs / CTSV

| Username / allow-listed Google e-mail | Name |
|---|---|
| `DEMO_CTSV_EMAIL` (default `ctsv@fpt.edu.vn`) | Trần Thị Mai Phương |

The Student Affairs (CTSV) account performs club governance approvals (establishment, transfers, disbanding), activity/proposal approvals, and experience self-declaration verification.

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

## FPT University Club Registry (Reference Dataset - 73 Clubs)

Tổng hợp 73 câu lạc bộ/đội/nhóm sinh viên tại 3 cơ sở (Hà Nội, Hồ Chí Minh, Đà Nẵng) phân loại theo 10 nhóm hoạt động và danh mục chuẩn của ClubHub. File dữ liệu có cấu trúc JSON sẵn sàng tích hợp: [`data/fptu_clubs_73.json`](data/fptu_clubs_73.json).

### Phân bổ theo nhóm hoạt động

| Nhóm hoạt động | Số lượng | Mã danh mục API |
|---|---:|---|
| Thể thao & võ thuật | 18 | `SPORTS` |
| Nghệ thuật, âm nhạc & biểu diễn | 17 | `ARTS` |
| Công nghệ & kỹ thuật | 6 | `TECHNOLOGY` |
| Truyền thông, thiết kế & sự kiện | 6 | `ARTS` |
| Ngôn ngữ & văn hóa quốc tế | 6 | `ACADEMIC` |
| Kinh doanh, tài chính & nghề nghiệp | 4 | `ACADEMIC` |
| Học thuật, kỹ năng & phát triển cá nhân | 5 | `ACADEMIC` |
| Trò chơi & tư duy | 5 | `SPORTS` / `ACADEMIC` |
| Cộng đồng, tình nguyện & sinh viên | 4 | `VOLUNTEER` |
| Khác / chưa phân loại rõ | 2 | `OTHER` |
| **Tổng cộng** | **73** | |

### Danh sách chi tiết 73 câu lạc bộ

| STT | Tên CLB / Đội / Nhóm | Mã đề xuất | Cơ sở | Nhóm hoạt động | Danh mục API | Facebook |
|---|---|---|---|---|---|---|
| 1 | CLB Street Workout | `FU-SWO` | Hà Nội | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/FuStreetWorkout](https://www.facebook.com/FuStreetWorkout) |
| 2 | CLB iSkate | `FU-ISKT` | Hà Nội | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/FPTUISkate](https://www.facebook.com/FPTUISkate) |
| 3 | CLB Vovinam | `FU-VVIN` | Hà Nội | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/fvchn](https://www.facebook.com/fvchn) |
| 4 | Đội tuyển bóng đá | `FU-FOOT` | Hà Nội | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/fu.football.team](https://www.facebook.com/fu.football.team) |
| 5 | CLB Bóng chuyền | `FU-VOL` | Hà Nội | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/FptVolleyballClub/](https://www.facebook.com/FptVolleyballClub/) |
| 6 | CLB Cầu lông | `FU-BADM` | Hà Nội | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/FPTUbadminton/](https://www.facebook.com/FPTUbadminton/) |
| 7 | CLB Gym | `FU-GYM` | Hà Nội | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/FuGymnastic](https://www.facebook.com/FuGymnastic) |
| 8 | CLB Bóng đá Đại học FPT (FPTU.FC) | `FPTU-FC` | Hà Nội | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/FPTU.FC/](https://www.facebook.com/FPTU.FC/) |
| 9 | CLB Taekwondo | `FU-TAEK` | Hà Nội | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/FTCTaekwondo/](https://www.facebook.com/FTCTaekwondo/) |
| 10 | CLB Côn nhị khúc | `FU-NUNCH` | Hà Nội | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/nunchaku.fnc/](https://www.facebook.com/nunchaku.fnc/) |
| 11 | CLB Bóng bàn | `FU-TTEN` | Hà Nội | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/FPT.Tabletennis/](https://www.facebook.com/FPT.Tabletennis/) |
| 12 | CLB Muay Thái | `FU-MUAY` | Hà Nội | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/fmuc.fptu/](https://www.facebook.com/fmuc.fptu/) |
| 13 | FVC - FPT Vovinam Club HCM | `HCM-FVC` | Hồ Chí Minh | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/fvchcm/](https://www.facebook.com/fvchcm/) |
| 14 | FFC - Câu lạc bộ Bóng đá FPTU HCM | `HCM-FFC` | Hồ Chí Minh | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/FFC.FPTUHCM/](https://www.facebook.com/FFC.FPTUHCM/) |
| 15 | FBC - FPTU HCM Basketball Club | `HCM-FBC` | Hồ Chí Minh | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/fptuhcmbasketballclub/](https://www.facebook.com/fptuhcmbasketballclub/) |
| 16 | FVB - Câu lạc bộ Bóng chuyền FPTU HCM | `HCM-FVB` | Hồ Chí Minh | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/volleyballfpt/](https://www.facebook.com/volleyballfpt/) |
| 17 | FUDN Basketball Club | `DN-FBC` | Đà Nẵng | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/fudnbasketball](https://www.facebook.com/fudnbasketball) |
| 18 | FVC-ĐN - FU Vovinam Club Đà Nẵng | `DN-FVC` | Đà Nẵng | Thể thao & võ thuật | `SPORTS` | [https://www.facebook.com/fvcdn](https://www.facebook.com/fvcdn) |
| 19 | CLB Văn hóa nghệ thuật (Hebe) | `FU-HEBE` | Hà Nội | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/HebeFPT/](https://www.facebook.com/HebeFPT/) |
| 20 | CLB Melody | `FU-MELD` | Hà Nội | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/FptuMelodyClub](https://www.facebook.com/FptuMelodyClub) |
| 21 | CLB Guitar | `FU-GUIT` | Hà Nội | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/fuguitarclub/](https://www.facebook.com/fuguitarclub/) |
| 22 | CLB Hiphop | `FU-HIPH` | Hà Nội | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/hiphopfptuniversity](https://www.facebook.com/hiphopfptuniversity) |
| 23 | CLB Yosakoi | `FU-YOSA` | Hà Nội | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/FPTJuniorYosakoi](https://www.facebook.com/FPTJuniorYosakoi) |
| 24 | CLB Nhạc cụ truyền thống (FTIC) | `FU-FTIC` | Hà Nội | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/FTIC.FUHL](https://www.facebook.com/FTIC.FUHL) |
| 25 | CLB Điện ảnh | `FU-CINE` | Hà Nội | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/fucinemastudio/](https://www.facebook.com/fucinemastudio/) |
| 26 | Soleil Crew - CLB nhảy hiện đại | `FU-SOLE` | Hà Nội | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/soleilcrewfptu/](https://www.facebook.com/soleilcrewfptu/) |
| 27 | FTI - FPT Traditional Instruments | `HCM-FTI` | Hồ Chí Minh | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/ftihcm/](https://www.facebook.com/ftihcm/) |
| 28 | FBK - FPT Beat King Club | `HCM-FBK` | Hồ Chí Minh | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/FPTBeatKingClub/](https://www.facebook.com/FPTBeatKingClub/) |
| 29 | F# - F# Live Music Club | `HCM-FSHP` | Hồ Chí Minh | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/FsharpLiveMusicClub/](https://www.facebook.com/FsharpLiveMusicClub/) |
| 30 | FStyle - FStyle Crew | `HCM-FSTY` | Hồ Chí Minh | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/FStyleFamily/](https://www.facebook.com/FStyleFamily/) |
| 31 | MEC - Multimedia & Entertainment Club | `HCM-MEC` | Hồ Chí Minh | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/Mec.Fptuhcm/](https://www.facebook.com/Mec.Fptuhcm/) |
| 32 | FAC - FPT Art and Culture Community | `HCM-FAC` | Hồ Chí Minh | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/fac.fptuhcm/](https://www.facebook.com/fac.fptuhcm/) |
| 33 | UDC - Unity Dance Crew | `HCM-UDC` | Hồ Chí Minh | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/udcsg.fpt/](https://www.facebook.com/udcsg.fpt/) |
| 34 | FU-Dancing Club (DfP - Dance for Passion) | `DN-DFP` | Đà Nẵng | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/FUDancingClub/](https://www.facebook.com/FUDancingClub/) |
| 35 | NYS Club - CLB Nghệ thuật FUDN | `DN-NYS` | Đà Nẵng | Nghệ thuật, âm nhạc & biểu diễn | `ARTS` | [https://www.facebook.com/nysclubdn/](https://www.facebook.com/nysclubdn/) |
| 36 | F-Code | `F-CODE` | Hà Nội & Hồ Chí Minh | Công nghệ & kỹ thuật | `TECHNOLOGY` | [https://www.facebook.com/fcodefpt/](https://www.facebook.com/fcodefpt/) |
| 37 | CLB Kỹ sư phần mềm Nhật Bản (JS Club) | `FU-JSCL` | Hà Nội | Công nghệ & kỹ thuật | `TECHNOLOGY` | [https://www.facebook.com/fu.jsclub](https://www.facebook.com/fu.jsclub) |
| 38 | CLB AI | `FU-AICL` | Hà Nội | Công nghệ & kỹ thuật | `TECHNOLOGY` | [https://www.facebook.com/aiclub.fptu/](https://www.facebook.com/aiclub.fptu/) |
| 39 | FIA - FPT Information Assurance Club | `HCM-FIA` | Hồ Chí Minh | Công nghệ & kỹ thuật | `TECHNOLOGY` | [https://www.facebook.com/fptinformationassurance/](https://www.facebook.com/fptinformationassurance/) |
| 40 | HNF - Hardware Network FPTU | `HCM-HNF` | Hồ Chí Minh | Công nghệ & kỹ thuật | `TECHNOLOGY` | [https://www.facebook.com/hnffptu/](https://www.facebook.com/hnffptu/) |
| 41 | FU-Dever - CLB Lập trình | `DN-DEVR` | Đà Nẵng | Công nghệ & kỹ thuật | `TECHNOLOGY` | [https://www.facebook.com/FPTUDever/](https://www.facebook.com/FPTUDever/) |
| 42 | CLB Design | `FU-DESG` | Hà Nội | Truyền thông, thiết kế & sự kiện | `ARTS` | [https://www.facebook.com/fudesigners](https://www.facebook.com/fudesigners) |
| 43 | CLB Photography | `FU-PHOT` | Hà Nội | Truyền thông, thiết kế & sự kiện | `ARTS` | [https://www.facebook.com/FUphotography.club](https://www.facebook.com/FUphotography.club) |
| 44 | FEV - FPT Event Club | `HCM-FEV` | Hồ Chí Minh | Truyền thông, thiết kế & sự kiện | `ARTS` | [https://www.facebook.com/FPTEventClub/](https://www.facebook.com/FPTEventClub/) |
| 45 | CSG - CLB Truyền thông Cóc Sài Gòn | `HCM-CSG` | Hồ Chí Minh | Truyền thông, thiết kế & sự kiện | `ARTS` | [https://www.facebook.com/cocsaigonfuhcm/](https://www.facebook.com/cocsaigonfuhcm/) |
| 46 | FUM - FPT University Media | `DN-FUM` | Đà Nẵng | Truyền thông, thiết kế & sự kiện | `ARTS` | [https://www.facebook.com/FUMedia/](https://www.facebook.com/FUMedia/) |
| 47 | EVo - CLB Tổ chức sự kiện | `DN-EVO` | Đà Nẵng | Truyền thông, thiết kế & sự kiện | `ARTS` | [https://www.facebook.com/FPTUDNEventClub/](https://www.facebook.com/FPTUDNEventClub/) |
| 48 | CLB Tiếng Trung | `FU-CHIN` | Hà Nội | Ngôn ngữ & văn hóa quốc tế | `ACADEMIC` | [https://www.facebook.com/tiengtrungFPT/](https://www.facebook.com/tiengtrungFPT/) |
| 49 | Mây Mưa Club - CLB yêu thích Nhật Bản | `FU-MAYM` | Hà Nội | Ngôn ngữ & văn hóa quốc tế | `ACADEMIC` | [https://www.facebook.com/maymuaclub/](https://www.facebook.com/maymuaclub/) |
| 50 | CLB Tiếng Anh | `FU-ENGC` | Hà Nội | Ngôn ngữ & văn hóa quốc tế | `ACADEMIC` | [https://www.facebook.com/englishclub.fu/](https://www.facebook.com/englishclub.fu/) |
| 51 | CLB Dango (anime, manga, tokusatsu...) | `FU-DANG` | Hà Nội | Ngôn ngữ & văn hóa quốc tế | `ACADEMIC` | [https://www.facebook.com/groups/dango.clb/](https://www.facebook.com/groups/dango.clb/) |
| 52 | JSC - CLB Phong Cách Nhật Bản | `HCM-JSC` | Hồ Chí Minh | Ngôn ngữ & văn hóa quốc tế | `ACADEMIC` | [https://www.facebook.com/clbJSC/](https://www.facebook.com/clbJSC/) |
| 53 | Eflame - Câu lạc bộ Tiếng Anh | `DN-EFLM` | Đà Nẵng | Ngôn ngữ & văn hóa quốc tế | `ACADEMIC` | [https://www.facebook.com/EngCF/](https://www.facebook.com/EngCF/) |
| 54 | CLB Business | `FU-BUSN` | Hà Nội | Kinh doanh, tài chính & nghề nghiệp | `ACADEMIC` | [https://www.facebook.com/FU.Business/](https://www.facebook.com/FU.Business/) |
| 55 | CLB Chứng khoán (FSIC) | `FU-FSIC` | Hà Nội | Kinh doanh, tài chính & nghề nghiệp | `ACADEMIC` | [https://www.facebook.com/FSIC-FPT-Securities-Investment-Cl](https://www.facebook.com/FSIC-FPT-Securities-Investment-Cl) |
| 56 | BEC - Business Economics Club | `HCM-BEC` | Hồ Chí Minh | Kinh doanh, tài chính & nghề nghiệp | `ACADEMIC` | [https://www.facebook.com/bec.fptuhcm/](https://www.facebook.com/bec.fptuhcm/) |
| 57 | Skillcetera | `HCM-SKIL` | Hồ Chí Minh | Kinh doanh, tài chính & nghề nghiệp | `ACADEMIC` | [https://www.facebook.com/skillcetera/](https://www.facebook.com/skillcetera/) |
| 58 | CLB No Shy | `FU-NOSH` | Hà Nội | Học thuật, kỹ năng & phát triển cá nhân | `ACADEMIC` | [https://www.facebook.com/noshyclub/](https://www.facebook.com/noshyclub/) |
| 59 | CLB Sách | `FU-BOOK` | Hà Nội | Học thuật, kỹ năng & phát triển cá nhân | `ACADEMIC` | [https://www.facebook.com/fu.bukclub/](https://www.facebook.com/fu.bukclub/) |
| 60 | FU Debate Club - CLB Tranh biện | `FU-DEBT` | Hà Nội | Học thuật, kỹ năng & phát triển cá nhân | `ACADEMIC` | [https://www.facebook.com/FUDebateClub/](https://www.facebook.com/FUDebateClub/) |
| 61 | CLB Tâm lý học | `FU-PSYC` | Hà Nội | Học thuật, kỹ năng & phát triển cá nhân | `ACADEMIC` | [https://www.facebook.com/FPT.Psy/](https://www.facebook.com/FPT.Psy/) |
| 62 | FPS - FPTU Public Speaking | `HCM-FPS` | Hồ Chí Minh | Học thuật, kỹ năng & phát triển cá nhân | `ACADEMIC` | [https://www.facebook.com/FPTUPublicSpeakingClub/](https://www.facebook.com/FPTUPublicSpeakingClub/) |
| 63 | CLB Esports | `FU-ESPT` | Hà Nội | Trò chơi & tư duy | `SPORTS` | [https://www.facebook.com/ESCFUHL](https://www.facebook.com/ESCFUHL) |
| 64 | CLB Cờ vây | `FU-GOCL` | Hà Nội | Trò chơi & tư duy | `SPORTS` | [https://www.facebook.com/fptgoclub/](https://www.facebook.com/fptgoclub/) |
| 65 | CLB Cờ (Chess) | `FU-CHES` | Hà Nội | Trò chơi & tư duy | `SPORTS` | [https://www.facebook.com/FPTUChessClub/](https://www.facebook.com/FPTUChessClub/) |
| 66 | FCC - FPT Chess Club | `HCM-FCC` | Hồ Chí Minh | Trò chơi & tư duy | `SPORTS` | [https://www.facebook.com/FptChessClubFcc/](https://www.facebook.com/FptChessClubFcc/) |
| 67 | FBG - FPT Boardgame Club | `HCM-FBG` | Hồ Chí Minh | Trò chơi & tư duy | `ACADEMIC` | [https://www.facebook.com/fptboardgameclu](https://www.facebook.com/fptboardgameclu) |
| 68 | CLB Tình nguyện vì cộng đồng iGo | `FU-IGO` | Hà Nội | Cộng đồng, tình nguyện & sinh viên | `VOLUNTEER` | [https://www.facebook.com/iGoClub/](https://www.facebook.com/iGoClub/) |
| 69 | Quỹ Vỏ Chai | `FU-QVC` | Hà Nội | Cộng đồng, tình nguyện & sinh viên | `VOLUNTEER` | [https://www.facebook.com/qvcfpt/](https://www.facebook.com/qvcfpt/) |
| 70 | Hội Sinh viên | `FU-STUA` | Hà Nội | Cộng đồng, tình nguyện & sinh viên | `VOLUNTEER` | [https://www.facebook.com/hoisinhvien.fu](https://www.facebook.com/hoisinhvien.fu) |
| 71 | SitiGroup - Cộng đồng Sinh viên Tình nguyện | `HCM-SITI` | Hồ Chí Minh | Cộng đồng, tình nguyện & sinh viên | `VOLUNTEER` | [https://www.facebook.com/sititgroupfuhcm/](https://www.facebook.com/sititgroupfuhcm/) |
| 72 | Unicamp | `FU-UNIC` | Hà Nội | Khác / chưa phân loại rõ | `OTHER` | [https://www.facebook.com/unicamp.hl](https://www.facebook.com/unicamp.hl) |
| 73 | Color Team | `FU-COLR` | Hà Nội | Khác / chưa phân loại rõ | `OTHER` | [https://www.facebook.com/colorteamvn/](https://www.facebook.com/colorteamvn/) |
