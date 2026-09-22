# BÁO CÁO HOÀN THÀNH PHASE 05 — API GATEWAY SECURITY & RATE LIMITING

> **Thời điểm hoàn tất:** 2026-09-21  
> **Người thực hiện:** Antigravity AI Assistant  
> **Trạng thái:** HOÀN THÀNH (COMPLETE)

---

## 1. Phạm vi công việc đã thực hiện (Scope Completed)

1. **Khắc phục lỗi HIGH Ingress Rate Limiting thiếu hụt tại API Gateway & Bucket dùng chung (`SEC-F03`):**
   - **Xây dựng module rate limiting & proxy identity dùng chung (`RateLimitingExtensions`):**
     - Đặt tại `src/Shared/ClubReportHub.Shared/RateLimiting/RateLimitingExtensions.cs`.
     - Phương thức `ResolveClientKey`:
       - Nhận biết ngữ cảnh xác thực: Nếu request có token hợp lệ (`ClaimTypes.NameIdentifier` hoặc `sub`), key được định dạng `user:{userId}:{ip}`.
       - Với request ẩn danh: key định dạng `ip:{ip}`.
       - Giải quyết triệt để vấn đề sinh viên dùng chung IP mạng trường (NAT) làm nghẽn quota của nhau.
     - Hàm xử lý từ chối chuẩn hóa `CreateRateLimitRejectedHandler`:
       - Trả về mã lỗi HTTP `429 Too Many Requests`.
       - Thiết lập header `Retry-After: <seconds>` dựa trên lease metadata.
       - Trả về payload JSON có cấu trúc `{ "error": "TooManyRequests", "message": "...", "retryAfter": ... }`.
     - Cấu hình mạng tin cậy `ConfigureTrustedForwardedHeaders`:
       - Kích hoạt `ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto`.
       - Hỗ trợ nạp `ForwardedHeaders:KnownNetworks` và `ForwardedHeaders:KnownProxies` từ cấu hình (mặc định tin cậy loopback và các dải mạng riêng `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`).
       - Chống client bên ngoài giả mạo header `X-Forwarded-For` để qua mặt bộ giới hạn.

   - **Thiết lập Ingress Rate Limiting đa tầng tại `ApiGateway`:**
     - Tích hợp `AddRateLimiter` và `UseRateLimiter` vào pipeline của Gateway ngay sau `UseAuthentication` và `UseAuthorization` (đảm bảo `context.User` đã được giải mã trước khi tính partition key).
     - Định nghĩa các policy chuyên biệt, cấu hình qua `appsettings.json`:
       - `auth-login`: 60 requests/phút (Google sign-in, dev login).
       - `auth-refresh`: 30 requests/phút.
       - `upload`: 30 requests/phút (tải lên báo cáo / tài liệu đính kèm).
       - `export`: 20 requests/phút.
       - `general-api`: 300 requests/phút cho toàn bộ các endpoint thông thường.
       - `GlobalLimiter`: Giới hạn toàn cục 600 requests/phút cho các route không khớp (chống scanning/fuzzing), **hoàn toàn miễn trừ `/health` và `/`** để đảm bảo probe của Docker/Kubernetes không bị false-negative.

   - **Đồng bộ hóa Route Mapping & Transforms trong `yarp.json` và `Program.cs`:**
     - Thêm các route ưu tiên cao (`Order: 1`):
       - `auth-google`, `auth-dev-login` trỏ tới `auth-login` policy.
       - `auth-refresh` trỏ tới `auth-refresh` policy.
       - `report-upload`, `report-file-replace`, `report-attachment-upload` trỏ tới `upload` policy.
       - `exports` trỏ tới `export` policy.
     - Áp dụng `general-api` policy cho tất cả các route nghiệp vụ còn lại (`clubs`, `activities`, `reports`, `finance`, `admin`, `notifications`).
     - **Bổ sung YARP Transform toàn cục:**
       - Tự động loại bỏ header `X-Combined-Report-Workflow` khỏi tất cả request đi qua Gateway (`builderContext.AddRequestHeaderRemove("X-Combined-Report-Workflow")`), ngăn chặn client bên ngoài lợi dụng header này để bypass workflow kiểm duyệt ngân sách/báo cáo.

   - **Cải tiến `AuthService`:**
     - Kích hoạt `app.UseForwardedHeaders()` kết hợp `ConfigureTrustedForwardedHeaders` để `AuthService` nhận đúng IP thực tế của client do Gateway chuyển tiếp, thay vì nhận IP container của Gateway.
     - Cập nhật partition key sang `ResolveClientKey(context)` và bổ sung `Retry-After` header + JSON chuẩn khi bị 429.

2. **Thẩm định an toàn dịch vụ gRPC nội bộ (`POT-F02`):**
   - **Xác nhận trạng thái:** `VERIFIED` — Không có lỗ hổng phơi nhiễm ra ngoài.
   - **Bằng chứng:**
     - `KpiGrpcService` được triển khai thuần túy trên mạng nội bộ `internal-net` trong `docker-compose.yml`, hoàn toàn không có thuộc tính `ports:` để map ra máy chủ host hay internet.
     - File cấu hình YARP Gateway (`yarp.json`) không có bất kỳ route nào trỏ tới cluster `kpi-grpc-service`.
     - Chỉ có `ReportService` giao tiếp trực tiếp với `KpiGrpcService` qua mạng nội bộ `http://kpi-grpc-service:8080`.

3. **Xây dựng bộ kiểm thử hồi quy tự động:**
   - Tạo file [GatewayRateLimitingAndSecurityTests.cs](file:///c:/Users/ADMIN/Downloads/DOAN/fptu-xperience-clubhub-api/tests/Backend.StabilizationTests/GatewayRateLimitingAndSecurityTests.cs) gồm 9 ca kiểm thử chuyên biệt:
     - Kiểm tra khi vượt ngưỡng limit trả về HTTP 429 kèm header `Retry-After` và JSON payload chuẩn.
     - Kiểm tra 2 IP client khác nhau có bucket độc lập (IP A hết quota không ảnh hưởng IP B).
     - Kiểm tra 2 user khác nhau trên cùng một IP (sau NAT) có bucket độc lập.
     - Kiểm tra endpoint `/health` không bao giờ bị throttled ngay cả khi gửi request liên tục.
     - Kiểm tra proxy không tin cậy không thể giả mạo `X-Forwarded-For`.
     - Kiểm tra YARP Transform tự động gỡ bỏ header `X-Combined-Report-Workflow`.
     - Kiểm tra `ResolveClientKey` sinh key `ip:{ip}` cho request ẩn danh và `user:{id}:{ip}` cho request đã đăng nhập.
     - Kiểm tra kiến trúc: `KpiGrpcService` không có route public và không mở port trong docker-compose.

---

## 2. File thay đổi (Files Changed)

- `src/Shared/ClubReportHub.Shared/RateLimiting/RateLimitingExtensions.cs`: [NEW] Xây dựng tiện ích tính partition key, xử lý 429 Retry-After, và cấu hình trusted forwarded headers.
- `src/Gateway/ApiGateway/Program.cs`: Cấu hình ForwardedHeaders, RateLimiter với 5 policies + global fallback, YARP header removal transforms.
- `src/Gateway/ApiGateway/appsettings.json`: Bổ sung cấu hình `RateLimiting` và `ForwardedHeaders`.
- `src/Gateway/ApiGateway/yarp.json`: Thêm route rate limiter policies cho auth, uploads, exports, và general APIs.
- `src/Services/AuthService/Program.cs`: Thêm `UseForwardedHeaders()`.
- `src/Services/AuthService/Extensions/AuthServiceCollectionExtensions.cs`: Tích hợp `ResolveClientKey`, `CreateRateLimitRejectedHandler`, và `ConfigureTrustedForwardedHeaders`.
- `tests/Backend.StabilizationTests/Backend.StabilizationTests.csproj`: Bổ sung tham chiếu `ApiGateway` và package `Yarp.ReverseProxy`.
- `tests/Backend.StabilizationTests/GatewayRateLimitingAndSecurityTests.cs`: [NEW] 9 ca kiểm thử tự động cho Gateway rate limiting & security.
- `docs/remediation/FINDINGS_LEDGER.md`: Cập nhật `SEC-F03` (FIXED), `POT-F02` (VERIFIED).
- `docs/remediation/TEST_MATRIX.md`: Ghi nhận 9 test pass cho Phase 05.
- `docs/remediation/CHECKPOINT.md`: Cập nhật trạng thái chuyển giao sang Phase 06.

---

## 3. Kết quả kiểm thử thực tế

- **Build Solution:** `dotnet build ClubReportHub.sln -c Release -warnaserror` 👉 **0 Warning(s), 0 Error(s)**.
- **Solution Tests:** `dotnet test ClubReportHub.sln -c Release --no-build` 👉 **93/93 tests PASSED** (65 Stabilization + 28 Admin).
- **Stand-alone Tests:** `dotnet test tests/ClubReportHub.Tests/ClubReportHub.Tests.csproj -c Release` 👉 **39/39 tests PASSED**.
- **Tổng số test toàn hệ thống:** **132/132 tests PASSED** (+9 test mới cho Phase 05).
- **Docker Compose:** `docker compose config --quiet` 👉 **Valid**.

---

## 4. Sẵn sàng cho Phase tiếp theo

- **Phase tiếp theo:** **WAVE 2 — PHASE 06: Cross-Service Workflow Authorization**
- **Trọng tâm:**
  - Sửa lỗi **`SEC-F04`** (MEDIUM): Public client giả mạo header `X-Combined-Report-Workflow` bypass tuần tự duyệt báo cáo / ngân sách.
  - Sửa lỗi **`SEC-F05`** (HIGH): Report budget link proposalId không xác thực cùng CLB/báo cáo gốc.
  - Thẩm định **`POT-F04`** (MEDIUM): Tạo activity từ báo cáo đã duyệt không xác thực lại tính hợp lệ của duyệt.
