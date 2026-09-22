# BÁO CÁO HOÀN THÀNH PHASE 07 — JWT AUTHORIZATION FRESHNESS & REVOCATION

> **Thời điểm hoàn tất:** 2026-09-21  
> **Người thực hiện:** Antigravity AI Assistant  
> **Trạng thái:** HOÀN THÀNH (COMPLETE)

---

## 1. Phạm vi công việc đã thực hiện (Scope Completed)

1. **Khắc phục lỗi HIGH: JWT claims vẫn hợp lệ sau khi user bị khóa hoặc hạ quyền (`SEC-F06`):**
   - **Bổ sung `SecurityVersion` vào User Model & Database:**
     - Cập nhật `src/Services/AuthService/Models/User.cs`: Thêm thuộc tính `public int SecurityVersion { get; set; } = 1;`.
     - Cấu hình `AuthDbContext.cs` với `HasDefaultValue(1)`.
     - Tạo EF Core Migration `20260921200000_AddSecurityVersionToUser.cs` và đồng bộ `AuthDbContextModelSnapshot.cs`.
   - **Bổ sung Claim `ver` vào JWT Token:**
     - Cập nhật `src/Shared/ClubReportHub.Shared/Auth/JwtTokenFactory.cs`:
       - Phương thức `CreateToken` nhận thêm tham số `int securityVersion = 1`.
       - Đính kèm claim `"ver"` vào JWT payload đại diện cho phiên bản bảo mật của tài khoản.
     - Cập nhật `RefreshTokenService.CreateAuthResponse` truyền `user.SecurityVersion` khi tạo token đăng nhập mới hoặc refresh.
   - **Cơ chế Xác thực Security Stamp tại Middleware JWT (`OnTokenValidated`):**
     - Tạo interface `IUserSecurityStampValidator` tại `ClubReportHub.Shared.Auth`.
     - Móc vào sự kiện `JwtBearerEvents.OnTokenValidated` trong `JwtServiceCollectionExtensions.cs`:
       - Trích xuất `userId` từ token.
       - Giải quyết `IUserSecurityStampValidator` từ request DI container.
       - Nếu validator trả về `false`, lập tức gọi `context.Fail("The token is no longer valid due to security stamp or account status changes.")` trả về HTTP 401 Unauthorized.
   - **Triển khai Trực tiếp tại `AuthService` (`AuthDbContextSecurityStampValidator`):**
     - Kiểm tra trực tiếp đối chiếu User từ Database:
       - User không tồn tại -> Reject.
       - User `!IsActive` -> Reject.
       - User `IsLocked` -> Reject.
       - Token `ver < user.SecurityVersion` -> Reject.
       - Token chứa role đã bị thu hồi trong DB -> Reject.
     - Tối ưu hiệu năng bằng `IMemoryCache` (TTL 10 giây).
     - Chủ động xóa cache (`cache.Remove($"sec_stamp:user:{userId}")`) ngay lập tức khi tài khoản bị khóa, mở khóa, đổi quyền hoặc đăng xuất để loại bỏ hoàn toàn độ trễ của cache.
   - **Tăng `SecurityVersion` và Thu hồi Quyền khi Có Biến Động Tài Khoản:**
     - `HandleLockUser`: `user.IsLocked = true; user.SecurityVersion++;`
     - `HandleUnlockUser`: `user.IsLocked = false; user.SecurityVersion++;`
     - `HandleUpdateUser`: Khi `!request.IsActive` hoặc đổi email hoặc đổi role -> `user.SecurityVersion++;`
     - `HandleLogout`: Tăng `user.SecurityVersion++` khi đăng xuất, làm mất hiệu lực ngay lập tức của mọi JWT access token đang lưu hành của tài khoản đó.
   - **Endpoint Trạng Thái Người Dùng & Remote Security Stamp Validator cho Gateway:**
     - Cung cấp endpoint `GET /api/auth/user-status/{id:int}` tại `AuthEndpoints.cs` trả về `{ userId, isActive, isLocked, securityVersion, roles }`.
     - Triển khai `RemoteUserSecurityStampValidator` trong `ClubReportHub.Shared.Auth`:
       - Gọi qua `HttpClient` tới endpoint `user-status/{userId}` với cache ngắn (30 giây).
       - Có cơ chế Graceful Degradation (fail-open) khi `AuthService` tạm thời gặp sự cố mạng hoặc lỗi 500, tránh làm tê liệt toàn bộ hệ thống Gateway.
   - **Giảm Thời Hạn JWT Access Token từ 120 phút xuống 30 phút:**
     - Cập nhật cấu hình `ExpirationMinutes: 30` tại:
       - `src/Services/AuthService/appsettings.json`
       - `src/Gateway/ApiGateway/appsettings.json`
       - `docker-compose.yml` (biến môi trường `Jwt__ExpirationMinutes: "30"`).

---

## 2. Danh mục file đã chỉnh sửa & tạo mới

| STT | File | Trạng thái | Nội dung thay đổi |
| :---: | :--- | :---: | :--- |
| 1 | `src/Services/AuthService/Models/User.cs` | Sửa đổi | Bổ sung trường `SecurityVersion` (mặc định = 1) |
| 2 | `src/Services/AuthService/Data/AuthDbContext.cs` | Sửa đổi | Cấu hình default value 1 cho `SecurityVersion` |
| 3 | `src/Services/AuthService/Migrations/20260921200000_AddSecurityVersionToUser.cs` | Tạo mới | Migration thêm cột `SecurityVersion` vào bảng `Users` |
| 4 | `src/Services/AuthService/Migrations/AuthDbContextModelSnapshot.cs` | Sửa đổi | Cập nhật snapshot Entity Framework Core |
| 5 | `src/Shared/ClubReportHub.Shared/Auth/IUserSecurityStampValidator.cs` | Tạo mới | Định nghĩa interface xác thực tính tươi mới của token |
| 6 | `src/Shared/ClubReportHub.Shared/Auth/RemoteUserSecurityStampValidator.cs` | Tạo mới | Triển khai xác thực qua remote HTTP call và helper DI |
| 7 | `src/Shared/ClubReportHub.Shared/Auth/JwtTokenFactory.cs` | Sửa đổi | Thêm tham số `securityVersion` và claim `ver` |
| 8 | `src/Shared/ClubReportHub.Shared/Auth/JwtServiceCollectionExtensions.cs` | Sửa đổi | Kích hoạt `IUserSecurityStampValidator` trong `OnTokenValidated` |
| 9 | `src/Services/AuthService/Services/AuthDbContextSecurityStampValidator.cs` | Tạo mới | Validator kiểm tra User DB và bộ đệm cache |
| 10 | `src/Services/AuthService/Services/RefreshTokenService.cs` | Sửa đổi | Truyền `user.SecurityVersion` khi tạo token |
| 11 | `src/Services/AuthService/Endpoints/UserEndpoints.cs` | Sửa đổi | Tăng `SecurityVersion` và xóa cache khi update/lock/unlock |
| 12 | `src/Services/AuthService/Endpoints/AuthEndpoints.cs` | Sửa đổi | Thêm `GET /api/auth/user-status/{id}`, tăng `SecurityVersion` khi logout |
| 13 | `src/Services/AuthService/Extensions/AuthServiceCollectionExtensions.cs` | Sửa đổi | Đăng ký `MemoryCache` và `AuthDbContextSecurityStampValidator` |
| 14 | `src/Gateway/ApiGateway/Program.cs` | Sửa đổi | Đăng ký `RemoteUserSecurityStampValidator` nếu cấu hình `AuthServiceUrl` |
| 15 | `src/Services/AuthService/appsettings.json` | Sửa đổi | Giảm `ExpirationMinutes` từ 120 xuống 30 |
| 16 | `src/Gateway/ApiGateway/appsettings.json` | Sửa đổi | Giảm `ExpirationMinutes` xuống 30 và thêm `Services:AuthServiceUrl` |
| 17 | `docker-compose.yml` | Sửa đổi | Cập nhật `Jwt__ExpirationMinutes: "30"` |
| 18 | `tests/Backend.StabilizationTests/JwtAuthorizationFreshnessTests.cs` | Tạo mới | 9 automated regression tests cho `SEC-F06` |

---

## 3. Kết quả Kiểm thử & Đảm bảo Chất lượng (Quality Assurance)

- **Biên dịch:** Toàn bộ solution biên dịch thành công 100% với cờ `-warnaserror` (0 cảnh báo, 0 lỗi).
- **Kiểm thử tự động:**
  - `Backend.StabilizationTests`: **87/87 tests PASSED** (+9 regression tests mới cho Phase 07).
  - `AdminService.IntegrationTests`: **28/28 tests PASSED**.
  - `ClubReportHub.Tests`: **39/39 tests PASSED**.
  - **Tổng cộng: 154/154 tests PASSED** (100% thành công, 0 hồi quy).
- **Nội dung 9 Test Cases Mới tại `JwtAuthorizationFreshnessTests.cs`:**
  1. `ActiveUserToken_WithMatchingSecurityVersion_IsAccepted`: Token còn hạn với đúng phiên bản bảo mật được chấp nhận.
  2. `StaleToken_AfterUserLocked_IsRejectedWith401`: Token cũ bị từ chối 401 ngay khi user bị khóa.
  3. `StaleToken_AfterUserDeactivated_IsRejectedWith401`: Token cũ bị từ chối 401 ngay khi user bị vô hiệu hóa.
  4. `StaleToken_AfterSecurityVersionIncrement_IsRejected_AndNewTokenAccepted`: Token cũ bị từ chối sau khi tăng version; token mới được cấp với version mới thì hợp lệ.
  5. `StaleToken_WithRevokedRoleClaim_IsRejectedWith401`: Token mang claim quyền đã bị thu hồi khỏi DB bị từ chối 401.
  6. `Logout_IncrementsSecurityVersion_AndInvalidatesAccessToken`: Đăng xuất làm tăng version và vô hiệu hóa ngay lập tức JWT access token cũ.
  7. `UserStatusEndpoint_ReturnsAccurateDetailsAndVersion`: Endpoint user-status trả về chính xác trạng thái và version, trả về 404 nếu không tìm thấy.
  8. `RemoteSecurityStampValidator_GracefulDegradation_OnNetworkOutage`: Validator từ xa hoạt động dự phòng mềm mại (fail-open) khi AuthService gặp lỗi mạng.
  9. `RemoteSecurityStampValidator_RejectsWhenSecurityVersionHigherOnServer`: Validator từ xa từ chối token nếu phiên bản trên server cao hơn.
