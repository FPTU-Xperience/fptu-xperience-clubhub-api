# BÁO CÁO HOÀN THÀNH PHASE 11 — CLUB SERVICE DATA INTEGRITY & CONCURRENCY INVARIANTS

> **Thời điểm hoàn tất:** 2026-09-22  
> **Người thực hiện:** Antigravity AI Assistant  
> **Trạng thái:** HOÀN THÀNH (COMPLETE)

---

## 1. Phạm vi công việc đã thực hiện (Scope Completed)

1. **Khắc phục lỗi HIGH: Quy tắc tối đa 2 thủ quỹ chỉ kiểm tra bằng application check (race condition) (`DATA-F01`):**
   - **Mô hình Database & Invariant Check:**
     - Trong [`ClubMembership.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Models/ClubMembership.cs), bổ sung thuộc tính `public int? TreasurerSlot { get; set; }`.
     - Trong [`ClubDbContext.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Data/ClubDbContext.cs):
       - Ràng buộc Check Constraint cấp bảng: `CK_ClubMemberships_TreasurerSlot` với biểu thức `[TreasurerSlot] IS NULL OR [TreasurerSlot] IN (1, 2)`.
       - Ràng buộc Filtered Unique Index: `entity.HasIndex(x => new { x.ClubId, x.TreasurerSlot }).IsUnique().HasFilter("[TreasurerSlot] IS NOT NULL");`.
       - Bổ sung `entity.Property(x => x.ConcurrencyToken).IsConcurrencyToken()` trên entity `Club`.
   - **Logic Phân Bổ Slot & Xử Lý Concurrency:**
     - Trong [`MembershipEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Endpoints/MembershipEndpoints.cs):
       - Khi gán quyền thủ quỹ (`AssignTreasurer`), thuật toán kiểm tra các slot đã chiếm (1 hoặc 2) và tự động gán slot trống thấp nhất. Nếu cả slot 1 và slot 2 đều đã có chủ, lập tức trả về `409 Conflict`.
       - Tăng `club.ConcurrencyToken = Guid.NewGuid()` trước khi lưu. Bọc `SaveChangesAsync` trong `try ... catch (DbUpdateException)` để chuyển hóa xung đột concurrency/unique index thành HTTP `409 Conflict` kèm thông báo tường minh: `"A club can have at most two treasurers."`.
       - Khi hạ quyền thủ quỹ về thành viên thường (`RemoveTreasurerRole`, `ApplyMemberRole`) hoặc xóa/từ chối membership, slot được giải phóng (`TreasurerSlot = null`) để thành viên khác có thể tái sử dụng slot này.
   - **Migration Database:**
     - Tạo bản migration [`20260921141638_AddClubDataIntegrityInvariants.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Migrations/20260921141638_AddClubDataIntegrityInvariants.cs) tích hợp backfill script tự động gán slot 1 và 2 cho các thủ quỹ hợp lệ hiện có theo `ClubId` và `ROW_NUMBER()`.

2. **Khắc phục lỗi HIGH: Quy tắc duy nhất 1 chủ nhiệm active chỉ kiểm tra ở tầng ứng dụng (`DATA-F05`):**
   - **Ràng buộc Database Filtered Unique Index:**
     - Trong [`ClubDbContext.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Data/ClubDbContext.cs):
       - Filtered Unique Index 1: `entity.HasIndex(x => x.ClubId).IsUnique().HasFilter("[IsActive] = 1");` bảo đảm mỗi CLB chỉ có duy nhất 1 bản ghi chủ nhiệm đang hoạt động.
       - Filtered Unique Index 2: `entity.HasIndex(x => x.ManagerUserId).IsUnique().HasFilter("[IsActive] = 1");` bảo đảm mỗi user chỉ có thể làm chủ nhiệm của duy nhất 1 CLB tại cùng một thời điểm.
   - **Xử lý Concurrency & Chuẩn hóa Response:**
     - Trong [`ManagerEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Endpoints/ManagerEndpoints.cs):
       - Cập nhật `club.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync();` trước khi thay đổi trạng thái manager assignment, ngăn chặn triệt để tình trạng ghi bẩn (dirty insert) dưới các request đồng thời.
       - Bắt lỗi `DbUpdateException` và chuẩn hóa trả về HTTP `409 Conflict`: `"Each club owner can manage one club only and each club can have only one active owner."`.
       - Chuyển đổi kết quả trả về qua `ClubMappers.ToResponse(updated)` thay vì trả raw entity EF Core, loại bỏ triệt để lỗi JSON serialization cycle.

3. **Khắc phục lỗi MEDIUM: Đơn giải thể hoặc chuyển giao CLB đang chờ chỉ check ở app (`DATA-F06`):**
   - **Ràng buộc Database Filtered Unique Index:**
     - Trong [`ClubDbContext.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Data/ClubDbContext.cs):
       - Ràng buộc trên `ClubDisbandRequests`: `entity.HasIndex(x => x.ClubId).IsUnique().HasFilter("[Status] = 'Pending'");`
       - Ràng buộc trên `ClubOwnershipTransfers`: `entity.HasIndex(x => x.ClubId).IsUnique().HasFilter("[Status] = 'Pending'");`
   - **Chuẩn hóa Mã Lỗi HTTP & Khóa Concurrency:**
     - Trong [`DisbandEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Endpoints/DisbandEndpoints.cs) và [`TransferEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Endpoints/TransferEndpoints.cs):
       - Chuẩn hóa mã trả về khi đã có đơn đang chờ từ `400 BadRequest` sang `409 Conflict`.
       - Tăng `club.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync();` để khóa tuần tự tiến trình trên aggregate CLB trước khi thêm bản ghi yêu cầu mới.
       - Bắt `DbUpdateException` và trả về `409 Conflict` chuẩn hóa.
       - Bổ sung kiểm tra hợp lệ khi duyệt đơn chuyển nhượng: người nhận chuyển nhượng không được đang là chủ nhiệm của một CLB khác (trả `409 Conflict`).

---

## 2. Danh mục file đã chỉnh sửa & tạo mới

| STT | File | Trạng thái | Nội dung thay đổi |
| :---: | :--- | :---: | :--- |
| 1 | `src/Services/ClubService/Models/Club.cs` | Sửa đổi | Thêm `[ConcurrencyCheck] public Guid ConcurrencyToken { get; set; }` |
| 2 | `src/Services/ClubService/Models/ClubMembership.cs` | Sửa đổi | Thêm `public int? TreasurerSlot { get; set; }` |
| 3 | `src/Services/ClubService/Services/ClubMemberRoleRules.cs` | Sửa đổi | Cập nhật `ApplyMemberRole` reset `TreasurerSlot = null` |
| 4 | `src/Services/ClubService/Data/ClubDbContext.cs` | Sửa đổi | Cấu hình filtered unique indexes cho manager, disband, transfer, treasurer slot, check constraint `CK_ClubMemberships_TreasurerSlot`, và concurrency token |
| 5 | `src/Services/ClubService/Endpoints/MembershipEndpoints.cs` | Sửa đổi | Logic gán slot 1-2, bump ConcurrencyToken, giải phóng slot, catch `DbUpdateException` -> 409 Conflict |
| 6 | `src/Services/ClubService/Endpoints/ManagerEndpoints.cs` | Sửa đổi | Concurrency token bump, bắt `DbUpdateException` -> 409 Conflict, serialize response qua `ClubMappers.ToResponse` |
| 7 | `src/Services/ClubService/Endpoints/DisbandEndpoints.cs` | Sửa đổi | Chuẩn hóa 409 Conflict khi đã có đơn pending, concurrency lock trên Club |
| 8 | `src/Services/ClubService/Endpoints/TransferEndpoints.cs` | Sửa đổi | Chuẩn hóa 409 Conflict khi đã có đơn pending, concurrency lock trên Club, check new owner manager status |
| 9 | `src/Services/ClubService/Endpoints/ApplicationEndpoints.cs` | Sửa đổi | Catch `DbUpdateException` -> 409 Conflict khi duyệt đơn thành lập CLB |
| 10 | `src/Tools/DemoDataSeeder/DemoDatasetSeeder.cs` | Sửa đổi | Gán `TreasurerSlot = 1` cho thủ quỹ seed sẵn |
| 11 | `src/Services/ClubService/Migrations/20260921141638_AddClubDataIntegrityInvariants.cs` | Tạo mới | Migration với SQL backfill gán slot 1-2 cho các thủ quỹ cũ và NEWID() cho ConcurrencyToken |
| 12 | `src/Services/ClubService/Migrations/ClubDbContextModelSnapshot.cs` | Sửa đổi | Cập nhật ModelSnapshot với các index, check constraint, và thuộc tính mới |
| 13 | `tests/Backend.StabilizationTests/ClubDataIntegrityConcurrencyTests.cs` | Tạo mới | Bộ 11 test cases kiểm thử toàn diện metadata constraint, tuần tự slot, giải phóng slot, và concurrent race conditions |
| 14 | `tests/Backend.StabilizationTests/RefreshTokenSecurityTests.cs` | Sửa đổi | Sử dụng shared-cache in-memory SQLite connection string để hỗ trợ concurrent transactions độc lập |
| 15 | `src/Services/AuthService/Services/RefreshTokenService.cs` | Sửa đổi | Bắt `DbUpdateException` rộng hơn (bao gồm database locks và concurrency conflicts) |

---

## 3. Kết quả Kiểm thử Hồi quy (Verification Results)

1. **Bộ test chuyên biệt Phase 11 (`ClubDataIntegrityConcurrencyTests`):**
   - Đạt **11/11 tests PASSED (100%)**:
     - `ClubDbContext_Metadata_ContainsRequiredFilteredIndexesAndCheckConstraints`: Kiểm tra sự hiện diện của 5 filtered unique index và 1 check constraint.
     - `AssignTreasurer_SequentialAssignments_AllocatesSlotOneAndTwo`: Kiểm tra gán slot 1 và slot 2 tuần tự.
     - `AssignTreasurer_ThirdTreasurer_ReturnsConflict409`: Kiểm tra từ chối thủ quỹ thứ 3 với 409 Conflict.
     - `AssignTreasurer_SlotReuse_WhenTreasurerRemoved_ReusesFreeSlot`: Kiểm tra giải phóng và tái sử dụng slot trống.
     - `AssignTreasurer_ConcurrentAssignments_AllowsMaxTwoAndNormalizesConflict409`: Kiểm tra đua tranh gán thủ quỹ đồng thời.
     - `AssignManager_WhenManagerAlreadyManagesAnotherClub_ReturnsConflict409`: Kiểm tra 1 manager chỉ quản lý 1 CLB.
     - `AssignManager_ConcurrentAssignments_AllowsOnlyOneActiveManagerPerClub`: Kiểm tra đua tranh gán chủ nhiệm đồng thời.
     - `SubmitDisbandRequest_WhenPendingRequestExists_ReturnsConflict409`: Kiểm tra chặn đơn giải thể thứ 2.
     - `SubmitDisbandRequest_ConcurrentRequests_OnlyOneSucceedsSecondReturnsConflict409`: Kiểm tra đua tranh gửi đơn giải thể đồng thời.
     - `SubmitTransferRequest_WhenPendingRequestExists_ReturnsConflict409`: Kiểm tra chặn đơn chuyển giao thứ 2.
     - `SubmitTransferRequest_ConcurrentRequests_OnlyOneSucceedsSecondReturnsConflict409`: Kiểm tra đua tranh gửi đơn chuyển giao đồng thời.
     - `ApproveTransferRequest_WhenNewOwnerManagesAnotherClub_ReturnsConflict409`: Kiểm tra chặn duyệt chuyển giao nếu người nhận đã là chủ nhiệm CLB khác.

2. **Kiểm tra toàn bộ Solution:**
   - `dotnet test ClubReportHub.sln -c Release`: **148/148 tests PASSED (100%)**
     - `AdminService.IntegrationTests`: 28/28 passed.
     - `Backend.StabilizationTests`: 120/120 passed.
   - `dotnet test tests/ClubReportHub.Tests/ClubReportHub.Tests.csproj -c Release`: **39/39 tests PASSED (100%)**
   - **Tổng cộng: 187/187 tests PASSED (0 lỗi, 0 skipped).**

3. **Kiểm tra Biên dịch:**
   - `dotnet build ClubReportHub.sln -c Release -warnaserror`: **0 Warnings, 0 Errors.**
