# BÁO CÁO HOÀN THÀNH PHASE 10 — DATABASE LIFECYCLE STANDARDIZATION & MIGRATION BASELINE

> **Thời điểm hoàn tất:** 2026-09-21  
> **Người thực hiện:** Antigravity AI Assistant  
> **Trạng thái:** HOÀN THÀNH (COMPLETE)

---

## 1. Phạm vi công việc đã thực hiện (Scope Completed)

1. **Khắc phục lỗi MEDIUM: Activity covering-index upgrader bỏ qua index cũ không có covering (`PERF-F01`):**
   - **Đồng bộ hóa Mô hình EF Core với Covering Indexes:**
     - Trong [`ActivityDbContext.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ActivityService/Data/ActivityDbContext.cs), bổ sung thuộc tính bao gồm (`IncludeProperties`) vào các chỉ mục:
       - `ClubActivity`: `IX_Activities_StartTimeUtc` INCLUDE (`ClubId`, `Title`, `Status`).
       - `ActivityParticipant`: `IX_ActivityParticipants_UserId` INCLUDE (`ActivityId`, `AttendanceStatus`).
       - `ActivityAttendance`: `IX_ActivityAttendances_UserId_AttendanceDate` INCLUDE (`ActivityId`, `Status`).
   - **Nâng cấp Cơ Chế Kiểm Tra Covering Index An Toàn Tại Upgrader:**
     - Trong [`ActivitySchemaUpgrader.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ActivityService/Data/ActivitySchemaUpgrader.cs):
       - Trước đây, upgrader chỉ kiểm tra `IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = ...)` nên khi các bảng đã có index mặc định (tạo bởi `EnsureCreated` hoặc schema ban đầu), code hoàn toàn bỏ qua việc tạo covering index với mệnh đề `INCLUDE`.
       - Đã nâng cấp câu truy vấn kiểm tra `sys.index_columns.is_included_column = 1`. Nếu index đã tồn tại nhưng thiếu cột INCLUDE (không phải covering index), upgrader chủ động drop index cũ và tạo lại chỉ mục tối ưu với mệnh đề `INCLUDE (...)`.

2. **Khắc phục lỗi MEDIUM: Report index upgrader có thể bỏ sót các cột INCLUDE trong `IX_Reports_UpdatedAtUtc` (`POT-F08`):**
   - **Đồng bộ hóa Mô hình EF Core tại `ReportDbContext.cs`:**
     - Bổ sung cấu hình `entity.HasIndex(x => x.UpdatedAtUtc).IsDescending().IncludeProperties(x => new { x.ClubId, x.Period, x.Status });` và composite index `entity.HasIndex(x => new { x.CreatedByUserId, x.Status });` vào [`ReportDbContext.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Data/ReportDbContext.cs).
   - **Nâng cấp Upgrader tại `ReportSchemaUpgrader.cs`:**
     - Trong [`ReportSchemaUpgrader.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Data/ReportSchemaUpgrader.cs), bổ sung đoạn lệnh kiểm tra nếu `IX_Reports_UpdatedAtUtc` tồn tại mà chưa có cột INCLUDE (`NOT EXISTS (SELECT 1 FROM sys.index_columns WHERE is_included_column = 1)`), upgrader tiến hành `DROP INDEX [IX_Reports_UpdatedAtUtc]` trước khi tạo lại với mệnh đề `INCLUDE ([ClubId], [Period], [Status])`.

3. **Khắc phục Nợ Kiến Trúc: Chuẩn hóa vòng đời Database Migration trên toàn bộ Microservices (`ARCH-F02`):**
   - **Chuyển đổi hoàn toàn từ `EnsureCreated` sang EF Core Migrations:**
     - **`ActivityService`**:
       - Khởi tạo migration baseline chính thức: `20260921140108_InitialActivityBaseline.cs` và `ActivityDbContextModelSnapshot.cs`.
       - Triển khai kỹ thuật **Idempotent Baseline Migration**: Script kiểm tra `IF OBJECT_ID(N'[dbo].[Activities]', N'U') IS NULL` trước khi tạo bảng, đảm bảo tương thích tuyệt đối cho cả database mới tinh lẫn database đã có sẵn bảng từ trước (ngăn ngừa triệt để lỗi SQL Server Error 2714 làm ngưng trệ `MigrateAsync`).
       - Cập nhật [`DatabaseInitializationExtensions.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ActivityService/Extensions/DatabaseInitializationExtensions.cs) chuyển sang `await db.ApplyMigrationsWithRetryAsync(logger)`.
     - **`FinanceService`**:
       - Khởi tạo migration baseline chính thức: `20260921140143_InitialFinanceBaseline.cs` và `FinanceDbContextModelSnapshot.cs`.
       - Triển khai logic idempotent kiểm tra tồn tại bảng `BudgetProposals`, `FinanceTransactions`, `Settlements`, `OutboxMessages`.
       - Cập nhật [`Program.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/FinanceService/Program.cs) chuyển sang `await db.ApplyMigrationsWithRetryAsync(logger)`.
     - **`NotificationService`**:
       - Khởi tạo migration baseline chính thức: `20260921140159_InitialNotificationBaseline.cs` và `NotificationDbContextModelSnapshot.cs`.
       - Triển khai logic idempotent kiểm tra tồn tại bảng `Notifications`, `ProcessedEvents`.
       - Cập nhật [`Program.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/NotificationService/Program.cs) chuyển sang `await db.ApplyMigrationsWithRetryAsync(logger)`.
   - **Kết quả Chuẩn Hóa Toàn Hệ Thống:**
     - 100% (7/7) microservices có database hiện đều sử dụng thống nhất EF Core Migrations với `ApplyMigrationsWithRetryAsync`, hoàn toàn loại bỏ `EnsureCreated` trong code production.

---

## 2. Danh mục file đã chỉnh sửa & tạo mới

| STT | File | Trạng thái | Nội dung thay đổi |
| :---: | :--- | :---: | :--- |
| 1 | `src/Services/ActivityService/Data/ActivityDbContext.cs` | Sửa đổi | Thêm `IncludeProperties` cho 3 covering indexes |
| 2 | `src/Services/ActivityService/Data/ActivitySchemaUpgrader.cs` | Sửa đổi | Kiểm tra `is_included_column = 1` và drop index cũ trước khi tạo covering index (`PERF-F01`) |
| 3 | `src/Services/ActivityService/Migrations/20260921140108_InitialActivityBaseline.cs` | Tạo mới | Baseline migration idempotent cho ActivityService |
| 4 | `src/Services/ActivityService/Migrations/ActivityDbContextModelSnapshot.cs` | Tạo mới | EF Core model snapshot cho ActivityService |
| 5 | `src/Services/ActivityService/Extensions/DatabaseInitializationExtensions.cs` | Sửa đổi | Chuyển sang `ApplyMigrationsWithRetryAsync` |
| 6 | `src/Services/ReportService/Data/ReportDbContext.cs` | Sửa đổi | Thêm `IncludeProperties` và `IsDescending` cho `IX_Reports_UpdatedAtUtc` |
| 7 | `src/Services/ReportService/Data/ReportSchemaUpgrader.cs` | Sửa đổi | Kiểm tra `is_included_column = 1` và drop index cũ trước khi tạo lại (`POT-F08`) |
| 8 | `src/Services/FinanceService/Migrations/20260921140143_InitialFinanceBaseline.cs` | Tạo mới | Baseline migration idempotent cho FinanceService |
| 9 | `src/Services/FinanceService/Migrations/FinanceDbContextModelSnapshot.cs` | Tạo mới | EF Core model snapshot cho FinanceService |
| 10 | `src/Services/FinanceService/Program.cs` | Sửa đổi | Chuyển sang `ApplyMigrationsWithRetryAsync` |
| 11 | `src/Services/NotificationService/Migrations/20260921140159_InitialNotificationBaseline.cs` | Tạo mới | Baseline migration idempotent cho NotificationService |
| 12 | `src/Services/NotificationService/Migrations/NotificationDbContextModelSnapshot.cs` | Tạo mới | EF Core model snapshot cho NotificationService |
| 13 | `src/Services/NotificationService/Program.cs` | Sửa đổi | Chuyển sang `ApplyMigrationsWithRetryAsync` |
| 14 | `tests/Backend.StabilizationTests/DatabaseLifecycleAndIndexUpgradeTests.cs` | Tạo mới | 4 unit/architecture regression tests cho Phase 10 |

---

## 3. Kết quả Kiểm thử & Đảm bảo Chất lượng (Quality Assurance)

- **Biên dịch:** Toàn bộ solution biên dịch thành công 100% với `-warnaserror` (0 cảnh báo, 0 lỗi).
- **Kiểm thử tự động:**
  - `Backend.StabilizationTests`: **109/109 tests PASSED** (+4 tests mới cho Phase 10).
  - `AdminService.IntegrationTests`: **28/28 tests PASSED**.
  - `ClubReportHub.Tests`: **39/39 tests PASSED**.
  - **Tổng cộng: 176/176 tests PASSED** (100% thành công, 0 hồi quy).
- **Nội dung 4 Test Cases Mới tại `DatabaseLifecycleAndIndexUpgradeTests.cs`:**
  1. `ActivityDbContext_ModelConfiguresCoveringIndexes_WithIncludeAnnotations`: Xác minh 3 covering indexes của Activity (`StartTimeUtc`, `UserId`, `UserId+AttendanceDate`) cấu hình chính xác `SqlServer:Include` annotations.
  2. `ReportDbContext_ModelConfiguresCoveringIndexes_AndCompositeIndexes`: Xác minh `IX_Reports_UpdatedAtUtc` có `SqlServer:Include` (`ClubId`, `Period`, `Status`) và composite index `(CreatedByUserId, Status)`.
  3. `ActivitySchemaUpgrader_SqlContainsNonCoveringIndexInspectionAndDrop_BeforeRecreate`: Xác minh script upgrader kiểm tra `is_included_column = 1` và thực hiện drop index cũ non-covering trước khi tạo lại.
  4. `ReportSchemaUpgrader_SqlContainsNonCoveringIndexInspectionAndDrop_BeforeRecreate`: Xác minh script upgrader của ReportService kiểm tra `is_included_column = 1` và drop index cũ trước khi tạo lại.
  5. `MicroserviceMigrations_AllTargetDatabasesHaveRegisteredMigrationsAndSnapshots`: Xác minh toàn bộ các dịch vụ cơ sở dữ liệu đều có Migration và ModelSnapshot hợp lệ trong assembly.
