# GitNexus Engineering Plan — Admin FE API contract gap

> Độ sâu: Deep, đầy đủ 13 phần, impact depth 3, có PDG.
> Backend: FPTU-Xperience/fptu-xperience-clubhub-api; nhánh codex/final-review-fixes.
> Evidence verified at commit 37272c3c2c9419f5ebaf63ef1113e5e323d729aa.
> GitNexus được refresh trong phiên bằng analyze --index-only --pdg; context ghi nhận HEAD hiện tại. MCP vẫn trả cờ stale 12 commit ở một số kết quả; mọi kết luận chức năng dưới đây dựa vào source hiện tại, không dựa vào tính đầy đủ của graph.
> Evidence provenance schema 2; global dirty digest 0a9c85780067d9afcd0764f307b60891e3cee927ee11eaeb5ec7826d10fd82cd; 63 cited paths đã sắp xếp ở §11; chỉ loại đúng đường dẫn plan này khỏi global digest.
> Trạng thái: hoàn tất phân tích và kế hoạch; CHƯA triển khai. Các mục D1–D4 là quyết định nghiệp vụ/security còn mở, không được coi là đã hỗ trợ.
> Publication: người dùng đã cho phép ghi Markdown trực tiếp trên Windows, thay trình write-plan không tương thích; snapshot vẫn do helper gốc sinh.

## 1. Objective

[inferred] Bổ sung các API thực sự thiếu mà Admin FE cần, thống nhất phần contract có thể tương thích, và chỉ rõ phần tích hợp FE còn thiếu. Không sao chép dữ liệu mock thành nghiệp vụ thật.

Phạm vi kế hoạch:

- [verified] Đối chiếu đủ 121 method/path trong API_1.md với route đăng ký ở backend và cách FE gọi API.
- [inferred] Ưu tiên endpoint đọc, alias không đổi nghiệp vụ, rồi thao tác ghi có authorization/validation/test.
- [inferred] Giữ endpoint và payload cũ đang được consumer khác dùng; không đổi array thành object trên cùng contract một cách âm thầm.
- [inferred] Không đổi JWT role, XP/KPI, semester/quest/reward, EF/migration, bí mật hay deployment chỉ để khớp mock. Thay đổi những mục này cần scope được chốt riêng.
- [verified] Phiên hiện tại chỉ viết tài liệu; không commit/push/deploy hoặc chạy thao tác dữ liệu production.

## 2. Current Behaviour

### 2.1 Baseline và dữ liệu đầu vào

[verified] Backend HEAD 37272c3c2c9419f5ebaf63ef1113e5e323d729aa; working tree sạch trước khi tạo plan. FE đã cập nhật sang chores/refactor-css tại f22bda074fb0d541961077e5ee052879a1b8e70c, theo origin cùng nhánh; nhánh local cũ codex/login-homepage vẫn được giữ. Đây là baseline đã chọn từ lần cập nhật FE, không khẳng định đang dùng main của FE.

[verified] Repo FE là FPTU-Xperience/fptu-xperience-admin-ui. Các đường dẫn FE trong tài liệu được ký hiệu FE: và tính tương đối từ root FE; chúng không thuộc manifest Git backend. Tài liệu ngoài repo: API_1.md, SHA-256 98f7bb0cffde5785e1c7edecc61e96dbc0e3ab84d14e52ca0196e12a9e60eb53. Nội dung tài liệu là yêu cầu tham khảo, không có quyền cho phép bypass auth hoặc xóa dữ liệu.

[verified] FE:src/services/api.js có wrapper cho các nhóm trong tài liệu, nhưng các import/call thực tế chỉ tập trung ở FE:src/context/AuthContext.jsx, FE:src/context/WorkspaceContext.jsx và FE:src/pages/Dashboard.jsx. FE:src/context/WorkspaceContext.jsx:188 trở đi thực hiện commit vào state/localStorage; các trang Accounts/Clubs/Quests/Seasons gọi hàm local này. Có wrapper không chứng minh nút UI đã gọi backend.

[verified] FE AuthContext chấp nhận cả response.token và response.accessToken. Vì vậy tên accessToken của backend không phải lỗi chặn login hiện tại. Ngược lại, /api/v1/*/me chỉ trả subjectId, userId, email, roles; FE ghi đè user từ login bằng payload me nên có thể mất tên hiển thị.

[verified] FE Workspace/Dashboard yêu cầu users pageSize=200, trong khi UserEndpoints:37 hạ giá trị ngoài 1–100 về 20. Dashboard đếm users.length thay vì total, tính activeStudents bằng hệ số 0.68, và đọc aggregate.anomalies vốn không có trong contract ReportService. Đây là sai khác tích hợp/số liệu, không phải thiếu route.

### 2.2 Ma trận đầy đủ

[verified] E chỉ có nghĩa khai báo cùng HTTP method và đường dẫn sau khi chuẩn hóa tên/constraint của tham số route và dấu / cuối. Không chứng minh request, response, authorization hay runtime đã đạt. Kết quả là static source audit; health và ba route /api/v1/*/me được kiểm tra riêng theo registration group.

[inferred] A1/A2 là 6 alias tiềm năng. N1–N6 là nhóm triển khai ở §6–7. D1–D4 cần quyết định ở §12. Trong 34 method/path thiếu, 28 không phải 6 alias này; không được diễn giải thành 28 chức năng mới hoàn toàn.

| Nhóm | Tài liệu | Đã khai báo | Chưa khai báo đúng method/path |
| --- | ---: | ---: | ---: |
| 1. Authentication | 7 | 7 | 0 |
| 2. Users | 12 | 7 | 5 |
| 3. Clubs | 32 | 29 | 3 |
| 4. Activities | 12 | 6 | 6 |
| 5. Reports | 21 | 13 | 8 |
| 6. KPI | 2 | 2 | 0 |
| 7. Deadlines | 6 | 3 | 3 |
| 8. Finance | 19 | 11 | 8 |
| 9. Notifications | 4 | 3 | 1 |
| 10. Exports | 4 | 4 | 0 |
| 11. System Health | 2 | 2 | 0 |
| Tổng | 121 | 87 | 34 |

| Mục API_1 | Method/path | Kết quả tại HEAD | Hướng xử lý / bằng chứng |
| --- | --- | --- | --- |
| 1.1 | POST /api/auth/google | E — đã khai báo | src/Services/AuthService/Endpoints/AuthEndpoints.cs:23; đối chiếu shape ở §6 |
| 1.2 | POST /api/auth/dev-login | E — đã khai báo | src/Services/AuthService/Endpoints/AuthEndpoints.cs:28; đối chiếu shape ở §6 |
| 1.3 | POST /api/auth/logout | E — đã khai báo | src/Services/AuthService/Endpoints/AuthEndpoints.cs:44; đối chiếu shape ở §6 |
| 1.4 | POST /api/auth/refresh | E — đã khai báo | src/Services/AuthService/Endpoints/AuthEndpoints.cs:40; đối chiếu shape ở §6 |
| 1.5 | GET /api/v1/me | E — đã khai báo | src/Services/AdminService/Endpoints/AdminEndpoints.cs; đối chiếu shape ở §6 |
| 1.6 | GET /api/v1/admin/me | E — đã khai báo | src/Services/AdminService/Endpoints/AdminEndpoints.cs; đối chiếu shape ở §6 |
| 1.7 | GET /api/v1/student-affairs/me | E — đã khai báo | src/Services/AdminService/Endpoints/AdminEndpoints.cs; đối chiếu shape ở §6 |
| 2.1 | GET /api/users | E — đã khai báo | src/Services/AuthService/Endpoints/UserEndpoints.cs:21; đối chiếu shape ở §6 |
| 2.2 | GET /api/users/{id} | Thiếu method/path | N1 — đọc user theo id, SystemAdministration |
| 2.3 | GET /api/users/me | Thiếu method/path | N1 — profile chính chủ, AllActors; tách khỏi group quản trị |
| 2.4 | POST /api/users | E — đã khai báo | src/Services/AuthService/Endpoints/UserEndpoints.cs:22; đối chiếu shape ở §6 |
| 2.5 | PUT /api/users/{id} | E — đã khai báo | src/Services/AuthService/Endpoints/UserEndpoints.cs:23; đối chiếu shape ở §6 |
| 2.6 | DELETE /api/users/{id} | Thiếu method/path | D1 — quyết định vô hiệu hóa/retention, không xóa cứng mặc định |
| 2.7 | PATCH /api/users/{id}/lock | E — đã khai báo | src/Services/AuthService/Endpoints/UserEndpoints.cs:24; đối chiếu shape ở §6 |
| 2.8 | PATCH /api/users/{id}/unlock | E — đã khai báo | src/Services/AuthService/Endpoints/UserEndpoints.cs:25; đối chiếu shape ở §6 |
| 2.9 | GET /api/roles | E — đã khai báo | src/Services/AuthService/Endpoints/RoleEndpoints.cs:17; đối chiếu shape ở §6 |
| 2.10 | POST /api/roles | E — đã khai báo | src/Services/AuthService/Endpoints/RoleEndpoints.cs:18; đối chiếu shape ở §6 |
| 2.11 | POST /api/users/{userId}/roles | Thiếu method/path | D1 — hợp đồng role phải giữ invariant một actor role |
| 2.12 | DELETE /api/users/{userId}/roles/{roleId} | Thiếu method/path | D1 — không cho tài khoản mất actor role cuối |
| 3.1 | GET /api/clubs | E — đã khai báo | src/Services/ClubService/Endpoints/ClubEndpoints.cs:25; đối chiếu shape ở §6 |
| 3.2 | GET /api/clubs/{id} | E — đã khai báo | src/Services/ClubService/Endpoints/ClubEndpoints.cs:45; đối chiếu shape ở §6 |
| 3.3 | GET /api/clubs/me/memberships | E — đã khai báo | src/Services/ClubService/Endpoints/ClubEndpoints.cs:35; đối chiếu shape ở §6 |
| 3.4 | GET /api/clubs/me/managed | E — đã khai báo | src/Services/ClubService/Endpoints/ClubEndpoints.cs:30; đối chiếu shape ở §6 |
| 3.5 | GET /api/clubs/me/access | E — đã khai báo | src/Services/ClubService/Endpoints/ClubEndpoints.cs:40; đối chiếu shape ở §6 |
| 3.6 | POST /api/clubs | E — đã khai báo | src/Services/ClubService/Endpoints/ClubEndpoints.cs:55; đối chiếu shape ở §6 |
| 3.7 | PUT /api/clubs/{id} | E — đã khai báo | src/Services/ClubService/Endpoints/ClubEndpoints.cs:61; đối chiếu shape ở §6 |
| 3.8 | DELETE /api/clubs/{id} | E — đã khai báo | src/Services/ClubService/Endpoints/ClubEndpoints.cs:67; đối chiếu shape ở §6 |
| 3.9 | POST /api/clubs/{clubId}/join | E — đã khai báo | src/Services/ClubService/Endpoints/MembershipEndpoints.cs:27; đối chiếu shape ở §6 |
| 3.10 | GET /api/clubs/{clubId}/members | E — đã khai báo | src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs:28; đối chiếu shape ở §6 |
| 3.11 | GET /api/clubs/{clubId}/members/{memberId} | E — đã khai báo | src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs:33; đối chiếu shape ở §6 |
| 3.12 | POST /api/clubs/{clubId}/members | Thiếu method/path | D3 — nhập thành viên có kiểm soát; không giả mạo đơn/đồng ý |
| 3.13 | PUT /api/clubs/{clubId}/members/{memberId} | Thiếu method/path | N5 — whitelist thông tin hồ sơ; role đi qua workflow hiện có |
| 3.14 | DELETE /api/clubs/{clubId}/members/{memberId} | E — đã khai báo | src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs:38; đối chiếu shape ở §6 |
| 3.15 | GET /api/clubs/{clubId}/memberships | E — đã khai báo | src/Services/ClubService/Endpoints/MembershipEndpoints.cs:32; đối chiếu shape ở §6 |
| 3.16 | POST /api/clubs/memberships/{membershipId}/approve | E — đã khai báo | src/Services/ClubService/Endpoints/MembershipEndpoints.cs:37; đối chiếu shape ở §6 |
| 3.17 | POST /api/clubs/memberships/{membershipId}/reject | E — đã khai báo | src/Services/ClubService/Endpoints/MembershipEndpoints.cs:42; đối chiếu shape ở §6 |
| 3.18 | POST /api/clubs/{clubId}/treasurers | E — đã khai báo | src/Services/ClubService/Endpoints/MembershipEndpoints.cs:47; đối chiếu shape ở §6 |
| 3.19 | POST /api/clubs/applications | E — đã khai báo | src/Services/ClubService/Endpoints/ApplicationEndpoints.cs:36; đối chiếu shape ở §6 |
| 3.20 | GET /api/clubs/applications | E — đã khai báo | src/Services/ClubService/Endpoints/ApplicationEndpoints.cs:25; đối chiếu shape ở §6 |
| 3.21 | GET /api/clubs/applications/me | E — đã khai báo | src/Services/ClubService/Endpoints/ApplicationEndpoints.cs:31; đối chiếu shape ở §6 |
| 3.22 | GET /api/clubs/applications/{id} | Thiếu method/path | N2 — chỉ chủ đơn hoặc reviewer hợp lệ |
| 3.23 | PUT /api/clubs/applications/{id} | E — đã khai báo | src/Services/ClubService/Endpoints/ApplicationEndpoints.cs:42; đối chiếu shape ở §6 |
| 3.24 | POST /api/clubs/applications/{id}/approve | E — đã khai báo | src/Services/ClubService/Endpoints/ApplicationEndpoints.cs:48; đối chiếu shape ở §6 |
| 3.25 | POST /api/clubs/applications/{id}/request-revision | E — đã khai báo | src/Services/ClubService/Endpoints/ApplicationEndpoints.cs:54; đối chiếu shape ở §6 |
| 3.26 | POST /api/clubs/applications/{id}/reject | E — đã khai báo | src/Services/ClubService/Endpoints/ApplicationEndpoints.cs:60; đối chiếu shape ở §6 |
| 3.27 | GET /api/clubs/disband-requests | E — đã khai báo | src/Services/ClubService/Endpoints/DisbandEndpoints.cs:35; đối chiếu shape ở §6 |
| 3.28 | POST /api/clubs/disband-requests/{id}/approve | E — đã khai báo | src/Services/ClubService/Endpoints/DisbandEndpoints.cs:41; đối chiếu shape ở §6 |
| 3.29 | POST /api/clubs/disband-requests/{id}/reject | E — đã khai báo | src/Services/ClubService/Endpoints/DisbandEndpoints.cs:47; đối chiếu shape ở §6 |
| 3.30 | GET /api/clubs/transfer-requests | E — đã khai báo | src/Services/ClubService/Endpoints/TransferEndpoints.cs:44; đối chiếu shape ở §6 |
| 3.31 | POST /api/clubs/transfer-requests/{id}/approve | E — đã khai báo | src/Services/ClubService/Endpoints/TransferEndpoints.cs:50; đối chiếu shape ở §6 |
| 3.32 | POST /api/clubs/transfer-requests/{id}/reject | E — đã khai báo | src/Services/ClubService/Endpoints/TransferEndpoints.cs:56; đối chiếu shape ở §6 |
| 4.1 | GET /api/activities | E — đã khai báo | src/Services/ActivityService/Endpoints/ActivityEndpoints.cs:38; đối chiếu shape ở §6 |
| 4.2 | GET /api/activities/{id} | E — đã khai báo | src/Services/ActivityService/Endpoints/ActivityEndpoints.cs:107; đối chiếu shape ở §6 |
| 4.3 | POST /api/activities | E — đã khai báo | src/Services/ActivityService/Endpoints/ActivityEndpoints.cs:146; đối chiếu shape ở §6 |
| 4.4 | PUT /api/activities/{id} | Thiếu method/path | N5 — sửa lịch/nội dung; khóa bản ghi đã hoàn thành hoặc report-linked |
| 4.5 | DELETE /api/activities/{id} | Thiếu method/path | D3 — xác nhận cancel hay delete và retention lịch sử điểm danh |
| 4.6 | POST /api/activities/{id}/check-in | Thiếu method/path | N5 — tự check-in, danh tính từ JWT, ngày Việt Nam |
| 4.7 | POST /api/activities/{id}/participants | Thiếu method/path | N5 — đăng ký chính chủ hoặc manager đúng club |
| 4.8 | PATCH /api/activities/{id}/complete | Thiếu method/path | N5 — chuyển trạng thái có điều kiện, chống lặp side effect |
| 4.9 | GET /api/activities/{id}/my-attendance | Thiếu method/path | N5 — lịch sử của chính chủ, có phân trang |
| 4.10 | GET /api/clubs/{clubId}/activities/{activityId}/attendance | E — đã khai báo | src/Services/ActivityService/Endpoints/AttendanceManagementEndpoints.cs:29; đối chiếu shape ở §6 |
| 4.11 | PUT /api/clubs/{clubId}/activities/{activityId}/attendance/{memberId} | E — đã khai báo | src/Services/ActivityService/Endpoints/AttendanceManagementEndpoints.cs:202; đối chiếu shape ở §6 |
| 4.12 | PUT /api/clubs/{clubId}/activities/{activityId}/attendance | E — đã khai báo | src/Services/ActivityService/Endpoints/AttendanceManagementEndpoints.cs:348; đối chiếu shape ở §6 |
| 5.1 | GET /api/reports | E — đã khai báo | src/Services/ReportService/Endpoints/ReportQueryEndpoints.cs:17; đối chiếu shape ở §6 |
| 5.2 | GET /api/reports/summary | E — đã khai báo | src/Services/ReportService/Endpoints/ReportQueryEndpoints.cs:18; đối chiếu shape ở §6 |
| 5.3 | GET /api/reports/aggregate | E — đã khai báo | src/Services/ReportService/Endpoints/ReportQueryEndpoints.cs:19; đối chiếu shape ở §6 |
| 5.4 | GET /api/reports/{id} | E — đã khai báo | src/Services/ReportService/Endpoints/ReportQueryEndpoints.cs:21; đối chiếu shape ở §6 |
| 5.5 | POST /api/reports | E — đã khai báo | src/Services/ReportService/Endpoints/ReportCrudEndpoints.cs:21; đối chiếu shape ở §6 |
| 5.6 | PUT /api/reports/{id} | E — đã khai báo | src/Services/ReportService/Endpoints/ReportCrudEndpoints.cs:22; đối chiếu shape ở §6 |
| 5.7 | DELETE /api/reports/{id} | Thiếu method/path | D3 — retention báo cáo/audit/finance/file trước khi cho xóa |
| 5.8 | POST /api/reports/{id}/submit | E — đã khai báo | src/Services/ReportService/Endpoints/ReportWorkflowEndpoints.cs:21; đối chiếu shape ở §6 |
| 5.9 | POST /api/reports/{id}/review | E — đã khai báo | src/Services/ReportService/Endpoints/ReportWorkflowEndpoints.cs:23; đối chiếu shape ở §6 |
| 5.10 | POST /api/reports/{id}/approve | E — đã khai báo | src/Services/ReportService/Endpoints/ReportWorkflowEndpoints.cs:24; đối chiếu shape ở §6 |
| 5.11 | POST /api/reports/{id}/reject | E — đã khai báo | src/Services/ReportService/Endpoints/ReportWorkflowEndpoints.cs:26; đối chiếu shape ở §6 |
| 5.12 | POST /api/reports/upload | E — đã khai báo | src/Services/ReportService/Endpoints/ReportFileEndpoints.cs:21; đối chiếu shape ở §6 |
| 5.13 | GET /api/reporting-deadlines | Thiếu method/path | A2 — alias deadline có cùng scope, thêm YARP route |
| 5.14 | GET /api/reports/{id}/file | Thiếu method/path | A1 — alias GET uploaded-file |
| 5.15 | GET /api/reports/{id}/file/download | Thiếu method/path | A1 — alias GET uploaded-file/download |
| 5.16 | GET /api/reports/{id}/file/preview | Thiếu method/path | A1 — alias GET uploaded-file/preview |
| 5.17 | POST /api/reports/{id}/file | Thiếu method/path | A1 — POST file gọi cùng xử lý PUT uploaded-file |
| 5.18 | DELETE /api/reports/{id}/file | Thiếu method/path | A1 — alias DELETE uploaded-file |
| 5.19 | POST /api/reports/{id}/attachments/upload | E — đã khai báo | src/Services/ReportService/Endpoints/ReportFileEndpoints.cs:29; đối chiếu shape ở §6 |
| 5.20 | GET /api/reports/{reportId}/attachments/{attachmentId}/download | E — đã khai báo | src/Services/ReportService/Endpoints/ReportFileEndpoints.cs:32; đối chiếu shape ở §6 |
| 5.21 | DELETE /api/reports/{reportId}/attachments/{attachmentId} | Thiếu method/path | N4 — check report + attachment + author + trạng thái rồi xóa metadata |
| 6.1 | GET /api/kpis/leaderboard | E — đã khai báo | src/Services/ReportService/Endpoints/KpiEndpoints.cs:26; đối chiếu shape ở §6 |
| 6.2 | GET /api/kpis/rules | E — đã khai báo | src/Services/ReportService/Endpoints/KpiEndpoints.cs:25; đối chiếu shape ở §6 |
| 7.1 | GET /api/deadlines | E — đã khai báo | src/Services/ReportService/Endpoints/DeadlineEndpoints.cs:18; đối chiếu shape ở §6 |
| 7.2 | GET /api/deadlines/{period} | Thiếu method/path | N3 — lookup period chuẩn hóa, 404 nếu không có |
| 7.3 | GET /api/deadlines/me | E — đã khai báo | src/Services/ReportService/Endpoints/DeadlineEndpoints.cs:22; đối chiếu shape ở §6 |
| 7.4 | POST /api/deadlines | E — đã khai báo | src/Services/ReportService/Endpoints/DeadlineEndpoints.cs:19; đối chiếu shape ở §6 |
| 7.5 | PUT /api/deadlines/{period} | Thiếu method/path | N3 — update theo period, không đổi khóa qua body |
| 7.6 | DELETE /api/deadlines/{period} | Thiếu method/path | N3 — đề xuất vô hiệu hóa IsActive, không xóa lịch sử báo cáo |
| 8.1 | GET /api/finance/proposals | E — đã khai báo | src/Services/FinanceService/Endpoints/ProposalEndpoints.cs:22; đối chiếu shape ở §6 |
| 8.2 | GET /api/finance/proposals/{id} | E — đã khai báo | src/Services/FinanceService/Endpoints/ProposalEndpoints.cs:23; đối chiếu shape ở §6 |
| 8.3 | POST /api/finance/proposals | E — đã khai báo | src/Services/FinanceService/Endpoints/ProposalEndpoints.cs:24; đối chiếu shape ở §6 |
| 8.4 | POST /api/finance/proposals/{id}/submit | Thiếu method/path | D2 — create đã Submitted; chốt idempotent acknowledge hay Draft thật |
| 8.5 | POST /api/finance/proposals/{id}/manager-review | Thiếu method/path | D2 — review là ghi chú hay transition? không alias approve |
| 8.6 | POST /api/finance/proposals/{id}/manager-approve | E — đã khai báo | src/Services/FinanceService/Endpoints/ProposalEndpoints.cs:25; đối chiếu shape ở §6 |
| 8.7 | POST /api/finance/proposals/{id}/manager-reject | E — đã khai báo | src/Services/FinanceService/Endpoints/ProposalEndpoints.cs:26; đối chiếu shape ở §6 |
| 8.8 | POST /api/finance/proposals/{id}/review | Thiếu method/path | D2 — giữ cổng internal workflow với proposal gắn báo cáo |
| 8.9 | POST /api/finance/proposals/{id}/approve | E — đã khai báo | src/Services/FinanceService/Endpoints/ProposalEndpoints.cs:27; đối chiếu shape ở §6 |
| 8.10 | POST /api/finance/proposals/{id}/reject | E — đã khai báo | src/Services/FinanceService/Endpoints/ProposalEndpoints.cs:29; đối chiếu shape ở §6 |
| 8.11 | GET /api/finance/settlements | E — đã khai báo | src/Services/FinanceService/Endpoints/SettlementEndpoints.cs:16; đối chiếu shape ở §6 |
| 8.12 | GET /api/finance/settlements/{id} | Thiếu method/path | N2 — đọc settlement, kiểm tra club qua proposal |
| 8.13 | POST /api/finance/proposals/{proposalId}/settlements | E — đã khai báo | src/Services/FinanceService/Endpoints/SettlementEndpoints.cs:17; đối chiếu shape ở §6 |
| 8.14 | POST /api/finance/settlements/{id}/submit | Thiếu method/path | D2 — create đã Submitted; không tạo thêm ledger khi gọi lặp |
| 8.15 | POST /api/finance/settlements/{id}/review | Thiếu method/path | D2 — chốt ý nghĩa review, không tự phê duyệt |
| 8.16 | POST /api/finance/settlements/{id}/approve | E — đã khai báo | src/Services/FinanceService/Endpoints/SettlementEndpoints.cs:18; đối chiếu shape ở §6 |
| 8.17 | POST /api/finance/settlements/{id}/reject | Thiếu method/path | N6 — reject Submitted, không tự duyệt, không tạo approval transaction |
| 8.18 | GET /api/finance/transactions | E — đã khai báo | src/Services/FinanceService/Endpoints/TransactionEndpoints.cs:14; đối chiếu shape ở §6 |
| 8.19 | POST /api/finance/transactions | Thiếu method/path | D2 — quyền tạo bút toán thủ công, loại và idempotency chưa có contract |
| 9.1 | GET /api/notifications | E — đã khai báo | src/Services/NotificationService/Endpoints/NotificationEndpoints.cs:19; đối chiếu shape ở §6 |
| 9.2 | GET /api/notifications/{id} | Thiếu method/path | N2 — chỉ đúng recipient/user-role scope hiện có |
| 9.3 | PUT /api/notifications/{id}/read | E — đã khai báo | src/Services/NotificationService/Endpoints/NotificationEndpoints.cs:20; đối chiếu shape ở §6 |
| 9.4 | PUT /api/notifications/read-all | E — đã khai báo | src/Services/NotificationService/Endpoints/NotificationEndpoints.cs:21; đối chiếu shape ở §6 |
| 10.1 | GET /api/exports | E — đã khai báo | src/Services/ExportService/Endpoints/ExportEndpoints.cs:27; đối chiếu shape ở §6 |
| 10.2 | GET /api/exports/{id} | E — đã khai báo | src/Services/ExportService/Endpoints/ExportEndpoints.cs:28; đối chiếu shape ở §6 |
| 10.3 | POST /api/exports | E — đã khai báo | src/Services/ExportService/Endpoints/ExportEndpoints.cs:29; đối chiếu shape ở §6 |
| 10.4 | GET /api/exports/{id}/download | E — đã khai báo | src/Services/ExportService/Endpoints/ExportEndpoints.cs:30; đối chiếu shape ở §6 |
| 11.1 | GET / | E — đã khai báo | src/Gateway/ApiGateway/Program.cs; đối chiếu shape ở §6 |
| 11.2 | GET /health | E — đã khai báo | src/Gateway/ApiGateway/Program.cs + StandardHealthCheckExtensions:27; đối chiếu shape ở §6 |


### 2.3 Vì sao chỉ thêm route chưa đủ

[verified] API_1 dùng ID mẫu dạng user-001/club-001/report-001, trong khi các domain route dùng int; chỉ audit và subjectId có loại khác. Không thay kiểu khóa DB theo ID mẫu.

[verified] Backend dùng trạng thái và tên trường thực, ví dụ RequestedAmount, TotalSpent, Title, StartTimeUtc, IsRead, ReportType/Details; tài liệu dùng amount, name, startAt, read, title/content. Finance tạo proposal/settlement ở Submitted và chưa có trạng thái Draft/UnderReview trong FinanceStatuses. Export yêu cầu ReportId dương, PDF/XLSX/DOCX, scope Report; không hiện thực xuất danh sách sinh viên theo season như ví dụ.

## 3. Relevant Architecture

[verified] Gateway YARP định tuyến /api/auth, users, roles tới AuthService; /api/clubs tới ClubService; route cụ thể /api/clubs/{clubId}/activities/... tới ActivityService; reports/deadlines/kpis tới ReportService; finance, notifications, exports và /api/v1 backoffice tới service tương ứng. Nguồn: src/Gateway/ApiGateway/yarp.json.

[verified] Các service sở hữu DbContext riêng và dùng JWT + authorization policy; các thao tác liên quan club thường tra scope qua ClubAccessClient. Không thêm truy cập thẳng DB Auth/Club vào AdminService chỉ để lấy tên hiển thị.

[verified] AuthPolicies.SystemAdministration nhận ADMIN/SYSTEM_ADMIN; StudentAffairsAdministration nhận ADMIN/STUDENT_AFFAIRS_ADMIN; BusinessAccess không nhận SYSTEM_ADMIN. AdminService có policy riêng cho hai workspace, được test 403 chéo. Đây không phải cùng một bộ quyền có thể thay bằng một check “is admin”.

[verified] ActorAccountPolicy hiện chỉ cho đúng một role ADMIN, CLUB_MANAGER hoặc CLUB_MEMBER qua Google/dev-login. AuthEndpoints:28 mở dev-login trong mọi environment, nhưng chỉ cho account có sẵn, active, unlocked và cấu hình actor hợp lệ; không tự cấp role từ chuỗi email.

[inferred] Ba ranh giới phải giữ khi bổ sung API: authority từ principal/club scope; state transition của report/finance; dữ liệu file và ledger phải gắn đúng resource. Tài liệu không thay thế các ranh giới này.

## 4. GitNexus Findings

### 4.1 Provenance và phạm vi graph

[graph] Context trước refresh: 12 commit sau index 33bf7fe. Runner identity schema 4 của CLI 1.6.12 khớp bản index; dùng node .gitnexus/run.cjs analyze --index-only --pdg, exit 0. Context sau refresh ghi commit 37272c3c2c9419f5ebaf63ef1113e5e323d729aa, indexed_at 2026-09-22T16:13:51.648Z; 16,692 nodes, 34,960 edges, 126 clusters, 345 flows.

[graph] Analyzer cảnh báo quá trình trích xuất flow bị giới hạn: 38 entry candidates không vào bảng, 125 callees bị bỏ, 6 walks hết budget. Không có flow không đồng nghĩa không có code path. Resource processes chỉ trả top 20 của một view 50; không dùng nó như danh mục toàn bộ 345 flows.

[graph] MCP query/context/impact vẫn gắn “Index is 12 commits behind HEAD” sau khi context resource đã đổi HEAD, dù symbol ranges đã theo source mới. Ghi nhận metadata không nhất quán; không refresh lặp vô hạn, không coi blast-radius là chứng nhận sạch. FE graph cũng 5 commit cũ; source FE hiện tại là bằng chứng chính.

### 4.2 Symbol chính, caller và rủi ro

[graph] context(name) và impact(target, direction=upstream, maxDepth=3) đã chạy cho UserEndpoints, ReportFileEndpoints, ProposalEndpoints, SettlementEndpoints, ActivityEndpoints. Cả năm trả risk=UNKNOWN, direct=0; trích kết quả: “No callers resolved. Absence of edges is not evidence the symbol is unused”. Method-level impact cho HandleUpdateUser, MapReportFileEndpoints, MapActivityEndpoints, ApproveSettlement cũng không resolve caller.

[verified] Text confirmation sau UNKNOWN tìm được caller/registration thực:

| Boundary | Caller/consumer xác nhận | Cần bảo vệ khi sửa |
| --- | --- | --- |
| UserEndpoints | src/Services/AuthService/Program.cs:57; JwtAuthorizationFreshnessTests.cs:291; RefreshTokenSecurityTests.cs:300; FE WorkspaceContext/Dashboard | Auth/session invalidation, danh sách người dùng |
| ReportFileEndpoints | src/Services/ReportService/Endpoints/ReportEndpoints.cs:15; YARP report upload routes; FE api.js wrappers | Authorization, rate limit upload, download/preview và file ownership |
| ProposalEndpoints | src/Services/FinanceService/Endpoints/FinanceEndpoints.cs:13; CrossServiceWorkflowAuthorizationTests.cs:184,260; ReportWorkflowEndpoints luồng gọi finance | Không mở cổng review trực tiếp cho report-linked proposal |
| SettlementEndpoints | src/Services/FinanceService/Endpoints/FinanceEndpoints.cs:14; FinanceAndReportDataIntegrityTests.cs:404 | Active-settlement uniqueness và ledger |
| ActivityEndpoints | src/Services/ActivityService/Program.cs:18; CrossServiceWorkflowAuthorizationTests.cs:565 và các host tiếp theo | Club scope, nguồn báo cáo được duyệt, outbox |

[inferred] Không có d=1 graph-resolved nào bị bỏ khỏi danh sách; các caller source-derived ở bảng trên bù phần graph không quan sát được, không làm UNKNOWN trở thành LOW. Mức rủi ro thiết kế cao nhất là auth/role và ledger; đây là đánh giá source-derived, không phải verdict HIGH của GitNexus.

[graph] query “UserEndpoints update roles...” dẫn đến HandleUpdateUser → ValidateUpdateUser → Success; query report file dẫn đến UploadReportFile → normalize/club access; resource processes có MapAttendanceManagementEndpoints → FetchAccessFromApiAsync và MapCreateActivity → FetchAccessFromApiAsync. Các flow chỉ dùng xác định vị trí cần xem.

## 5. Statement-Level PDG Findings

PDG được giới hạn ở ba hàm. Lượt giới hạn 12 edges ban đầu bị truncated ở hai hàm; đã chạy lại limit=100 và có đủ 41/21 edges. Không dùng kết quả cắt làm kết luận đầy đủ. Chỉ giữ lát cắt liên quan dưới đây.

### 5.1 HandleUpdateUser — AuthService UserEndpoints:144–274

[graph][verified] controls:41, flows(variable=user):9, không truncated. Luồng source xác nhận:

1. 157–195: validate request, tìm account, kiểm email trùng, yêu cầu đúng một actor role hợp lệ.
2. 203–229: chặn tác động ADMIN bởi actor không có ADMIN; chặn tự deactive/đổi role; chặn mất ADMIN active cuối.
3. 235–250: cập nhật thông tin, xóa GoogleSubject nếu đổi email, thay role rồi SaveChanges.
4. 252–267: xác định thay đổi ảnh hưởng quyền; tăng SecurityVersion, bỏ cache security stamp, lưu, rồi revoke refresh token.

[graph] Dòng định nghĩa user:163 có REACHING_DEF tới kiểm null:168, role:198, mutation:235/243/246, version:258, revoke:267, response:273.

[inferred] Bất kỳ endpoint role/disable mới nào đều phải đi qua cùng invariant và invalidation; không viết trực tiếp bảng UserRoles chỉ vì API_1 có assign/remove. Source hiện có nhiều lần SaveChanges; không khẳng định thao tác này đã atomic. Nếu triển khai mutation mới phải đặt transaction/concurrency test, không tiện tay refactor toàn bộ auth trong đợt alias.

### 5.2 ReplaceUploadedFile — ReportFileEndpoints:363–448

[graph][verified] controls:21, không truncated. Trật tự bắt buộc: tìm report:372–381 → author scope:384–386 → chỉ Draft/Rejected:389–391 → multipart:394–400 → file tồn tại/validate:401–409 → lưu nội dung:415 → metadata/audit/SaveChanges:422–445.

[graph] flows(variable=savedFile) trả 0; không có bằng chứng tuple/property-flow được mô hình hóa. [verified] Source vẫn cho thấy savedFile được dùng tạo metadata. Không tự dựng PDG edge để bù.

[inferred] POST /file phải gọi cùng logic của PUT /uploaded-file, giữ nguyên checks và giới hạn tải lên; không tạo handler “đơn giản” bỏ authorization. File bytes được ghi trước DB save: failure cleanup cần test, chưa chứng minh rollback cả filesystem. Không lộ StoragePath hay preview exception nội bộ.

### 5.3 ApproveSettlement — SettlementEndpoints:148–185

[graph][verified] controls:7, không truncated: không tồn tại →404; status khác Submitted →400; creator của proposal tự duyệt →400. Chỉ nhánh hợp lệ mới đặt settlement Approved, parent proposal Settled, thêm FinanceTransaction và SaveChanges.

[inferred] Reject/review hoặc submit bổ sung không được đi nhầm nhánh này, không tạo approval transaction, không chuyển parent Settled. Check-then-write hiện tại không chứng minh chống double-approve khi đồng thời; cần test DB relational/SQL Server và bảo vệ concurrency cho đường mới.

[graph] explain(target=src/Services/ReportService/Endpoints/ReportFileEndpoints.cs) trả 0 taint findings và cảnh báo closure/property/implicit flows không được mô hình hóa. Không có finding không phải bằng chứng file API an toàn.

## 6. Proposed Changes

Các mục dưới đây là đề xuất [inferred], chưa phải code đã sửa. Symbol dùng để chỉ điểm mở rộng đều đã thấy trong source; tên handler mới sẽ được đặt khi triển khai, không giả định chúng đang tồn tại.

### 6.1 Quy tắc contract chung

- Giữ nguyên URL/fields cũ. Alias URL gọi cùng handler/service và cùng policy. Có field alias mới thì chỉ thêm khi cùng ý nghĩa; nếu client gửi cả field cũ và mới trái nhau, trả 400, không chọn ngẫu nhiên.
- Giữ ID thực là int; client có thể biểu diễn số thành string, nhưng không chấp nhận ID mẫu có prefix. Phân biệt memberId (membership) với userId, reportId với attachmentId, proposalId với settlementId.
- Không đổi list array cũ thành paginated object mà không có contract opt-in hoặc migration FE. FE đã có normalizer array/items/data; ưu tiên dùng normalizer. Với route đang có pagination, dùng page/pageSize optional với default rõ ràng, cap 100 và total; không bắt query bắt buộc ngoài ý muốn.
- Error: 400 invalid input, 401 token thiếu/sai, 403 không đúng quyền, 404 resource không tồn tại hoặc đã được che scope theo quy ước service, 409 invariant/concurrency. Không biến 403/500 thành array trống hay success.
- Không thêm một global middleware đổi toàn bộ JSON. Request adaptation nằm ở boundary của từng service; giữ validation hiện có và kiểm null trước Trim/ToArray.
- Không log access/refresh token, credential, secret, StoragePath hoặc đường dẫn vật lý. Correlation ID và resource ID đủ cho chẩn đoán.
- Không giảm auth/rate limit của gateway để cho mock token chạy được.

### 6.2 Request/response khác nhau đáng chú ý

[verified] Cột “hiện tại” dựa vào các file Contracts trong manifest và các handler đã đọc. Cột xử lý là đề xuất [inferred].

| Nhóm | API_1 / FE | Backend hiện tại | Xử lý |
| --- | --- | --- | --- |
| Login | token | accessToken + expiry + user | FE đã hỗ trợ; không cần sửa token contract chỉ để đổi tên |
| Me/profile | id/fullName/username/studentCode/clubIds | v1 me: subjectId/userId/email/roles | Thêm /users/me cho profile Auth; FE hợp nhất với v1 actor, không thay policy backoffice; studentCode/clubIds chưa có nguồn thì không bịa |
| Users | pageSize=200, status/createdAt | cap100, isActive/isLocked, user summary | FE phân trang và dùng total; phân biệt inactive với locked; không tạo createdAt giả ở server |
| Clubs | type/status/managerId/foundedAt | category/isActive/managers; dữ liệu giới thiệu phong phú hơn | type↔category có thể là alias; không lấy manager đầu tiên làm owner; nguồn foundedAt chưa xác nhận |
| Applications | status filter; review string/note | list hiện không lọc status; review object có note/conditions/signature | Thêm filter phía server, request object rõ ràng; không mặc định chuỗi thành approval |
| Activities | name/startAt/endAt | title/startTimeUtc/endTimeUtc/meetingDays | Alias field có kiểm conflict; giữ offset/timezone và validation |
| Attendance | checkedIn; bulk attendances[userId] | status/note; bulk items[memberId]; present/absent/excused/late/not-marked | Không ép mọi status thành boolean; resolve user trong đúng roster, validate toàn bộ batch trước ghi |
| Reports | title/content/rating | reportType/tag/executiveSummary/details; review.feedback | Nội dung/rating không tương đương: FE dùng structured contract; không âm thầm bỏ rating hoặc biến thành XP |
| Report files | /file + POST replacement | /uploaded-file + PUT replacement | Thêm năm alias, giữ old routes |
| Deadlines | type/period/dueDate, thiếu isActive | period/dueDate/isActive | Không tự tạo type mới; create mặc định active nếu thống nhất, update bỏ field thì giữ giá trị cũ; sửa nullable/default rõ ràng |
| KPI | leaderboard/name/score; rules.weight | clubs/clubName/points; rule code/points/description | Adapter hiển thị theo nghĩa points; weight không tự suy ra từ points; giữ công thức KPI |
| Finance | amount/review, tạo Draft giả | requestedAmount/approvedAmount/totalSpent/note; create Submitted | Map amount theo từng endpoint; review/submit phải qua D2 |
| Notifications | read/type/createdAt + pagination | isRead/eventType/createdAtUtc; list tối đa100 dạng array | Adapter field; thêm GET detail dùng cùng recipient scope; không tính total=100 như tổng DB |
| Exports | type=excel, name, filters.season, clubId=all | exportType PDF/XLSX/DOCX, bắt buộc reportId, scope Report | excel↔XLSX chỉ cho report export hợp lệ; xuất sinh viên theo kỳ là capability D4 |
| Health | JSON status/timestamp | root service/status; /health mặc định health-check text | FE hỗ trợ text; không đổi readiness/liveness vì mock JSON |

### 6.3 A1 — Report file aliases

File: src/Services/ReportService/Endpoints/ReportFileEndpoints.cs; symbol: ReportFileEndpoints. Dependency: group BusinessAccess, report author/view checks, upload storage logic hiện có.

- Thêm GET /{reportId:int}/file, GET /file/preview, GET /file/download, POST /file, DELETE /file.
- Dùng đúng tên reportId trong route binding của handler đang có; không map {id} vào tham số reportId gây binder hiểu là query.
- POST /file dùng cùng replacement logic với PUT /uploaded-file, multipart field file, cancellation token, cùng antiforgery metadata phù hợp bearer API. Không redirect POST sang PUT.
- Không nhân bản state mutation hoặc generate preview thêm lần nữa. Có thể giữ response backend hiện tại và chuẩn hóa FE; phải ghi rõ response metadata hay report envelope trước test.
- File src/Gateway/ApiGateway/yarp.json: thêm route ưu tiên cho /api/reports/{reportId}/file tương đương report-file-replace để không rơi xuống general-api limiter. Giữ Default authorization và upload policy, không đổi cluster.
- Kiểm parity lỗi 401/403/404/400 và nội dung download giữa alias/canonical route.

### 6.4 A2 + N3 — Deadline compatibility

File: src/Services/ReportService/Endpoints/DeadlineEndpoints.cs; symbol: DeadlineEndpoints; contract ở src/Services/ReportService/Contracts/ReportContracts.cs.

- GET /api/reporting-deadlines: alias quản trị tương đương /api/deadlines, cùng StudentAffairsAdministration; không làm endpoint công khai. Nếu manager cần dùng, FE dùng /deadlines/me với scope hiện có, không nới alias mặc định.
- Thêm YARP /api/reporting-deadlines tới report-service, Default auth và general-api limit.
- GET /api/deadlines/{period}: lookup theo period đã trim, 404 không có; /me phải còn match literal, không bị route period bắt.
- PUT /{period}: update đúng record, 404 nếu chưa có; body period khác path →400; không chuyển kỳ qua update. Giữ POST upsert cũ.
- DELETE /{period}: đề xuất tắt IsActive để bảo toàn tham chiếu và lịch sử; mô tả rõ soft-disable trong contract. GET quản trị vẫn có thể xem bản inactive; GET me loại inactive. Gọi lặp không sinh side effect.
- Period FA26 và FALL2026 không được tự coi là một kỳ; D4 chốt mapping nếu thật sự cần. Không sửa semester engine.
- [verified] Model ReportingDeadline hiện có IsActive nên hướng soft-disable không đòi thêm cột.

### 6.5 N1 — User read/profile

File: src/Services/AuthService/Endpoints/UserEndpoints.cs; symbol: UserEndpoints; DTO tại src/Services/AuthService/Contracts/AuthContracts.cs.

- GET /api/users/{id:int}: cùng SystemAdministration, truy vấn AsNoTracking + roles, trả summary/profile whitelist, 404 nếu không có.
- GET /api/users/me: map riêng với AllActors trước/ngoài group SystemAdministration; lấy userId từ principal. Không nhận id/email/role trong body/query để chọn account khác.
- Không trả GoogleSubject, SecurityVersion, token hoặc bất kỳ data auth nội bộ. Chỉ thêm field có nguồn dữ liệu thật.
- Giữ /api/v1/*/me là actor identity của AdminService. Ưu tiên FE ghép profile từ /users/me thay vì thêm cross-DB dependency cho AdminService.
- D1 xử lý DELETE/assign/remove roles riêng. Không biến endpoint đọc thành lý do mở SYSTEM_ADMIN/CTSV vào login policy.

### 6.6 N2 — Missing scoped detail reads và filter thật

- src/Services/ClubService/Endpoints/ApplicationEndpoints.cs, ApplicationEndpoints: GET /applications/{applicationId:int}; owner hoặc reviewer có quyền theo rule hiện có, không lộ đơn của người khác. Thêm status filter optional cho list đang bỏ qua query, dùng status backend thực và whitelist. Giữ list shape cũ.
- src/Services/FinanceService/Endpoints/SettlementEndpoints.cs, SettlementEndpoints: GET /settlements/{id:int}; include proposal để kiểm club. Reviewer hợp lệ hoặc actor có finance access của club mới thấy. Không lấy scope từ clubId do client tùy chọn để vượt check.
- src/Services/NotificationService/Endpoints/NotificationEndpoints.cs, NotificationEndpoints: GET /{id:int}; dùng cùng recipient resolution/access check như MarkAsReadAsync. Không cho đọc thông báo chỉ vì biết ID. Không thay notification “read” semantics theo mock.

### 6.7 N4 — Attachment deletion

File: src/Services/ReportService/Endpoints/ReportFileEndpoints.cs; symbol: ReportFileEndpoints.

- DELETE /{reportId:int}/attachments/{attachmentId:int}; tìm bằng cả hai khóa, xác thực author scope và trạng thái được sửa như file API (Draft/Rejected).
- Xóa metadata trong DB cùng audit; ID của report khác →không xóa gì. Không dùng StoragePath từ client.
- Không xóa nội dung vật lý trước DB commit. Bản đầu có thể chỉ gỡ metadata và ghi nhận orphan cleanup cần follow-up; retention/tái sử dụng file phải được chốt trước physical deletion.
- Body file metadata hoặc absolute path không phải authority. Test traversal, prefix-collision và rollback; giữ masking đường dẫn.

### 6.8 N5 — Activities và member profile writes

Files: src/Services/ActivityService/Endpoints/ActivityEndpoints.cs, src/Services/ActivityService/Endpoints/AttendanceManagementEndpoints.cs, src/Services/ActivityService/Contracts/ActivityContracts.cs, src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs và Contracts/ClubContracts.cs.

Symbols: ActivityEndpoints, AttendanceManagementEndpoints, MemberManagementEndpoints.

- PUT /activities/{id}: chỉ manager đúng club hoặc reviewer được phép; whitelist content/time/meeting-days. Không đổi clubId/creator/sourceReport bằng body. Report-linked hoặc Completed/Cancelled không sửa lịch tự do; trả conflict và hướng dùng workflow nguồn.
- POST /participants: chính chủ đăng ký (ID từ JWT); manager muốn thêm người khác phải quản lý club và resolve thành viên thực. Tên từ dữ liệu tin cậy; không cho body.fullName giả mạo danh tính. Kiểm membership active/approved và duplicate idempotently.
- POST /check-in: chỉ chính chủ, đúng membership và hoạt động đang mở; server ghi timestamp/date, dùng quy ước ngày Việt Nam của attendance. Body không được backdate hoặc chỉ định actor khác. Duplicate trong cùng ngày trả kết quả đã có, không tăng đếm lần nữa.
- GET /my-attendance: chỉ principal hiện tại, phân trang; không mở lịch sử của thành viên khác qua query.
- PATCH /complete: authorize club, chỉ chuyển từ trạng thái đang được phép, kiểm thời gian hoàn tất và source-report invariant; repeat không phát thêm side effect. Không triển khai thưởng XP.
- PUT /members/{memberId}: chỉ quản lý membership đúng club; whitelist thông tin hồ sơ, không sửa UserId/ClubId/status/role tùy ý. Đổi treasurer/manager phải theo endpoint chuyên biệt hiện có; role trong payload trái phép →400/403, không bỏ qua im lặng.
- Bulk attendance giữ memberId semantics và các status hiện có; nếu hỗ trợ userId compatibility thì resolve tất cả trong roster của club trước mutation, reject trùng/không tồn tại/out-of-scope cả batch.
- [assumed] Các unique key/index đủ cho participant/check-in idempotency chưa được kiểm đầy đủ trong phiên. Executor phải đọc mapping/migration liên quan trước khi chọn DB write pattern; nếu cần schema mới, dừng riêng mục đó để chốt scope migration. Không khẳng định application-level check là đủ.
- D3 giữ riêng direct-add member và delete activity; không giả lập join-form acceptance hoặc xóa lịch sử đã dùng tính thống kê.

### 6.9 N6 + D2 — Finance

Files: src/Services/FinanceService/Endpoints/ProposalEndpoints.cs, SettlementEndpoints.cs, TransactionEndpoints.cs và Contracts/FinanceContracts.cs. Symbols: ProposalEndpoints, SettlementEndpoints, TransactionEndpoints.

N6 có thể triển khai theo invariant hiện tại:

- POST /settlements/{id}/reject: StudentAffairsAdministration, chỉ Submitted, cấm proposal creator tự quyết toán; note validation.
- Ghi Rejected + reviewer/time/note, giữ proposal còn Approved để cho nộp settlement mới; không thêm SettlementApproved transaction và không đặt parent Settled.
- Cạnh tranh approve/reject hoặc hai reject: conditional write/transaction để đúng một transition thắng; loser 409 hoặc idempotent cùng kết quả theo contract chốt trước test.
- Giữ unique active-settlement guard của CreateSettlement và kiểm amount <= approved budget, HTTPS receipt.

Các route D2 chưa được phép chọn nghĩa thay người dùng:

- Submit proposal/settlement: đề xuất giữ create=Submitted như hiện tại, endpoint submit chỉ acknowledge đúng record đã Submitted một cách idempotent sau auth; không thêm event/ledger. Nếu FE cần Draft có thể sửa/xóa trước submit, phải thiết kế Draft thật như một thay đổi workflow riêng.
- Manager-review/final-review/settlement-review: payload “review” không nói quyết định. Không alias sang approve. Phương án ưu tiên là explicit note-only operation giữ state; nếu muốn UnderReview cần định nghĩa state transition và ảnh hưởng final-review rules trước.
- Transactions POST: hiện ledger do workflow sinh. Không mở tùy ý Amount/Type/ReferenceId vì dễ giả mạo phê duyệt; cần whitelist loại manual adjustment, actor, audit và idempotency key trước khi hiện thực.
- Proposal gắn SourceReportId vẫn phải đi qua cổng combined-report/internal-token và state nguồn. Không nhét secret nội bộ vào frontend.

### 6.10 D1, D3, D4 — Quyết định cần chốt, không làm giả API thành công

- D1: DELETE user = vô hiệu hóa hay xóa vĩnh viễn? Khuyến nghị vô hiệu hóa, giữ audit/foreign references; role API phải giữ chính xác một actor role, self/final-admin guard, security version/cache/refresh revoke. Không mở multi-role vì mẫu roles[].
- D3: xóa report/activity có giữ audit, điểm danh, budget và exports không? Khuyến nghị không xóa bản đã publish/submit/finance-linked; chọn archive/cancel nếu giữ lịch sử. Direct-add member cần quy trình xác nhận riêng, không tự tích “đồng ý nội quy”.
- D4: role CTSV/SYSTEM_ADMIN có cần đăng nhập thật trong bản này? Có thì cần scope thay đổi ActorAccountPolicy được phê duyệt và test riêng. Tương tự xuất danh sách sinh viên theo kỳ không phải alias của export một report; cần định nghĩa dataset, scope quyền, kỳ, format và nguồn trường.
- Chưa chốt: giữ route/feature chưa hỗ trợ và ghi rõ; không trả 200 rỗng để làm dashboard hết lỗi. Đợt A/N không được gắn nhãn hoàn tất toàn bộ API_1.

### 6.11 FE integration handoff — không tự mở rộng thành sửa UI trong đợt backend

Các vị trí xác nhận: FE:src/services/api.js; FE:src/context/AuthContext.jsx; FE:src/context/WorkspaceContext.jsx; FE:src/pages/Dashboard.jsx. Khi người dùng yêu cầu nối FE:

- Bật chế độ API thật bằng VITE_API_BASE_URL, tắt mock fallback; hiển thị network/auth lỗi thật.
- Ghép actor identity/profile, không overwrite fullName bằng response me thiếu field; không mặc định ADMIN khi response không hợp lệ.
- Thay local-only mutations bằng endpoint đúng quyền khi endpoint tương ứng đã được nghiệm thu. Hành động chưa có API phải đánh dấu chưa hỗ trợ, không báo đã lưu server.
- Sửa phân trang users và dùng total; thay hệ số 0.68/anomalies mock bằng số liệu thực đã có contract, hoặc hiển thị chưa có dữ liệu.
- Dùng contract mapper theo bảng 6.2; không tự đổi role backend để hợp với preview switch.
- Không nối quest/reward/XP/semester vào các endpoint report/kpi không tương đương.

## 7. Implementation Sequence

Tất cả bước dưới đây là tương lai; chưa có bước implementation nào đã chạy. Mỗi bước bàn giao kèm test pass của phạm vi đó, không commit một nhánh chứa test cố tình đỏ.

1. **Re-anchor nhẹ**: kiểm HEAD/dirty digest/manifest §11 và FE HEAD. Giữ user changes; đọc lại duy nhất evidence đã drift. Cập nhật impact trước từng symbol edit theo AGENTS. Chốt hợp đồng response giữ tương thích và đánh dấu D1–D4; không đợi những D này để bắt đầu API đọc.
2. **Test harness + N1/N2**: tạo contract tests trong Backend.StabilizationTests dựa vào TestServer hiện có; thêm user/profile, application, settlement, notification reads và status filter. Test 401/403/cross-club trước success. Giữ AdminService auth contract regression.
3. **A1/A2 + gateway**: thêm alias file/deadline, cùng handler và metadata; test gateway mapping + upload limiter + old/new routes parity. Không sửa deployment workflow hay Compose project.
4. **N3/N4**: period GET/PUT/soft-disable; attachment metadata deletion với resource-scoped auth, audit và failure tests. Contract ghi rõ soft-disable và retention.
5. **Contract compatibility pass**: sửa optional pagination và alias field chỉ nơi tương đương. Test JSON cũ/mới + conflicting inputs. Report structure/rating và export dataset không tương đương giữ ở D4, không chắp dữ liệu giả.
6. **N5**: member profile update; activity update/register/self-check-in/my-attendance/complete. Xác minh constraint persistence trước write, test timezone/idempotency/cross-club/batch atomicity. Nếu cần migration chưa được chốt, ghi riêng blocker thay vì sửa schema tiện tay.
7. **N6**: settlement reject và concurrency với approval; test ledger/parent status không đổi sai. Chạy lại finance/report-linked authorization regressions.
8. **Resolve D từng nhóm**: chỉ hiện thực sau quyết định tương ứng, mỗi nhóm là thay đổi độc lập với state table + authorization + tests. Không dùng việc hoàn tất 7 bước trước làm bằng chứng D đã xong.
9. **Integration handoff**: trả contract catalog + kết quả test cho FE. Chỉ sửa FE khi scope đó được yêu cầu; smoke qua gateway bằng token thật môi trường local/testing, mock tắt. Chưa nối FE thì ghi backend-ready, không nói UI end-to-end đã chạy.
10. **Final verification**: full CI-equivalent restore/build/test/format/Compose; SQL Server checks có DB test riêng. Nếu sau đó được yêu cầu commit, chạy detect_changes scope all, xử lý partial/truncated và review diff; commit chỉ các mục đã thống nhất. Không deploy production từ kế hoạch này.

## 8. Test Strategy

### 8.1 Test files và scenarios

[verified] Backend.StabilizationTests dùng net8.0, xUnit, TestServer, EF InMemory/SQLite và reference các service liên quan. AdminService.IntegrationTests dùng WebApplicationFactory, SQLite/SQL Server. Có thể mở rộng harness sẵn; InMemory không chứng minh SQL transaction/unique index.

| File / loại | Input → action → expected |
| --- | --- |
| tests/Backend.StabilizationTests/CrossServiceWorkflowAuthorizationTests.cs — thêm cases vào suite hiện có | Data-driven catalog: route, valid body/query, expected policy; 87 existing routes giữ behavior, các route A/N hết 404/405 với input hợp lệ; D đánh dấu pending rõ ràng, không fake pass |
| tests/Backend.StabilizationTests/JwtAuthorizationFreshnessTests.cs — mở rộng | User bị disable/role thay đổi → token cũ bị từ chối; profile me không nhận userId giả; không lộ token/internal fields |
| tests/Backend.StabilizationTests/RefreshTokenSecurityTests.cs — regression | Mutation account thuộc scope → refresh token cũ không tiếp tục phát quyền cũ; không đổi family rotation behavior |
| tests/AdminService.IntegrationTests/AdminApiTests.cs — regression | /v1/me giữ subjectId/userId; admin↔CTSV endpoint chéo vẫn403; forged body/query/header không nâng quyền |
| tests/Backend.StabilizationTests/ReportAttachmentSecurityTests.cs — mở rộng | Alias/canonical download cùng bytes + headers; khác club/report/attachment → denied không mutation; path traversal và physical path masking giữ nguyên |
| tests/Backend.StabilizationTests/FinanceAndReportDataIntegrityTests.cs — mở rộng | Hai settlement active → chỉ một success; amount > approved →400; reject không sinh approval transaction; approve/reject đồng thời → một transition và số ledger đúng |
| tests/Backend.StabilizationTests/CrossServiceWorkflowAuthorizationTests.cs — regression | Proposal report-linked thiếu/mismatch internal token → denied; client không giả chuyển state report qua finance; activity nguồn report khác club/unapproved → denied |
| CrossServiceWorkflowAuthorizationTests.cs — attendance scenarios mới | UTC gần nửa đêm tương ứng ngày VN → đúng date; check-in lặp cùng ngày → một row; giả userId/memberId khác club →denied; batch một row sai →không ghi batch |
| CrossServiceWorkflowAuthorizationTests.cs — reads/deadlines mới | Chính chủ vs actor khác; /deadlines/me literal không thành period; period/body conflict400; soft-disable không xóa lịch sử; notification recipient-role leak bị chặn |
| CrossServiceWorkflowAuthorizationTests.cs — shape/query mới | Không page/pageSize → default hợp lệ; pageSize>100 không làm FE tưởng đủ dữ liệu; old JSON và alias JSON cùng nghĩa; conflict alias400; thiếu required field400 thay vì500 |
| Export contract scenarios, chỉ khi D4 chốt | Report export có reportId đúng scope → job hợp lệ; students/season chưa hỗ trợ → rõ lỗi, không tạo job sai; token khác chủ không tải file; file hết hạn không thành công giả |

[verified] Test nguồn hiện có được đọc: JWT stale/locked/role checks; Finance active-settlement và concurrency harness; file response masking; Admin me và 403 chéo. Chúng chưa được chạy trong phiên planning này.

### 8.2 Verification commands cho phiên implementation

[verified] Các lệnh được đối chiếu .github/workflows/backend-validate.yml và project files, không phải thông báo đã pass:

~~~powershell
dotnet restore ClubReportHub.sln
dotnet build ClubReportHub.sln --configuration Release --no-restore -warnaserror
dotnet test ClubReportHub.sln --configuration Release --no-build --logger "trx" --collect:"XPlat Code Coverage" --results-directory ./TestResults
dotnet format src/Services/AuthService/AuthService.csproj --no-restore --verify-no-changes
dotnet format src/Services/ClubService/ClubService.csproj --no-restore --verify-no-changes
dotnet format src/Services/AdminService/AdminService.csproj --no-restore --verify-no-changes
dotnet format tests/Backend.StabilizationTests/Backend.StabilizationTests.csproj --no-restore --verify-no-changes
dotnet format tests/AdminService.IntegrationTests/AdminService.IntegrationTests.csproj --no-restore --verify-no-changes
docker compose config --quiet
~~~

[inferred] Chạy full formatting phạm vi đã sửa và các gate EF/dependency scan đúng reusable CI; không cài/đổi dependency trong đợt alias nếu không cần. Nếu không đổi models thì EF has-pending-model-changes vẫn phải sạch. SQL Server integration cần DB test isolated và connection string từ môi trường, không dùng database production. Dotnet trên máy hiện resolve 10.0.401 vì global.json rollForward=latestMajor, còn CI dùng8.0.x: kết quả local không thay thế CI .NET8.

[verified] FE package.json có npm test và npm run build; chỉ chạy các lệnh này khi làm đợt FE hoặc smoke tích hợp đã được yêu cầu. Không có script lint để viện dẫn.

### 8.3 Lệnh thực sự đã chạy và kết quả của phiên plan

| Kiểm tra | Kết quả chính xác |
| --- | --- |
| git status/rev-parse backend | Branch codex/final-review-fixes, HEAD37272c3c2c9419f5ebaf63ef1113e5e323d729aa, sạch trước plan |
| git status/rev-parse FE | Branch chores/refactor-css, HEADf22bda074fb0d541961077e5ee052879a1b8e70c, sạch |
| node .gitnexus/run.cjs status --json | Ban đầu stale12 commit; runner schema4 current |
| node .gitnexus/run.cjs analyze --index-only --pdg | Exit0, index được cập nhật, có cảnh báo giới hạn process extraction |
| context/impact/PDG/explain | Kết quả và giới hạn ở §4–5; UNKNOWN không được xem là ít rủi ro |
| Static route audit Node + source registration reads | 121 dòng tài liệu;87 declared;34 missing đúng method/path; không chạy HTTP |
| docker compose config --quiet | Exit0, không output; chỉ xác nhận cấu hình parse được |
| dotnet --version | 10.0.401 |
| evidence-provenance.mjs snapshot schema2 | Exit0;63 cited paths; digest trong header/§11 |
| docker image ls | Không kết nối Docker Desktop Linux engine; không khởi động daemon trong phiên plan |
| Build/test/HTTP smoke/deploy | **Chưa chạy** trong phiên planning; không có kết luận production hay end-to-end thành công |

## 9. Risk and Impact Analysis

- [inferred] **Auth/security — cao về thiết kế**: contract role trong tài liệu mâu thuẫn allowed-login actors. Thêm assign/remove có thể phá single-role/self/final-admin guard hoặc không revoke session. Giữ cả JWT freshness tests và policy tests.
- [verified] **Dev login đang mở mọi environment** và xác thực bằng email account có sẵn, không chứng minh chủ email. [inferred] Đây là rủi ro production đã có, không được quảng bá là secure login hay mở rộng theo mẫu mock. Đề nghị một thay đổi security được operator chốt riêng; không âm thầm thay baseline trong đợt thêm API.
- [inferred] **Ledger/approval — cao**: duplicate/retry/concurrent review có thể sinh hai bút toán hoặc state parent sai. Yêu cầu relational test, conditional state update và không cho client chọn loại approval transaction.
- [inferred] **File — cao**: alias có thể né upload limiter; attachment ID/StoragePath có thể dẫn tới nhầm resource hoặc xóa ngoài scope. Dùng cùng handler, authorization, safe path logic; chỉ physical cleanup sau commit/retention.
- [verified] **Nguồn báo cáo liên service**: finance proposal gắn report có validation internal workflow; activity từ report phải kiểm Approved và cùng club. [inferred] Không cho endpoint “review” mới bypass những check này.
- [inferred] **Compatibility**: consumer khác có thể dùng array/old property names. Thiết kế additive và opt-in, không rename/bỏ field cũ; số missing không bao gồm mismatch body.
- [verified] **Pagination và số liệu UI**: FE hiện đếm một page, giữ seed local và dùng hệ số mock. [inferred] API green không đồng nghĩa dashboard đúng; cần tách data contract và integration acceptance.
- [inferred] **Performance**: scoped detail/list nên projection/AsNoTracking, tránh N+1 và tải mọi participant/attendance để đếm; chưa có benchmark, không hứa tăng tốc.
- [assumed] **Schema/retention**: đợt A/N ưu tiên không migration. Unique check-in, audit retention, soft-delete report/activity chưa được xác nhận đủ; cần xem mapping liên quan khi làm mục đó. Không xóa production volume.
- [graph] **Blast-radius coverage**: cả5 primary UNKNOWN, metadata stale mâu thuẫn và flow truncation. Không có chứng nhận all-clear; mọi caller source-derived §4.2 phải có regression hoặc smoke thích hợp.
- [inferred] **Deployment**: plan không thay Compose/project/main-only/needs validate/secrets/volumes/rollback/current symlink. Không kết luận rollout từ source audit.

## 10. Files Expected to Change

Danh sách là phạm vi dự kiến cho phiên triển khai; phiên này chỉ thêm plan.

| File | Symbol đã xác nhận / phạm vi | Lý do |
| --- | --- | --- |
| src/Services/AuthService/Endpoints/UserEndpoints.cs | UserEndpoints | N1 reads/profile; D1 chỉ sau quyết định |
| src/Services/AuthService/Contracts/AuthContracts.cs | DTO user/profile, không đổi token issuer | Payload whitelist nếu cần |
| src/Services/ClubService/Endpoints/ApplicationEndpoints.cs | ApplicationEndpoints | Detail read, status filter |
| src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs | MemberManagementEndpoints | N5 profile write; D3 add-member sau quyết định |
| src/Services/ClubService/Contracts/ClubContracts.cs | Request/response compatibility có giới hạn | Field tương đương và validation |
| src/Services/ActivityService/Endpoints/ActivityEndpoints.cs | ActivityEndpoints | N5 lifecycle/participant/self attendance |
| src/Services/ActivityService/Endpoints/AttendanceManagementEndpoints.cs | AttendanceManagementEndpoints | Mapping bulk tương thích nếu được chọn, không bỏ status |
| src/Services/ActivityService/Contracts/ActivityContracts.cs | DTO aliases/nullable input | Request validation, không đổi khóa |
| src/Services/ReportService/Endpoints/ReportFileEndpoints.cs | ReportFileEndpoints | A1 + N4 |
| src/Services/ReportService/Endpoints/DeadlineEndpoints.cs | DeadlineEndpoints | A2 + N3 |
| src/Services/ReportService/Contracts/ReportContracts.cs | Deadline/file compatibility | Định nghĩa request/response rõ ràng |
| src/Services/FinanceService/Endpoints/SettlementEndpoints.cs | SettlementEndpoints | N2 + N6; D2 sau quyết định |
| src/Services/FinanceService/Endpoints/ProposalEndpoints.cs | ProposalEndpoints | Pagination; D2 sau quyết định |
| src/Services/FinanceService/Endpoints/TransactionEndpoints.cs | TransactionEndpoints | Chỉ nếu manual ledger D2 được chốt |
| src/Services/FinanceService/Contracts/FinanceContracts.cs | DTO theo endpoint | Không dùng amount mơ hồ |
| src/Services/NotificationService/Endpoints/NotificationEndpoints.cs | NotificationEndpoints | N2 scoped detail |
| src/Gateway/ApiGateway/yarp.json | Route configuration | Deadline alias và limiter cho file alias |
| tests/Backend.StabilizationTests/CrossServiceWorkflowAuthorizationTests.cs | Suite hiện có, thêm contract cases | Catalog và boundary tests |
| Existing test files ở §8.1 | Regression cases | Auth, file, finance, gateway behavior |

[inferred] Không cần sửa application business toàn repo, migrations, production .env, reusable validation hay deployment để làm alias/reads. Report delete, login-policy expansion, student export dataset và model additions nằm ngoài tập sẵn sàng triển khai cho tới khi D tương ứng chốt. FE paths §6.11 là handoff, chưa phải phạm vi tự động sửa.

## 11. Reusable Implementation Context

[verified] JSON dưới đây giữ nguyên evidence_provenance schema2 do helper snapshot trả về. Chỉ cited-path manifest được lưu; không kèm raw global dirty manifest. Digest HEAD/index/worktree có thể khác do byte/line-ending representation dù Git báo clean; executor phải so đúng từng layer.

~~~json
{
  "task_summary": "121 Admin FE API contracts:87 method/path declared,34 not declared;6 alias candidates; keep domain/security semantics",
  "acceptance_criteria": [
    "Inventory every documented route by method/path and actual FE usage",
    "Separate missing routes from shape/semantic mismatches",
    "Keep authentication/role authorization explicit",
    "Define additive compatible changes and tests",
    "Publish complete Deep plan with pinned provenance"
  ],
  "evidence_provenance": {
    "schema_version": 2,
    "head_commit": "37272c3c2c9419f5ebaf63ef1113e5e323d729aa",
    "generated_plan_path": "docs/plans/2026-09-22-gitnexus-plan-admin-api-contract-gap.md",
    "global_dirty_digest": {
      "algorithm": "sha256",
      "canonicalization": "gitnexus-evidence-provenance-v2 NUL-framed UTF-8 records",
      "value": "0a9c85780067d9afcd0764f307b60891e3cee927ee11eaeb5ec7826d10fd82cd"
    },
    "cited_path_manifest": [
      {
        "path": ".github/workflows/backend-validate.yml",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:0162a0da884a13e64883c545c53fdaf941ead54d75094b12898365626794851e",
        "index_digest": "sha256:0162a0da884a13e64883c545c53fdaf941ead54d75094b12898365626794851e",
        "worktree_digest": "sha256:691b796eca26e26d7c376e1df0d51bae9656fc9f160b5fe076912964b5d1a5b3",
        "untracked_digest": "absent"
      },
      {
        "path": "AGENTS.md",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:544c9e742271d91067cc19111a6d536b07f391dd43d5d74f3dca601f84c4d52b",
        "index_digest": "sha256:544c9e742271d91067cc19111a6d536b07f391dd43d5d74f3dca601f84c4d52b",
        "worktree_digest": "sha256:8c27bc41e10ce84ac715b1ffebd62c7fddd17b06a1048179f8174e3264fe780a",
        "untracked_digest": "absent"
      },
      {
        "path": "global.json",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:546b7a626451526a3d390b862254cb0d15f62042ad1b4e40eccbb44168ba8f7c",
        "index_digest": "sha256:546b7a626451526a3d390b862254cb0d15f62042ad1b4e40eccbb44168ba8f7c",
        "worktree_digest": "sha256:4ec88897f496f873c9e272325ace3b39d692b87882f69b966af5d06560da1dc3",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Gateway/ApiGateway/Program.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:aa87fef2f2003f1d544f81fc869f56001575f4846e677d0f4987fca14f8f0705",
        "index_digest": "sha256:aa87fef2f2003f1d544f81fc869f56001575f4846e677d0f4987fca14f8f0705",
        "worktree_digest": "sha256:cd8b2b3b2f36d062403aeb00a81b0fedc57c83fa10501bc915b2ad6d1b70bf8f",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Gateway/ApiGateway/yarp.Development.json",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:093f7f65f4bffdfa8bfa9a10ef31c76151e51f307c4965493cead0614baedf0a",
        "index_digest": "sha256:093f7f65f4bffdfa8bfa9a10ef31c76151e51f307c4965493cead0614baedf0a",
        "worktree_digest": "sha256:bbace286425ec91c3a23c25e46489e9992e0ade3ba5af51079ae6308f49a4c92",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Gateway/ApiGateway/yarp.json",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:5985166a42a8c838c2d7c1edfff6806635ff5a09912e99c852a8086ecf37430a",
        "index_digest": "sha256:5985166a42a8c838c2d7c1edfff6806635ff5a09912e99c852a8086ecf37430a",
        "worktree_digest": "sha256:573d36107c30f875f9e31846e01f1722875118932dae6bc42e3bcd630d35e552",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ActivityService/Contracts/ActivityContracts.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:65135a4549cdf81b6ae4a11666b17b3d3d5d52fb5057a542e3eadc3b4ce5fde6",
        "index_digest": "sha256:65135a4549cdf81b6ae4a11666b17b3d3d5d52fb5057a542e3eadc3b4ce5fde6",
        "worktree_digest": "sha256:e3f940a559c298bd40286854925b00b9956eb611136735df037956408c0fde16",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ActivityService/Endpoints/ActivityEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:5a913b113c721e06305d1a4857c28065b25d1b84f6ccb748e4502aadcea90f40",
        "index_digest": "sha256:5a913b113c721e06305d1a4857c28065b25d1b84f6ccb748e4502aadcea90f40",
        "worktree_digest": "sha256:f9219f5d949162068268b753c450bb88f10ba36300840a5dfc0561e03ebb0548",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ActivityService/Endpoints/AttendanceManagementEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:942266f1b2af530fb6804091677c05fbb9a2b866383136626154a357f6f4539c",
        "index_digest": "sha256:942266f1b2af530fb6804091677c05fbb9a2b866383136626154a357f6f4539c",
        "worktree_digest": "sha256:840e9ebf4efd359d5b9ca2a5808044d8f108390a028c9984b49549d0cd8b4c5e",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ActivityService/Models/ActivityAttendance.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:5e4bb2a02ecb03638e65eba69ca0a2cecccd17a50a9a817ed322062892d11aab",
        "index_digest": "sha256:5e4bb2a02ecb03638e65eba69ca0a2cecccd17a50a9a817ed322062892d11aab",
        "worktree_digest": "sha256:f27cf7d445e6815df4182a40fbf42471af3e8d72b2825dc6bd9a7674a34fb95c",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ActivityService/Models/ActivityParticipant.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:0d591e2e4cdd4a910a666d0da8dc4ec6099df5e1e7ae4d8af129c60aa134b192",
        "index_digest": "sha256:0d591e2e4cdd4a910a666d0da8dc4ec6099df5e1e7ae4d8af129c60aa134b192",
        "worktree_digest": "sha256:ae1f7b6503bedc056db47f33aa26cf9ae19e8e6c8a77c3af49b25a74176a9b31",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ActivityService/Models/ClubActivity.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:431cce02cf2a667a2fad4c3389626cb0e62efc0470ca9582b90a13050165d53f",
        "index_digest": "sha256:431cce02cf2a667a2fad4c3389626cb0e62efc0470ca9582b90a13050165d53f",
        "worktree_digest": "sha256:b5a2db4d78388e39ae0d91363b53588d6a7345d1e565838e63c8f168cd7fe8f8",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ActivityService/Program.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:8bc38085d9f0b54296183d909b4204cf4e6a19187fba865ad8ba6a169349c04c",
        "index_digest": "sha256:8bc38085d9f0b54296183d909b4204cf4e6a19187fba865ad8ba6a169349c04c",
        "worktree_digest": "sha256:c22d2ad4e47b42f161d667ec84298bf542b50a1f615380d6d8fb838ecabdf97a",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/AdminService/Contracts/AdminContracts.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:264e74425fbf377d4b40c82032a130ee0c2d43360205a04056d12ccc72d2078f",
        "index_digest": "sha256:264e74425fbf377d4b40c82032a130ee0c2d43360205a04056d12ccc72d2078f",
        "worktree_digest": "sha256:264e74425fbf377d4b40c82032a130ee0c2d43360205a04056d12ccc72d2078f",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/AdminService/Endpoints/AdminEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:cba86f8f12d59ccd61c13f54b1af6b87ecebdaef3af77db5351505c0bdd023ac",
        "index_digest": "sha256:cba86f8f12d59ccd61c13f54b1af6b87ecebdaef3af77db5351505c0bdd023ac",
        "worktree_digest": "sha256:cba86f8f12d59ccd61c13f54b1af6b87ecebdaef3af77db5351505c0bdd023ac",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/AuthService/Contracts/AuthContracts.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:cdb4bc15d0284aa6366d02c1cdaa3da8d3032c573fdb241a64a70618e61f248a",
        "index_digest": "sha256:cdb4bc15d0284aa6366d02c1cdaa3da8d3032c573fdb241a64a70618e61f248a",
        "worktree_digest": "sha256:80bbdefc4e4a81bcb020acebac71c83ad21bbd2a49fc1505a225c024c8418642",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/AuthService/Endpoints/AuthEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:a1f081cfa379f1d986f8a1cac8284e249cc9523a90f1c8161e7422f8e682866c",
        "index_digest": "sha256:a1f081cfa379f1d986f8a1cac8284e249cc9523a90f1c8161e7422f8e682866c",
        "worktree_digest": "sha256:4b1805f9563fcb67593c3571d9f8fc981339f59fca84579bbcf09169da8217b2",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/AuthService/Endpoints/RoleEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:77878fb53d6ea1ce1c8b101f31259756edf4e5889cbfda6ddc3376e6631a92e8",
        "index_digest": "sha256:77878fb53d6ea1ce1c8b101f31259756edf4e5889cbfda6ddc3376e6631a92e8",
        "worktree_digest": "sha256:a8c15e94da929579898b9010d4b9abbc320fd46efb1cb7a06a3bfa6b839c206c",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/AuthService/Endpoints/UserEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:a4cecd7b322e032febaa9d48e5620539c1842f8f6a4e5b5421008f24a468b319",
        "index_digest": "sha256:a4cecd7b322e032febaa9d48e5620539c1842f8f6a4e5b5421008f24a468b319",
        "worktree_digest": "sha256:fa3f3634193b2ad848acea40c1a42bb77a08e32a1f9d4acdf85c0b56e36d10f5",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/AuthService/Program.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:e92d0c94157de0e2f30f253cd909d72c03d90a2786ce08201ee0073e6a82f122",
        "index_digest": "sha256:e92d0c94157de0e2f30f253cd909d72c03d90a2786ce08201ee0073e6a82f122",
        "worktree_digest": "sha256:444c067cb9ce349a9e3a32bd052ce3bad6f6d93090c028a37021ee660b09ade6",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/AuthService/Services/ActorAccountPolicy.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:7fa786563b6cdc92e6ed2152cffce7f25fb7eabc4bc79c34bde5c805320f2259",
        "index_digest": "sha256:7fa786563b6cdc92e6ed2152cffce7f25fb7eabc4bc79c34bde5c805320f2259",
        "worktree_digest": "sha256:e6e71030537d5499a15a93a3f8234c32ccf570c0604d415957f3e6794b66f566",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ClubService/Contracts/ClubContracts.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:60749b38bef7780739a919d42e6d56d575633500af72ca34f9da1d5d16b225bf",
        "index_digest": "sha256:60749b38bef7780739a919d42e6d56d575633500af72ca34f9da1d5d16b225bf",
        "worktree_digest": "sha256:d41e09323902236606e45eb7a1b791a9e1a27f9f69d8a8442878d65bea1163f4",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ClubService/Endpoints/ApplicationEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:bb825b2d7009fa6f99225b2a743f962f47cf65ec9427d5fdf5e90f58d4faaa27",
        "index_digest": "sha256:bb825b2d7009fa6f99225b2a743f962f47cf65ec9427d5fdf5e90f58d4faaa27",
        "worktree_digest": "sha256:a59e8bcfcf8b79543cda4385d283e22a14e41471f7df6aeb1715f546cc3ba986",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ClubService/Endpoints/ClubEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:f5774a78ff1721488efbae096d678f7e15477cb6fe5e0d0affd432243e989c27",
        "index_digest": "sha256:f5774a78ff1721488efbae096d678f7e15477cb6fe5e0d0affd432243e989c27",
        "worktree_digest": "sha256:6520589b1145e698254c31d81d44c710593b19ea3e61ad4f40c3486215f5d9df",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ClubService/Endpoints/DisbandEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:52aef065bc347f681c750ec8f321beac9b497e21aca0c96fa6ca0eccb5406ca5",
        "index_digest": "sha256:52aef065bc347f681c750ec8f321beac9b497e21aca0c96fa6ca0eccb5406ca5",
        "worktree_digest": "sha256:ec0652f594b802a30536125228c2df64d656ff4e823f9b70e52138c9c40928bb",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:79b99f4d043a8904d211c54f89faa3b8c22eb01230dcb7918425dc48cd94546e",
        "index_digest": "sha256:79b99f4d043a8904d211c54f89faa3b8c22eb01230dcb7918425dc48cd94546e",
        "worktree_digest": "sha256:54a612c266c13967849d51a0106aefd2e0ef3b0815f65510dd2618127adba3bd",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ClubService/Endpoints/MembershipEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:e35560961c8af64663d028396d4e2bfc9baf6005e18ea42061b432beabb70e53",
        "index_digest": "sha256:e35560961c8af64663d028396d4e2bfc9baf6005e18ea42061b432beabb70e53",
        "worktree_digest": "sha256:bb2534c0afba9ddd223bbe0096d4c07ef02ed8381946e89cf09c6667ad291440",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ClubService/Endpoints/TransferEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:0fef11e3770a6f649274c425ac51ed0a071cfd1c8616edc2fbc5cd7e0cc402e0",
        "index_digest": "sha256:0fef11e3770a6f649274c425ac51ed0a071cfd1c8616edc2fbc5cd7e0cc402e0",
        "worktree_digest": "sha256:d0faf657e5afaef419caae79e7d22aa81ca328236bb9dee6670a9886763584d7",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ExportService/Contracts/ExportContracts.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:67b08a010adbc571f29482fda838b2a853b3bf43767dca3c78d173af923d38ef",
        "index_digest": "sha256:67b08a010adbc571f29482fda838b2a853b3bf43767dca3c78d173af923d38ef",
        "worktree_digest": "sha256:611487b5a1d0aaad591115fe741f034be97064e528bab850d79383219cfac89b",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ExportService/Endpoints/ExportEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:8aa7451e965d7b7991987233df2189bf58051e0f6459bdc5200a444de4e76276",
        "index_digest": "sha256:8aa7451e965d7b7991987233df2189bf58051e0f6459bdc5200a444de4e76276",
        "worktree_digest": "sha256:d78a4528fe4090f98b907dbcc9d927b28edb8c0000988ca6bf74c853a8f9dfed",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/FinanceService/Contracts/FinanceContracts.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:9dde810199f44ebde5bd1eed2f25369a95b493bc44563ca5a70fd235d9a66890",
        "index_digest": "sha256:9dde810199f44ebde5bd1eed2f25369a95b493bc44563ca5a70fd235d9a66890",
        "worktree_digest": "sha256:d34e2cd4a4709377e59f0b24cb7d05e958bcc624bda733d578ada7c2f9f7892f",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/FinanceService/Endpoints/FinanceEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:e6a0bebdd1b5ed8aa3b88b10c74704189467d137f13bbad823495191fb751894",
        "index_digest": "sha256:e6a0bebdd1b5ed8aa3b88b10c74704189467d137f13bbad823495191fb751894",
        "worktree_digest": "sha256:92089ca73de98867bbd237e9b3d779d62e9154a0f939e19be24c9dec3e5795c3",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/FinanceService/Endpoints/ProposalEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:fc91a00e654d3efc2658f9f7b958326192c86cf47d29cbc4f519a5bd6fa5b56a",
        "index_digest": "sha256:fc91a00e654d3efc2658f9f7b958326192c86cf47d29cbc4f519a5bd6fa5b56a",
        "worktree_digest": "sha256:402c3b0a2bb502b2d597d8e0509c3826efdbcaa836efec797376e163d3c3be0d",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/FinanceService/Endpoints/SettlementEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:95fabe11f5d87f5efb76fb03574dd2cabe65ba5835630c0dd241628d295d0da8",
        "index_digest": "sha256:95fabe11f5d87f5efb76fb03574dd2cabe65ba5835630c0dd241628d295d0da8",
        "worktree_digest": "sha256:97b140d6b371145c29625fce9a51f8f0895698fd8b08d5ef7e1476de91dbb762",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/FinanceService/Endpoints/TransactionEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:a2cc54c818d0059d152751b2f54875a1139f52bacc1bac244d166b5bc81e8420",
        "index_digest": "sha256:a2cc54c818d0059d152751b2f54875a1139f52bacc1bac244d166b5bc81e8420",
        "worktree_digest": "sha256:1ddd365cefb3fce9f72d7dee7d8c503fbceae54dbb824f7d0bda1ecf38dc7305",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/FinanceService/Models/BudgetProposal.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:4bbd09ee06267d2a2e67c1692fad5c7a4e0e8e24407859522b1b3a8df7a6f619",
        "index_digest": "sha256:4bbd09ee06267d2a2e67c1692fad5c7a4e0e8e24407859522b1b3a8df7a6f619",
        "worktree_digest": "sha256:ed35fc659f0ff6cfdd1a08e99f74a13be7763d117f9f6a4cce80a5a9cbf2bd41",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/FinanceService/Models/FinanceStatuses.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:d173153a68c60dcb72daac7d0f27aa4fb1a7fbecfd4ded949bf1ec8695b27a21",
        "index_digest": "sha256:d173153a68c60dcb72daac7d0f27aa4fb1a7fbecfd4ded949bf1ec8695b27a21",
        "worktree_digest": "sha256:f90f762d2ce55fd95da20520e76cf545dcdad78e1c7ea15d6645fdd399ecae0c",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/FinanceService/Models/FinanceTransaction.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:92f5aa732e2867c00f869da4001729b0f3e4a2719589389d1c3651375d7205f2",
        "index_digest": "sha256:92f5aa732e2867c00f869da4001729b0f3e4a2719589389d1c3651375d7205f2",
        "worktree_digest": "sha256:001040d032b0bba73ce269cbbe7a6f7fdb0e8b65f6cd74323d4c26a802da0f30",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/FinanceService/Models/Settlement.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:02fa7eb0c0035ca34a9bfe4132a769f9e1ffb17389073a1fdfe015965f834c9a",
        "index_digest": "sha256:02fa7eb0c0035ca34a9bfe4132a769f9e1ffb17389073a1fdfe015965f834c9a",
        "worktree_digest": "sha256:a77a37dfb763b990fddae7fdbaec39bd3a6092c5939f367caceaa833b0df40ac",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/NotificationService/Contracts/NotificationContracts.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:bd941805764a3201420f9d02f8064a74538d23a42dfb20034ccab2c6a504ae9d",
        "index_digest": "sha256:bd941805764a3201420f9d02f8064a74538d23a42dfb20034ccab2c6a504ae9d",
        "worktree_digest": "sha256:ab574862da51797da491d3307afe5ba79ba8c0a1ed21fb747bed9a515d601cbf",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/NotificationService/Endpoints/NotificationEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:3216e21cfe3e2743c691b433603fcb1793660890b2b0c3f48f8e8771d264c762",
        "index_digest": "sha256:3216e21cfe3e2743c691b433603fcb1793660890b2b0c3f48f8e8771d264c762",
        "worktree_digest": "sha256:e5ee6d8ef24dfe34e1bca8f0dd29e9c47fa99092b5bb762e358d8de4d80cbb1f",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ReportService/Contracts/ReportContracts.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:c4dcb91a9e9e45a9b2a06511a714b331dac178a73a458e366e0a13c4326418a5",
        "index_digest": "sha256:c4dcb91a9e9e45a9b2a06511a714b331dac178a73a458e366e0a13c4326418a5",
        "worktree_digest": "sha256:cb1b1149c8e55d017ac014d474c0e46838b83f7df019fb084c6901673826d793",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ReportService/Endpoints/DeadlineEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:af4da0f9d83575a2fcca1ac895f4bd81e3841ad6e6961bf5d91e1f99de1b83cf",
        "index_digest": "sha256:af4da0f9d83575a2fcca1ac895f4bd81e3841ad6e6961bf5d91e1f99de1b83cf",
        "worktree_digest": "sha256:e598c78740cfbfa8b678c9d0ec7f5e59cbfa6b96dd9d78bac64e21694c65553f",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ReportService/Endpoints/KpiEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:874725539df33538c652dcfa52740fdc164efd8b3b2f3eca6dd19f780f3c5a68",
        "index_digest": "sha256:874725539df33538c652dcfa52740fdc164efd8b3b2f3eca6dd19f780f3c5a68",
        "worktree_digest": "sha256:1b9d50517442419f494462ed56e9f384467e136b321c9037e7c5c0c60e6d2764",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ReportService/Endpoints/ReportCrudEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:cbf944ec2bbf0efcadf840d7edc8da3892b128c22ad04edd7540f246ea90a19b",
        "index_digest": "sha256:cbf944ec2bbf0efcadf840d7edc8da3892b128c22ad04edd7540f246ea90a19b",
        "worktree_digest": "sha256:cc5e9a048b31e964cc44526637c0ae0e71f58ce37c7db46a036f6f9bc23ec88d",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ReportService/Endpoints/ReportEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:6946b9738a8bb0ab8d9c592b20e0aab37145a8197be826e9d6547aa379c351da",
        "index_digest": "sha256:6946b9738a8bb0ab8d9c592b20e0aab37145a8197be826e9d6547aa379c351da",
        "worktree_digest": "sha256:c82ffe4bfef117ae733c47e00a4a509fd42efb2f298112a20b449ac7af6635ad",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ReportService/Endpoints/ReportFileEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:5c60e9488a27ff7dec66747de1224d12ecdb07bfd06610293df7d0091e7817ed",
        "index_digest": "sha256:5c60e9488a27ff7dec66747de1224d12ecdb07bfd06610293df7d0091e7817ed",
        "worktree_digest": "sha256:2c3f746de8ae297749f110dc501cc004fb3d8a2613e8b354bbcfc17bdadee469",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ReportService/Endpoints/ReportQueryEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:6f068f97b710fb39802217c9415ce6aef24d10b400976f2dd84be593c31c4683",
        "index_digest": "sha256:6f068f97b710fb39802217c9415ce6aef24d10b400976f2dd84be593c31c4683",
        "worktree_digest": "sha256:01ae9ecc9b28c3d2b243da4d345b097ba05b1f93a643d764f19a1cf4215a98d2",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ReportService/Endpoints/ReportWorkflowEndpoints.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:2452587f7863a57ad8c608afd859aa035ea82496d9e299ac061f707afbc0ff45",
        "index_digest": "sha256:2452587f7863a57ad8c608afd859aa035ea82496d9e299ac061f707afbc0ff45",
        "worktree_digest": "sha256:ebe1f53944e055b37564acdbda38f825c16ece964d58409d72b3ca07d47ba129",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ReportService/Models/Report.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:bb96a5eaeda6a35ca4f6a5559b1bb94a51c40b48664b5d5c56c1d47cea2b5a39",
        "index_digest": "sha256:bb96a5eaeda6a35ca4f6a5559b1bb94a51c40b48664b5d5c56c1d47cea2b5a39",
        "worktree_digest": "sha256:3663fb59d9b170adea6691a3dc499cebb4dbe10ed6aa03d0dd46b98bc5ed1f97",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Services/ReportService/Models/ReportingDeadline.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:df4971c6420809677af490507bb831e51197ad2e38624faccae0b5108e91123a",
        "index_digest": "sha256:df4971c6420809677af490507bb831e51197ad2e38624faccae0b5108e91123a",
        "worktree_digest": "sha256:a4452db78fd950d7b2a69052e1422a80a1a9a781d461c5b6e5e6ba9d4de85f80",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Shared/ClubReportHub.Shared/Auth/AuthConstants.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:869d350f7e043d9b838e0513e707c4eb0164ac40221eb1219724e045d29e5c4a",
        "index_digest": "sha256:869d350f7e043d9b838e0513e707c4eb0164ac40221eb1219724e045d29e5c4a",
        "worktree_digest": "sha256:b9e7c544d8f1e09c6191775bec7d304732b7b907b268bedde74726856d071a35",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Shared/ClubReportHub.Shared/Auth/JwtServiceCollectionExtensions.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:6d92e396eb2768725ce51cdc4f0bbb0a66d5ac16462f433e362e81494757fa08",
        "index_digest": "sha256:6d92e396eb2768725ce51cdc4f0bbb0a66d5ac16462f433e362e81494757fa08",
        "worktree_digest": "sha256:1200efdb847073eff70e389dc77d5aad5d2c896170c71123206c2321df245ff7",
        "untracked_digest": "absent"
      },
      {
        "path": "src/Shared/ClubReportHub.Shared/Health/StandardHealthCheckExtensions.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:161772aada169c6049dd196df1d890bb196f807e5b83d5006f8cc9057407d0e1",
        "index_digest": "sha256:161772aada169c6049dd196df1d890bb196f807e5b83d5006f8cc9057407d0e1",
        "worktree_digest": "sha256:d9b7641cdaa4043da8bd5c3b093ec9f961b129abf0cc2ccdaed06f918d0f93ee",
        "untracked_digest": "absent"
      },
      {
        "path": "tests/AdminService.IntegrationTests/AdminApiTests.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:5d18b824b22fb069ea736fb200f921531dd61bac8035069a3d943483a46e4d0c",
        "index_digest": "sha256:5d18b824b22fb069ea736fb200f921531dd61bac8035069a3d943483a46e4d0c",
        "worktree_digest": "sha256:5d18b824b22fb069ea736fb200f921531dd61bac8035069a3d943483a46e4d0c",
        "untracked_digest": "absent"
      },
      {
        "path": "tests/AdminService.IntegrationTests/AdminService.IntegrationTests.csproj",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:5cc284ef710509251b0d70fa6202e8a94f9dca4c82ffc379a8abb799968c2fc2",
        "index_digest": "sha256:5cc284ef710509251b0d70fa6202e8a94f9dca4c82ffc379a8abb799968c2fc2",
        "worktree_digest": "sha256:b2cc4fb6e4aa80191cd36cce2ec8322a7179ca9044a26ff4322d9d786ca2aeca",
        "untracked_digest": "absent"
      },
      {
        "path": "tests/Backend.StabilizationTests/Backend.StabilizationTests.csproj",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:0bbc97c0d52ff32cf5b3fa54787ca9289b5daeb4447faa85fb2120e0bdc0bdbb",
        "index_digest": "sha256:0bbc97c0d52ff32cf5b3fa54787ca9289b5daeb4447faa85fb2120e0bdc0bdbb",
        "worktree_digest": "sha256:987cc52e82b227756941cae47213c88c254454888510eb25b7b334f039e09bda",
        "untracked_digest": "absent"
      },
      {
        "path": "tests/Backend.StabilizationTests/CrossServiceWorkflowAuthorizationTests.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:6fe6ddf18006e302bb057e0106df845130bd7a86e4e2a9781715b6344aa7ea4b",
        "index_digest": "sha256:6fe6ddf18006e302bb057e0106df845130bd7a86e4e2a9781715b6344aa7ea4b",
        "worktree_digest": "sha256:a287630bc032bfdf2e122de77f0d01026c7e5fdc53006c919b82bd52d08bddcd",
        "untracked_digest": "absent"
      },
      {
        "path": "tests/Backend.StabilizationTests/FinanceAndReportDataIntegrityTests.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:23836b7220061733da523fa0a5906e6d9a711fb2d7b7d6dd0a24d2e346922a2d",
        "index_digest": "sha256:23836b7220061733da523fa0a5906e6d9a711fb2d7b7d6dd0a24d2e346922a2d",
        "worktree_digest": "sha256:32590b225101305d4f545c1697fc932bff2993bd33323b6ff12511c410a13e3e",
        "untracked_digest": "absent"
      },
      {
        "path": "tests/Backend.StabilizationTests/JwtAuthorizationFreshnessTests.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:091e2ff5fb2c0661add533e53b7f37be2eb637b78a596bc6292a54b4e8131c25",
        "index_digest": "sha256:091e2ff5fb2c0661add533e53b7f37be2eb637b78a596bc6292a54b4e8131c25",
        "worktree_digest": "sha256:14da34cc922ec5d8c227eaccd799c5ee9cf5780e4394d1e38e7597775f9ed0e0",
        "untracked_digest": "absent"
      },
      {
        "path": "tests/Backend.StabilizationTests/RefreshTokenSecurityTests.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:2b180c31aa1dbb711d75fa4d806a1f45c7b0abacf73350c16bcdd726f1aa1667",
        "index_digest": "sha256:2b180c31aa1dbb711d75fa4d806a1f45c7b0abacf73350c16bcdd726f1aa1667",
        "worktree_digest": "sha256:7adc49285fd22a1c5b37b97e2431217db59838d634f9694d98d821b99fe1d917",
        "untracked_digest": "absent"
      },
      {
        "path": "tests/Backend.StabilizationTests/ReportAttachmentSecurityTests.cs",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:c15657faf472f01279e48de2a7fd1008217a47c6a71af0ae149830f41cde65d2",
        "index_digest": "sha256:c15657faf472f01279e48de2a7fd1008217a47c6a71af0ae149830f41cde65d2",
        "worktree_digest": "sha256:4dcfd331c01b033294123ce1e8564eb76eeaea91c5db442f35278ea268eb5109",
        "untracked_digest": "absent"
      },
      {
        "path": "tests/ClubReportHub.Tests/ClubReportHub.Tests.csproj",
        "object_kind": {
          "head": "regular",
          "index": "regular",
          "worktree": "regular",
          "untracked": "absent"
        },
        "state": "clean",
        "rename_from": null,
        "rename_to": null,
        "head_digest": "sha256:6ce24197a4838033ec1061f8d362eee9b29c9850e1b4641d52b2bf163c80aea4",
        "index_digest": "sha256:6ce24197a4838033ec1061f8d362eee9b29c9850e1b4641d52b2bf163c80aea4",
        "worktree_digest": "sha256:abfbfbbf8fe941aca6c89316df146fc38502256785355d13f90772cbfc5ac7ba",
        "untracked_digest": "absent"
      }
    ]
  },
  "external_evidence": {
    "frontend_repo": "FPTU-Xperience/fptu-xperience-admin-ui",
    "branch": "chores/refactor-css",
    "head_commit": "f22bda074fb0d541961077e5ee052879a1b8e70c",
    "files": [
      {
        "path": "C:/Users/kenfi/Downloads/API_1.md",
        "sha256": "98f7bb0cffde5785e1c7edecc61e96dbc0e3ab84d14e52ca0196e12a9e60eb53"
      },
      {
        "path": "C:/Users/kenfi/Desktop/FPT_FE_ADMIN/src/services/api.js",
        "sha256": "ad29ab611c85e187a1c3584acf29ac220e79af46d5320460d6d988ea17098e25"
      },
      {
        "path": "C:/Users/kenfi/Desktop/FPT_FE_ADMIN/src/context/WorkspaceContext.jsx",
        "sha256": "dde0a676dd6acf945f1e8ea05884f2e70a1fbbd72658a8a49ecc06cfb35b3987"
      },
      {
        "path": "C:/Users/kenfi/Desktop/FPT_FE_ADMIN/src/context/AuthContext.jsx",
        "sha256": "221d73bbc92def5db0b47866de4a44a803bb53aae4d3039d16c324d832904659"
      },
      {
        "path": "C:/Users/kenfi/Desktop/FPT_FE_ADMIN/src/pages/Dashboard.jsx",
        "sha256": "7c501d4efd6f717f1d3789ea990e4dac0aece948a1bcfed58595764eecfedfff"
      }
    ],
    "note": "Separate FE repo and input document are not part of backend dirty digest; verify separately."
  },
  "primary_symbols": [
    {
      "symbol": "UserEndpoints",
      "file": "src/Services/AuthService/Endpoints/UserEndpoints.cs",
      "lines": "13-394",
      "role": "User reads, disable and actor-role mutation",
      "source_verified": true
    },
    {
      "symbol": "ReportFileEndpoints",
      "file": "src/Services/ReportService/Endpoints/ReportFileEndpoints.cs",
      "lines": "17-714",
      "role": "Aliases, attachment ownership and file lifecycle",
      "source_verified": true
    },
    {
      "symbol": "ProposalEndpoints",
      "file": "src/Services/FinanceService/Endpoints/ProposalEndpoints.cs",
      "lines": "18-553",
      "role": "Proposal state machine and report-linked review",
      "source_verified": true
    },
    {
      "symbol": "SettlementEndpoints",
      "file": "src/Services/FinanceService/Endpoints/SettlementEndpoints.cs",
      "lines": "12-186",
      "role": "Settlement reads and review accounting",
      "source_verified": true
    },
    {
      "symbol": "ActivityEndpoints",
      "file": "src/Services/ActivityService/Endpoints/ActivityEndpoints.cs",
      "lines": "16-344",
      "role": "Missing activity lifecycle and participant endpoints",
      "source_verified": true
    }
  ],
  "related_symbols": [
    {
      "symbol": "HandleUpdateUser",
      "relationship": "method in UserEndpoints",
      "relevance": "Single-role/self/final-admin guards and session revocation"
    },
    {
      "symbol": "ReplaceUploadedFile",
      "relationship": "method in ReportFileEndpoints",
      "relevance": "Alias must keep authorization/validation and file persistence order"
    },
    {
      "symbol": "ApproveSettlement",
      "relationship": "method in SettlementEndpoints",
      "relevance": "Reject/review must not trigger approval accounting"
    },
    {
      "symbol": "ApplicationEndpoints",
      "relationship": "related source boundary",
      "relevance": "Scoped detail and status filter"
    },
    {
      "symbol": "MemberManagementEndpoints",
      "relationship": "related source boundary",
      "relevance": "Member profile whitelist"
    },
    {
      "symbol": "AttendanceManagementEndpoints",
      "relationship": "related source boundary",
      "relevance": "Roster IDs and multistatus attendance"
    },
    {
      "symbol": "DeadlineEndpoints",
      "relationship": "related source boundary",
      "relevance": "Alias and period operations"
    },
    {
      "symbol": "NotificationEndpoints",
      "relationship": "related source boundary",
      "relevance": "Recipient-scoped detail"
    },
    {
      "symbol": "ActorAccountPolicy",
      "relationship": "auth constraint",
      "relevance": "Only ADMIN/CLUB_MANAGER/CLUB_MEMBER allowed at current login"
    },
    {
      "symbol": "ExportEndpoints",
      "relationship": "capability boundary",
      "relevance": "Only per-report exports; student dataset not alias"
    }
  ],
  "execution_path": [
    "FE api.js -> gateway auth/rate limiter -> service policy",
    "Trusted principal and resource scope -> validation/state guards -> persistence/audit/outbox -> DTO",
    "FE local-only WorkspaceContext commits still need explicit integration"
  ],
  "pdg_constraints": [
    {
      "description": "User role and account guard before mutation; invalidation after meaningful change",
      "affected_statements": [
        "src/Services/AuthService/Endpoints/UserEndpoints.cs:182",
        "src/Services/AuthService/Endpoints/UserEndpoints.cs:203",
        "src/Services/AuthService/Endpoints/UserEndpoints.cs:210",
        "src/Services/AuthService/Endpoints/UserEndpoints.cs:218",
        "src/Services/AuthService/Endpoints/UserEndpoints.cs:252"
      ],
      "implementation_consequence": "New role/disable endpoints preserve single-role/self/final-admin/session rules; no claim current multisave is atomic"
    },
    {
      "description": "Report author+Draft/Rejected+multipart+file validation precede storage",
      "affected_statements": [
        "src/Services/ReportService/Endpoints/ReportFileEndpoints.cs:384",
        "src/Services/ReportService/Endpoints/ReportFileEndpoints.cs:389",
        "src/Services/ReportService/Endpoints/ReportFileEndpoints.cs:394",
        "src/Services/ReportService/Endpoints/ReportFileEndpoints.cs:415"
      ],
      "implementation_consequence": "Reuse canonical handler and upload limiter; savedFile PDG flow unresolved; test DB failure after file write"
    },
    {
      "description": "Settlement approval only Submitted and not proposal creator",
      "affected_statements": [
        "src/Services/FinanceService/Endpoints/SettlementEndpoints.cs:160",
        "src/Services/FinanceService/Endpoints/SettlementEndpoints.cs:165",
        "src/Services/FinanceService/Endpoints/SettlementEndpoints.cs:170"
      ],
      "implementation_consequence": "Reject/review cannot create approval transaction or parent Settled; relational concurrency verification"
    }
  ],
  "architectural_patterns": [
    {
      "pattern": "Grouped minimal API authorization",
      "example_location": "src/Services/AuthService/Endpoints/UserEndpoints.cs:15",
      "usage_guidance": "Self-profile outside management group, preserve old policies"
    },
    {
      "pattern": "Report file canonical handlers",
      "example_location": "src/Services/ReportService/Endpoints/ReportFileEndpoints.cs:19",
      "usage_guidance": "Use reportId binding name and same upload constraints"
    },
    {
      "pattern": "Recipient scope",
      "example_location": "src/Services/NotificationService/Endpoints/NotificationEndpoints.cs:24",
      "usage_guidance": "New detail read uses same recipient/access logic"
    },
    {
      "pattern": "Backoffice actor separate from Auth profile",
      "example_location": "src/Services/AdminService/Endpoints/AdminEndpoints.cs:59",
      "usage_guidance": "No cross-DB dependency for display name"
    }
  ],
  "files_to_modify": [
    {
      "file": "src/Services/AuthService/Endpoints/UserEndpoints.cs",
      "symbols": [
        "UserEndpoints"
      ],
      "intended_change": "N1 user detail/self profile; D1 gated"
    },
    {
      "file": "src/Services/AuthService/Contracts/AuthContracts.cs",
      "symbols": [],
      "intended_change": "DTO fields from real data only"
    },
    {
      "file": "src/Services/ClubService/Endpoints/ApplicationEndpoints.cs",
      "symbols": [
        "ApplicationEndpoints"
      ],
      "intended_change": "N2 application detail/status filter"
    },
    {
      "file": "src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs",
      "symbols": [
        "MemberManagementEndpoints"
      ],
      "intended_change": "N5 profile whitelist; D3 admission gated"
    },
    {
      "file": "src/Services/ClubService/Contracts/ClubContracts.cs",
      "symbols": [],
      "intended_change": "Bounded contract compatibility"
    },
    {
      "file": "src/Services/ActivityService/Endpoints/ActivityEndpoints.cs",
      "symbols": [
        "ActivityEndpoints"
      ],
      "intended_change": "N5 lifecycle, registration, check-in and history"
    },
    {
      "file": "src/Services/ActivityService/Endpoints/AttendanceManagementEndpoints.cs",
      "symbols": [
        "AttendanceManagementEndpoints"
      ],
      "intended_change": "Roster-based compatibility validation"
    },
    {
      "file": "src/Services/ActivityService/Contracts/ActivityContracts.cs",
      "symbols": [],
      "intended_change": "Alias validation without changing keys"
    },
    {
      "file": "src/Services/ReportService/Endpoints/ReportFileEndpoints.cs",
      "symbols": [
        "ReportFileEndpoints"
      ],
      "intended_change": "A1 aliases and N4 attachment unlink"
    },
    {
      "file": "src/Services/ReportService/Endpoints/DeadlineEndpoints.cs",
      "symbols": [
        "DeadlineEndpoints"
      ],
      "intended_change": "A2 deadline alias and N3 period operations"
    },
    {
      "file": "src/Services/ReportService/Contracts/ReportContracts.cs",
      "symbols": [],
      "intended_change": "Deadline/file contract details"
    },
    {
      "file": "src/Services/FinanceService/Endpoints/SettlementEndpoints.cs",
      "symbols": [
        "SettlementEndpoints"
      ],
      "intended_change": "N2 detail and N6 guarded reject"
    },
    {
      "file": "src/Services/FinanceService/Endpoints/ProposalEndpoints.cs",
      "symbols": [
        "ProposalEndpoints"
      ],
      "intended_change": "Pagination compatibility; D2 gated"
    },
    {
      "file": "src/Services/FinanceService/Endpoints/TransactionEndpoints.cs",
      "symbols": [
        "TransactionEndpoints"
      ],
      "intended_change": "Only if D2 manual-ledger decision approved"
    },
    {
      "file": "src/Services/FinanceService/Contracts/FinanceContracts.cs",
      "symbols": [],
      "intended_change": "Endpoint-specific amount and review input"
    },
    {
      "file": "src/Services/NotificationService/Endpoints/NotificationEndpoints.cs",
      "symbols": [
        "NotificationEndpoints"
      ],
      "intended_change": "N2 recipient-scoped detail"
    },
    {
      "file": "src/Gateway/ApiGateway/yarp.json",
      "symbols": [],
      "intended_change": "Deadline alias and file upload limiter"
    }
  ],
  "tests": [
    {
      "file": "tests/Backend.StabilizationTests/CrossServiceWorkflowAuthorizationTests.cs",
      "scenarios": [
        "Add data-driven contract route cases; pending D items cannot fake-pass",
        "Preserve combined report internal-token guard",
        "Alias file route parity and gateway limiter",
        "Scoped detail reads, invalid actor/club IDs, optional pagination",
        "Attendance date VN, idempotent duplicate, invalid batch no mutation",
        "Deadline literal /me, path/body conflict and soft-disable"
      ]
    },
    {
      "file": "tests/Backend.StabilizationTests/JwtAuthorizationFreshnessTests.cs",
      "scenarios": [
        "Role/account status change invalidates old JWT",
        "Self-profile identity cannot be overridden"
      ]
    },
    {
      "file": "tests/Backend.StabilizationTests/RefreshTokenSecurityTests.cs",
      "scenarios": [
        "Revoked refresh token cannot preserve old privileges"
      ]
    },
    {
      "file": "tests/Backend.StabilizationTests/FinanceAndReportDataIntegrityTests.cs",
      "scenarios": [
        "One active settlement under concurrent create",
        "Approve/reject concurrent exactly one winner",
        "Reject cannot create approval ledger or mark parent Settled"
      ]
    },
    {
      "file": "tests/Backend.StabilizationTests/ReportAttachmentSecurityTests.cs",
      "scenarios": [
        "Cross-report attachment deletion denied",
        "Path traversal and physical path masking preserved"
      ]
    },
    {
      "file": "tests/AdminService.IntegrationTests/AdminApiTests.cs",
      "scenarios": [
        "v1 actor identity preserved",
        "Admin/CTSV cross-access403",
        "Forged actor input cannot elevate"
      ]
    }
  ],
  "verification_commands": [
    "dotnet restore ClubReportHub.sln",
    "dotnet build ClubReportHub.sln --configuration Release --no-restore -warnaserror",
    "dotnet test ClubReportHub.sln --configuration Release --no-build --logger \"trx\" --collect:\"XPlat Code Coverage\" --results-directory ./TestResults",
    "dotnet format src/Services/AuthService/AuthService.csproj --no-restore --verify-no-changes",
    "dotnet format src/Services/ClubService/ClubService.csproj --no-restore --verify-no-changes",
    "dotnet format src/Services/AdminService/AdminService.csproj --no-restore --verify-no-changes",
    "dotnet format tests/Backend.StabilizationTests/Backend.StabilizationTests.csproj --no-restore --verify-no-changes",
    "dotnet format tests/AdminService.IntegrationTests/AdminService.IntegrationTests.csproj --no-restore --verify-no-changes",
    "docker compose config --quiet"
  ],
  "precommit_gate": "Only before an authorized commit: node .gitnexus/run.cjs detect-changes --scope all --repo .; partial/truncated is not clean.",
  "risks": [
    "Existing email-only production dev-login is a separate security risk",
    "Login roles vs CTSV UI mismatch",
    "Accounting/file transitions need retention and concurrency guarantees",
    "FE local-only changes and mock-derived stats hide gaps",
    "Graph UNKNOWN + cached stale metadata cannot prove low risk",
    "No Docker engine/HTTP/UI test in this planning phase"
  ],
  "assumptions": [
    "Giữ role và approval semantics hiện tại; trước khi mở rộng, chốt D1/D2/D4 với chủ API.",
    "Deadline DELETE đề xuất IsActive=false; xác nhận contract soft-disable trước khi triển khai.",
    "Unique key participant/check-in chưa được kiểm đầy đủ; executor đọc EF mapping/migration liên quan trước N5, không tự thêm migration ngoài scope.",
    "FA26 và FALL2026 chưa được xác nhận cùng kỳ; đối chiếu nguồn period chính thức trước khi normalize.",
    "FE baseline là chores/refactor-css f22bda0; kiểm HEAD trước tích hợp.",
    "API_1 là mẫu contract tham khảo, không cho phép mock login, suy quyền từ email hoặc làm giả số liệu.",
    "Sửa FE là đợt tích hợp riêng; không coi có backend endpoint là UI đã ghi server."
  ],
  "open_questions": [
    "D1: DELETE user vô hiệu hóa hay xóa cứng? Assign/remove role có nghĩa gì khi account phải có đúng một actor role?",
    "D2: Giữ create=Submitted hay thêm Draft thật? Review chỉ ghi chú hay đổi trạng thái?",
    "D2: Có cho tạo transaction thủ công không? Cần chốt role, loại bút toán, audit và idempotency.",
    "D3: Chính sách retention/cancel/delete report/activity và direct-add thành viên không giả mạo consent là gì?",
    "D4: Có mở đăng nhập thật cho SYSTEM_ADMIN/STUDENT_AFFAIRS_ADMIN trong đợt này không?",
    "D4: Có cần xuất danh sách sinh viên theo kỳ không? Dataset, nguồn dữ liệu, format và scope quyền phải được định nghĩa.",
    "D4: Nguồn thật cho studentCode, foundedAt, managerId đơn, rating, KPI weight và anomalies là gì?"
  ],
  "avoid": [
    "Do not repeat full discovery; reverify drifted source and specifically unresolved mappings",
    "Do not read UNKNOWN/empty graph callers as low risk",
    "Do not remove old array/field/route contracts silently",
    "Do not infer role from email or issue mock backend tokens",
    "Do not broaden login or skip combined-report internal-token guard without decision",
    "Do not add XP/semester/quest/reward behavior to match mock examples",
    "Do not duplicate approval ledger/outbox on retries",
    "Do not alter production volumes/secrets/Compose/deployment",
    "Do not claim tests/HTTP/production success from static coverage"
  ],
  "publication_override": "User explicitly authorized direct Markdown via apply_patch on Windows; schema2 snapshot generated with original helper; no safe-write receipt claimed."
}
~~~

## 12. Assumptions and Open Questions

### Assumptions cần xác minh

- [assumed] Giữ role và approval semantics hiện tại; trước khi mở rộng, chốt D1/D2/D4 với chủ API.
- [assumed] Deadline DELETE đề xuất IsActive=false; xác nhận contract soft-disable trước khi triển khai.
- [assumed] Unique key participant/check-in chưa được kiểm đầy đủ; executor đọc EF mapping/migration liên quan trước N5, không tự thêm migration ngoài scope.
- [assumed] FA26 và FALL2026 chưa được xác nhận cùng kỳ; đối chiếu nguồn period chính thức trước khi normalize.
- [assumed] FE baseline là chores/refactor-css f22bda0; kiểm HEAD trước tích hợp.
- [assumed] API_1 là mẫu contract tham khảo, không cho phép mock login, suy quyền từ email hoặc làm giả số liệu.
- [assumed] Sửa FE là đợt tích hợp riêng; không coi có backend endpoint là UI đã ghi server.

### Các quyết định còn mở

- D1: DELETE user vô hiệu hóa hay xóa cứng? Assign/remove role có nghĩa gì khi account phải có đúng một actor role?
- D2: Giữ create=Submitted hay thêm Draft thật? Review chỉ ghi chú hay đổi trạng thái?
- D2: Có cho tạo transaction thủ công không? Cần chốt role, loại bút toán, audit và idempotency.
- D3: Chính sách retention/cancel/delete report/activity và direct-add thành viên không giả mạo consent là gì?
- D4: Có mở đăng nhập thật cho SYSTEM_ADMIN/STUDENT_AFFAIRS_ADMIN trong đợt này không?
- D4: Có cần xuất danh sách sinh viên theo kỳ không? Dataset, nguồn dữ liệu, format và scope quyền phải được định nghĩa.
- D4: Nguồn thật cho studentCode, foundedAt, managerId đơn, rating, KPI weight và anomalies là gì?

[inferred] D1–D4 chỉ khóa nhóm tương ứng, không khóa endpoint đọc và alias tương đương. Hoàn thành các nhóm A/N không đồng nghĩa hỗ trợ đầy đủ121 API.

### Giới hạn và follow-up ngoài scope

- [graph] Context backend đã ghi HEAD mới nhưng MCP staleness vẫn mâu thuẫn; process extraction bị giới hạn và primary impact UNKNOWN. Source là chuẩn, không đổi analyzer trong đợt API.
- [verified] Docker engine chưa truy cập được; chưa chạy service/HTTP/UI. Compose parse không chứng minh runtime healthy.
- [verified] Người dùng đã xác nhận ghi Markdown trực tiếp trên Windows thay writer của skill. Snapshot gốc vẫn sử dụng được cho63 cited paths hiện có; không tuyên bố có safe-write receipt.
- [inferred] Hardening dev-login, transaction toàn bộ user update, redesign quyền, cleanup orphan files, dashboard số liệu thực và export dataset mới cần follow-up có scope rõ.
- [assumed] Các trường request mẫu thiếu hoặc không tương đương phải chốt với API owner; không trả success nhưng bỏ qua dữ liệu người dùng gửi.

## 13. Definition of Done

### Phiên lập kế hoạch này

- [x] Pin backend/FE commit, đọc API_1 và source hiện tại.
- [x] Inventory121 method/path:87 declared,34 missing; tách shape/semantics.
- [x] Graph navigation, depth3 impact, xác minh UNKNOWN bằng source, PDG ba hàm.
- [x] Có thứ tự triển khai, files/tests, D1–D4 và context pack/provenance.
- [x] Chỉ thêm tài liệu; không sửa application, schema, deployment hay production data.

### Phiên triển khai tiếp theo

- [ ] Mỗi mục A/N có request/response/policy rõ và valid call qua gateway.
- [ ] Old routes/fields hoạt động; alias upload giữ auth/limiter/storage guards.
- [ ] Auth/session, club scope, IDs, duplicate/concurrency và file safety có tests.
- [ ] D1–D4 được chốt+triển khai+test hoặc còn pending rõ; không báo full API_1 khi còn thiếu.
- [ ] Không bịa KPI/XP/anomalies/semester/export data hoặc no-op success.
- [ ] Restore/build/test/format/EF/dependency/Compose có kết quả thật; SQL invariants được kiểm ở DB phù hợp.
- [ ] Nếu có scope FE: UI ghi server thật và reload giữ dữ liệu, mock tắt, lỗi không bị che; số liệu không lấy từ hệ số mock.
- [ ] Trước commit được yêu cầu: diff review và detect_changes không partial/truncated.
- [ ] Báo rõ local validation và phần chưa kiểm; không suy production success từ máy local.
