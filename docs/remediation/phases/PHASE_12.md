# BÁO CÁO HOÀN THÀNH PHASE 12 — FINANCE & REPORT DATABASE INVARIANTS

> **Thời điểm hoàn tất:** 2026-09-22  
> **Người thực hiện:** Antigravity AI Assistant  
> **Trạng thái:** HOÀN THÀNH (COMPLETE)

---

## 1. Phạm vi công việc đã thực hiện (Scope Completed)

1. **Khắc phục lỗi HIGH: Quy tắc duy nhất 1 quyết toán active không được ràng buộc ở DB (`DATA-F02`):**
   - **Mô hình Database & Invariant Check:**
     - Trong [`FinanceDbContext.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/FinanceService/Data/FinanceDbContext.cs):
       - Ràng buộc Filtered Unique Index trên bảng `Settlements`:
         ```csharp
         entity.HasIndex(x => x.BudgetProposalId)
             .IsUnique()
             .HasFilter("[Status] = 'Submitted' OR [Status] = 'Approved'");
         ```
       - Đảm bảo một đề xuất ngân sách (`BudgetProposal`) chỉ có tối đa duy nhất 1 quyết toán ở trạng thái active (`Submitted` hoặc `Approved`) trong cơ sở dữ liệu.
   - **Xử lý Concurrency & Chuẩn hóa Response:**
     - Trong [`SettlementEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/FinanceService/Endpoints/SettlementEndpoints.cs):
       - Tăng số phiên bản của đề xuất ngân sách (`proposal.Version++`) và gọi `SaveChangesAsync()` trước khi ghi nhận quyết toán mới, đảm bảo cơ chế optimistic lock ngăn chặn race conditions đồng thời.
       - Bọc thao tác thêm bản ghi `Settlement` và `FinanceTransaction` trong `try ... catch (DbUpdateException)` để chuyển hóa xung đột unique index / concurrency sang HTTP `409 Conflict` kèm thông điệp: `"This proposal already has an active settlement."`.
   - **Idempotent Upgrader & EF Core Migration:**
     - Trong [`FinanceSchemaUpgrader.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/FinanceService/Data/FinanceSchemaUpgrader.cs):
       - Thêm bước kiểm tra nếu `IX_Settlements_BudgetProposalId` đã tồn tại nhưng chưa có `is_unique = 1`, script sẽ tự động drop index cũ và tạo lại Filtered Unique Index an toàn và idempotent.
     - Tạo migration EF Core [`20260921213929_AddActiveSettlementInvariant.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/FinanceService/Migrations/20260921213929_AddActiveSettlementInvariant.cs) và cập nhật [`FinanceDbContextModelSnapshot.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/FinanceService/Migrations/FinanceDbContextModelSnapshot.cs).

2. **Khắc phục lỗi MEDIUM: Tính duy nhất của `(ClubId, Period, Tag)` chỉ kiểm tra ở ứng dụng (`DATA-F04`):**
   - **Mô hình Database Filtered Unique Index:**
     - Trong [`ReportDbContext.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Data/ReportDbContext.cs):
       - Ràng buộc Filtered Unique Index trên bảng `Reports`:
         ```csharp
         entity.HasIndex(x => new { x.ClubId, x.Period, x.Tag })
             .IsUnique()
             .HasFilter("[ReportType] <> 'FUTURE_EVENT'");
         ```
       - Ràng buộc chỉ áp dụng đối với các báo cáo định kỳ thường (`[ReportType] <> 'FUTURE_EVENT'`), cho phép các đề xuất sự kiện tương lai (`FUTURE_EVENT`) có thể nộp nhiều sự kiện trong cùng một học kỳ mà không bị vi phạm tính duy nhất.
   - **Xử lý Concurrency & Chuẩn hóa Response:**
     - Trong [`ReportCrudEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Endpoints/ReportCrudEndpoints.cs):
       - Cả `CreateReport` và `UpdateReport` đều bọc `SaveChangesAsync` trong `try ... catch (DbUpdateException)` để bắt vi phạm unique index và trả về HTTP `409 Conflict`: `"A report for club {clubId}, period '{period}', and tag '{tag}' already exists."`.
     - Trong [`ReportFileEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Endpoints/ReportFileEndpoints.cs):
       - `UploadReportFile` bọc `SaveChangesAsync` trong `try ... catch (DbUpdateException)` trả về HTTP `409 Conflict` nếu quá trình tải file báo cáo phát hiện vi phạm bản ghi trùng lặp.
   - **Idempotent Upgrader & EF Core Migration:**
     - Trong [`ReportSchemaUpgrader.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Data/ReportSchemaUpgrader.cs):
       - Tự động drop non-unique index `IX_Reports_ClubId_Period_Tag` nếu chưa có `is_unique = 1` và tạo lại Filtered Unique Index `IX_Reports_ClubId_Period_Tag` có filter `[ReportType] <> 'FUTURE_EVENT'`.
     - Tạo migration EF Core [`20260921214032_AddReportPeriodTagUniqueConstraint.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Data/Migrations/20260921214032_AddReportPeriodTagUniqueConstraint.cs) kèm SQL guard `IF OBJECT_ID` và cập nhật [`ReportDbContextModelSnapshot.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Data/Migrations/ReportDbContextModelSnapshot.cs).

---

## 2. Danh mục file đã chỉnh sửa & tạo mới

| STT | File | Trạng thái | Nội dung thay đổi |
| :---: | :--- | :---: | :--- |
| 1 | `src/Services/FinanceService/Data/FinanceDbContext.cs` | Sửa đổi | Cấu hình filtered unique index trên `Settlements.BudgetProposalId` với status Submitted/Approved |
| 2 | `src/Services/FinanceService/Endpoints/SettlementEndpoints.cs` | Sửa đổi | Proposal concurrency version bump, try-catch `DbUpdateException` -> 409 Conflict |
| 3 | `src/Services/FinanceService/Data/FinanceSchemaUpgrader.cs` | Sửa đổi | Nâng cấp schema tự động thay thế index thường bằng Filtered Unique Index |
| 4 | `src/Services/FinanceService/Migrations/20260921213929_AddActiveSettlementInvariant.cs` | Tạo mới | Migration EF Core tạo index filtered cho Settlement |
| 5 | `src/Services/FinanceService/Migrations/FinanceDbContextModelSnapshot.cs` | Sửa đổi | Snapshot cập nhật model FinanceService |
| 6 | `src/Services/ReportService/Data/ReportDbContext.cs` | Sửa đổi | Cấu hình filtered unique index trên `Reports(ClubId, Period, Tag)` với `[ReportType] <> 'FUTURE_EVENT'` |
| 7 | `src/Services/ReportService/Endpoints/ReportCrudEndpoints.cs` | Sửa đổi | Catch `DbUpdateException` -> 409 Conflict cho CreateReport và UpdateReport |
| 8 | `src/Services/ReportService/Endpoints/ReportFileEndpoints.cs` | Sửa đổi | Catch `DbUpdateException` -> 409 Conflict cho UploadReportFile |
| 9 | `src/Services/ReportService/Data/ReportSchemaUpgrader.cs` | Sửa đổi | Nâng cấp schema tự động thay thế index thường bằng Filtered Unique Index |
| 10 | `src/Services/ReportService/Data/Migrations/20260921214032_AddReportPeriodTagUniqueConstraint.cs` | Tạo mới | Migration EF Core tạo unique constraint/index cho Report |
| 11 | `src/Services/ReportService/Data/Migrations/ReportDbContextModelSnapshot.cs` | Sửa đổi | Snapshot cập nhật model ReportService |
| 12 | `tests/Backend.StabilizationTests/FinanceAndReportDataIntegrityTests.cs` | Tạo mới | Bộ 8 test cases kiểm thử metadata filtered index, logic xung đột nghiệp vụ và race conditions concurrency |

---

## 3. Kết quả Kiểm thử Hồi quy (Verification Results)

1. **Bộ test chuyên biệt Phase 12 (`FinanceAndReportDataIntegrityTests`):**
   - Đạt **8/8 tests PASSED (100%)**:
     - `FinanceDbContext_Metadata_ContainsActiveSettlementFilteredUniqueIndex`: Xác nhận metadata Filtered Unique Index trên `Settlements` có filter chứa `Submitted` và `Approved`.
     - `ReportDbContext_Metadata_ContainsPeriodTagFilteredUniqueIndex`: Xác nhận metadata Filtered Unique Index trên `Reports` có filter loại trừ `FUTURE_EVENT`.
     - `CreateSettlement_WhenProposalAlreadyHasActiveSettlement_ReturnsConflict409`: Xác nhận gửi quyết toán thứ hai trên đề xuất đã có quyết toán active bị chặn và trả về 409 Conflict.
     - `CreateSettlement_ConcurrentRequests_OnlyOneSucceedsSecondReturnsConflict409`: Xác nhận 2 request gửi quyết toán đồng thời chỉ có 1 request thành công và request còn lại nhận 409 Conflict.
     - `CreateReport_WhenReportExistsForClubPeriodTag_ReturnsConflict409`: Xác nhận tạo báo cáo trùng `(ClubId, Period, Tag)` bị từ chối với 409 Conflict.
     - `CreateReport_FutureEvents_AllowsMultipleReportsSamePeriodAndTag`: Xác nhận báo cáo loại `FUTURE_EVENT` được phép có nhiều bản ghi cùng `(ClubId, Period, Tag)`.
     - `UpdateReport_WhenPeriodAndTagConflictWithExistingReport_ReturnsConflict409`: Xác nhận cập nhật báo cáo sang trùng kỳ/tag của báo cáo khác bị từ chối với 409 Conflict.
     - `UploadReportFile_WhenReportExistsForClubPeriodTag_ReturnsConflict409`: Xác nhận tải file tạo báo cáo trùng kỳ/tag bị từ chối với 409 Conflict.

2. **Kiểm tra toàn bộ Solution:**
   - `dotnet test ClubReportHub.sln -c Release`: **156/156 tests PASSED (100%)**
     - `AdminService.IntegrationTests`: 28/28 passed.
     - `Backend.StabilizationTests`: 128/128 passed.
   - `dotnet test tests/ClubReportHub.Tests/ClubReportHub.Tests.csproj -c Release`: **39/39 tests PASSED (100%)**
   - **Tổng cộng: 195/195 tests PASSED (0 lỗi, 0 skipped).**

3. **Kiểm tra Biên dịch:**
   - `dotnet build ClubReportHub.sln -c Release -warnaserror`: **0 Warnings, 0 Errors.**
