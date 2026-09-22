# MA TRẬN KIỂM THỬ HỒI QUY (TEST MATRIX)

> Tài liệu này theo dõi ánh xạ giữa các Finding (phát hiện lỗi) và các bộ kiểm thử tự động (Unit / Integration / Regression Tests).

---

## 1. Trạng thái Test Hiện tại (Cập nhật tại Phase 21 — 2026-09-22)

- **Total Passing Tests:** **287 passed** trên toàn solution.
  - `Backend.StabilizationTests`: 221 passed.
  - `AdminService.IntegrationTests`: 27 passed, 1 skipped khi không có live SQL Server.
  - `ClubReportHub.Tests`: 39 passed.
- **Failed Tests:** 0.
- **Skipped Tests:** 1 (live SQL migration test, có thông báo skip tường minh).

> [!NOTE]
> **Nguyên tắc kiểm thử thực tế cho đồ án tốt nghiệp:**  
> Đối với các finding thuộc nhóm rủi ro thấp, tiềm năng chưa xác minh hoặc mang tính vận hành môi trường (`LOW`, `POTENTIAL`, `OPERATIONAL` như `SEC-F15` allowed hosts, `OPS-F01` đến `OPS-F06` docker/deploy configuration, `POT-F12` swagger config), có thể thực hiện kiểm định xác nhận thông qua rà soát cấu hình (configuration review) hoặc kiểm tra thủ công (manual smoke check) thay vì bắt buộc phải viết riêng biệt một bài kiểm thử tự động (dedicated automated test). Các bài kiểm thử tự động tập trung tối đa nguồn lực vào các lỗi bảo mật nghiêm trọng (CRITICAL/HIGH), tính toàn vẹn dữ liệu và điều kiện tranh chấp cơ sở dữ liệu (concurrency / transactional integrity).

---

## 2. Ma trận Ánh xạ Finding → Regression Test

| Mã Finding | Mức độ | Phase | File Test dự kiến / hiện có | Loại test | Trạng thái test |
| :--- | :---: | :---: | :--- | :---: | :---: |
| `SEC-F01` | **CRITICAL** | Phase 01 | `AuthService.DevLoginTests` / `GatewayRouteTests` | Integration | CHƯA CÓ |
| `SEC-F02` | **HIGH** | Phase 01 | `AuthConfigurationTests` | Unit | CHƯA CÓ |
| `SEC-F03` | **HIGH** | Phase 05 | `Backend.StabilizationTests.GatewayRateLimitingAndSecurityTests` | Integration | **PASSED (9 cases)** |
| `POT-F02` | **MEDIUM** | Phase 05 | `Backend.StabilizationTests.GatewayRateLimitingAndSecurityTests` | Architecture / Security | **PASSED** |
| `SEC-F04` | **MEDIUM** | Phase 06 | `Backend.StabilizationTests.CrossServiceWorkflowAuthorizationTests` | Integration / Security | **PASSED (4 cases)** |
| `SEC-F05` | **HIGH** | Phase 06 | `Backend.StabilizationTests.CrossServiceWorkflowAuthorizationTests` | Integration / Business Integrity | **PASSED (3 cases)** |
| `POT-F04` | **MEDIUM** | Phase 06 | `Backend.StabilizationTests.CrossServiceWorkflowAuthorizationTests` | Integration / Authorization | **PASSED (6 cases)** |
| `SEC-F06` | **HIGH** | Phase 07 | `Backend.StabilizationTests.JwtAuthorizationFreshnessTests` | Integration / Security | **PASSED (9 cases)** |
| `SEC-F07` | **HIGH** | Phase 09 | `Backend.StabilizationTests.ClubAccessCacheInvalidationTests` | Unit / Integ / Concurrency | **PASSED (6 cases)** |
| `SEC-F08` | **MEDIUM** | Phase 08/21 | `Backend.StabilizationTests.RefreshTokenSecurityTests` | Concurrency / Integration | **PASSED (9 cases)** |
| `SEC-F09` | **MEDIUM** | Phase 08/21 | `Backend.StabilizationTests.RefreshTokenSecurityTests` | Security / Unit | **PASSED (9 cases)** |
| `SEC-F10` | **CRITICAL** | Phase 02 | `Backend.StabilizationTests.ReportAttachmentSecurityTests` | Unit / Security | **PASSED (17 cases)** |
| `POT-F05` | **LOW** | Phase 02 | `Backend.StabilizationTests.ReportAttachmentSecurityTests` | Unit / Security | **PASSED** |
| `SEC-F11` | **HIGH** | Phase 09 | `Backend.StabilizationTests.MemberStatisticsValidationTests` | Integration / Security | **PASSED (4 cases)** |
| `SEC-F14` | **HIGH** | Phase 15 | `Backend.StabilizationTests.DocumentProcessingAndJobHardeningTests` | Integration / Security / Streaming / Zip-Bomb | **PASSED (12 cases)** |
| `PERF-F04`| **MEDIUM** | Phase 09 | `Backend.StabilizationTests.ClubAccessCacheInvalidationTests` | Cache / Concurrency | **PASSED (6 cases)** |
| `PERF-F01`| **MEDIUM** | Phase 10 | `Backend.StabilizationTests.DatabaseLifecycleAndIndexUpgradeTests` | Architecture / Index Integrity | **PASSED (4 cases)** |
| `POT-F08` | **MEDIUM** | Phase 10 | `Backend.StabilizationTests.DatabaseLifecycleAndIndexUpgradeTests` | Architecture / Index Integrity | **PASSED (4 cases)** |
| `ARCH-F02`| **LOW**    | Phase 10 | `Backend.StabilizationTests.DatabaseLifecycleAndIndexUpgradeTests` | Architecture / Migration Baseline | **PASSED (4 cases)** |
| `DATA-F01` | **HIGH** | Phase 11 | `Backend.StabilizationTests.ClubDataIntegrityConcurrencyTests` | Concurrency / DB Constraint | **PASSED (11 cases)** |
| `DATA-F02` | **HIGH** | Phase 12 | `Backend.StabilizationTests.FinanceAndReportDataIntegrityTests` | Concurrency / DB Filtered Index | **PASSED (8 cases)** |
| `DATA-F03` | **MEDIUM** | Phase 13/21 | `Backend.StabilizationTests.ReportWorkflowAtomicityAndOutboxConcurrencyTests`, `FinanceAndReportDataIntegrityTests` | Transaction Atomicity / Rollback | **PASSED** |
| `DATA-F04` | **MEDIUM** | Phase 12/21 | `Backend.StabilizationTests.FinanceAndReportDataIntegrityTests` | Integration / DB Filtered Index / SQLite Concurrency | **PASSED** |
| `DATA-F05` | **HIGH** | Phase 11 | `Backend.StabilizationTests.ClubDataIntegrityConcurrencyTests` | Concurrency / DB Filtered Index | **PASSED (11 cases)** |
| `DATA-F06` | **MEDIUM** | Phase 11 | `Backend.StabilizationTests.ClubDataIntegrityConcurrencyTests` | Concurrency / DB Filtered Index | **PASSED (11 cases)** |
| `DATA-F07` | **MEDIUM** | Phase 17 | `Backend.StabilizationTests.ObservabilityAndLoggingStandardsTests` | Report Deadline Missing Clubs Calculation | **PASSED (18 cases suite)** |
| `REL-F01` | **MEDIUM** | Phase 14 | `Backend.StabilizationTests.CrossServiceOutboxAndDualWriteTests` | Integration / Transactional Outbox | **PASSED (10 cases)** |
| `REL-F02` | **MEDIUM** | Phase 13/21 | `Backend.StabilizationTests.ReportWorkflowAtomicityAndOutboxConcurrencyTests` | Concurrency / Lease-Claim / Batch Continuation | **PASSED** |
| `REL-F03` | **CRITICAL** | Phase 03 | `Backend.StabilizationTests.RedisStreamRetentionTests` | Unit / Mock | **PASSED (10 cases)** |
| `POT-F15` | **LOW** | Phase 03 | `Backend.StabilizationTests.RedisStreamRetentionTests` | Unit / Architecture | **PASSED** |
| `REL-F04` | **HIGH** | Phase 04 | `Backend.StabilizationTests.NotificationConsumerRecoveryTests` | Unit / Integration | **PASSED (7 cases)** |
| `REL-F05` | **MEDIUM** | Phase 14 | `Backend.StabilizationTests.CrossServiceOutboxAndDualWriteTests` | Integration / Outbox Dual-Write Elimination | **PASSED (10 cases)** |
| `REL-F06` | **MEDIUM** | Phase 17 | `Backend.StabilizationTests.ObservabilityAndLoggingStandardsTests` | MapGlobalErrorEndpoint RFC 7807 All Verbs | **PASSED (18 cases suite)** |
| `REL-F07` | **MEDIUM** | Phase 18 | `Backend.StabilizationTests.OperationalHardeningAndHealthCheckTests` | Integration / HealthCheck Readiness & Liveness | **PASSED (32 cases suite)** |
| `REL-F08` | **MEDIUM** | Phase 15 | `Backend.StabilizationTests.DocumentProcessingAndJobHardeningTests` | Unit / Bounded Worker / Cancellation | **PASSED (12 cases)** |
| `OPS-F06` | **LOW** | Phase 18 | `Backend.StabilizationTests.OperationalHardeningAndHealthCheckTests` | Dockerignore / Appsettings Test Leak Prevention | **PASSED (32 cases suite)** |
| `OPS-F07` | **LOW** | Phase 15 | `Backend.StabilizationTests.DocumentProcessingAndJobHardeningTests` | Integration / Docker Volume | **PASSED (12 cases)** |
| `CQ-F01` | **LOW** | Phase 15 | `Backend.StabilizationTests.DocumentProcessingAndJobHardeningTests` | Unit / Error Handling & Cleanup | **PASSED (12 cases)** |
| `PERF-F02`| **MEDIUM** | Phase 16 | `Backend.StabilizationTests.DatabaseQueryOptimizationTests` | DB-Side Aggregation & Projection | **PASSED (4 cases)** |
| `PERF-F03`| **LOW**    | Phase 16 | `Backend.StabilizationTests.DatabaseQueryOptimizationTests` | AsNoTracking ChangeTracker Verification | **PASSED (4 cases)** |
| `PERF-F05`| **LOW**    | Phase 16 | `Backend.StabilizationTests.DatabaseQueryOptimizationTests` | CancellationToken Abort Responsiveness | **PASSED (4 cases)** |
| `OBS-F01` | **HIGH**   | Phase 17 | `Backend.StabilizationTests.ObservabilityAndLoggingStandardsTests` | Correlation ID Ingress/Egress Propagation | **PASSED (18 cases suite)** |
| `OBS-F02` | **MEDIUM** | Phase 17 | `Backend.StabilizationTests.ObservabilityAndLoggingStandardsTests` | AuthService Structured Logging & Token Masking | **PASSED (18 cases suite)** |
| `OBS-F03` | **MEDIUM** | Phase 17 | `Backend.StabilizationTests.ObservabilityAndLoggingStandardsTests` | EF Core LogLevel Warning SQL Leak Prevention | **PASSED (18 cases suite)** |
| `SEC-F13` | **MEDIUM** | Phase 18 | `Backend.StabilizationTests.OperationalHardeningAndHealthCheckTests` | SecurityHeadersMiddleware Security Headers Verification | **PASSED (32 cases suite)** |
| `SEC-F16` | **LOW** | Phase 18 | `Backend.StabilizationTests.OperationalHardeningAndHealthCheckTests` | ContentDispositionSanitizer Path & CRLF Stripping | **PASSED (32 cases suite)** |
| `POT-F14` | **INFO** | Phase 18 | `Backend.StabilizationTests.OperationalHardeningAndHealthCheckTests` | DemoDataSeeder Production & Reset Confirmation Guards | **PASSED (32 cases suite)** |
| `TEST-F01`| **HIGH** | Phase 19/21 | `Backend.StabilizationTests` suite | Unit & Integration across 10 service hosts | **PASSED (221 tests)** |
| `TEST-F02`| **HIGH** | Phase 19 | `Backend.StabilizationTests` suite | Regression tests for all Critical & High findings | **PASSED** |
| `TEST-F03`| **MEDIUM** | Phase 19 | `Backend.StabilizationTests.SolutionIntegrityTests` & `ClubReportHub.sln` | Test Suite In-Solution & CI Execution | **PASSED (39 tests in CI)** |
| `TEST-F04`| **MEDIUM** | Phase 19 | `AdminService.IntegrationTests.SqlServerMigrationTests` | Explicit Skip Reporting vs CI Mandatory Execution | **PASSED** |
| `TEST-F05`| **MEDIUM** | Phase 19 | `.github/workflows/backend-validate.yml` | EF Core Model Drift Verification across 8 services | **PASSED (8/8 in sync)** |
| `TEST-F06`| **MEDIUM** | Phase 19 | `Backend.StabilizationTests` & CI Coverlet | Concurrency Invariants & XPlat Code Coverage Collection | **PASSED (Coverlet active)** |
| `SEC-F12` | **MEDIUM** | Phase 19 | `.github/workflows/backend-validate.yml` & `dotnet list --vulnerable` | Transitive Vulnerability Pinning & CI Hard-Gate | **PASSED (0 vulnerable pkgs)** |
| `POT-F18` | **LOW** | Phase 19 | `.github/workflows/backend-validate.yml` | CI Hardening, Timeouts, Hard Gates, Compose Validation | **PASSED** |
| `POT-F19` | **INFO** | Phase 19 | `Backend.StabilizationTests.JwtAuthorizationFreshnessTests` | JWT Pipeline & Security Stamp Verification | **PASSED (9 cases)** |
| `ARCH-F01` | **MEDIUM** | Phase 20 | `Backend.StabilizationTests.ArchitectureResilienceTests` | Graceful Degradation on Activity Outage & Cross-Service Invariants | **PASSED (5 cases)** |

