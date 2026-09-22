# BÁO CÁO HOÀN THÀNH PHASE 14 — CROSS-SERVICE OUTBOX EXPANSION & DUAL-WRITE ELIMINATION

> **Thời điểm hoàn tất:** 2026-09-22  
> **Người thực hiện:** Antigravity AI Assistant  
> **Trạng thái:** HOÀN THÀNH (COMPLETE)

---

## 1. Phạm vi công việc đã thực hiện (Scope Completed)

1. **Khắc phục lỗi MEDIUM: Nuốt sự kiện hoàn tất xuất tệp và dual-write không an toàn (`REL-F01`):**
   - **Hiện trạng trước sửa đổi:**
     - Trong `ExportGenerationJob.cs`, sau khi lưu trạng thái export thành `Completed`, code thực hiện publish sự kiện `ExportCompletedEvent` lên Redis Stream thông qua khối `try ... catch (Exception) { logger.LogWarning(...); }`. Nếu Redis Stream gặp sự cố tạm thời hoặc mạng bị ngắt, sự kiện xuất tệp bị nuốt mất hoàn toàn, người dùng và các dịch vụ khác (NotificationService) không hề nhận được thông báo file xuất đã sẵn sàng.
     - Trong `ExportEndpoints.cs`, endpoint `CreateExport` thực hiện lưu bản ghi `ExportRequest` vào database sau đó mới gọi direct `eventBus.PublishAsync(ExportRequestedEvent)` mà không có bảo đảm tính nguyên tử.
   - **Giải pháp Transactional Outbox:**
     - Tích hợp `OutboxMessages` DbSet và cấu hình model `modelBuilder.ApplyOutboxConfiguration()` vào [`ExportDbContext.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ExportService/Data/ExportDbContext.cs).
     - Đăng ký `builder.Services.AddTransactionalOutbox<ExportDbContext>()` trong [`ExportService/Program.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ExportService/Program.cs).
     - Tái cấu trúc [`ExportGenerationJob.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ExportService/Services/ExportGenerationJob.cs): Thêm `ExportCompletedEvent` trực tiếp vào `OutboxMessages` bằng `db.AddOutboxMessage(...)` và commit cùng với trạng thái `ExportStatuses.Completed` trong một lệnh `db.SaveChangesAsync()` duy nhất. Gỡ bỏ hoàn toàn khối try/catch nuốt lỗi.
     - Tái cấu trúc [`ExportEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ExportService/Endpoints/ExportEndpoints.cs): Lưu `ExportRequest` và thông điệp outbox `ExportRequestedEvent` trong cùng một transaction cơ sở dữ liệu.
     - Tạo cơ chế tự động nâng cấp idempotent [`ExportSchemaUpgrader.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ExportService/Data/ExportSchemaUpgrader.cs) và sinh bản migration EF Core [`20260921220153_AddExportTransactionalOutbox.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ExportService/Migrations/20260921220153_AddExportTransactionalOutbox.cs).

2. **Khắc phục lỗi MEDIUM: ClubService và ActivityService thực hiện dual-write (DB + Redis) không nguyên tử (`REL-F05`):**
   - **Hiện trạng trước sửa đổi:**
     - Trong `ClubService`: Các endpoint tạo CLB (`ClubEndpoints.cs`), duyệt đơn tạo CLB (`ApplicationEndpoints.cs`), phê duyệt/từ chối/bổ nhiệm/xóa thành viên (`MembershipEndpoints.cs`), chuyển giao chủ nhiệm (`TransferEndpoints.cs`), xóa thành viên (`MemberManagementEndpoints.cs`), và gán chủ nhiệm (`ManagerEndpoints.cs`) đều gọi `await db.SaveChangesAsync()` rồi trực tiếp gọi `eventBus.PublishAsync(...)` lên Redis Stream. Nhiều nơi sử dụng khối `try/catch` bỏ qua lỗi bus ngầm (`// Background bus transient errors should not abort transaction`). Hậu quả: nếu Redis chết hoặc trễ, DB đã thay đổi nhưng event mất hút; hoặc nếu Redis lỗi không có try-catch, request thất bại dù DB đã cập nhật!
     - Trong `ActivityService`: Endpoint tạo hoạt động thông thường và tạo hoạt động từ báo cáo đã duyệt (`ActivityEndpoints.cs`) gọi `await db.SaveChangesAsync()` rồi direct gọi `eventBus.PublishAsync(ActivityCreatedEvent)`.
   - **Giải pháp Transactional Outbox trên toàn hệ thống:**
     - **ClubService:**
       - Thêm `OutboxMessages` DbSet và `ApplyOutboxConfiguration` vào [`ClubDbContext.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Data/ClubDbContext.cs).
       - Đăng ký `builder.Services.AddTransactionalOutbox<ClubDbContext>()` trong [`ClubService/Program.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Program.cs).
       - Tạo [`ClubSchemaUpgrader.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Data/ClubSchemaUpgrader.cs) và sinh migration EF Core [`20260921220436_AddClubTransactionalOutbox.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Migrations/20260921220436_AddClubTransactionalOutbox.cs).
       - Chuyển toàn bộ các sự kiện `ClubCreatedEvent` và `ClubAccessInvalidatedEvent` sang phương thức `db.AddOutboxMessage(...)` thực thi đồng thời trong transaction của `ClubDbContext`, loại bỏ hoàn toàn việc gọi trực tiếp `IEventBus` trong request thread.
     - **ActivityService:**
       - Thêm `OutboxMessages` DbSet và `ApplyOutboxConfiguration` vào [`ActivityDbContext.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ActivityService/Data/ActivityDbContext.cs).
       - Đăng ký `services.AddTransactionalOutbox<ActivityDbContext>()` trong [`ServiceCollectionExtensions.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ActivityService/Extensions/ServiceCollectionExtensions.cs).
       - Nâng cấp [`ActivitySchemaUpgrader.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ActivityService/Data/ActivitySchemaUpgrader.cs) bổ sung bảng và chỉ mục `OutboxMessages`.
       - Tạo [`ActivityDbContextFactory.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ActivityService/Data/ActivityDbContextFactory.cs) và sinh migration EF Core [`20260921220616_AddActivityTransactionalOutbox.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ActivityService/Migrations/20260921220616_AddActivityTransactionalOutbox.cs).
       - Thay thế toàn bộ lệnh direct publish `ActivityCreatedEvent` trong [`ActivityEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ActivityService/Endpoints/ActivityEndpoints.cs) bằng `db.AddOutboxMessage(...)` được commit nguyên tử cùng thực thể hoạt động.

---

## 2. Danh mục file đã chỉnh sửa & tạo mới

| STT | File | Trạng thái | Nội dung thay đổi |
| :---: | :--- | :---: | :--- |
| 1 | `src/Services/ExportService/Data/ExportDbContext.cs` | Sửa đổi | Bổ sung `DbSet<OutboxMessage>` và gọi `ApplyOutboxConfiguration()` |
| 2 | `src/Services/ExportService/Program.cs` | Sửa đổi | Đăng ký `AddTransactionalOutbox<ExportDbContext>()` và gọi `ExportSchemaUpgrader.ApplyAsync(db)` |
| 3 | `src/Services/ExportService/Services/ExportGenerationJob.cs` | Sửa đổi | Thêm `ExportCompletedEvent` vào outbox và commit cùng trạng thái export, loại bỏ try/catch nuốt lỗi |
| 4 | `src/Services/ExportService/Endpoints/ExportEndpoints.cs` | Sửa đổi | Commit `ExportRequest` và `ExportRequestedEvent` nguyên tử trong cùng transaction |
| 5 | `src/Services/ExportService/Data/ExportSchemaUpgrader.cs` | Tạo mới | Schema upgrader idempotent tạo bảng `OutboxMessages` và indexes cho ExportService |
| 6 | `src/Services/ExportService/Migrations/20260921220153_AddExportTransactionalOutbox.cs` | Tạo mới | Migration EF Core cho ExportService outbox |
| 7 | `src/Services/ExportService/Migrations/ExportDbContextModelSnapshot.cs` | Sửa đổi | Cập nhật ModelSnapshot cho ExportDbContext |
| 8 | `src/Services/ClubService/Data/ClubDbContext.cs` | Sửa đổi | Bổ sung `DbSet<OutboxMessage>` và gọi `ApplyOutboxConfiguration()` |
| 9 | `src/Services/ClubService/Program.cs` | Sửa đổi | Đăng ký `AddTransactionalOutbox<ClubDbContext>()` và gọi `ClubSchemaUpgrader.ApplyAsync(db)` |
| 10 | `src/Services/ClubService/Data/ClubSchemaUpgrader.cs` | Tạo mới | Schema upgrader idempotent tạo bảng `OutboxMessages` và indexes cho ClubService |
| 11 | `src/Services/ClubService/Endpoints/ClubEndpoints.cs` | Sửa đổi | Commit `Club` và `ClubCreatedEvent` nguyên tử trong transaction, bỏ direct event publish |
| 12 | `src/Services/ClubService/Endpoints/ApplicationEndpoints.cs` | Sửa đổi | Commit `Club`, duyệt đơn và `ClubCreatedEvent` nguyên tử trong transaction, bỏ direct publish |
| 13 | `src/Services/ClubService/Endpoints/MembershipEndpoints.cs` | Sửa đổi | Ghi `ClubAccessInvalidatedEvent` vào outbox trước `SaveChangesAsync` cho tất cả thay đổi trạng thái |
| 14 | `src/Services/ClubService/Endpoints/TransferEndpoints.cs` | Sửa đổi | Ghi `ClubAccessInvalidatedEvent` vào outbox trước `SaveChangesAsync` khi duyệt chuyển nhượng |
| 15 | `src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs` | Sửa đổi | Ghi `ClubAccessInvalidatedEvent` vào outbox trước `SaveChangesAsync` khi xóa thành viên |
| 16 | `src/Services/ClubService/Endpoints/ManagerEndpoints.cs` | Sửa đổi | Ghi `ClubAccessInvalidatedEvent` vào outbox trước `SaveChangesAsync` khi bổ nhiệm chủ nhiệm |
| 17 | `src/Services/ClubService/Migrations/20260921220436_AddClubTransactionalOutbox.cs` | Tạo mới | Migration EF Core cho ClubService outbox |
| 18 | `src/Services/ClubService/Migrations/ClubDbContextModelSnapshot.cs` | Sửa đổi | Cập nhật ModelSnapshot cho ClubDbContext |
| 19 | `src/Services/ActivityService/Data/ActivityDbContext.cs` | Sửa đổi | Bổ sung `DbSet<OutboxMessage>` và gọi `ApplyOutboxConfiguration()` |
| 20 | `src/Services/ActivityService/Extensions/ServiceCollectionExtensions.cs` | Sửa đổi | Đăng ký `AddTransactionalOutbox<ActivityDbContext>()` |
| 21 | `src/Services/ActivityService/Data/ActivitySchemaUpgrader.cs` | Sửa đổi | Bổ sung DDL idempotent tạo bảng `OutboxMessages` và claim indexes |
| 22 | `src/Services/ActivityService/Data/ActivityDbContextFactory.cs` | Tạo mới | Factory khởi tạo DbContext lúc design-time phục vụ EF Core migration |
| 23 | `src/Services/ActivityService/Endpoints/ActivityEndpoints.cs` | Sửa đổi | Commit `ClubActivity` và `ActivityCreatedEvent` nguyên tử trong transaction, bỏ direct publish |
| 24 | `src/Services/ActivityService/Migrations/20260921220616_AddActivityTransactionalOutbox.cs` | Tạo mới | Migration EF Core cho ActivityService outbox |
| 25 | `src/Services/ActivityService/Migrations/ActivityDbContextModelSnapshot.cs` | Sửa đổi | Cập nhật ModelSnapshot cho ActivityDbContext |
| 26 | `tests/Backend.StabilizationTests/Backend.StabilizationTests.csproj` | Sửa đổi | Thêm tham chiếu đến dự án `ExportService` |
| 27 | `tests/Backend.StabilizationTests/CrossServiceOutboxAndDualWriteTests.cs` | Tạo mới | Bộ 10 test cases kiểm thử outbox và xóa bỏ dual-write xuyên suốt Export, Club và Activity |

---

## 3. Kết quả Kiểm thử Hồi quy (Verification Results)

1. **Bộ test chuyên biệt Phase 14 (`CrossServiceOutboxAndDualWriteTests`):**
   - Đạt **10/10 tests PASSED (100%)**:
     - `ExportGenerationJob_OnCompletion_InsertsOutboxMessageAtomically`: Xác nhận hoàn tất export lưu đồng thời trạng thái `Completed`, thông tin file, và outbox event `ExportCompletedEvent` trong cùng transaction.
     - `ExportGenerationJob_OnFailure_MarksFailedAndDoesNotInsertOutboxMessage`: Xác nhận khi xuất file lỗi, trạng thái thành `Failed` và tuyệt đối KHÔNG sinh ra outbox event sai lệch.
     - `ExportService_ExportRequestedEvent_QueuedInOutbox`: Xác nhận yêu cầu xuất tệp đưa `ExportRequestedEvent` vào outbox.
     - `ClubService_CreateClub_PersistsOutboxMessageAtomically`: Xác nhận tạo CLB lưu đồng thời thực thể `Club` và `ClubCreatedEvent` vào outbox.
     - `ClubService_ApproveApplication_PersistsOutboxMessageAtomically`: Xác nhận duyệt đơn CLB lưu CLB, đơn duyệt, và `ClubCreatedEvent` nguyên tử.
     - `ClubService_MembershipStateChanges_PersistAccessInvalidatedOutboxMessages`: Xác nhận duyệt thành viên, từ chối, bổ nhiệm thủ quỹ, và hủy thành viên đều ghi `ClubAccessInvalidatedEvent` vào outbox mà không gọi direct publish.
     - `ClubService_OwnershipTransferAndManager_PersistAccessInvalidatedOutboxMessages`: Xác nhận duyệt chuyển giao chủ nhiệm ghi `ClubAccessInvalidatedEvent` vào outbox với user ID của cả hai chủ nhiệm cũ và mới.
     - `ActivityService_CreateActivity_PersistsOutboxMessageAtomically`: Xác nhận tạo hoạt động lưu `ClubActivity` và `ActivityCreatedEvent` nguyên tử vào outbox.
     - `ActivityService_CreateFromApprovedReport_PersistsOutboxMessageAtomically`: Xác nhận tạo hoạt động từ báo cáo duyệt lưu `ActivityCreatedEvent` vào outbox.
     - `OutboxPublisher_ProcessesExportClubAndActivityOutboxMessages_Successfully`: Xác nhận background service `OutboxPublisherBackgroundService` xử lý thông điệp pending thành công từ `ExportDbContext`, `ClubDbContext`, và `ActivityDbContext`, publish lên `IEventBus` và chuyển trạng thái sang `Published`.

2. **Kiểm tra toàn bộ Solution:**
   - `dotnet test ClubReportHub.sln -c Release`: **174/174 tests PASSED (100%)**
     - `AdminService.IntegrationTests`: 28/28 passed.
     - `Backend.StabilizationTests`: 146/146 passed.
   - `dotnet test tests/ClubReportHub.Tests/ClubReportHub.Tests.csproj -c Release`: **39/39 tests PASSED (100%)**
   - **Tổng cộng: 213/213 tests PASSED (0 lỗi, 0 skipped).**

3. **Kiểm tra Biên dịch:**
   - `dotnet build ClubReportHub.sln -c Release -warnaserror`: **0 Warnings, 0 Errors.**
