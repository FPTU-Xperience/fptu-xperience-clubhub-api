# SỔ ĐĂNG KÝ DATABASE MIGRATIONS (DATABASE MIGRATION REGISTER)

> Tài liệu này quản lý vòng đời cơ sở dữ liệu, các bản migration, chiến lược an toàn dữ liệu và kế hoạch rollback cho toàn bộ 7 database dịch vụ của ClubReportHub.

---

## 1. Hiện trạng vòng đời Schema (Baseline tại Phase 00)

| Dịch vụ | Database Name | Cơ chế khởi tạo hiện tại | Số file Migration hiện có | Đánh giá nợ kỹ thuật |
| :--- | :--- | :--- | :---: | :--- |
| **AuthService** | `ClubReportHub_Auth` | EF Core Migrations | 4 | Đầy đủ, hoạt động chuẩn. |
| **ClubService** | `ClubReportHub_Club` | EF Core Migrations | 10 | Đầy đủ, đã chuẩn hóa index ở pass trước. |
| **ActivityService** | `ClubReportHub_Activity` | EF Core Migrations (`InitialActivityBaseline`) | 1 | Đã baseline chuẩn hóa sang EF Migrations (Phase 10), covering indexes configured. |
| **ReportService** | `ClubReportHub_Report` | EF Core Migrations + Raw Upgrader | 6 | Covering index `IX_Reports_UpdatedAtUtc` đã sửa; upgrader và model đồng bộ. |
| **FinanceService** | `ClubReportHub_Finance` | EF Core Migrations (`InitialFinanceBaseline`) | 1 | Đã baseline chuẩn hóa sang EF Migrations (Phase 10). |
| **ExportService** | `ClubReportHub_Export` | EF Core Migrations | 3 | Đầy đủ. |
| **NotificationService** | `ClubReportHub_Notification` | EF Core Migrations (`InitialNotificationBaseline`) | 1 | Đã baseline chuẩn hóa sang EF Migrations (Phase 10). |
| **AdminService** | `ClubReportHub_Admin` | EF Core Migrations | 2 | Đầy đủ. |

---

## 2. Kế hoạch Migrations trong Quá trình Remediation

| Phase | Dịch vụ | Tên Migration dự kiến | Mục đích | Rủi ro dữ liệu |
| :---: | :--- | :--- | :--- | :--- |
| **Phase 07** | AuthService | `AddSecurityVersionToUser` | Thêm cột `SecurityVersion` (default 1) phục vụ JWT freshness (`SEC-F06`) | An toàn, cột có default value = 1 |
| **Phase 10** | ActivityService | `InitialActivityBaseline` | Chuyển từ EnsureCreated sang EF Migration chính thức | Cần script đồng bộ an toàn dữ liệu cũ |
| **Phase 10** | FinanceService | `InitialFinanceBaseline` | Chuyển từ EnsureCreated sang EF Migration chính thức | Cần script đồng bộ an toàn dữ liệu cũ |
| **Phase 10** | NotificationService| `InitialNotificationBaseline` | Chuyển từ EnsureCreated sang EF Migration chính thức | Cần script đồng bộ an toàn dữ liệu cũ |
| **Phase 11** | ClubService | `20260921141638_AddClubDataIntegrityInvariants` | Thêm Filtered Unique Indexes (Active Manager, Pending Disband, Pending Transfer, Treasurer Slot 1-2), Check Constraint `CK_ClubMemberships_TreasurerSlot`, cột `TreasurerSlot` và `ConcurrencyToken` | Đã kèm SQL backfill script tự động gán slot (1, 2) cho thủ quỹ cũ và default NEWID() cho ConcurrencyToken |
| **Phase 12** | FinanceService | `20260921213929_AddActiveSettlementInvariant`| Thêm Filtered Unique Index trên `Settlements.BudgetProposalId` với `[Status] IN ('Submitted', 'Approved')` | Đã kèm drop index non-unique cũ idempotent trong Upgrader và migration; an toàn dữ liệu |
| **Phase 12** | ReportService | `20260921214032_AddReportPeriodTagUniqueConstraint` | Thêm Filtered Unique Index trên `Reports (ClubId, Period, Tag)` với `[ReportType] <> 'FUTURE_EVENT'` | Đã kèm drop index non-unique cũ idempotent trong Upgrader và migration; an toàn dữ liệu |
| **Phase 13** | ReportService | `20260921215039_AddOutboxMessageClaimFields` | Thêm cột lease/claim (`ClaimedAtUtc`, `ClaimExpiresAtUtc`, `ClaimedByInstanceId`, `ConcurrencyToken`) và index trên `OutboxMessages` (`REL-F02`) | Đã kèm SQL guards idempotent trong Upgrader và migration; an toàn dữ liệu |
| **Phase 13** | FinanceService | `20260921215054_AddOutboxMessageClaimFields` | Đồng bộ cột lease/claim và index trên `OutboxMessages` (`REL-F02`) | Đã kèm SQL guards idempotent trong Upgrader và migration; an toàn dữ liệu |
| **Phase 14** | ExportService | `20260921220153_AddExportTransactionalOutbox` | Thêm bảng `OutboxMessages` với đầy đủ cột lease/claim và index cho ExportService (`REL-F01`) | An toàn, tạo bảng mới |
| **Phase 14** | ClubService | `20260921220436_AddClubTransactionalOutbox` | Thêm bảng `OutboxMessages` với đầy đủ cột lease/claim và index cho ClubService (`REL-F05`) | An toàn, tạo bảng mới |
| **Phase 14** | ActivityService | `20260921220616_AddActivityTransactionalOutbox` | Thêm bảng `OutboxMessages` với đầy đủ cột lease/claim và index cho ActivityService (`REL-F05`) | An toàn, tạo bảng mới |

### 2.1. Danh mục Migration Đã Áp Dụng Thực Tế (Applied Migrations through Phase 14)

Dưới đây là danh sách các bản EF Core Migration thực tế hiện hữu trong repository cho đến hết Phase 14:

| Dịch vụ | Tên File Migration trong Repository | Phase thực hiện | Nội dung & Ràng buộc Schema |
| :--- | :--- | :---: | :--- |
| **AuthService** | `20260702031700_InitialCreate.cs`<br>`20260718090000_AddRefreshTokens.cs`<br>`20260913090000_GoogleOnlyAuthentication.cs`<br>`20260921200000_AddSecurityVersionToUser.cs` | Pre-remediation<br>Pre-remediation<br>Pre-remediation<br>**Phase 07** | Khởi tạo Identity & Users<br>Bảng RefreshTokens<br>Bỏ local password auth<br>Bổ sung cột `SecurityVersion` (default 1) cho JWT freshness |
| **ClubService** | `20260702031705_InitialCreate.cs`<br>`20260710025043_ClubProductWorkflows.cs`<br>`20260710084215_ClubRequestDetails.cs`<br>`20260711063430_MembershipReviewNote.cs`<br>`20260719112320_ExpandedClubApplicationForms.cs`<br>`20260719120207_SoftDeleteClubs.cs`<br>`20260721175111_AddDisbandAndTransferEntities.cs`<br>`20260722030350_AddClubMemberActivityManagement.cs`<br>`20260918043924_AddClubDiscoveryFieldsAndIndexes.cs`<br>`20260921141638_AddClubDataIntegrityInvariants.cs`<br>`20260921220436_AddClubTransactionalOutbox.cs` | Pre-remediation<br>Pre-remediation<br>Pre-remediation<br>Pre-remediation<br>Pre-remediation<br>Pre-remediation<br>Pre-remediation<br>Pre-remediation<br>Pre-remediation<br>**Phase 11**<br>**Phase 14** | Bảng Clubs & Memberships cơ sở<br>Workflow sản phẩm CLB<br>Chi tiết yêu cầu CLB<br>Ghi chú duyệt thành viên<br>Mẫu đơn đăng ký mở rộng<br>Soft delete trạng thái CLB<br>Bảng giải thể và chuyển nhượng<br>Quản lý hoạt động thành viên<br>Cột tìm kiếm và chỉ mục khám phá CLB<br>Filtered Unique Indexes (Manager, Disband, Transfer, Treasurer Slot 1-2), Check Constraint `CK_ClubMemberships_TreasurerSlot`, cột `TreasurerSlot` và `ConcurrencyToken`<br>Bảng `OutboxMessages` và indexes phục vụ transactional outbox (`REL-F05`) |
| **ActivityService** | `20260921140108_InitialActivityBaseline.cs`<br>`20260921220616_AddActivityTransactionalOutbox.cs` | **Phase 10**<br>**Phase 14** | Chuẩn hóa toàn bộ schema Activity từ `EnsureCreated` sang EF Core Migration chính thức; cấu hình covering index với INCLUDE<br>Bảng `OutboxMessages` và indexes phục vụ transactional outbox (`REL-F05`) |
| **ReportService** | `20260702031711_InitialCreate.cs`<br>`20260710025043_ReportTags.cs`<br>`20260721130327_EnhanceClubActivityReports.cs`<br>`20260722051641_AddReportUploadedFile.cs`<br>`20260722160804_AddReportSeedKey.cs`<br>`20260921214032_AddReportPeriodTagUniqueConstraint.cs`<br>`20260921215039_AddOutboxMessageClaimFields.cs` | Pre-remediation<br>Pre-remediation<br>Pre-remediation<br>Pre-remediation<br>Pre-remediation<br>**Phase 12**<br>**Phase 13** | Bảng Reports cơ sở<br>Gắn tag báo cáo<br>Bổ sung chi tiết hoạt động báo cáo<br>Tệp tải lên đính kèm báo cáo<br>Khóa định danh dữ liệu mẫu<br>Filtered Unique Index trên `Reports (ClubId, Period, Tag)` với filter `[ReportType] <> 'FUTURE_EVENT'`<br>Cột claim/lease (`ClaimedAtUtc`, `ClaimExpiresAtUtc`, `ClaimedByInstanceId`, `ConcurrencyToken`) và index trên `OutboxMessages` (`REL-F02`) |
| **FinanceService** | `20260921140143_InitialFinanceBaseline.cs`<br>`20260921213929_AddActiveSettlementInvariant.cs`<br>`20260921215054_AddOutboxMessageClaimFields.cs` | **Phase 10**<br>**Phase 12**<br>**Phase 13** | Chuẩn hóa schema Finance từ `EnsureCreated` sang EF Core Migration chính thức<br>Filtered Unique Index trên `Settlements.BudgetProposalId` với status `Submitted` hoặc `Approved`<br>Cột claim/lease và index trên `OutboxMessages` (`REL-F02`) |
| **ExportService** | `20260702031717_InitialCreate.cs`<br>`20260722030907_AddReportSnapshotFields.cs`<br>`20260921220153_AddExportTransactionalOutbox.cs` | Pre-remediation<br>Pre-remediation<br>**Phase 14** | Bảng ExportJobs cơ sở<br>Bổ sung trường snapshot dữ liệu báo cáo<br>Bảng `OutboxMessages` và indexes phục vụ transactional outbox (`REL-F01`) |
| **NotificationService**| `20260921140159_InitialNotificationBaseline.cs` | **Phase 10** | Chuẩn hóa schema Notifications từ `EnsureCreated` sang EF Core Migration chính thức |
| **AdminService** | `20260917114447_InitialAdminFoundation.cs` | Pre-remediation | Khởi tạo bảng Admin audit và quản trị hệ thống |

---

## 3. Quy trình Kiểm thử và Thực thi Migration An toàn

Trước khi áp dụng bất kỳ migration nào vào môi trường:
1. **Pre-flight Data Inspection:** Chạy query kiểm tra xem trong dữ liệu hiện có bản ghi nào vi phạm unique constraint sắp tạo hay không.
2. **Fresh DB Creation Test:** Kiểm tra migration chạy thành công trên database trống hoàn toàn (`EnsureCreated` hoặc `dotnet ef database update`).
3. **Upgrade DB Test:** Kiểm tra migration chạy trơn tru trên database đã có sẵn schema cũ mà không làm mất dữ liệu.
4. **Rollback Verification:** Kiểm tra phương thức `Down()` hoạt động chính xác và không phá hủy ngoài ý muốn.
