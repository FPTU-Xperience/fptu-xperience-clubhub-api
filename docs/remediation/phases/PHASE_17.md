# BÁO CÁO HOÀN THÀNH PHASE 17 — API CORRECTNESS, OBSERVABILITY & LOGGING STANDARDS

> **Thời điểm hoàn tất:** 2026-09-22  
> **Người thực hiện:** Antigravity AI Assistant  
> **Trạng thái:** HOÀN THÀNH (COMPLETE)

---

## 1. Phạm vi công việc đã thực hiện (Scope Completed)

1. **Khắc phục lỗi HIGH: Correlation ID bị thiếu hoặc rơi rớt qua 5 service và các HttpClient (`OBS-F01`):**
   - **Hiện trạng trước sửa đổi:**
     - `CorrelationIdMiddleware` chỉ ghi nhận Correlation ID vào `context.Items` và gắn vào Response header lúc bắt đầu gửi (`OnStarting`), nhưng không đặt lại vào `context.Request.Headers[CorrelationIdConstants.HeaderName]`.
     - Nhiều service (`ClubService`, `ActivityService`, `ExportService`, `NotificationService`, `AuthService`) chưa đăng ký `AddClubReportTracing()` hoặc thiếu middleware `app.UseCorrelationId()`.
     - Các `HttpClient` liên dịch vụ (`ClubAccessClient`, `RemoteUserSecurityStampValidator`, `ActivityStatisticsClient`, `ClubMemberRosterClient`, `ReportVerificationClient`, `ReportService` client trong ExportService) thiếu `AddCorrelationIdForwarding()` delegating handler, dẫn đến việc mất dấu trace khi request đi qua chuỗi microservices.
   - **Giải pháp:**
     - Cập nhật `CorrelationIdMiddleware.cs`: tự động sinh GUID chuẩn nếu thiếu header, ghi nhận đồng thời vào `context.Items[CorrelationIdConstants.ItemKey]` và `context.Request.Headers[CorrelationIdConstants.HeaderName]`.
     - Đăng ký `builder.Services.AddClubReportTracing()` và gắn middleware `app.UseCorrelationId()` trên toàn bộ pipeline của các service: `AuthService`, `ClubService`, `ActivityService`, `FinanceService`, `ReportService`, `NotificationService`, `ExportService`.
     - Bổ sung `.AddCorrelationIdForwarding().AddStandardResilienceHandler()` cho toàn bộ các `HttpClient` giao tiếp nội bộ giữa các microservice.

2. **Khắc phục lỗi MEDIUM: AuthService thiếu log có cấu trúc cho đăng nhập, đổi mật khẩu, cấp token (`OBS-F02`):**
   - **Hiện trạng trước sửa đổi:**
     - Các luồng xác thực quan trọng (`HandleGoogleSignIn`, `HandleDevLogin`, `HandleRefresh`, `HandleLogout`) và quản lý tài khoản (`HandleCreateUser`, `HandleUpdateUser`, `HandleLockUser`, `HandleUnlockUser`) thiếu logging có cấu trúc (structured audit logging).
     - Việc khắc phục sự cố hoặc kiểm toán an ninh trên môi trường phân tán gặp khó khăn khi không có log ghi nhận ID người dùng, địa chỉ IP và Correlation ID.
   - **Giải pháp:**
     - Bổ sung structured audit logging với message templates chuẩn: `{Email}, {UserId}, {ClientIp}, {CorrelationId}` cho tất cả các luồng đăng nhập thành công/thất bại, làm mới token, đăng xuất và thay đổi trạng thái khóa/mở khóa tài khoản.
     - Kiểm thử bảo mật đảm bảo tuyệt đối không lộ access token, refresh token hoặc secret trong các log parameters.

3. **Khắc phục lỗi MEDIUM: Cấu hình Production để log câu lệnh SQL ở mức Information gây rò rỉ dữ liệu (`OBS-F03`):**
   - **Hiện trạng trước sửa đổi:**
     - Mặc định `"Default": "Information"` khiến `Microsoft.EntityFrameworkCore.Database.Command` in toàn bộ câu lệnh SQL và giá trị tham số (parameters) ra log console.
     - Trên môi trường production, điều này có thể làm lộ dữ liệu nhạy cảm (thông tin cá nhân sinh viên, chi tiết ngân sách, nội dung báo cáo).
   - **Giải pháp:**
     - Thêm cấu hình tường minh `"Microsoft.EntityFrameworkCore": "Warning"` trong mục `Logging:LogLevel` trên tất cả 8 file cấu hình `appsettings.json` của các service có kết nối cơ sở dữ liệu (`ReportService`, `ClubService`, `ActivityService`, `FinanceService`, `AuthService`, `NotificationService`, `ExportService`, `AdminService`).
     - Ngăn chặn triệt để việc log chi tiết câu truy vấn và giá trị tham số trong log vận hành.

4. **Khắc phục lỗi MEDIUM: Deadline job tính toán sai tập hợp CLB chưa nộp báo cáo (`DATA-F07`):**
   - **Hiện trạng trước sửa đổi:**
     - Trong `ReportService/Jobs/ReportDeadlineJobs.cs`, phương thức `PublishReminderForPeriod` lọc danh sách CLB thiếu báo cáo bằng câu lệnh:
       `db.Reports.Where(x => x.Period != period && submittedClubIds.Contains(x.ClubId))`.
     - Logic này bị đảo ngược: kiểm tra `submittedClubIds.Contains(...)` thay vì tìm các CLB KHÔNG nằm trong danh sách đã nộp. Hậu quả là những CLB đã nộp lại bị gửi email nhắc nhở, còn các CLB chưa nộp bao giờ hoặc chưa nộp kỳ này bị bỏ qua hoàn toàn.
   - **Giải pháp:**
     - Tái cấu trúc logic xác định CLB chưa nộp:
       1. Truy vấn danh sách `submittedClubIds` (các CLB có báo cáo trong kỳ với trạng thái khác `Draft`).
       2. Truy vấn danh sách tất cả các CLB đã biết trong hệ thống (`allKnownClubIds`).
       3. Lọc danh sách CLB thiếu báo cáo: `allKnownClubIds.Where(id => !submittedClubIds.Contains(id)).ToList()`.
     - Đảm bảo các CLB có báo cáo `Draft` hoặc chưa có báo cáo nào trong kỳ được nhắc nhở chính xác.

5. **Khắc phục lỗi MEDIUM: UseExceptionHandler("/error") kết hợp MapGet trả về 405 cho mutation lỗi (`REL-F06`):**
   - **Hiện trạng trước sửa đổi:**
     - ASP.NET Core `UseExceptionHandler("/error")` giữ nguyên phương thức HTTP của request ban đầu khi chuyển hướng nội bộ (re-execute). Khi một mutation endpoint (POST/PUT/DELETE/PATCH) gặp ngoại lệ chưa được xử lý, request được chuyển đến `/error` với phương thức tương ứng.
     - Vì `/error` được cấu hình bằng `app.MapGet("/error", ...)`, Kestrel từ chối request với mã lỗi `405 Method Not Allowed`, làm mất thông tin ngoại lệ và vi phạm chuẩn RFC 7807 ProblemDetails.
   - **Giải pháp:**
     - Tạo extension method dùng chung `MapGlobalErrorEndpoint()` trong `ClubReportHub.Shared/Errors/GlobalErrorEndpointExtensions.cs`.
     - Sử dụng `endpoints.Map("/error", ...)` chấp nhận tất cả các HTTP verb (GET, POST, PUT, DELETE, PATCH, OPTIONS...).
     - Trích xuất thông tin ngoại lệ từ `IExceptionHandlerFeature`, gắn `correlationId` và `traceId`, trả về payload RFC 7807 ProblemDetails với `statusCode: 500` và `Content-Type: application/problem+json`.
     - Thay thế toàn bộ các khai báo `MapGet("/error", ...)` tại tất cả các service.

---

## 2. Danh mục file đã chỉnh sửa & tạo mới

| STT | File | Trạng thái | Nội dung thay đổi |
| :---: | :--- | :---: | :--- |
| 1 | `src/Shared/ClubReportHub.Shared/Tracing/CorrelationIdMiddleware.cs` | Sửa đổi | Cập nhật gán Correlation ID vào `Request.Headers` và `Items` |
| 2 | `src/Shared/ClubReportHub.Shared/Auth/ClubAccessClient.cs` | Sửa đổi | Thêm `AddCorrelationIdForwarding()` và resilience handler |
| 3 | `src/Shared/ClubReportHub.Shared/Auth/RemoteUserSecurityStampValidator.cs` | Sửa đổi | Thêm `AddCorrelationIdForwarding()` và resilience handler |
| 4 | `src/Shared/ClubReportHub.Shared/Errors/GlobalErrorEndpointExtensions.cs` | Tạo mới | Định nghĩa `MapGlobalErrorEndpoint()` chấp nhận mọi HTTP verb, chuẩn hóa RFC 7807 ProblemDetails |
| 5 | `src/Services/ReportService/Jobs/ReportDeadlineJobs.cs` | Sửa đổi | Sửa logic tính toán CLB thiếu báo cáo chính xác (`DATA-F07`) |
| 6 | `src/Services/ReportService/Program.cs` | Sửa đổi | Sử dụng `MapGlobalErrorEndpoint()` |
| 7 | `src/Services/ReportService/appsettings.json` | Sửa đổi | Cấu hình `Microsoft.EntityFrameworkCore: Warning` |
| 8 | `src/Services/ClubService/Program.cs` | Sửa đổi | Thêm tracing, `UseCorrelationId`, `MapGlobalErrorEndpoint`, correlation forwarding |
| 9 | `src/Services/ClubService/appsettings.json` | Sửa đổi | Cấu hình `Microsoft.EntityFrameworkCore: Warning` |
| 10 | `src/Services/ActivityService/Extensions/ServiceCollectionExtensions.cs` | Sửa đổi | Thêm `AddClubReportTracing()`, correlation forwarding cho member roster và report verification clients |
| 11 | `src/Services/ActivityService/Extensions/ApplicationPipelineExtensions.cs` | Sửa đổi | Thêm `UseCorrelationId()` |
| 12 | `src/Services/ActivityService/Endpoints/SystemEndpoints.cs` | Sửa đổi | Sử dụng `MapGlobalErrorEndpoint()` |
| 13 | `src/Services/ActivityService/appsettings.json` | Sửa đổi | Cấu hình `Microsoft.EntityFrameworkCore: Warning` |
| 14 | `src/Services/FinanceService/Program.cs` | Sửa đổi | Sử dụng `MapGlobalErrorEndpoint()` |
| 15 | `src/Services/FinanceService/appsettings.json` | Sửa đổi | Cấu hình `Microsoft.EntityFrameworkCore: Warning` |
| 16 | `src/Services/AuthService/Extensions/AuthServiceCollectionExtensions.cs` | Sửa đổi | Thêm `AddClubReportTracing()` |
| 17 | `src/Services/AuthService/Endpoints/AuthEndpoints.cs` | Sửa đổi | Thêm structured audit logging cho login, dev-login, refresh, logout |
| 18 | `src/Services/AuthService/Endpoints/UserEndpoints.cs` | Sửa đổi | Thêm structured audit logging cho user create, update, lock, unlock |
| 19 | `src/Services/AuthService/Program.cs` | Sửa đổi | Thêm `UseCorrelationId()`, `MapGlobalErrorEndpoint()` |
| 20 | `src/Services/AuthService/appsettings.json` | Sửa đổi | Cấu hình `Microsoft.EntityFrameworkCore: Warning` |
| 21 | `src/Services/ExportService/Program.cs` | Sửa đổi | Thêm tracing, `UseCorrelationId`, `MapGlobalErrorEndpoint`, correlation forwarding |
| 22 | `src/Services/ExportService/appsettings.json` | Sửa đổi | Cấu hình `Microsoft.EntityFrameworkCore: Warning` |
| 23 | `src/Services/NotificationService/Program.cs` | Sửa đổi | Thêm tracing, `UseCorrelationId`, `MapGlobalErrorEndpoint()` |
| 24 | `src/Services/NotificationService/appsettings.json` | Sửa đổi | Cấu hình `Microsoft.EntityFrameworkCore: Warning` |
| 25 | `src/Services/AdminService/appsettings.json` | Sửa đổi | Cấu hình `Microsoft.EntityFrameworkCore: Warning` |
| 26 | `tests/Backend.StabilizationTests/ObservabilityAndLoggingStandardsTests.cs` | Tạo mới | Bộ test 18 ca kiểm thử kiểm tra toàn diện correlation id, structured logs, ef core config, deadline logic, error endpoint |

---

## 3. Kết quả kiểm thử & xác minh (Verification)

1. **Kiểm thử tự động chuyên biệt Phase 17 (`ObservabilityAndLoggingStandardsTests.cs`):**
   - 18/18 test cases PASSED (100%):
     - `CorrelationIdMiddleware_WhenHeaderMissing_GeneratesGuidAndAttachesToRequestAndItems`: PASS.
     - `CorrelationIdMiddleware_WhenHeaderPresent_PreservesIncomingHeaderAndSetsItems`: PASS.
     - `CorrelationIdDelegatingHandler_AppendsCorrelationIdHeaderToOutgoingRequests`: PASS.
     - `StructuredAuditLogging_NeverExposesPlaintextTokensOrSecretsInLogTemplates`: PASS.
     - `AppsettingsConfiguration_SetsEntityFrameworkCoreLogLevelToWarning_AcrossAllDataServices` (8 dịch vụ): PASS.
     - `ReportDeadlineJobs_PublishDailyReminderAsync_AccuratelyIdentifiesMissingClubsAndExcludesSubmittedClubs`: PASS.
     - `MapGlobalErrorEndpoint_HandlesAnyHttpMethod_WithRfc7807ProblemDetails` (5 HTTP verbs: GET, POST, PUT, DELETE, PATCH): PASS.

2. **Kiểm thử hồi quy toàn bộ hệ thống (Full Solution Test Suite):**
   - `AdminService.IntegrationTests`: 28/28 test cases PASSED.
   - `Backend.StabilizationTests`: 180/180 test cases PASSED.
   - `ClubReportHub.Tests`: 39/39 test cases PASSED.
   - **Tổng cộng: 247/247 test cases PASSED (100% pass rate).**

3. **Kiểm tra biên dịch & cảnh báo:**
   - `dotnet build ClubReportHub.sln -c Release -warnaserror`: **0 Warning(s), 0 Error(s)**.

4. **Kiểm tra Docker Compose:**
   - `docker compose config --quiet`: **Thành công (Exit code 0)**.

5. **Phân tích tác động đồ thị (GitNexus Graph Impact):**
   - `detect_changes({scope: "all"})`: Toàn bộ thay đổi mã nguồn và quan hệ liên dịch vụ được kiểm tra đồng bộ, an toàn, không có orphan symbol hay phá vỡ dependency graph.
