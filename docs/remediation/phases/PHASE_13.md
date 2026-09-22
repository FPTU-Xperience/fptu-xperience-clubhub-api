# BÁO CÁO HOÀN THÀNH PHASE 13 — REPORT WORKFLOW ATOMICITY & OUTBOX PUBLISHING CONCURRENCY

> **Thời điểm hoàn tất:** 2026-09-22  
> **Người thực hiện:** Antigravity AI Assistant  
> **Trạng thái:** HOÀN THÀNH (COMPLETE)

---

## 1. Phạm vi công việc đã thực hiện (Scope Completed)

1. **Khắc phục lỗi MEDIUM: Ghi Report state, audit log và outbox không nguyên tử trong 1 transaction (`DATA-F03`):**
   - **Hiện trạng trước sửa đổi:**
     - Các endpoint thay đổi trạng thái quy trình báo cáo (`SubmitReport`, `LinkFutureEventBudget`, `ReviewReport`, `ApproveReport`, `RejectReport`) cũng như tạo và cập nhật (`CreateReport`, `UpdateReport`, các thao tác đính kèm) thực hiện gọi `await db.SaveChangesAsync()` ghi trạng thái báo cáo và thông điệp outbox, sau đó mới gọi tiếp `await AuditHelper.AddAuditAsync()` sinh ra một lệnh `SaveChangesAsync()` thứ hai riêng biệt.
     - Nếu tiến trình bị gián đoạn hoặc gặp sự cố cơ sở dữ liệu ở bước ghi audit log, trạng thái báo cáo và sự kiện outbox đã được commit trong khi bản ghi kiểm toán bị thiếu hụt, phá vỡ tính toàn vẹn kiểm toán (audit trail integrity).
   - **Giải pháp tái cấu trúc nguyên tử:**
     - Nâng cấp [`AuditHelper.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Extensions/AuditHelper.cs) bổ sung phương thức đồng bộ `public static void AddAudit(...)` cho phép thêm trực tiếp thực thể `AuditLog` vào DbContext mà không gọi lưu ngay.
     - Đồng bộ hóa toàn bộ các endpoint workflow trong [`ReportWorkflowEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Endpoints/ReportWorkflowEndpoints.cs), [`ReportCrudEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Endpoints/ReportCrudEndpoints.cs), và [`ReportFileEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Endpoints/ReportFileEndpoints.cs): gọi `AuditHelper.AddAudit(...)` *trước* khi thực hiện lưu, gom toàn bộ thay đổi trạng thái báo cáo (`Report`), sự kiện tích hợp outbox (`OutboxMessage`), và lịch sử kiểm toán (`AuditLog`) vào một lệnh `await db.SaveChangesAsync()` duy nhất.
     - Đảm bảo tính nguyên tử ACID 100%: hoặc toàn bộ cùng thành công và được ghi nhận, hoặc toàn bộ rollback nếu có bất kỳ lỗi nào xảy ra.

2. **Khắc phục lỗi MEDIUM: Outbox publisher đọc bản ghi pending không có cơ chế claim/lock, gây publish trùng (`REL-F02`):**
   - **Hiện trạng trước sửa đổi:**
     - Trong [`OutboxPublisherBackgroundService.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Shared/ClubReportHub.Shared/Data/OutboxPublisherBackgroundService.cs), background worker truy vấn các bản ghi có `Status == OutboxMessageStatus.Pending` mà không có cơ chế lock hoặc lease.
     - Khi chạy trong môi trường phân tán đa replica hoặc nhiều container worker cùng lúc, các worker sẽ đọc cùng một tập bản ghi pending và cùng publish lên Redis Stream, dẫn đến xuất bản trùng lặp (duplicate events) trên toàn bộ hệ thống.
   - **Thiết kế cơ chế Distributed Lease / Claim nguyên tử:**
     - Trong [`OutboxMessage.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Shared/ClubReportHub.Shared/Data/OutboxMessage.cs):
       - Bổ sung các trường quản lý lease/claim:
         - `public DateTimeOffset? ClaimedAtUtc { get; set; }`
         - `public DateTimeOffset? ClaimExpiresAtUtc { get; set; }`
         - `public string? ClaimedByInstanceId { get; set; }`
         - `[ConcurrencyCheck] public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();`
       - Bổ sung index tối ưu truy vấn claim: `IX_OutboxMessages_Status_ClaimExpiresAtUtc_OccurredAtUtc` trên `([Status], [ClaimExpiresAtUtc], [OccurredAtUtc])`.
     - Trong [`OutboxOptions.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Shared/ClubReportHub.Shared/Data/OutboxOptions.cs):
       - Bổ sung cấu hình thời hạn lease: `public TimeSpan ClaimDuration { get; set; } = TimeSpan.FromSeconds(30);`.
     - Trong [`OutboxPublisherBackgroundService.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Shared/ClubReportHub.Shared/Data/OutboxPublisherBackgroundService.cs):
       - Mỗi instance worker sinh một `_instanceId` riêng biệt (`Guid.NewGuid().ToString("N")`).
       - Truy vấn các ứng viên hợp lệ: `Status == Pending` HOẶC `Status == Processing` nhưng đã hết hạn lease (`ClaimExpiresAtUtc <= nowUtc` do worker trước bị crash).
       - Thực hiện claim nguyên tử bằng optimistic concurrency lock: gán `Status = Processing`, `ClaimedByInstanceId = _instanceId`, `ClaimExpiresAtUtc = nowUtc.Add(_options.ClaimDuration)`, tăng `ConcurrencyToken = Guid.NewGuid()` và lưu vào cơ sở dữ liệu.
       - Nếu có worker khác tranh chấp cùng lúc trên cùng bản ghi, `DbUpdateConcurrencyException` sẽ được bắt và tách entity (`Detached`), bảo đảm đúng 1 worker duy nhất chiếm được quyền xử lý bản ghi đó.
       - Sau khi publish thành công lên `IEventBus`, chuyển `Status = Published`, xóa `ClaimExpiresAtUtc = null`, ghi nhận `ProcessedAtUtc = nowUtc`, và tăng `ConcurrencyToken`.
       - Nếu publish gặp lỗi, tăng `RetryCount`, ghi lại `ErrorMessage`, và nếu chưa vượt quá `MaxRetries`, trả lại `Status = Pending` để worker khác hoặc chu kỳ sau retry.
   - **Schema Migrations & Upgraders:**
     - Nâng cấp idempotent SQL trong [`ReportSchemaUpgrader.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Data/ReportSchemaUpgrader.cs) và [`FinanceSchemaUpgrader.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/FinanceService/Data/FinanceSchemaUpgrader.cs).
     - Tạo 2 bản migration EF Core:
       - ReportService: [`20260921215039_AddOutboxMessageClaimFields.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Data/Migrations/20260921215039_AddOutboxMessageClaimFields.cs)
       - FinanceService: [`20260921215054_AddOutboxMessageClaimFields.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/FinanceService/Migrations/20260921215054_AddOutboxMessageClaimFields.cs)

---

## 2. Danh mục file đã chỉnh sửa & tạo mới

| STT | File | Trạng thái | Nội dung thay đổi |
| :---: | :--- | :---: | :--- |
| 1 | `src/Shared/ClubReportHub.Shared/Data/OutboxMessage.cs` | Sửa đổi | Thêm các trường lease/claim (`ClaimedAtUtc`, `ClaimExpiresAtUtc`, `ClaimedByInstanceId`, `ConcurrencyToken`) và cấu hình index tối ưu |
| 2 | `src/Shared/ClubReportHub.Shared/Data/OutboxOptions.cs` | Sửa đổi | Thêm cấu hình `ClaimDuration` (mặc định 30 giây) |
| 3 | `src/Shared/ClubReportHub.Shared/Data/OutboxPublisherBackgroundService.cs` | Sửa đổi | Triển khai cơ chế lease/claim nguyên tử với optimistic concurrency lock, tự động reclaim bản ghi hết hạn lease |
| 4 | `src/Shared/ClubReportHub.Shared/ClubReportHub.Shared.csproj` | Sửa đổi | Thêm `InternalsVisibleTo` cấp quyền kiểm thử cho `Backend.StabilizationTests` |
| 5 | `src/Services/ReportService/Extensions/AuditHelper.cs` | Sửa đổi | Thêm phương thức đồng bộ `AddAudit` để queue bản ghi audit vào DbContext trước khi save |
| 6 | `src/Services/ReportService/Endpoints/ReportWorkflowEndpoints.cs` | Sửa đổi | Gom `Report` state, `OutboxMessage`, và `AuditLog` vào 1 lệnh `SaveChangesAsync` duy nhất cho toàn bộ workflow |
| 7 | `src/Services/ReportService/Endpoints/ReportCrudEndpoints.cs` | Sửa đổi | Gom `Report` create/update, `OutboxMessage`, và `AuditLog` vào 1 lệnh `SaveChangesAsync` |
| 8 | `src/Services/ReportService/Endpoints/ReportFileEndpoints.cs` | Sửa đổi | Gom các thao tác file/attachment và `AuditLog` vào 1 lệnh `SaveChangesAsync` |
| 9 | `src/Services/ReportService/Data/ReportSchemaUpgrader.cs` | Sửa đổi | Nâng cấp bảng `OutboxMessages` bổ sung các cột lease/claim và index idempotent |
| 10 | `src/Services/FinanceService/Data/FinanceSchemaUpgrader.cs` | Sửa đổi | Đồng bộ bảng `OutboxMessages` bổ sung các cột lease/claim và index idempotent |
| 11 | `src/Services/ReportService/Data/Migrations/20260921215039_AddOutboxMessageClaimFields.cs` | Tạo mới | Migration EF Core cho ReportService |
| 12 | `src/Services/ReportService/Data/Migrations/ReportDbContextModelSnapshot.cs` | Sửa đổi | Cập nhật ModelSnapshot cho ReportService |
| 13 | `src/Services/FinanceService/Migrations/20260921215054_AddOutboxMessageClaimFields.cs` | Tạo mới | Migration EF Core cho FinanceService |
| 14 | `src/Services/FinanceService/Migrations/FinanceDbContextModelSnapshot.cs` | Sửa đổi | Cập nhật ModelSnapshot cho FinanceService |
| 15 | `tests/Backend.StabilizationTests/ReportWorkflowAtomicityAndOutboxConcurrencyTests.cs` | Tạo mới | Bộ 8 test cases kiểm thử lease/claim đa luồng, reclaim lease hết hạn, và tính nguyên tử của quy trình báo cáo |

---

## 3. Kết quả Kiểm thử Hồi quy (Verification Results)

1. **Bộ test chuyên biệt Phase 13 (`ReportWorkflowAtomicityAndOutboxConcurrencyTests`):**
   - Đạt **8/8 tests PASSED (100%)**:
     - `OutboxPublisher_ConcurrentWorkers_ClaimMessagesWithoutDuplicatePublishing`: Xác nhận 3 worker chạy đồng thời xử lý 10 bản ghi pending mà không hề bị publish trùng (mỗi event được publish đúng 1 lần duy nhất).
     - `OutboxPublisher_ExpiredClaim_IsReclaimedByActiveWorker`: Xác nhận một bản ghi bị khóa bởi worker đã chết (expired lease) được worker còn sống tự động claim lại và publish thành công.
     - `OutboxPublisher_UnexpiredClaim_IsNotClaimedByOtherWorkers`: Xác nhận bản ghi đang có lease hợp lệ không bị worker khác can thiệp.
     - `OutboxMessage_Metadata_ContainsClaimAndConcurrencyProperties`: Xác nhận metadata cấu hình thuộc tính `ConcurrencyToken`, `ClaimedByInstanceId`, và index composite trên database.
     - `SubmitReport_ReportStateOutboxAndAudit_SavedAtomically`: Xác nhận `SubmitReport` commit đồng thời trạng thái `UnderReview`/`Submitted`, outbox event `ReportSubmittedEvent`, và audit log `"Submit"` trong cùng 1 transaction.
     - `ReviewReport_ReportStateOutboxAndAudit_SavedAtomically`: Xác nhận `ReviewReport` commit đồng thời trạng thái `UnderReview`, outbox event, và audit log `"ManagerReview"`.
     - `ApproveReport_ReportStateOutboxFeedbackAndAudit_SavedAtomically`: Xác nhận `ApproveReport` commit đồng thời trạng thái `Approved`, feedback phê duyệt, outbox event `ReportApprovedEvent`, và audit log `"Approve"`.
     - `RejectReport_ReportStateOutboxFeedbackAndAudit_SavedAtomically`: Xác nhận `RejectReport` commit đồng thời trạng thái `Rejected`, feedback từ chối, outbox event `ReportRejectedEvent`, và audit log `"Reject"`.

2. **Kiểm tra toàn bộ Solution:**
   - `dotnet test ClubReportHub.sln -c Release`: **164/164 tests PASSED (100%)**
     - `AdminService.IntegrationTests`: 28/28 passed.
     - `Backend.StabilizationTests`: 136/136 passed.
   - `dotnet test tests/ClubReportHub.Tests/ClubReportHub.Tests.csproj -c Release`: **39/39 tests PASSED (100%)**
   - **Tổng cộng: 203/203 tests PASSED (0 lỗi, 0 skipped).**

3. **Kiểm tra Biên dịch:**
   - `dotnet build ClubReportHub.sln -c Release -warnaserror`: **0 Warnings, 0 Errors.**
