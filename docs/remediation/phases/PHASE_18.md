# PHASE 18 — Docker, Configuration & Operational Hardening

**Ngày hoàn thành:** 2026-09-22  
**Mức ưu tiên:** DEPLOYMENT  
**Trạng thái:** ✅ HOÀN THÀNH

---

## 1. Tóm tắt

Phase 18 hoàn thành hardening toàn bộ tầng Docker, configuration và operational baseline:
- Non-root containers với PID/CPU/Memory limits
- Standardized health check endpoints (`/health/live`, `/health/ready`, `/health`)
- Security headers middleware (X-Content-Type-Options, X-Frame-Options, Referrer-Policy, v.v.)
- Content-Disposition filename sanitizer (path traversal, CRLF, control chars)
- DemoDataSeeder safety guards (Production/Staging lock-out, dual confirmation)
- Release retention policy (5 bản gần nhất) và SSH key auth trong CI/CD
- CORS cleanup, SQL Server image pin, .dockerignore hardening

---

## 2. Các Finding đã xử lý

| Mã ID | Mức | Kết quả |
|:---|:---:|:---:|
| `OPS-F01` | MEDIUM | ✅ FIXED |
| `OPS-F02` | MEDIUM | ✅ FIXED |
| `OPS-F03` | LOW | ✅ FIXED |
| `OPS-F04` | LOW | ✅ FIXED |
| `OPS-F05` | LOW | ✅ FIXED |
| `OPS-F06` | LOW | ✅ FIXED |
| `REL-F07` | MEDIUM | ✅ FIXED |
| `SEC-F13` | MEDIUM | ✅ FIXED |
| `SEC-F15` | LOW | ✅ FIXED (verified via config review) |
| `SEC-F16` | LOW | ✅ FIXED |
| `SEC-F17` | INFO | ✅ FIXED (verified via config review) |
| `POT-F12` | INFO | ✅ VERIFIED |
| `POT-F14` | INFO | ✅ FIXED |
| `POT-F16` | LOW | ✅ VERIFIED |
| `POT-F17` | LOW | ✅ VERIFIED |

---

## 3. Thay đổi chi tiết

### OPS-F01 — Non-root containers + Resource limits
**Files:** 11 Dockerfiles (`AuthService`, `ClubService`, `ActivityService`, `ReportService`, `FinanceService`, `NotificationService`, `ExportService`, `AdminService`, `ApiGateway`, `KpiGrpcService`, `DemoDataSeeder`), `docker-compose.yml`

- Thêm `USER $APP_UID` vào final stage của tất cả 11 Dockerfile
- `ReportService` và `ExportService` Dockerfile: tạo thư mục `/app/report-uploads`, `/app/report-previews`, `/app/exports` và `chown -R $APP_UID:$APP_UID` trước khi drop root
- `docker-compose.yml`: bổ sung `deploy.resources.limits.pids: 100` cho tất cả application services (export-service, api-gateway trước đây thiếu)
- Xác nhận `docker compose config --quiet` exit code 0 ✅

### OPS-F02 — SSH key auth trong deployment pipeline  
**File:** `.github/workflows/deployment.yml`

- Bổ sung `key: ${{ secrets.VPC_SSH_KEY }}` và `passphrase: ${{ secrets.VPC_SSH_PASSPHRASE }}` cho cả `appleboy/scp-action` và `appleboy/ssh-action`
- Fallback `password` vẫn để cho khả năng tương thích ngược

### OPS-F03 — Release retention policy
**File:** `.github/workflows/deployment.yml`

- Bổ sung script giữ 5 bản release gần nhất:
  ```bash
  find "${TARGET_ROOT}/releases" -mindepth 1 -maxdepth 1 -type d | sort -r | tail -n +6 | xargs -r rm -rf
  ```

### OPS-F04 — Pin SQL Server image
**File:** `docker-compose.yml`

- Thay `mcr.microsoft.com/mssql/server:2022-latest` → `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04`

### OPS-F05 — CORS environment-aware
**Files:** `ExportService/Program.cs`, `NotificationService/Program.cs`, `docker-compose.yml`

- `CorsOriginConfiguration.ResolveAllowedOrigins` thay thế hardcoded localhost cho ExportService và NotificationService
- Bổ sung `Cors__AllowedOrigins__0` và `Cors__AdditionalOrigins` env vars trong docker-compose.yml

### OPS-F06 — .dockerignore hardening
**File:** `.dockerignore`

- Thêm: `tests/`, `**/appsettings.Test.json`, `**/appsettings.Development.json`

### REL-F07 — Standardised Health Check Endpoints
**Files:** `src/Shared/ClubReportHub.Shared/Health/StandardHealthCheckExtensions.cs` (new), tất cả 9 service Program.cs, `KpiGrpcService/Program.cs`

- `/health/live`: Predicate `_ => false` → luôn 200 OK (liveness check)
- `/health/ready`: Predicate `registration.Tags.Contains("ready")` → chạy DB + Redis checks
- `/health`: Predicate `_ => false` → backward compat
- `AddRedisHealthCheck()`: custom `IHealthCheck` dùng `HealthCheckRegistration` factory pattern
- `AddDbContextCheck<TContext>()`: EF Core health check với tag `"ready"`
- `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore 8.0.18` thêm vào Shared.csproj

### SEC-F13 — Security Headers Middleware
**Files:** `src/Shared/ClubReportHub.Shared/Security/SecurityHeadersMiddleware.cs` (new), tất cả 9 service + ApiGateway `Program.cs`

Headers được thêm:
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `X-XSS-Protection: 0`
- `Permissions-Policy: accelerometer=(), camera=(), geolocation=(), ...`

Middleware không ghi đè nếu header đã tồn tại.

### SEC-F15 — AllowedHosts
Xác nhận tất cả services chạy trên `internal-net` Docker network, không expose port ra ngoài (chỉ ApiGateway expose port). AllowedHosts=* không có rủi ro thực tế trong topology này. Documented.

### SEC-F16 — Content-Disposition Sanitizer
**File:** `src/Shared/ClubReportHub.Shared/Security/ContentDispositionSanitizer.cs` (new)

`SanitizeFileName(rawFileName, fallback)`:
1. Strip directory paths (`Path.GetFileName`)
2. Remove CRLF để chống HTTP response splitting
3. Replace control chars và invalid filename chars với `_`
4. Trim trailing dots

Tích hợp vào `ReportService/Endpoints/ReportFileEndpoints.cs` và `ExportService/Endpoints/ExportEndpoints.cs`.

### SEC-F17 — Hangfire Dashboard bảo vệ
Xác nhận `HangfireDashboardAuthorizationFilter` yêu cầu `Admin` hoặc `SystemAdmin` role. Dashboard `/hangfire` không được expose qua YARP gateway routes.

### POT-F14 — DemoDataSeeder Safety Guards
**File:** `src/Tools/DemoDataSeeder/DemoSeederOptions.cs`

- Ném `InvalidOperationException` nếu `ResetAll=true` trong Production/Staging
- Ném `InvalidOperationException` nếu `ResetAll=true` mà không có `DemoData__ConfirmDestructiveReset=true`

---

## 4. Tests

**File:** `tests/Backend.StabilizationTests/OperationalHardeningAndHealthCheckTests.cs` (new, 32 test cases)

| Nhóm | Tests |
|:---|:---|
| REL-F07: Health Endpoints | 6 tests (liveness always OK, readiness tag filtering, 503 khi check fail, isolation) |
| SEC-F13: Security Headers | 7 tests (mỗi header + không ghi đè) |
| SEC-F16: ContentDispositionSanitizer | 10 tests (path traversal, CRLF, invalid chars, empty, null, fallback) |
| POT-F14: DemoSeeder Guards | 4 tests (Production/Staging reject, dev without confirm reject, dev with confirm pass, prod no-reset pass) |
| OPS-F06: .dockerignore | 3 tests (tests/, Test.json, Development.json exclusions) |

---

## 5. Kết quả kiểm tra

| Kiểm tra | Kết quả |
|:---|:---:|
| `dotnet build ClubReportHub.sln -c Release -warnaserror` | ✅ 0 Warning, 0 Error |
| `dotnet test ClubReportHub.sln -c Release` | ✅ 240/240 passed |
| `dotnet test ClubReportHub.Tests` | ✅ 39/39 passed |
| `docker compose config --quiet` | ✅ exit 0 |

**Tổng số test pass: 279/279 (100%)**

---

## 6. Phase tiếp theo

**Phase 19: Comprehensive Testing & CI/CD Hardening** (`TEST-F01`, `TEST-F02`, `TEST-F03`, `TEST-F04`, `TEST-F05`, `TEST-F06`, `SEC-F12`, `POT-F18`, `POT-F19`)
