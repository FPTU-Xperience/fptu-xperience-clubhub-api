# KẾ HOẠCH TỔNG THỂ SỬA CHỮA & TÁI CẤU TRÚC FPTU-XPERIENCE CLUBHUB API

> **REMEDIATION MASTER PLAN — PLANNING ONLY**  
> Cơ sở: `CLUBHUB_AUDIT.md` — audit toàn bộ hệ thống qua Phase 0–9.  
> Mục tiêu: sửa các vấn đề có ảnh hưởng thực sự, giữ kiến trúc hiện tại khi hợp lý, cải thiện bảo mật, data integrity, reliability, performance, testability và deployment mà không over-engineer đồ án tốt nghiệp.  
> Phạm vi của tài liệu này: **chỉ lập kế hoạch, không sửa code, không tạo migration, không thay đổi cấu hình và không triển khai thay đổi**.

---

# PHẦN I — ĐÁNH GIÁ HIỆN TRẠNG VÀ ĐỊNH HƯỚNG SỬA CHỮA

## 1. Tổng quan hiện trạng

Bản audit đã hoàn thành từ Phase 0 đến Phase 9, bao gồm:

- Repository discovery.
- Architecture & dependency audit.
- Code quality & business logic.
- Authentication & authorization.
- Complete API endpoint security audit.
- Database, query & performance.
- Application security beyond authorization.
- Runtime performance, scalability & reliability.
- Testing, CI/CD & deployment.
- Final consolidated engineering audit.

Theo Phase 9, sau khi chuẩn hóa và loại bỏ các finding trùng lặp, hệ thống có:

- 58 confirmed findings.
- 19 potential/unverified findings.

Các finding confirmed được phân loại theo mức độ:

- 3 CRITICAL.
- 15 HIGH.
- 29 MEDIUM.
- 10 LOW.
- 1 INFO.

19 mục potential/unverified không được mặc định xem là bug thật cho tới khi được tái xác minh trong lúc remediation.

---

## 2. Kiến trúc hiện tại nên được giữ lại

Stack chính:

| Thành phần | Công nghệ |
|---|---|
| Backend | ASP.NET Core 8 Minimal APIs |
| API Gateway | YARP |
| Authentication | JWT + Google Sign-in |
| Database | SQL Server 2022 |
| ORM | Entity Framework Core 8 |
| Messaging | Redis Streams |
| Background jobs | Hangfire |
| Internal RPC | gRPC |
| Deployment | Docker Compose |
| CI/CD | GitHub Actions |

Kiến trúc hiện tại là service-oriented, chia theo vertical slice/service với database-per-service.

Định hướng remediation:

- Không chuyển toàn bộ hệ thống sang Clean Architecture.
- Không rewrite thành monolith.
- Không chia thêm microservices nếu không có lợi ích rõ ràng.
- Không thay Redis Streams, Hangfire, EF Core, YARP chỉ để “đẹp kiến trúc”.
- Không đưa thêm Kafka, RabbitMQ, Kubernetes, service mesh hoặc công nghệ enterprise nếu không thật sự cần.
### 2.1. Định hướng phạm vi đồ án tốt nghiệp (Graduation Project Scope Policy)
Để tối ưu hóa thời gian, tài nguyên và đảm bảo tính khả thi cao nhất cho một đồ án tốt nghiệp cấp đại học (moderate scope):
- **Ưu tiên trọng tâm cốt lõi:**
  - Giữ vững và khắc phục triệt để các lỗi bảo mật nghiêm trọng (Critical/High Security: injection, bypass auth, arbitrary file read, token freshness, rate limiting).
  - Đảm bảo tính toàn vẹn dữ liệu và tính đúng đắn của nghiệp vụ (Data Integrity, Invariants, Transactions, Concurrency token, Filtered Unique Indexes).
  - Đảm bảo tính bền vững của các luồng sự kiện chính (Reliability: Redis Stream bounded capacity, PEL consumer recovery, Transactional Outbox).
- **Trì hoãn có chủ đích (Explicitly Deferred Enterprise Scope):**
  - Trì hoãn việc đập đi xây lại toàn bộ kiến trúc phân tán (Distributed architecture rewrite / Clean Architecture / Event Sourcing phức tạp) khi kiến trúc Vertical Slice/Minimal APIs hiện tại vẫn đáp ứng tốt.
  - Trì hoãn tích hợp các hệ thống quản lý bí mật doanh nghiệp nâng cao (HashiCorp Vault, AWS/Azure Secret Manager); sử dụng biến môi trường chuẩn Docker/file `.env` bảo mật cục bộ.
  - Trì hoãn thiết lập pipeline quét bảo mật enterprise tĩnh/động toàn diện (Full SAST / DAST / Container image scanning nâng cao); tập trung quét phụ thuộc cơ bản bằng CLI (`dotnet list package --vulnerable`).
  - Trì hoãn các bài đo tải quy mô lớn (High-load / Stress / Soak benchmarks hàng triệu người dùng); chỉ tập trung vào kiểm thử tích hợp, kiểm thử hồi quy và race-condition concurrency tests cho các ca nghiệp vụ then chốt.
- **Tiến độ thực hiện hiện tại:**
  - Hệ thống đã hoàn tất thành công **Phase 00 đến Phase 12** (toàn bộ Wave 0, Wave 1, Wave 2 và các phase nền tảng dữ liệu của Wave 3).
  - **Phase tiếp theo đang sẵn sàng thực hiện: Phase 13 (Report Workflow Atomicity & Outbox Publishing Concurrency: DATA-F03, REL-F02).**

---

# PHẦN II — CHIA TOÀN BỘ CÔNG VIỆC THÀNH CÁC PHASE

Kế hoạch remediation mới chia thành **21 phase**, từ Phase 00 đến Phase 20.

Mỗi phase được thiết kế đủ nhỏ để AI coding tool có thể xử lý trong một context riêng mà không phải load lại toàn bộ audit và repository.

## WAVE 0 — Preparation & Safety Net

- Phase 00 — Repository Baseline & Remediation Management

## WAVE 1 — Critical Security & Availability

- Phase 01 — Authentication Bypass & Development Login
- Phase 02 — Report Attachment Security Reconstruction
- Phase 03 — Redis Stream Retention & Capacity Control
- Phase 04 — Notification Consumer & Failed Message Recovery
- Phase 05 — API Gateway Security & Rate Limiting

## WAVE 2 — Authorization & Business Integrity

- Phase 06 — Cross-Service Workflow Authorization
- Phase 07 — JWT Authorization Freshness
- Phase 08 — Refresh Token Lifecycle
- Phase 09 — Club Resource Authorization & Member Statistics

## WAVE 3 — Database & Reliability Reconstruction

- Phase 10 — Database Lifecycle Standardization
- Phase 11 — ClubService Data Integrity
- Phase 12 — Finance & Report Database Invariants
- Phase 13 — Report Transaction & Shared Outbox Reliability
- Phase 14 — Club, Activity & Export Event Delivery

## WAVE 4 — Performance & Code Quality

- Phase 15 — Document Processing & Export Pipeline
- Phase 16 — Database Query Optimization
- Phase 17 — API Correctness, Observability & Logging

## WAVE 5 — Deployment, Testing & Final Integration

- Phase 18 — Docker, Configuration & Operational Hardening
- Phase 19 — Comprehensive Testing & CI/CD Hardening
- Phase 20 — Selective Architecture Refactoring & Final Acceptance

---

# WAVE 0 — PREPARATION & SAFETY NET

# PHASE 00 — Repository Baseline & Remediation Management

## Mục tiêu

Thiết lập baseline trước khi sửa để mọi AI tool biết trạng thái thật của repository, không ghi đè thay đổi hiện có và có thể tiếp tục công việc qua nhiều context/session.

## 00.1. Xác minh repository hiện tại

Snapshot audit:

```text
Branch: develop
Commit: 33bf7fe
```

Trước khi sửa phải kiểm tra lại:

- Branch hiện tại.
- Commit hiện tại.
- Working tree.
- File tracked/untracked.
- Thay đổi phát sinh sau audit.
- Thay đổi Docker.
- Thay đổi migrations.
- Thay đổi configuration.
- Finding nào đã được sửa từ sau audit.

Phải bảo vệ các thay đổi đã tồn tại trong audit:

```text
M  AGENTS.md
D  GEMINI.md
D  scripts/run-gemini-task.ps1
?? docs/audit/
```

Không tự ý restore, delete hoặc overwrite các thay đổi này.

## 00.2. Thiết lập baseline

Chạy các kiểm tra phù hợp:

```powershell
dotnet --info

dotnet build ClubReportHub.sln -c Release -warnaserror

dotnet test ClubReportHub.sln -c Release

docker compose config --quiet
```

Nếu Docker/SQL Server/Redis chạy được thì xác minh môi trường integration riêng trước khi đụng tới database migration hoặc concurrency.

Audit lịch sử ghi nhận:

- 50 test pass trong solution.
- 39 test pass trong project riêng `ClubReportHub.Tests`.
- Tổng 89 test.

Không được lấy con số này làm baseline mới nếu chưa chạy lại trên repository hiện tại.

## 00.3. Thiết lập cấu trúc quản lý remediation

Đề xuất:

```text
docs/
└── remediation/
    ├── MASTER_PLAN.md
    ├── FINDINGS_LEDGER.md
    ├── DECISIONS.md
    ├── TEST_MATRIX.md
    ├── API_CONTRACT_CHANGES.md
    ├── DATABASE_MIGRATION_REGISTER.md
    ├── CHECKPOINT.md
    └── phases/
        ├── PHASE_00.md
        ├── PHASE_01.md
        ├── ...
        └── PHASE_20.md
```

Vai trò:

| File | Nội dung |
|---|---|
| MASTER_PLAN.md | Kế hoạch tổng thể |
| FINDINGS_LEDGER.md | Mapping toàn bộ findings |
| DECISIONS.md | Quyết định kiến trúc/nghiệp vụ |
| TEST_MATRIX.md | Finding → regression test |
| API_CONTRACT_CHANGES.md | Thay đổi ảnh hưởng frontend |
| DATABASE_MIGRATION_REGISTER.md | Migration/rollback/data safety |
| CHECKPOINT.md | Trạng thái ngắn để AI khác tiếp tục |
| PHASE_XX.md | Báo cáo từng phase |

## 00.4. Quy tắc test

Phân loại:

### Unit test

Kiểm tra business rules độc lập.

### Integration test

Kiểm tra:

- Endpoint.
- Authentication.
- Authorization.
- Database.
- Transaction.
- Service integration.

### End-to-end test

Kiểm tra workflow xuyên nhiều service qua Gateway.

Đối với:

- Concurrency.
- Filtered unique indexes.
- SQL Server locking/transaction.

Không được chỉ test bằng EF InMemory hoặc SQLite rồi kết luận production database an toàn.

## Điều kiện hoàn thành Phase 00

- Baseline repo được xác minh.
- Không mất thay đổi hiện có.
- Có findings ledger.
- Có test matrix.
- Có checkpoint format.
- Có DB test environment rõ ràng.
- Có quy tắc cho các AI tool tiếp tục qua session khác.

---

# WAVE 1 — CRITICAL SECURITY & AVAILABILITY

# PHASE 01 — Authentication Bypass & Development Login

**Mức ưu tiên:** CRITICAL  
**Services:** AuthService, ApiGateway

## Mục tiêu

Loại bỏ khả năng sử dụng development login để giả danh người dùng trong production.

## Vấn đề

Audit xác định endpoint:

```http
POST /api/auth/dev-login
```

có thể được đăng ký anonymous và phát hành access/refresh token chỉ dựa vào email trong điều kiện production hiện tại.

## Công việc

### 01.1. Chuẩn hóa authentication contract

Tách rõ:

- Google Sign-in.
- Development login.
- Test login.
- Refresh token.
- Logout.

### 01.2. Disable dev login trong Production

Mục tiêu:

```text
Production
  Google Sign-in: enabled
  Dev login: disabled
  Test login: disabled

Development / Test
  Dev login: configurable
```

Không để một env flag tùy ý bật email-only login trên production.

### 01.3. Chuẩn hóa feature flag

Thống nhất key, ví dụ:

```text
Auth:EnableDevLogin
```

Kiểm tra:

- `appsettings*.json`
- Docker Compose environment.
- `.env.example`
- Tests.
- Production code.

Xóa hoặc sửa dead configuration chỉ sau khi xác nhận dependency.

### 01.4. Kiểm tra Gateway

Không để bảo vệ chỉ phụ thuộc một tầng.

Kiểm tra:

- Gateway route policy.
- Service endpoint registration.
- Production environment guard.

### 01.5. Xử lý nhu cầu demo

Nếu đồ án cần demo nhanh:

- Production/deployed environment: Google Sign-in.
- Development/local demo: dev login được bật có chủ đích.

Không dùng dev login như production login thật.

## Regression tests

- Production không cấp token qua dev-login.
- Development bật/tắt đúng.
- Google Sign-in còn hoạt động.
- Refresh token còn hoạt động.
- Disabled/locked account không được dùng dev login.
- Placeholder JWT signing key không được dùng trong production.

## Exit criteria

Không thể lấy production access token chỉ bằng email.

---

# PHASE 02 — Report Attachment Security Reconstruction

**Mức ưu tiên:** CRITICAL  
**Service:** ReportService

## Mục tiêu

Loại bỏ path traversal / arbitrary file read thông qua attachment metadata.

## Công việc

### 02.1. Thiết kế lại attachment metadata

Client không được quyết định physical path.

Client chỉ nên cung cấp dữ liệu như:

```text
ReportId
FileName
ContentType
FileSize
```

Server quyết định storage identifier/path.

### 02.2. Server sở hữu storage path

Luồng:

```text
Configured Storage Root
        ↓
Generated File ID
        ↓
Validated Server Path
        ↓
Database Metadata
```

### 02.3. Path containment validation

Mọi thao tác:

- Save.
- Download.
- Preview.
- Delete.
- Cleanup.

đều phải đảm bảo path nằm dưới storage root.

### 02.4. Audit dữ liệu attachment hiện có

Kiểm tra:

- Absolute path.
- Relative path lạ.
- Path ngoài root.
- Missing files.
- Duplicate files.
- Invalid metadata.

Không tự động xóa dữ liệu trước khi phân loại.

### 02.5. Filename sanitization

Đảm bảo behavior nhất quán trên Windows và Linux.

### 02.6. Content-type / extension validation

Chỉ hỗ trợ các định dạng thật sự cần cho nghiệp vụ.

Không cần antivirus enterprise nếu phạm vi đồ án không yêu cầu.

## Regression tests

- `../` traversal.
- Absolute path.
- Windows path.
- Linux path.
- Path outside root.
- Invalid filename.
- Unauthorized attachment download.
- Valid upload/download.
- Response không lộ physical storage path.

## Exit criteria

Client không thể điều khiển server đọc file tùy ý.

---

# PHASE 03 — Redis Stream Retention & Capacity Control

**Mức ưu tiên:** CRITICAL  
**Scope:** Shared Messaging, Redis, Docker Compose

## Mục tiêu

Ngăn Redis Streams tăng vô hạn và làm Redis hết memory.

## Công việc

### 03.1. Xác định messaging topology

Lập inventory:

- Stream names.
- Producers.
- Consumer groups.
- Pending entries.
- DLQ.
- Retry behavior.
- Idempotency strategy.

### 03.2. Thiết lập retention

Có thể dùng bounded stream / MAXLEN nhưng phải tính tới:

- Consumer downtime.
- Pending messages.
- Burst traffic.
- Notification backlog.

Không đặt MAXLEN tùy ý.

### 03.3. Metrics

Theo dõi:

```text
Stream length
Redis memory usage
Pending messages
Oldest pending age
Consumer lag
Publish failures
DLQ length
```

### 03.4. Recovery procedure

Xác định xử lý khi:

- Redis gần đầy.
- Redis noeviction.
- Publish fail.
- Consumer down dài.

## Validation

- Dừng consumer.
- Publish số lượng lớn.
- Kiểm tra stream growth.
- Khởi động consumer.
- Đảm bảo message cần thiết không bị trim quá sớm.

## Exit criteria

Redis Streams có retention strategy và không còn growth vô hạn.

---

# PHASE 04 — Notification Consumer & Failed Message Recovery

**Mức ưu tiên:** HIGH  
**Service:** NotificationService

## Mục tiêu

Đảm bảo pending/failed Redis messages được retry hoặc đưa vào DLQ.

## Công việc

### 04.1. Startup recovery

Phân biệt:

- New messages.
- Pending messages của consumer hiện tại.
- Abandoned messages từ consumer cũ.
- Retry-exhausted messages.

### 04.2. Retry flow

```text
Receive
  ↓
Check ProcessedEvents
  ↓
Already processed?
  ├─ Yes → ACK
  └─ No
       ↓
     Process
       ↓
     Success?
       ├─ Yes → persist → ACK
       └─ No
            ↓
         Retry limit?
            ├─ No → pending/reclaim
            └─ Yes → DLQ
```

### 04.3. Idempotency

Giữ `ProcessedEvents` hoặc cơ chế tương đương.

Không để restart tạo duplicate notification.

### 04.4. DLQ

Phải có:

- Event data tối thiểu.
- Failure reason.
- Retry count.
- Timestamp.
- Correlation ID nếu có.

## Regression tests

- Consumer restart.
- Failure before ACK.
- Abandoned pending message.
- Duplicate event.
- Retry exhaustion.
- DLQ.
- Redis restart.

## Exit criteria

Không còn pending message bị kẹt vô thời hạn mà không có recovery.

---

# PHASE 05 — API Gateway Security & Rate Limiting

**Mức ưu tiên:** HIGH  
**Services:** ApiGateway, Shared Auth/CORS

## Mục tiêu

Thêm rate limiting và hardening cho ingress.

## Công việc

### 05.1. Rate limiting

Phân nhóm:

| Endpoint | Policy |
|---|---|
| Login | chặt |
| Refresh token | chặt |
| Upload | theo user/cost |
| Export | giới hạn số request/job |
| Standard APIs | moderate |
| Health | không để limit gây false-negative |

### 05.2. Trusted proxy / client IP

Chỉ trust forwarded headers từ proxy thực sự đáng tin.

Không cho client giả `X-Forwarded-For` để né limit.

### 05.3. 429 response

Chuẩn hóa:

```http
429 Too Many Requests
```

cùng retry metadata phù hợp.

### 05.4. Header forwarding

Xác minh internal workflow headers không thể bị dùng làm security boundary từ public client.

## Tests

- Same user burst.
- Multiple clients.
- Forged forwarded IP.
- Login rate limit.
- Export rate limit.
- Health check.
- Gateway route authorization.

## Exit criteria

Gateway có rate limiting hợp lý và không trust client-controlled security headers.

---

# WAVE 2 — AUTHORIZATION & BUSINESS INTEGRITY

# PHASE 06 — Cross-Service Workflow Authorization

**Mức ưu tiên:** HIGH  
**Services:** ReportService, FinanceService, ActivityService, ExportService

## Mục tiêu

Không để client-controlled request data/header trở thành bằng chứng của internal workflow hoặc ownership.

## Phần A — Report ↔ Finance

Vấn đề điển hình:

```http
X-Combined-Report-Workflow: true
```

không được coi là bằng chứng đủ tin cậy rằng request đến từ workflow nội bộ.

### Công việc

1. Trace toàn bộ call sites.
2. Vẽ report/finance state machine.
3. Xác định source of truth từng field.
4. Không trust public header để bypass rule.
5. Revalidate workflow state.
6. Revalidate relationship giữa Report và BudgetProposal.

Quan hệ cần xác minh:

```text
Report.ClubId == BudgetProposal.ClubId
```

và:

```text
BudgetProposal.SourceReportId == Report.Id
```

## Phần B — Approved Report → Activity

Đối với:

```http
POST /api/activities/from-approved-report
```

kiểm tra:

- Report thật sự Approved.
- ClubId match.
- DetailId thuộc Report.
- Không tạo duplicate Activity nếu nghiệp vụ không cho phép.

## Phần C — Service URL configuration

Sửa các vấn đề:

- ReportService → FinanceService.
- ReportService → ActivityService.
- ExportService → ReportService.

Production không fallback localhost cho dependency bắt buộc.

## Regression tests

- Cross-club linking bị reject.
- Forged workflow header không bypass.
- Approved report happy path.
- Invalid report state.
- Duplicate activity creation.
- Docker service URLs.
- Local dev service URLs.

## Exit criteria

Cross-service workflow dựa trên server-trusted state thay vì client-controlled metadata.

---

# PHASE 07 — JWT Authorization Freshness

**Mức ưu tiên:** HIGH  
**Services:** AuthService, Shared Auth, Gateway

## Mục tiêu

Role/account state thay đổi phải có hiệu lực đủ nhanh đối với access token đang tồn tại.

## Vấn đề

JWT có thể giữ:

- Role cũ.
- Club privilege cũ.
- Active account state cũ.

sau khi user bị:

- Lock.
- Deactivate.
- Demote.
- Remove quyền.

## Hướng thiết kế

Không cần stateful session enterprise quá phức tạp.

Có thể dùng kết hợp:

- Access token lifetime ngắn hơn.
- Account/security version.
- Critical operation revalidation.
- Small TTL cache.
- Invalidation khi role/account thay đổi.

Chỉ rút token lifetime không đủ nếu yêu cầu thu hồi ngay.

## Công việc

1. Xác định account/security version model.
2. Update token claims.
3. Update token validation.
4. Update account mutation flows.
5. Xác định privileged operations cần revalidate.
6. Test behavior khi AuthService unavailable.

## Regression tests

- Lock account sau khi issue JWT.
- Deactivate.
- Change role.
- Remove membership.
- Refresh token sau lock.
- Google login sau deactivate.
- Token security version mismatch.

## Exit criteria

Stale privilege không tồn tại lâu ngoài policy được định nghĩa.

---

# PHASE 08 — Refresh Token Lifecycle

**Mức ưu tiên:** MEDIUM/HIGH  
**Service:** AuthService

## Mục tiêu

Refresh token không lưu raw, rotation atomic, replay được xử lý đúng.

## Công việc

### 08.1. Hash refresh token

DB lưu hash thay vì raw secret.

### 08.2. Atomic rotation

Một refresh token chỉ được dùng thành công một lần.

### 08.3. Replay handling

Phát hiện reuse.

### 08.4. Token family ownership

Không cho user thao tác token family không thuộc session của mình.

### 08.5. Migration strategy

Lựa chọn:

- Invalidate toàn bộ legacy refresh token và yêu cầu login lại.
- Hoặc migration hai cơ chế nếu thật sự cần.

Đối với đồ án, invalidate + re-login thường đơn giản và an toàn hơn.

## Regression tests

- Concurrent refresh.
- Replay.
- Revoke.
- Logout.
- Cross-user token.
- Raw token không xuất hiện trong DB.

## Exit criteria

Refresh token có rotation, replay protection và không stored plaintext.

---

# PHASE 09 — Club Resource Authorization & Member Statistics

**Mức ưu tiên:** HIGH  
**Services:** ClubService, ActivityService, Shared

## Phần A — ClubAccess cache

### Mục tiêu

Quyền Club không stale quá lâu sau membership/role changes.

### Công việc

- Inventory toàn bộ ClubAccessClient usage.
- Chuẩn hóa cache key.
- Negative caching với TTL hợp lý.
- Cache stampede protection.
- Invalidation khi membership/role thay đổi.
- Revalidation cho privileged mutation.

Không chỉ giảm TTL rồi coi như đã giải quyết triệt để.

## Phần B — Member statistics

Các endpoint cần xem lại:

```http
POST /api/activities/clubs/{clubId}/member-statistics
POST /api/activities/clubs/{clubId}/member-statistics/detail
```

Không trust caller-supplied:

- UserId.
- JoinedAtUtc.
- Membership data.

ActivityService phải xác nhận membership từ source of truth phù hợp.

## Regression tests

- Manager club A không query user ngoài quyền.
- Removed member mất quyền.
- Treasurer demotion.
- Fake JoinedAtUtc.
- Cache invalidation.
- High concurrent cache miss.

## Exit criteria

Club-scoped authorization luôn bám theo membership state đáng tin cậy.

---

# WAVE 3 — DATABASE & RELIABILITY RECONSTRUCTION

# PHASE 10 — Database Lifecycle Standardization

**Mức ưu tiên:** FOUNDATION  
**Services:** mọi service có database

## Mục tiêu

Chuẩn hóa schema lifecycle.

Hiện trạng pha trộn:

```text
EF Migrations
EnsureCreated()
Raw SQL schema upgraders
```

## Định hướng

Dùng EF Core Migrations làm cơ chế versioning chính cho database service.

Nhưng không được:

- Xóa `EnsureCreated` tùy tiện.
- Tạo Initial Migration rồi chạy trên DB production hiện có.
- Assume schema hiện tại match model.

## Theo service

| Service | Việc cần làm |
|---|---|
| Auth | Verify migration history |
| Club | Verify schema + constraints |
| Report | Reconcile migrations/raw upgrader |
| Finance | Baseline migration strategy |
| Activity | Baseline migration strategy |
| Notification | Baseline migration strategy |
| Export | Verify migrations |
| Admin | Verify migrations |

## Quy trình

```text
Inspect Current Schema
    ↓
Compare EF Model
    ↓
Inspect Migration History
    ↓
Identify Drift
    ↓
Define Baseline
    ↓
Create Migration
    ↓
Test Fresh DB
    ↓
Test Existing DB
    ↓
Verify Data Preservation
```

## Rollback/forward-fix

Mỗi migration phase phải ghi:

- Preflight.
- Backup.
- Migration.
- Post-check.
- Rollback nếu an toàn.
- Forward-fix nếu rollback nguy hiểm.

## Exit criteria

Fresh DB và upgrade DB đều tạo đúng schema mà không mất dữ liệu.

---

# PHASE 11 — ClubService Data Integrity

**Mức ưu tiên:** HIGH  
**Service:** ClubService

## 11.1. Treasurer maximum

Business rule:

```text
Max 2 active treasurers per club
```

Hiện read-check-write có race.

Cần thiết kế:

- Transaction/locking.
- Hoặc data model có DB-enforced invariant.

Một unique index đơn giản không tự enforce “maximum 2”.

## 11.2. Active manager uniqueness

Xác minh business rule:

```text
One active manager assignment per manager
```

Nếu đúng, tạo database-safe enforcement phù hợp.

## 11.3. Disband request

Không nhiều pending disband requests cùng club.

## 11.4. Ownership transfer

Không nhiều pending ownership transfers cùng club.

## 11.5. Conflict normalization

Expected constraint conflict → 409 Conflict.

Không để predictable integrity conflict thành 500.

## Tests

SQL Server thật:

- Concurrent treasurer assignment.
- Concurrent manager assignment.
- Concurrent disband.
- Concurrent ownership transfer.

## Exit criteria

Business invariant không thể bị phá bằng concurrent requests.

---

# PHASE 12 — Finance & Report Database Invariants

**Mức ưu tiên:** HIGH  
**Services:** FinanceService, ReportService

## Phần A — Active settlement uniqueness

Bảo vệ:

```text
One active settlement per proposal
```

Xác định chính xác trạng thái active.

Đảm bảo duplicate settlement không kéo theo duplicate financial transaction.

## Phần B — Report uniqueness

Xác minh rule:

```text
ClubId + Period + Tag
```

Xác định report types nào được unique, loại nào được phép nhiều.

Dùng filtered unique index nếu phù hợp.

## Phần C — Existing data cleanup

Trước migration:

- Detect duplicate reports.
- Detect duplicate active settlements.
- Generate remediation report.
- Không auto-delete.

## Tests

- Concurrent create settlement.
- Concurrent create report.
- Existing duplicate dataset.
- 409 normalization.

## Exit criteria

Uniqueness/integrity được bảo vệ ở DB, không chỉ bằng application pre-check.

---

# PHASE 13 — Report Transaction & Shared Outbox Reliability

**Mức ưu tiên:** MEDIUM/HIGH  
**Services:** ReportService, Shared

## Mục tiêu

Report state + audit log + outbox phải atomic khi cùng database.

## Công việc

### 13.1. Transaction boundaries

Áp dụng cho:

- Create.
- Update.
- Submit.
- Review.
- Approve.
- Reject.
- Finance-linking.

### 13.2. Outbox claim

Không để hai publisher đồng thời claim cùng message.

### 13.3. Retry/idempotency

At-least-once delivery cần consumer idempotent.

### 13.4. Failure injection

Test lỗi:

- Before transaction commit.
- SaveChanges fail.
- After commit.
- Redis unavailable.
- Publish success nhưng mark-sent fail.

## Exit criteria

Không tồn tại trạng thái Report updated nhưng audit/outbox bị mất do save sequence.

---

# PHASE 14 — Club, Activity & Export Event Delivery

**Mức ưu tiên:** MEDIUM  
**Services:** ClubService, ActivityService, ExportService

## Mục tiêu

Loại bỏ DB-write + direct-publish failure windows ở các luồng quan trọng.

## Scope

- Club events.
- Membership/application events.
- Activity creation.
- Export completion.

## Export đặc biệt

Tránh:

```text
Export marked Completed
        ↓
Redis publish fails
        ↓
Notification lost forever
```

## Hướng xử lý

Dùng outbox chọn lọc cho các workflow cần durability.

Không chuyển toàn bộ hệ thống sang async/event-driven chỉ vì đã có outbox.

## Tests

- Redis down lúc DB commit.
- Redis recovery.
- Outbox retry.
- Duplicate publish.
- Duplicate notification.
- API retry.

## Exit criteria

Important events có eventual delivery hợp lý.

---

# WAVE 4 — PERFORMANCE & CODE QUALITY

# PHASE 15 — Document Processing & Export Pipeline

**Mức ưu tiên:** HIGH  
**Services:** ReportService, ExportService

## Mục tiêu

Giảm memory pressure, tránh synchronous heavy document work và làm rõ export failure behavior.

## Phần A — Upload

- Stream khi hợp lý.
- Hạn chế byte-array copy.
- Validate size.
- Validate archive entry count.
- Validate decompressed size.
- Cleanup temp files.

## Phần B — Preview generation

Đưa heavy preview sang background job nếu workflow hiện tại cho phép.

State machine gợi ý:

```text
Pending
  ↓
Processing
  ↓
Completed / Failed
```

## Phần C — Hangfire concurrency

Điều chỉnh worker count theo memory limit.

## Phần D — Persistent preview storage

Không để preview quan trọng mất khi container restart nếu deployment cần persistence.

## Phần E — Export snapshot

Không swallow deserialize exception rồi vẫn báo export completed.

Phân biệt:

```text
Required snapshot malformed → Failed
Optional snapshot absent → documented fallback
```

## Tests

- Valid DOCX/XLSX.
- Corrupt file.
- Near-limit file.
- High compression ratio.
- Concurrent preview jobs.
- Container restart.
- Job cancellation.
- Export failed snapshot.

## Exit criteria

Document pipeline có bounded resource use và failure state đáng tin cậy.

---

# PHASE 16 — Database Query Optimization

**Mức ưu tiên:** PERFORMANCE  
**Services:** Report, Activity, Club, Finance, Export

## Nguyên tắc

Không tối ưu bằng cảm tính.

Cần đo:

- Query count.
- Logical reads.
- CPU.
- Latency.
- Allocation.
- Memory.

## 16.1. KPI leaderboard

Ưu tiên DB-side aggregation thay vì materialize object graph lớn.

## 16.2. Read-only query

Xem xét:

```csharp
AsNoTracking()
```

và projection.

## 16.3. Pagination

Đánh giá:

- Club list.
- Activity list.
- Report list.
- Finance proposals.
- Export requests.
- KPI leaderboard.

Không bắt buộc cursor pagination cho mọi endpoint.

## 16.4. Index optimization

Chỉ thêm index khi:

- Query pattern rõ.
- Existing index không cover.
- Execution plan / workload ủng hộ.

## 16.5. CancellationToken

Bổ sung cho EF/API call nơi còn thiếu và hữu ích.

## Benchmark

So sánh before/after:

```text
p50
p95
SQL logical reads
query count
memory
CPU
```

## Exit criteria

Mỗi optimization có evidence chứ không chỉ “code trông đẹp hơn”.

---

# PHASE 17 — API Correctness, Observability & Logging

**Mức ưu tiên:** STABILITY  
**Scope:** nhiều service

## 17.1. Deadline reminder

Sửa logic xác định club chưa nộp report theo current period.

Phân biệt:

- Submitted current period.
- Old reports only.
- Never submitted.
- Inactive club.

## 17.2. Exception handling

Không để mutation exception trở thành 405.

Chuẩn hóa:

- ProblemDetails.
- HTTP status.
- Correlation ID.

## 17.3. Health checks

Tách:

```text
/health/live
/health/ready
```

Readiness phụ thuộc các dependency thiết yếu.

## 17.4. Correlation ID

Giữ xuyên:

```text
Gateway
→ Service A
→ Service B
→ Logs
```

## 17.5. Auth security logs

Log:

- Login success/failure.
- Refresh.
- Lock/unlock.
- Role change.
- Suspicious auth events.

Không log:

- Access token.
- Refresh token.
- Signing secret.
- Credentials.

## 17.6. SQL logging

Giảm sensitive SQL logging ở Production.

## Exit criteria

Production troubleshooting đủ tốt cho demo/deployment và không lộ dữ liệu nhạy cảm.

---

# WAVE 5 — DEPLOYMENT, TESTING & FINAL INTEGRATION

# PHASE 18 — Docker, Configuration & Operational Hardening

**Mức ưu tiên:** DEPLOYMENT

## Hạng mục

| Hạng mục | Việc cần làm |
|---|---|
| Container user | chạy non-root nếu khả thi |
| Resource limits | CPU/memory/PID hợp lý |
| Health checks | bổ sung Docker healthcheck |
| Environments | Development/Test/Production rõ |
| CORS | chỉ origin cần thiết |
| TLS | xác định termination point |
| Forwarded headers | chỉ trust proxy phù hợp |
| Security headers | bổ sung ở tầng thích hợp |
| Secrets | không expose không cần thiết |
| SQL account | giảm phụ thuộc `sa` |
| SSH deploy | dùng SSH key/quyền tối thiểu |
| SQL image | pin version/tag hợp lý |
| NuGet advisories | update/pin packages cần thiết |
| Release cleanup | tránh giữ release vô hạn |
| Swagger/Hangfire | không public nhầm |
| DemoDataSeeder | guard chống reset nhầm |

## Nguyên tắc cho đồ án

Không bắt buộc:

- Vault.
- Kubernetes.
- Service mesh.
- Enterprise secrets platform.

Ưu tiên:

- Docker configuration đúng.
- Env separation.
- Non-root.
- Least privilege SQL.
- SSH keys.
- Reasonable resource limits.

## Exit criteria

Docker Compose / VPS deployment có baseline hardening phù hợp.

---

# PHASE 19 — Comprehensive Testing & CI/CD Hardening

**Mức ưu tiên:** BẮT BUỘC TRƯỚC RELEASE

## Mục tiêu

Biến tất cả fix quan trọng thành regression tests.

## Test scope theo service

| Service | Test bắt buộc |
|---|---|
| Auth | login, refresh, revoke, account state |
| Gateway | route policy, rate limit, proxy headers |
| Club | membership, manager, treasurer, ownership |
| Activity | statistics, attendance, publication |
| Report | attachments, approval, deadline, transaction |
| Finance | proposal, settlement, concurrency |
| Export | generation, download, event delivery |
| Notification | consume, retry, DLQ, idempotency |
| Admin | JWT pipeline, role boundaries |
| KPI gRPC | contract/boundary |
| DemoDataSeeder | safety guards |

## CI Pipeline

```text
Pull Request
   ↓
Restore
   ↓
Build
   ↓
Unit Tests
   ↓
Integration Tests
   ↓
SQL Server Tests
   ↓
Security Regression
   ↓
Migration Validation
   ↓
Dependency Scan
   ↓
Docker Build
   ↓
Ready for Review
```

## Công việc

- Add `ClubReportHub.Tests` vào solution.
- Bổ sung missing service test projects nếu thật sự cần.
- SQL Server-backed concurrency tests.
- Migration validation.
- Dependency vulnerability scanning.
- Collect code coverage.
- Security regression tests.
- SDK consistency.
- Docker build validation.

Không đặt mục tiêu máy móc 90–100% coverage.

Mục tiêu:

- Critical business rules có test.
- Authorization boundaries có test.
- Concurrency invariants có test.
- Security regressions có test.
- Cross-service workflows trọng yếu có test.

## Exit criteria

CI tự phát hiện regression ở những finding quan trọng đã sửa.

---

# PHASE 20 — Selective Architecture Refactoring & Final Acceptance

**Mức ưu tiên:** FINAL  
**Scope:** toàn bộ hệ thống

## Mục tiêu

Chỉ sau khi security, data integrity, reliability và tests ổn định mới tiến hành refactor cấu trúc có chọn lọc.

## 20.1. Review runtime cycles

Đánh giá lại:

```text
ClubService ↔ ActivityService
ReportService ↔ FinanceService
```

Không bắt buộc loại bỏ cycle nếu workflow vẫn đơn giản và reliability đã được kiểm soát.

Nếu cycle gây vấn đề thực:

- Chọn dependency direction.
- Tách query/read model.
- Event-driven handoff cho operation không cần sync result.
- Application orchestration nếu cần.

## 20.2. Tách business logic khỏi endpoint

Ưu tiên các workflow:

- Report approval.
- Budget linking.
- Settlement creation.
- Treasurer assignment.
- Ownership transfer.
- Export generation.

Có thể tách:

- Application service.
- Workflow service.
- Domain rule class.

Không tạo interface/abstraction cho mọi class một cách máy móc.

## 20.3. System-wide verification

Happy path xuyên hệ thống:

```text
Google Sign-in
    ↓
Club Membership
    ↓
Report Submission
    ↓
Finance Proposal
    ↓
Approval
    ↓
Activity Publication
    ↓
Notification
    ↓
Export
```

## 20.4. Frontend compatibility

Đối chiếu:

- Authentication.
- Routes.
- DTOs.
- Error format.
- Pagination.
- Preview states.
- Export states.
- Notification behavior.

## 20.5. Documentation

Update:

```text
README.md
API_ENDPOINTS.md
Architecture docs
Database migration guide
Deployment guide
Demo guide
```

## 20.6. Final acceptance

Phải xác minh:

- Build.
- Tests.
- Docker Compose.
- DB migrations.
- Main workflows.
- Security regression.
- Demo flow.
- Deployment flow.

## Exit criteria

Có final remediation report:

- Fixed.
- Verified.
- Accepted risk.
- Deferred.
- Still unverified.

---

# PHẦN III — TRACEABILITY: KHÔNG BỎ SÓT FINDING TỪ BẢN AUDIT

> **Quy tắc:** Mapping bên dưới dùng **ID canonical ở Phase 9** của `CLUBHUB_AUDIT.md`, không dùng ID trước khi merge/renumber. Mỗi finding có một phase chủ trì. Một số việc có thể phụ thuộc vào phase khác (ví dụ tests ở Phase 19); phase chủ trì vẫn chịu trách nhiệm tạo test ngay khi sửa, không chờ đến cuối.
>
> **Confirmed ≠ đã sửa.** Các dòng dưới đây là phân công để thực hiện trong tương lai. Khi code đã thay đổi, phải cập nhật `FINDINGS_LEDGER.md` với evidence và test thực tế.

## A. Coverage mapping — 58 confirmed findings

| Finding ID | Primary remediation phase |
|---|---|
| `ARCH-F01` | Phase 20 |
| `ARCH-F02` | Phase 10 |
| `ARCH-F03` | Phase 06 |
| `ARCH-F04` | Phase 06 |
| `SEC-F01` | Phase 01 |
| `SEC-F02` | Phase 01 |
| `SEC-F03` | Phase 05 |
| `SEC-F04` | Phase 06 |
| `SEC-F05` | Phase 06 |
| `SEC-F06` | Phase 07 |
| `SEC-F07` | Phase 09 |
| `SEC-F08` | Phase 08 |
| `SEC-F09` | Phase 08 |
| `SEC-F10` | Phase 02 |
| `SEC-F11` | Phase 09 |
| `SEC-F12` | Phase 18 |
| `SEC-F13` | Phase 18 |
| `SEC-F14` | Phase 15 |
| `SEC-F15` | Phase 18 |
| `SEC-F16` | Phase 02 |
| `SEC-F17` | Phase 18 |
| `DATA-F01` | Phase 11 |
| `DATA-F02` | Phase 12 |
| `DATA-F03` | Phase 13 |
| `DATA-F04` | Phase 12 |
| `DATA-F05` | Phase 11 |
| `DATA-F06` | Phase 11 |
| `DATA-F07` | Phase 17 |
| `REL-F01` | Phase 14 |
| `REL-F02` | Phase 13 |
| `REL-F03` | Phase 03 |
| `REL-F04` | Phase 04 |
| `REL-F05` | Phase 14 |
| `REL-F06` | Phase 17 |
| `REL-F07` | Phase 17 |
| `REL-F08` | Phase 15 |
| `PERF-F01` | Phase 10 |
| `PERF-F02` | Phase 16 |
| `PERF-F03` | Phase 16 |
| `PERF-F04` | Phase 09 |
| `PERF-F05` | Phase 16 |
| `OBS-F01` | Phase 17 |
| `OBS-F02` | Phase 17 |
| `OBS-F03` | Phase 17 |
| `OPS-F01` | Phase 18 |
| `OPS-F02` | Phase 18 |
| `OPS-F03` | Phase 18 |
| `OPS-F04` | Phase 18 |
| `OPS-F05` | Phase 18 |
| `OPS-F06` | Phase 18 |
| `OPS-F07` | Phase 15 |
| `TEST-F01` | Phase 19 |
| `TEST-F02` | Phase 19 |
| `TEST-F03` | Phase 19 |
| `TEST-F04` | Phase 19 |
| `TEST-F05` | Phase 19 |
| `TEST-F06` | Phase 19 |
| `CQ-F01` | Phase 15 |

## B. Coverage mapping — 19 potential/unverified findings

Các mục này **chỉ kiểm chứng trước**; chỉ thực hiện remediation nếu kết quả chứng minh có lỗi hoặc là cải tiến phù hợp phạm vi đồ án. Không cộng các mục này vào số confirmed defects chỉ vì nằm trong kế hoạch.

| Potential / Unverified ID | Phase kiểm chứng chủ trì |
|---|---|
| `POT-F01` | Phase 01 |
| `POT-F02` | Phase 18 |
| `POT-F03` | Phase 08 |
| `POT-F04` | Phase 06 |
| `POT-F05` | Phase 02 |
| `POT-F06` | Phase 16 |
| `POT-F07` | Phase 16 |
| `POT-F08` | Phase 10 |
| `POT-F09` | Phase 16 |
| `POT-F10` | Phase 02 |
| `POT-F11` | Phase 15 |
| `POT-F12` | Phase 18 |
| `POT-F13` | Phase 19 |
| `POT-F14` | Phase 18 |
| `POT-F15` | Phase 16 |
| `POT-F16` | Phase 16 |
| `POT-F17` | Phase 18 |
| `POT-F18` | Phase 19 |
| `POT-F19` | Phase 19 |

## C. Checklist đối soát sau mỗi wave

- [ ] Mọi finding thuộc wave được đánh dấu `Not started / In progress / Fixed pending verification / Verified / Not applicable / Accepted risk / Deferred` trong ledger.
- [ ] Với `Verified`, lưu đường dẫn file đã sửa, regression test, kết quả chạy, môi trường và commit tham chiếu.
- [ ] Với potential/unverified, lưu kết quả `Confirmed / Not reproduced / Not applicable / Still unverified`, không suy đoán.
- [ ] Với thay đổi API, ghi tác động frontend và phương án tương thích.
- [ ] Với thay đổi database, ghi migrations, dữ liệu cũ, preflight và rollback/forward-fix.
- [ ] Bất cứ phát hiện mới nào đều có ID mới trong ledger; không tái sử dụng ID `REL-001` cũ của audit.

---

# PHẦN IV — QUẢN LÝ CONTEXT CHO AI TOOL

Do phạm vi lớn, mỗi AI session chỉ nên nhận:

1. `CLUBHUB_AUDIT.md`.
2. `MASTER_PLAN.md`.
3. `CHECKPOINT.md`.
4. File phase hiện tại.
5. Các source files liên quan tới phase hiện tại.

Không nên đưa toàn bộ source tree vào context thủ công.

## Format checkpoint bắt buộc

Sau mỗi phase:

```markdown
# PHASE XX CHECKPOINT

## Scope completed

## Files changed

## Migrations created

## API contract changes

## Tests added

## Tests executed

## Findings resolved

## Findings partially resolved

## Findings still open

## New findings discovered

## Decisions made

## Regression risks

## Known limitations

## Repository state

## Next phase prerequisites
```

---

# PHẦN V — RULES CHO AI KHI THỰC HIỆN REMEDIATION

Mỗi phase phải tuân theo:

## 1. Không tự sửa ngoài scope

Nếu phát hiện vấn đề ở phase khác:

- Ghi lại.
- Thêm vào ledger.
- Không tự mở rộng phạm vi trừ khi blocker.

## 2. Không rewrite toàn service

Ưu tiên minimal safe change.

## 3. Không thay public API tùy tiện

Nếu cần đổi contract:

- Ghi `API_CONTRACT_CHANGES.md`.
- Nêu frontend impact.
- Có migration/compatibility plan.

## 4. Database change phải có safety plan

Trước migration:

- Inspect current data.
- Detect conflicts.
- Backup strategy.
- Fresh DB test.
- Upgrade DB test.

## 5. Security fix phải có regression test

Không kết luận “đã an toàn” chỉ vì code nhìn đúng.

## 6. Performance fix phải đo

Không đổi query vì “có vẻ nhanh hơn”.

## 7. Không over-engineer

Không thêm framework/infrastructure mới nếu fix hiện tại đủ.

## 8. Build/test sau mỗi phase

Ít nhất:

```powershell
dotnet build
dotnet test
```

và targeted tests cho phase.

## 9. Không tự commit/push/deploy

Trừ khi người dùng yêu cầu rõ.

---

# PHẦN VI — THỨ TỰ THỰC HIỆN ĐỀ XUẤT

```text
PHASE 00
   ↓
PHASE 01
   ↓
PHASE 02
   ↓
PHASE 03
   ↓
PHASE 04
   ↓
PHASE 05
   ↓
PHASE 06
   ↓
PHASE 07
   ↓
PHASE 08
   ↓
PHASE 09
   ↓
PHASE 10
   ↓
PHASE 11
   ↓
PHASE 12
   ↓
PHASE 13
   ↓
PHASE 14
   ↓
PHASE 15
   ↓
PHASE 16
   ↓
PHASE 17
   ↓
PHASE 18
   ↓
PHASE 19
   ↓
PHASE 20
```

Có thể chạy song song hạn chế:

- Phase 03 và Phase 04 có thể cùng một nhóm AI nếu context đủ.
- Phase 11 và Phase 12 có thể làm song song sau Phase 10.
- Phase 15 và Phase 16 có thể song song.
- Phase 18 có thể bắt đầu sớm một phần nhưng final validation phải sau Phase 17.

Không nên chạy song song:

- Phase 01 với các auth refactor khác.
- Phase 06 với Phase 13 khi Report workflow đang thay đổi.
- Phase 10 với database invariant migrations.
- Phase 19 trước khi major implementation phases hoàn thành.

---

# PHẦN VII — PRIORITY CHO ĐỒ ÁN TỐT NGHIỆP

Nếu thời gian giới hạn, ưu tiên theo nhóm:

## MUST FIX

- Authentication bypass.
- Attachment path/security.
- Redis unbounded growth.
- Broken authorization.
- Critical club/resource ownership.
- Financial/report concurrency invariants.
- Refresh token issues.
- Notification stuck message recovery.
- Production service URL defects.
- Report transaction consistency.
- CI tests cho các phần trên.

## SHOULD FIX

- Database lifecycle.
- Outbox reliability.
- Query performance hot paths.
- Health/readiness.
- Correlation logging.
- Docker hardening.
- Dependency vulnerabilities.

## NICE TO HAVE

- Runtime dependency cycle cleanup.
- Deep architectural refactor.
- Advanced observability.
- Aggressive microservice decoupling.
- Complex distributed resiliency patterns.

---

# PHẦN VIII — DEFINITION OF DONE TOÀN DỰ ÁN

Remediation chỉ được xem là hoàn tất khi:

1. Không còn Critical finding chưa xử lý hoặc chưa có accepted-risk rõ ràng.
2. High findings quan trọng đã có regression test.
3. Authorization được kiểm tra theo resource/club, không chỉ theo role.
4. Database invariants không còn phụ thuộc hoàn toàn vào read-check-write.
5. Important event delivery có recovery.
6. Refresh token rotation an toàn.
7. Attachments không cho client điều khiển physical path.
8. Docker deployment chạy được.
9. Fresh DB migration thành công.
10. Existing DB upgrade test thành công.
11. Full solution build pass.
12. Full automated test suite pass.
13. Main end-to-end workflow pass.
14. README/API docs phản ánh behavior thật.
15. Có final ledger cho mọi finding:
    - Fixed.
    - Verified.
    - Accepted.
    - Deferred.
    - Unverified.

---

# PHẦN IX — MỤC TIÊU CUỐI CÙNG

Sau kế hoạch này, ClubHub API không cần trở thành một hệ thống enterprise quá phức tạp.

Mục tiêu hợp lý cho đồ án tốt nghiệp là:

- Kiến trúc rõ ràng.
- Security boundary đúng.
- Authorization không bypass dễ dàng.
- Database không dễ rơi vào trạng thái sai khi concurrent requests.
- Cross-service workflow đủ ổn định.
- Background jobs có recovery.
- Query không có bottleneck rõ ràng.
- Docker deployment ổn định.
- CI đủ mạnh để chống regression.
- Code dễ giải thích khi bảo vệ đồ án.
- Không over-engineer.
- Có bằng chứng test cho các quyết định kỹ thuật quan trọng.
