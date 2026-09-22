# THEO DÕI THAY ĐỔI API CONTRACT (API CONTRACT CHANGES)

> Tài liệu này ghi chép các thay đổi liên quan đến API endpoint, request/response DTOs, headers và trạng thái HTTP có thể ảnh hưởng đến Frontend hoặc dịch vụ bên ngoài.

---

## Nguyên tắc quản lý thay đổi API
1. Không tự ý thay đổi breaking change trên public API trừ khi bắt buộc vì lý do bảo mật.
2. Nếu phải thay đổi, cần tài liệu hóa chi tiết:
   - Endpoint bị ảnh hưởng
   - Thay đổi cụ thể (Request/Response DTO, Header, Status Code)
   - Ảnh hưởng tới Frontend
   - Phương án tương thích ngược (Backward compatibility)

---

## Danh sách thay đổi dự kiến (Theo Remediation Plan)

| Phase | Endpoint | Thay đổi dự kiến | Ảnh hưởng Frontend | Phương án chuyển tiếp |
| :---: | :--- | :--- | :--- | :--- |
| **Phase 01** | `POST /api/auth/dev-login` | Giữ endpoint theo quyết định accepted risk và điều khiển bằng `Auth:EnableDevLogin`. | Frontend tiếp tục dùng dev-login cho demo và kiểm thử đồ án. | Giữ nguyên contract hiện tại; không thay đổi trong Phase 21. |
| **Phase 02** | `POST /api/reports/attachments` | Loại bỏ trường `StoragePath` khỏi Request DTO (server tự sinh đường dẫn an toàn); ẩn đường dẫn vật lý khỏi Response DTO. | Frontend không cần và không được truyền `storagePath` khi gửi metadata đính kèm. | Hỗ trợ fallback/bỏ qua nếu client gửi lên giá trị này. |
| **Phase 06** | `POST /api/reports/...` | Gateway strip header `X-Combined-Report-Workflow` từ client ngoài; chỉ cho phép luồng nội bộ. | Frontend không thể tự inject header này để bypass workflow. | Không ảnh hưởng nếu frontend tuân thủ flow chuẩn. |
| **Phase 15** | `POST /api/reports/preview` / `export` | Chuyển xử lý file lớn sang background / trả về 202 Accepted với status URL thay vì render synchronous chặn thread. | Frontend nhận status URL và poll/chờ kết quả qua notification hoặc query status. | Giữ endpoint đồng bộ tạm thời cho file nhỏ nếu cần, hoặc thiết kế flow async. |
| **Phase 17** | `GET/POST /error` | Cho phép `/error` nhận mọi HTTP verb thay vì chỉ `GET` (tránh 405 Method Not Allowed khi có exception). | Frontend nhận đúng định dạng `ProblemDetails` (500) thay vì 405. | Hoàn toàn tương thích tốt hơn với frontend. |

---

## Nhật ký thay đổi thực tế đã áp dụng (Phases 01 – 12)

| Phase | Endpoint / Thành phần | Bản chất thay đổi | Chi tiết Request / Response / Status | Ảnh hưởng Frontend & Tương thích |
| :---: | :--- | :--- | :--- | :--- |
| **Phase 01** | `POST /api/auth/dev-login` | Accepted Risk & Cấu hình | Đồng bộ cờ `ENABLE_DEV_LOGIN` và `Auth:EnableDevLogin`. Giữ nguyên endpoint cho demo đồ án, bổ sung structured audit logging. | Frontend tiếp tục sử dụng dev-login để test giao diện và luồng kiểm thử bình thường. |
| **Phase 02** | `POST /api/reports/{id}/attachments`<br>`GET /api/reports/{id}/attachments` | Bảo mật tệp đính kèm (`SEC-F10`, `POT-F05`) | Trong `AddAttachmentRequest`, `StoragePath` chuyển thành tùy chọn (nullable). Trong `ReportAttachmentResponse`, `StoragePath` luôn được ẩn thành chuỗi rỗng `""`. | Frontend không cần gửi đường dẫn ổ đĩa máy chủ; không dựa vào trường `StoragePath` trong response (dùng `id` và endpoint download). Hoàn toàn tương thích ngược. |
| **Phase 05** | Toàn bộ API Ingress qua ApiGateway | Rate Limiting đa tầng & Stripping (`SEC-F03`) | Trả về `429 Too Many Requests` kèm header `Retry-After: <seconds>` và JSON `{ error, message, retryAfter }` khi vượt quota. Tự động strip header `X-Combined-Report-Workflow` từ external requests. | Frontend cần bắt mã `429` để hiển thị thông báo chờ hoặc retry tương ứng. Không ảnh hưởng các request thông thường trong ngưỡng sử dụng. |
| **Phase 06** | `POST /api/reports/future-events/budget`<br>`POST /api/activities/from-approved-report` | Kiểm tra chéo liên dịch vụ (`SEC-F04`, `SEC-F05`, `POT-F04`) | `LinkFutureEventBudget` trả `400 Bad Request` nếu `proposalId` không thuộc cùng CLB hoặc không trỏ về báo cáo gốc. Tạo Activity từ báo cáo trả `400 Bad Request` nếu báo cáo chưa duyệt hoặc sai CLB. | Ngăn chặn client gửi ID proposal/report của CLB khác. Frontend tuân thủ nghiệp vụ chuẩn không bị ảnh hưởng. |
| **Phase 07** | JWT Access Token<br>`GET /api/auth/user-status/{id}` | JWT Freshness & Thu hồi quyền (`SEC-F06`) | JWT Access Token bổ sung claim `"ver": <int>`. Thêm endpoint nội bộ `GET /api/auth/user-status/{id}`. Khi tài khoản bị khóa hoặc logout, request tiếp theo trả về `401 Unauthorized`. | Frontend khi nhận `401 Unauthorized` cần xóa token cục bộ và chuyển hướng người dùng về trang đăng nhập. |
| **Phase 08** | `POST /api/auth/refresh` | Refresh Token Hashing & Grace Period (`SEC-F08`, `SEC-F09`) | Token lưu DB được băm SHA-256; response chỉ trả chuỗi thô. Thêm cửa sổ ân hạn 30s cho concurrent refresh requests từ cùng client. | Xử lý mượt mà kịch bản đa tab mở đồng thời cùng refresh token, không còn bị đăng xuất oan khi mạng trễ. |
| **Phase 09** | `POST /api/clubs/{clubId}/member-roster/resolve`<br>`POST /api/activities/clubs/{clubId}/member-statistics` | Xác thực Roster & Quyền CLB (`SEC-F07`, `SEC-F11`) | Roster resolve hỗ trợ danh sách `UserIds`. Thống kê thành viên tự động loại bỏ user ngoài CLB; thống kê chi tiết trả `404 Not Found` nếu user không thuộc CLB; `JoinedAtUtc` chuẩn hóa từ server. Cache quyền CLB rút xuống 1m sliding / 3m absolute. | Frontend không thể gửi join date giả mạo từ client. Dữ liệu thống kê hiển thị chính xác theo thời gian gia nhập thực tế. |
| **Phase 11** | `POST /api/clubs/{clubId}/memberships/assign-treasurer`<br>`POST /api/clubs/{clubId}/managers`<br>`POST /api/clubs/{clubId}/disband-requests`<br>`POST /api/clubs/{clubId}/ownership-transfers` | Concurrency Invariants & Mã lỗi (`DATA-F01`, `DATA-F05`, `DATA-F06`) | Khi vượt quá 2 thủ quỹ, gán trùng chủ nhiệm đang active, hoặc gửi trùng đơn giải thể/chuyển giao đang chờ: chuẩn hóa trả về mã HTTP `409 Conflict` (thay vì 400 hoặc 500 unhandled exception). | Frontend cần xử lý mã HTTP `409 Conflict` để hiển thị thông báo lỗi nghiệp vụ rõ ràng cho người dùng. |
| **Phase 12** | `POST /api/finance/proposals/{id}/settlement`<br>`POST /api/reports`<br>`PUT /api/reports/{id}`<br>`POST /api/reports/{id}/file` | Invariants Quyết toán & Báo cáo (`DATA-F02`, `DATA-F04`) | Gửi quyết toán thứ hai trên đề xuất đã có quyết toán Submitted/Approved trả về `409 Conflict`. Tạo hoặc sửa báo cáo thường trùng `(ClubId, Period, Tag)` trả về `409 Conflict` (báo cáo `FUTURE_EVENT` vẫn cho phép nhiều bản ghi). | Frontend nhận diện `409 Conflict` khi người dùng nộp trùng báo cáo định kỳ hoặc tạo quyết toán trùng lặp. |
| **Phase 15** | `POST /api/exports/reports` / `POST /api/reports/{id}/preview` | Asynchronous Document Pipeline (`SEC-F14`, `REL-F08`, `CQ-F01`) | Quản lý tác vụ tạo PDF/Word qua Hangfire background jobs; xử lý lỗi snapshot deserialize bằng `InvalidOperationException` tường minh kèm trạng thái `Failed`. | Frontend polling trạng thái export hoặc nhận kết quả qua thông báo; loại bỏ rủi ro treo thread HTTP khi xuất file lớn. |
| **Phase 17** | `GET/POST /error` | Chuẩn hóa Observability & Error Handling (`OBS-F01`, `OBS-F02`, `OBS-F03`) | Bổ sung `ActivitySource` distributed tracing, `TraceId` trong mọi ProblemDetails response; hỗ trợ `/error` cho mọi HTTP method. | Frontend dễ dàng truy vết sự cố với `traceId` trong error modal. |
| **Phase 20** | `GET /api/clubs/{clubId}/members`<br>`GET /api/clubs/{clubId}/members/{memberId}` | Resilient Read Enrichment & Optional Pagination (`ARCH-F01`) | Các tham số `page`, `pageSize`, `historyPage`, `historyPageSize` chuyển thành tùy chọn (nullable với giá trị mặc định). Khi `ActivityService` gặp sự cố, endpoint tự động fallback chỉ số tham gia = 0 thay vì trả `503 Service Unavailable`. | Frontend có thể gọi danh sách thành viên mà không bắt buộc truyền query params; không bị crash màn hình thành viên khi service hoạt động gặp sự cố. |
| **Phase 21** | `POST /api/auth/refresh` | Siết chặt refresh-token hashing và concurrency (`SEC-F08`, `SEC-F09`) | Concurrent request cùng instance vẫn nhận response cache của winner. Khi raw replacement không còn trong cache, request cũ trả `401` thay vì trả replacement hash hoặc token chưa persist. | Frontend giữ response refresh thành công mới nhất; khi nhận `401`, dùng flow đăng nhập lại hiện có. Dev-login không thay đổi. |

