# BÁO CÁO HOÀN THÀNH PHASE 09 — CLUB RESOURCE AUTHORIZATION & MEMBER STATISTICS

> **Thời điểm hoàn tất:** 2026-09-21  
> **Người thực hiện:** Antigravity AI Assistant  
> **Trạng thái:** HOÀN THÀNH (COMPLETE)

---

## 1. Phạm vi công việc đã thực hiện (Scope Completed)

1. **Khắc phục lỗi HIGH: Cache phân quyền ClubAccess lưu 15 phút khiến user bị thu hồi quyền vẫn thao tác được (`SEC-F07`):**
   - **Rút ngắn Cache TTL an toàn:**
     - Trong [`ClubAccessClient.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Shared/ClubReportHub.Shared/Auth/ClubAccessClient.cs):
       - Rút ngắn thời gian tồn tại của cache từ 15 phút xuống còn **Sliding Expiration: 1 phút** và **Absolute Expiration: 3 phút**.
       - Điều này đảm bảo rằng các thay đổi phân quyền luôn phản ánh nhanh chóng ngay cả khi không có sự kiện thông báo.
   - **Hỗ trợ cờ `bypassCache` cho các thao tác sửa đổi & kiểm tra quyền cao:**
     - Bổ sung tham số `bool bypassCache = false` vào `GetMyAccessAsync(bearerToken, bypassCache, cancellationToken)`.
     - Cập nhật [`ReportExtensions.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ReportService/Extensions/ReportExtensions.cs) và [`ActivityEndpointHelpers.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ActivityService/Endpoints/ActivityEndpointHelpers.cs) để hỗ trợ `bypassCache`, cho phép các thao tác phê duyệt, tạo mới hoặc sửa đổi tài nguyên nhạy cảm ép buộc đọc dữ liệu mới nhất từ server nếu cần.
   - **Cơ chế Trục Xuất Cache Chủ Động qua Event Bus:**
     - Bổ sung sự kiện tích hợp [`ClubAccessInvalidatedEvent`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Shared/ClubReportHub.Shared/Events/IntegrationEvent.cs) với routing key `club.access.invalidated`.
     - Cung cấp các phương thức `InvalidateCache(int userId)` và `InvalidateUsers(IEnumerable<int> userIds)` trên [`ClubAccessClient`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Shared/ClubReportHub.Shared/Auth/ClubAccessClient.cs).
     - Tích hợp phát `ClubAccessInvalidatedEvent` tại toàn bộ các điểm đột biến quyền thành viên và quản lý CLB trong [`ClubService`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Endpoints/):
       - [`MembershipEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Endpoints/MembershipEndpoints.cs): Duyệt gia nhập (`ApproveMembership`), từ chối (`RejectMembership`), bổ nhiệm thủ quỹ (`AssignTreasurer`), bãi nhiệm thủ quỹ (`RemoveTreasurerRole`), xóa tư cách thành viên (`RemoveMembership`).
       - [`MemberManagementEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs): Xóa thành viên (`RemoveMember`).
       - [`ManagerEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Endpoints/ManagerEndpoints.cs): Bổ nhiệm chủ nhiệm mới (`AssignManager`), tự động vô hiệu hóa cache của cả chủ nhiệm cũ và mới.
       - [`TransferEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Endpoints/TransferEndpoints.cs): Duyệt bàn giao CLB (`ApproveTransferRequest`), vô hiệu hóa cache của cả người bàn giao và người tiếp nhận.

2. **Khắc phục lỗi MEDIUM: Thiếu negative caching và khóa chống cache stampede (`PERF-F04`):**
   - **Khóa Chống Cache Stampede (Dog-piling Protection):**
     - Triển khai `ConcurrentDictionary<int, SemaphoreSlim>` và mẫu hình double-checked locking trong [`ClubAccessClient.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Shared/ClubReportHub.Shared/Auth/ClubAccessClient.cs).
     - Khi hàng trăm request đồng thời yêu cầu phân quyền của cùng một user mà cache vừa hết hạn, chỉ duy nhất 1 request được phép gọi HTTP sang `ClubService`, các request còn lại đợi semaphore và tái sử dụng kết quả từ cache ngay khi request đầu hoàn tất.
   - **Negative Caching:**
     - Khi người dùng không thuộc bất kỳ câu lạc bộ nào (`access.Count == 0`), hệ thống lưu cache rỗng với TTL ngắn (1 phút) thay vì liên tục đánh query vào `ClubService`.

3. **Khắc phục lỗi HIGH: Query thống kê thành viên nhận userId ngoài CLB và tin join date từ client (`SEC-F11`):**
   - **Endpoint Roster Resolution Chuẩn Hóa:**
     - Nâng cấp endpoint `POST /api/clubs/{clubId}/member-roster/resolve` tại [`MemberManagementEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs):
       - Hỗ trợ lọc theo danh sách `UserIds`. Chỉ những thành viên có trạng thái `Approved` thuộc đúng `clubId` mới được trả về cùng ngày tham gia chuẩn `ReviewedAtUtc ?? RequestedAtUtc`.
   - **Xác Thực Tư Cách Thành Viên và Ngày Tham Gia Thẩm Quyền Tại `ActivityService`:**
     - Bổ sung `ClubMemberRosterClient.ResolveByUserIdsAsync` trong [`ClubMemberRosterClient.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ActivityService/Infrastructure/ClubMemberRosterClient.cs).
     - Cập nhật [`MemberStatisticsEndpoints.cs`](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/src/Services/ActivityService/Endpoints/MemberStatisticsEndpoints.cs):
       - **Thống kê hàng loạt (`POST /api/activities/clubs/{clubId}/member-statistics`):** Gọi `ClubMemberRosterClient` xác minh danh sách `userIds` được gửi lên. Người dùng không thuộc CLB lập tức bị loại bỏ khỏi thống kê; đồng thời ngày `JoinedAtUtc` do client tự gửi lên bị ghi đè hoàn toàn bằng ngày tham gia thẩm quyền từ cơ sở dữ liệu CLB.
       - **Thống kê chi tiết cá nhân (`POST /api/activities/clubs/{clubId}/member-statistics/detail`):** Gọi `ClubMemberRosterClient` kiểm tra `request.UserId`. Nếu không phải là thành viên hợp lệ của CLB, lập tức trả về **HTTP 404 Not Found**; nếu hợp lệ, `request.JoinedAtUtc` được ghi đè bằng ngày thực tế từ CLB trước khi tính toán lịch sử tham gia.

---

## 2. Danh mục file đã chỉnh sửa & tạo mới

| STT | File | Trạng thái | Nội dung thay đổi |
| :---: | :--- | :---: | :--- |
| 1 | `src/Shared/ClubReportHub.Shared/Events/IntegrationEvent.cs` | Sửa đổi | Bổ sung `ClubAccessInvalidatedEvent` |
| 2 | `src/Shared/ClubReportHub.Shared/Messaging/EventRoutingKeys.cs` | Sửa đổi | Bổ sung routing key `club.access.invalidated` |
| 3 | `src/Shared/ClubReportHub.Shared/Auth/ClubAccessClient.cs` | Sửa đổi | Cache TTL 1m/3m, `bypassCache`, negative cache, stampede lock, `InvalidateCache`/`InvalidateUsers` |
| 4 | `src/Services/ClubService/Contracts/ClubContracts.cs` | Sửa đổi | Mở rộng `ResolveClubMemberRosterRequest` hỗ trợ `IReadOnlyCollection<int>? UserIds` |
| 5 | `src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs` | Sửa đổi | Hỗ trợ lọc `UserIds` trong resolve roster; phát event khi xóa member; resolve `ActivityStatisticsClient` an toàn |
| 6 | `src/Services/ClubService/Endpoints/MembershipEndpoints.cs` | Sửa đổi | Phát `ClubAccessInvalidatedEvent` khi duyệt, từ chối, bổ nhiệm/bãi nhiệm thủ quỹ, xóa thành viên |
| 7 | `src/Services/ClubService/Endpoints/ManagerEndpoints.cs` | Sửa đổi | Phát `ClubAccessInvalidatedEvent` khi bổ nhiệm quản lý |
| 8 | `src/Services/ClubService/Endpoints/TransferEndpoints.cs` | Sửa đổi | Phát `ClubAccessInvalidatedEvent` khi duyệt chuyển giao CLB |
| 9 | `src/Services/ActivityService/Infrastructure/ClubMemberRosterClient.cs` | Sửa đổi | Bổ sung phương thức `ResolveByUserIdsAsync` |
| 10 | `src/Services/ActivityService/Endpoints/MemberStatisticsEndpoints.cs` | Sửa đổi | Kiểm tra danh sách thành viên và ghi đè ngày tham gia từ server cho cả batch và detail stats |
| 11 | `src/Services/ReportService/Extensions/ReportExtensions.cs` | Sửa đổi | Hỗ trợ tham số `bypassCache` trong `GetAuthorAccessAsync` |
| 12 | `src/Services/ActivityService/Endpoints/ActivityEndpointHelpers.cs` | Sửa đổi | Hỗ trợ tham số `bypassCache` trong `CanManageClubAsync` và `CanAuthorReportsAsync` |
| 13 | `tests/Backend.StabilizationTests/ClubAccessCacheInvalidationTests.cs` | Tạo mới | 6 unit & concurrency regression tests cho cache invalidation & stampede lock |
| 14 | `tests/Backend.StabilizationTests/MemberStatisticsValidationTests.cs` | Tạo mới | 4 integration regression tests cho member statistics validation & join date integrity |

---

## 3. Kết quả Kiểm thử & Đảm bảo Chất lượng (Quality Assurance)

- **Biên dịch:** Toàn bộ solution biên dịch thành công 100% với `-warnaserror` (0 cảnh báo, 0 lỗi).
- **Kiểm thử tự động:**
  - `Backend.StabilizationTests`: **104/104 tests PASSED** (+10 tests mới cho Phase 09).
  - `AdminService.IntegrationTests`: **28/28 tests PASSED**.
  - **Tổng cộng: 132/132 tests PASSED** (100% thành công, 0 hồi quy).
- **Nội dung 10 Test Cases Mới:**
  - `ClubAccessCacheInvalidationTests`:
    1. `GetMyAccessAsync_CacheHit_ReusesCachedValueWithoutCallingHttp`: Cache tái sử dụng đúng khi gọi liên tiếp.
    2. `InvalidateCache_ForcesNextCallToFetchFreshData`: Hàm `InvalidateCache` xóa cache user thành công, ép buộc gọi fresh HTTP.
    3. `InvalidateUsers_ForcesAllSpecifiedUsersToFetchFreshData`: Hàm `InvalidateUsers` xóa bulk nhiều user chính xác.
    4. `GetMyAccessAsync_WithBypassCacheTrue_AlwaysFetchesFresh`: Cờ `bypassCache: true` bỏ qua cache và lấy dữ liệu mới nhất.
    5. `GetMyAccessAsync_NegativeCaching_CachesEmptyAccess`: Kết quả rỗng được lưu cache tạm thời, tránh spam HTTP.
    6. `GetMyAccessAsync_StampedeProtection_AllowsOnlyOneConcurrentHttpRequest`: 20 luồng đồng thời chỉ kích hoạt duy nhất 1 cuộc gọi HTTP thực tế.
  - `MemberStatisticsValidationTests`:
    1. `ClubService_ResolveRosterMembers_ByUserIds_ReturnsOnlyApprovedClubMembers`: Endpoint resolve chỉ trả về thành viên được duyệt thuộc đúng CLB.
    2. `BatchMemberStatistics_ExcludesNonMembers_AndEnforcesAuthoritativeJoinedAtUtc`: Thống kê hàng loạt loại bỏ user ngoài CLB và ghi đè ngày gia nhập thẩm quyền.
    3. `DetailMemberStatistics_ReturnsNotFound_WhenUserIsNotClubMember`: Thống kê chi tiết trả về 404 Not Found khi user không thuộc CLB.
    4. `DetailMemberStatistics_EnforcesAuthoritativeJoinedAtUtc`: Thống kê chi tiết ghi đè ngày gia nhập thẩm quyền từ cơ sở dữ liệu CLB.
