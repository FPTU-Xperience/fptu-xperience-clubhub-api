# Rà soát và tối ưu code API Sinh viên, Quản lý CLB

Ngày: **25/10/2026**. Phạm vi **BE**, baseline `b1edcbf`. Không sửa FE, schema/migration, chính sách điểm hoặc quyết định governance đang chờ chốt.

**Đã rà soát tĩnh đủ 180/180 operation khác nhau** trong hợp hai actor; mỗi method + đường dẫn được tính riêng, kể cả alias. Bảng chi tiết bên dưới không đồng nghĩa mọi nhánh nghiệp vụ đều có test HTTP riêng.

**Kiểm chứng cuối: ĐẠT — 946/946 test, 0 lỗi, 0 bỏ qua**, gồm **32 case mới**. Baseline trước sửa: 914/914 test đạt. Có 13 case đã tái hiện lỗi trước khi áp dụng bản sửa tương ứng.

## Những thay đổi đã triển khai

- Danh sách hồ sơ hoạt động tải execution theo lô, dùng chung mapper với luồng chi tiết và ghi dữ liệu.
- Danh sách CLB COUNT thành viên tại SQL, không tải toàn bộ hồ sơ thành viên để đếm.
- Xem trạng thái PDF chỉ truy vấn metadata; tải PDF vẫn kiểm tra quyền hiện tại.
- Dùng lookup/dictionary/HashSet cho phiên hoạt động, công việc, completion và lịch sử đóng góp; giữ thứ tự và phạm vi dữ liệu.
- Dùng chung projection ô lịch học và metadata file báo cáo. Sửa cờ preview đang kiểm tra nhầm file gốc và chặn thư mục có tên chỉ trùng tiền tố.
- Dùng chung offset phân trang có phép tính long và giới hạn int; giữ mặc định và giới hạn đầu vào của từng API.
- Giải thể/chuyển chủ lưu đơn và version CLB cùng một lần SaveChanges, tránh commit nửa chừng.
- Refresh kiểm tra tài khoản và replacement/family/version/quyền hiện tại trước khi trả token mới hoặc pair trong cache; vẫn hỗ trợ nhiều tab.
- Logout chỉ thu hồi family của đúng actor; cache Auth có Size tương thích cấu hình production.
- Danh sách thông báo không tracking; truyền cancellation token cho các thao tác EF tương ứng.

## Phạm vi rà soát

| Service | Số operation |
|---|---:|
| ActivityService | 47 |
| AdminService | 32 |
| AuthService | 13 |
| ClubService | 40 |
| ExportService | 8 |
| FinanceService | 9 |
| NotificationService | 4 |
| ReportService | 27 |
| **Tổng** | **180** |

## Giới hạn và điểm cần giữ đúng

- Số 178 trước đó là route thuộc phạm vi quản lý, có tính cả một API sửa hồ sơ thành viên cố ý trả `403 PERSONAL_PROFILE_SELF_ONLY`. API này được giữ để bảo vệ quyền tự sửa hồ sơ, không mở lại quyền sửa thay.
- Hai PUT điểm danh quản lý chỉ có nhánh legacy dùng được nếu chưa có grant xác minh; canonical vẫn cần `attendance.verify`.
- Một số API vẫn phụ thuộc policy, kỳ, campus, ownership, state hoặc dữ liệu nguồn. Có route không đồng nghĩa đã hoàn thiện mọi yêu cầu contract.
- Chưa đổi chính sách fail-open khi authority lỗi hoặc cache quyền CLB của các luồng legacy Report/Finance. Đây là phát hiện quyền truy cập còn cần xử lý riêng, không được đánh dấu đã sửa.
- Media profile vẫn có I/O bên ngoài trong transaction; việc tách cần bảo toàn xác minh intent, idempotent replay, CAS và dọn object.
- Thống kê trải nghiệm CLB còn có thể gom việc đọc JSON/đếm theo vai trò và lần tham gia trước đó; đây là tối ưu tiếp theo, chưa được sửa trong đợt này.
- Không gom các guard chỉ giống hình thức nhưng khác quyền/trạng thái. Không thêm framework, tầng repository chung hoặc generic controller.
- Đã tham chiếu test có sẵn và thêm regression; chưa thực hiện kiểm thử tải hoặc nghiệm thu trên production.

## Kết quả kiểm chứng

| Kiểm tra | Kết quả |
|---|---|
| Build toàn bộ solution Release, `-warnaserror` | Đạt |
| Test toàn bộ solution, gồm migration/transaction trên SQL Server | 946 đạt; 0 lỗi; 0 bỏ qua |
| Test mới cho tối ưu và lỗi được phát hiện | 32 đạt |
| Hai test tạo đơn CLB đồng thời và hai test lỗi insert | 4 đạt trên SQL Server |
| Định dạng code theo cấu hình CI; kiểm tra lại file test thay đổi cuối | Đạt |
| EF `has-pending-model-changes` cho 8 service | Không có thay đổi model chưa có migration |
| Thu thập coverage | 3 file, tương ứng 3 assembly test; không dùng số lượng file để suy ra độ phủ đầy đủ |
| Hash mã C# sau build/test và phân tích graph | 32/32 file giữ nguyên |

Hai test đồng thời trước đây dùng EF InMemory. Sau khi gộp SaveChanges, provider này giữ insert trước khi gặp lỗi concurrency vì không hỗ trợ rollback; HTTP vẫn trả đúng 201/409 nhưng dữ liệu test còn hai đơn. Hai test được chuyển sang SQL Server để kiểm chứng đúng transaction và filtered unique index đang dùng; toàn bộ assertion được giữ nguyên. Không thêm nhánh xử lý riêng InMemory vào production.

GitNexus đã được làm mới sau thay đổi cuối. `detect_changes(scope: all)` nhận diện 64 symbol trong 24 file tracked và 69 flow liên quan, mức ảnh hưởng `critical` do helper dùng chung. Công cụ không liệt kê 8 file C# mới chưa tracked trong diff này; đã kiểm tra riêng cả 8 file có trong index, và helper phân trang có 7 caller. Graph có giới hạn dựng flow/dynamic dispatch C#; kết quả này được đối chiếu với source và test, không được diễn giải thành bảo đảm toàn bộ đường gọi đều đã được dựng.

Lệnh kiểm chứng chính: `dotnet build ClubReportHub.sln -c Release --no-restore -warnaserror` và `dotnet test ClubReportHub.sln -c Release --no-build --collect:"XPlat Code Coverage"`. Các biến kết nối SQL dành cho test Admin/Finance đã được cấu hình vào LocalDB dùng database tạm; kết quả không dựa vào việc bỏ qua các test này.

## Bảng từng API

SV = Sinh viên; QL = Quản lý CLB. Mọi dòng đã được đọc handler và helper trực tiếp; JSON đi kèm giữ ghi chú rà soát ban đầu trong `baselineReview`; `currentDisposition` là kết quả cuối, phân biệt sửa handler, dùng chung helper, bổ sung test và phát hiện còn lại.

| # | Method + route | Actor | Ghi chú |
|---:|---|---|---|
| 1 | `GET /api/activities` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 2 | `POST /api/activities` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 3 | `POST /api/activities/clubs/{clubId:int}/member-statistics` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 4 | `POST /api/activities/clubs/{clubId:int}/member-statistics/detail` | QL | Dùng offset chung; giữ validation hiện tại. |
| 5 | `POST /api/activities/{activityId:int}/assignments` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 6 | `POST /api/activities/{activityId:int}/assignments/{assignmentId:guid}/attendance-claims` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 7 | `POST /api/activities/{activityId:int}/assignments/{assignmentId:guid}/cancel` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 8 | `POST /api/activities/{activityId:int}/assignments/{assignmentId:guid}/completions` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 9 | `PUT /api/activities/{activityId:int}/cover-image` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 10 | `PATCH /api/activities/{activityId:int}/publication` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 11 | `PATCH /api/activities/{activityId:int}/recruitment` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 12 | `POST /api/activities/{activityId:int}/recruitment` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 13 | `POST /api/activities/{activityId:int}/recruitment/positions/{positionId:guid}/join` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 14 | `PATCH /api/activities/{activityId:int}/recruitment/status` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 15 | `GET /api/activities/{activityId:int}/resources` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 16 | `POST /api/activities/{activityId:int}/resources` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 17 | `GET /api/activities/{activityId:int}/resources/history` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 18 | `DELETE /api/activities/{activityId:int}/resources/{resourceId:guid}` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 19 | `PATCH /api/activities/{activityId:int}/resources/{resourceId:guid}` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 20 | `GET /api/activities/{activityId:int}/sessions` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 21 | `POST /api/activities/{activityId:int}/sessions` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 22 | `PATCH /api/activities/{activityId:int}/sessions/{sessionId:int}` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 23 | `GET /api/activities/{activityId:int}/sessions/{sessionId:int}/attendance-challenge` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 24 | `POST /api/activities/{activityId:int}/sessions/{sessionId:int}/cancel` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 25 | `POST /api/activities/{activityId:int}/sessions/{sessionId:int}/close` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 26 | `GET /api/activities/{activityId:int}/staffing` | SV/QL | Đọc định nghĩa một lần và gom lookup; giữ giới hạn minh chứng. |
| 27 | `DELETE /api/activities/{id:int}` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 28 | `GET /api/activities/{id:int}` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 29 | `PUT /api/activities/{id:int}` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 30 | `POST /api/activities/{id:int}/check-in` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 31 | `PATCH /api/activities/{id:int}/complete` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 32 | `GET /api/activities/{id:int}/my-attendance` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 33 | `POST /api/activities/{id:int}/participants` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 34 | `GET /api/activity-applications` | QL | Danh sách tải execution theo lô; dùng mapper và phân trang chung. |
| 35 | `POST /api/activity-applications` | QL | Dùng chung mapper hồ sơ hoạt động; guard/state giữ nguyên. |
| 36 | `GET /api/activity-applications/{id:int}` | QL | Dùng chung mapper hồ sơ hoạt động; guard/state giữ nguyên. |
| 37 | `PATCH /api/activity-applications/{id:int}` | QL | Dùng chung mapper hồ sơ hoạt động; guard/state giữ nguyên. |
| 38 | `GET /api/activity-applications/{id:int}/history` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 39 | `POST /api/activity-applications/{id:int}/submit` | QL | Dùng chung mapper hồ sơ hoạt động; guard/state giữ nguyên. |
| 40 | `GET /api/clubs/{clubId:int}/activities/{activityId:int}/attendance` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 41 | `PUT /api/clubs/{clubId:int}/activities/{activityId:int}/attendance` | QL | Chỉ dùng nhánh legacy; canonical cần grant attendance.verify riêng. |
| 42 | `PUT /api/clubs/{clubId:int}/activities/{activityId:int}/attendance/{memberId:int}` | QL | Chỉ dùng nhánh legacy; canonical cần grant attendance.verify riêng. |
| 43 | `GET /api/clubs/{clubId:int}/activity-recruitments` | SV/QL | Dùng offset chung; giữ validation hiện tại. |
| 44 | `GET /api/users/me/activity-history` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 45 | `GET /api/v2/activities/feed` | SV/QL | Dùng dictionary/HashSet cho phiên và trạng thái tham gia. |
| 46 | `GET /api/v2/activities/{activityId:int}` | SV/QL | Dùng dictionary/HashSet cho phiên và trạng thái tham gia. |
| 47 | `GET /api/v2/activities/{activityId:int}/sessions` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 48 | `GET /api/academic-calendar/{year}/{term}` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 49 | `POST /api/activities/{activityId:int}/contributions` | SV/QL | Đã gom lookup ở API chi tiết đóng góp; các handler khác cùng nhóm giữ nguyên. |
| 50 | `GET /api/activities/{activityId:int}/sessions/{sessionId:int}/manual-attendance-requests` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 51 | `POST /api/activities/{activityId:int}/sessions/{sessionId:int}/manual-attendance-requests` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 52 | `GET /api/bonus-matrix` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 53 | `GET /api/clubs/{clubId:int}/experience-statistics` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 54 | `POST /api/contributions/evidence` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 55 | `GET /api/contributions/evidence/{id:guid}` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 56 | `GET /api/contributions/evidence/{id:guid}/download` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 57 | `GET /api/contributions/{id:guid}` | SV/QL | Đã gom lookup ở API chi tiết đóng góp; các handler khác cùng nhóm giữ nguyên. |
| 58 | `PATCH /api/contributions/{id:guid}` | SV/QL | Đã gom lookup ở API chi tiết đóng góp; các handler khác cùng nhóm giữ nguyên. |
| 59 | `POST /api/contributions/{id:guid}/role-upgrade-requests` | SV/QL | Đã gom lookup ở API chi tiết đóng góp; các handler khác cùng nhóm giữ nguyên. |
| 60 | `GET /api/quests` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 61 | `GET /api/quests/me` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 62 | `GET /api/quests/{id}` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 63 | `POST /api/quests/{id}/complete` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 64 | `POST /api/quests/{id}/join` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 65 | `GET /api/users/me/contributions` | SV/QL | Đã gom lookup ở API chi tiết đóng góp; các handler khác cùng nhóm giữ nguyên. |
| 66 | `GET /api/users/me/experience-ledger` | SV/QL | Đã gom lookup ở API chi tiết đóng góp; các handler khác cùng nhóm giữ nguyên. |
| 67 | `GET /api/users/me/experience-summary` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 68 | `GET /api/v1/bonus-matrix` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 69 | `POST /api/v1/declarations` | SV/QL | Đã gom lookup ở API chi tiết đóng góp; các handler khác cùng nhóm giữ nguyên. Còn phát hiện/giới hạn cần xem chi tiết. |
| 70 | `GET /api/v1/declarations/me` | SV/QL | Đã gom lookup ở API chi tiết đóng góp; các handler khác cùng nhóm giữ nguyên. Còn phát hiện/giới hạn cần xem chi tiết. |
| 71 | `GET /api/v1/declarations/me/radar` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 72 | `GET /api/v1/declarations/{id:guid}` | SV/QL | Đã gom lookup ở API chi tiết đóng góp; các handler khác cùng nhóm giữ nguyên. Còn phát hiện/giới hạn cần xem chi tiết. |
| 73 | `PUT /api/v1/declarations/{id:guid}` | SV/QL | Đã gom lookup ở API chi tiết đóng góp; các handler khác cùng nhóm giữ nguyên. Còn phát hiện/giới hạn cần xem chi tiết. |
| 74 | `GET /api/v1/quests` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 75 | `GET /api/v1/quests/me` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 76 | `GET /api/v1/quests/{id}` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 77 | `POST /api/v1/quests/{id}/complete` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 78 | `POST /api/v1/quests/{id}/join` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 79 | `GET /api/v2/recommendations/activities` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 80 | `POST /api/auth/google` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 81 | `POST /api/auth/internal/login` | SV/QL | Đã bổ sung test HTTP tài khoản nội bộ; handler giữ nguyên. |
| 82 | `POST /api/auth/internal/register` | SV/QL | Đã bổ sung test HTTP tài khoản nội bộ; handler giữ nguyên. |
| 83 | `POST /api/auth/logout` | SV/QL | Logout chỉ thu hồi family thuộc actor hiện tại. |
| 84 | `POST /api/auth/refresh` | SV/QL | Đã kiểm tra lại tài khoản/session khi refresh. |
| 85 | `POST /api/media/upload-intents` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 86 | `GET /api/users/me` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 87 | `GET /api/users/me/profile` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 88 | `PATCH /api/users/me/profile` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 89 | `GET /api/users/me/study-schedule/{year}/{term}/{block}` | SV/QL | GET/import dùng chung mapper ô lịch; quyền và version giữ nguyên. |
| 90 | `POST /api/users/me/study-schedule/{year}/{term}/{block}/bulk-import` | SV/QL | GET/import dùng chung mapper ô lịch; quyền và version giữ nguyên. |
| 91 | `DELETE /api/users/me/study-schedule/{year}/{term}/{block}/{week}/{day}/{slotId}` | SV/QL | GET/import dùng chung mapper ô lịch; quyền và version giữ nguyên. |
| 92 | `PUT /api/users/me/study-schedule/{year}/{term}/{block}/{week}/{day}/{slotId}` | SV/QL | GET/import dùng chung mapper ô lịch; quyền và version giữ nguyên. |
| 93 | `GET /api/clubs` | SV/QL | Đếm thành viên tại SQL, không tải hồ sơ để đếm. |
| 94 | `POST /api/clubs/applications` | SV | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 95 | `GET /api/clubs/applications/me` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 96 | `GET /api/clubs/applications/{applicationId:int}` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 97 | `PUT /api/clubs/applications/{applicationId:int}` | SV | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 98 | `GET /api/clubs/categories` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 99 | `GET /api/clubs/manager/{managerUserId:int}` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 100 | `GET /api/clubs/me/access` | SV/QL | Giữ projection quyền hiện tại; không gom các quyền khác nghiệp vụ. |
| 101 | `GET /api/clubs/me/managed` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 102 | `GET /api/clubs/me/membership-applications` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 103 | `POST /api/clubs/me/membership-applications/{applicationId:int}/withdraw` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 104 | `GET /api/clubs/me/memberships` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 105 | `DELETE /api/clubs/memberships/{membershipId:int}` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 106 | `POST /api/clubs/memberships/{membershipId:int}/approve` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 107 | `POST /api/clubs/memberships/{membershipId:int}/member` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 108 | `POST /api/clubs/memberships/{membershipId:int}/reject` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 109 | `POST /api/clubs/{clubId:int}/disband` | QL | Đơn và version CLB lưu cùng transaction; kiểm thử trên SQL. |
| 110 | `GET /api/clubs/{clubId:int}/disband-request` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 111 | `GET /api/clubs/{clubId:int}/eligible-members` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 112 | `GET /api/clubs/{clubId:int}/experience-policies` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 113 | `GET /api/clubs/{clubId:int}/invitation-semesters` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 114 | `GET /api/clubs/{clubId:int}/member-roster` | QL | Dùng helper phân trang chống tràn số; giữ mặc định và phạm vi. |
| 115 | `POST /api/clubs/{clubId:int}/member-roster/resolve` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 116 | `GET /api/clubs/{clubId:int}/members` | QL | Dùng helper phân trang chống tràn số; giữ mặc định và phạm vi. |
| 117 | `POST /api/clubs/{clubId:int}/members` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 118 | `GET /api/clubs/{clubId:int}/members-for-transfer` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 119 | `DELETE /api/clubs/{clubId:int}/members/{memberId:int}` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 120 | `GET /api/clubs/{clubId:int}/members/{memberId:int}` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 121 | `PUT /api/clubs/{clubId:int}/members/{memberId:int}` | QL | Cố ý trả 403: hồ sơ cá nhân chỉ được tự sửa. |
| 122 | `PUT /api/clubs/{clubId:int}/public-images` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 123 | `PATCH /api/clubs/{clubId:int}/public-profile` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 124 | `GET /api/clubs/{clubId:int}/semester-plans/{semesterId}` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 125 | `PUT /api/clubs/{clubId:int}/semester-plans/{semesterId}` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 126 | `POST /api/clubs/{clubId:int}/transfer-ownership` | QL | Đơn và version CLB lưu cùng transaction; kiểm thử trên SQL. |
| 127 | `GET /api/clubs/{clubId:int}/transfer-request` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 128 | `GET /api/clubs/{clubId:int}/workspace-semesters` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 129 | `GET /api/clubs/{id:int}` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 130 | `POST /api/clubs/{id:int}/join` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 131 | `GET /api/clubs/{id:int}/memberships` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 132 | `POST /api/clubs/{id:int}/treasurers` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 133 | `GET /api/club-experience-reports/{id:guid}` | QL | Status PDF chỉ tải metadata; quyền tải file được kiểm tra lại. |
| 134 | `GET /api/club-experience-reports/{id:guid}/download` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 135 | `POST /api/clubs/{clubId:int}/experience-reports` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 136 | `GET /api/clubs/{clubId:int}/experience-reports/{id:guid}/download` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 137 | `GET /api/exports` | QL | Dùng helper phân trang chống tràn số; giữ mặc định và phạm vi. |
| 138 | `POST /api/exports` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 139 | `GET /api/exports/{id:int}` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 140 | `GET /api/exports/{id:int}/download` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 141 | `GET /api/finance/proposals` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 142 | `GET /api/finance/proposals/{id:int}` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 143 | `POST /api/finance/proposals/{id:int}/manager-approve` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 144 | `POST /api/finance/proposals/{id:int}/manager-reject` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 145 | `POST /api/finance/proposals/{id:int}/manager-review` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 146 | `POST /api/finance/proposals/{id:int}/submit` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 147 | `GET /api/finance/settlements` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 148 | `GET /api/finance/settlements/{id:int}` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 149 | `GET /api/finance/transactions` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 150 | `GET /api/notifications` | SV/QL | List bỏ tracking; truyền cancellation; giữ phạm vi người nhận. |
| 151 | `PUT /api/notifications/read-all` | SV/QL | List bỏ tracking; truyền cancellation; giữ phạm vi người nhận. |
| 152 | `GET /api/notifications/{id:int}` | SV/QL | List bỏ tracking; truyền cancellation; giữ phạm vi người nhận. |
| 153 | `PUT /api/notifications/{id:int}/read` | SV/QL | List bỏ tracking; truyền cancellation; giữ phạm vi người nhận. |
| 154 | `GET /api/deadlines/me` | QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 155 | `GET /api/kpis/leaderboard` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 156 | `GET /api/kpis/rules` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 157 | `GET /api/reports` | SV/QL | Danh sách dùng phân trang chung và mapper metadata chung. |
| 158 | `POST /api/reports` | QL | Dùng mapper chi tiết chung; các guard và workflow giữ nguyên. Còn phát hiện/giới hạn cần xem chi tiết. |
| 159 | `GET /api/reports/summary` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. |
| 160 | `POST /api/reports/upload` | QL | Dùng mapper chi tiết chung; các guard và workflow giữ nguyên. Còn phát hiện/giới hạn cần xem chi tiết. |
| 161 | `DELETE /api/reports/{id:int}` | QL | Giữ handler xóa, kiểm tra quyền và workflow hiện tại; xem giới hạn liên quan. |
| 162 | `GET /api/reports/{id:int}` | SV/QL | Chi tiết dùng mapper metadata chung; giữ che dữ liệu tài chính. |
| 163 | `PUT /api/reports/{id:int}` | QL | Dùng mapper chi tiết chung; các guard và workflow giữ nguyên. Còn phát hiện/giới hạn cần xem chi tiết. |
| 164 | `POST /api/reports/{id:int}/attachments` | QL | Dùng mapper chi tiết chung; các guard và workflow giữ nguyên. Còn phát hiện/giới hạn cần xem chi tiết. |
| 165 | `POST /api/reports/{id:int}/attachments/upload` | QL | Dùng mapper chi tiết chung; các guard và workflow giữ nguyên. Còn phát hiện/giới hạn cần xem chi tiết. |
| 166 | `GET /api/reports/{id:int}/attachments/{attachmentId:int}/download` | SV/QL | Đã rà soát; giữ handler và các kiểm tra nghiệp vụ hiện có. Còn phát hiện/giới hạn cần xem chi tiết. |
| 167 | `POST /api/reports/{id:int}/reject` | QL | Dùng mapper chi tiết chung; các guard và workflow giữ nguyên. Còn phát hiện/giới hạn cần xem chi tiết. |
| 168 | `POST /api/reports/{id:int}/review` | QL | Dùng mapper chi tiết chung; các guard và workflow giữ nguyên. Còn phát hiện/giới hạn cần xem chi tiết. |
| 169 | `POST /api/reports/{id:int}/submit` | QL | Dùng mapper chi tiết chung; các guard và workflow giữ nguyên. Còn phát hiện/giới hạn cần xem chi tiết. |
| 170 | `DELETE /api/reports/{reportId:int}/attachments/{attachmentId:int}` | QL | Giữ handler xóa, kiểm tra quyền và workflow hiện tại; xem giới hạn liên quan. |
| 171 | `DELETE /api/reports/{reportId:int}/file` | QL | Response dùng mapper chung; workflow ghi file giữ nguyên. Còn phát hiện/giới hạn cần xem chi tiết. |
| 172 | `GET /api/reports/{reportId:int}/file` | SV/QL | Sửa metadata/preview và kiểm tra đường dẫn; download bỏ tracking. Còn phát hiện/giới hạn cần xem chi tiết. |
| 173 | `POST /api/reports/{reportId:int}/file` | QL | Response dùng mapper chung; workflow ghi file giữ nguyên. Còn phát hiện/giới hạn cần xem chi tiết. |
| 174 | `GET /api/reports/{reportId:int}/file/download` | SV/QL | Sửa metadata/preview và kiểm tra đường dẫn; download bỏ tracking. Còn phát hiện/giới hạn cần xem chi tiết. |
| 175 | `GET /api/reports/{reportId:int}/file/preview` | SV/QL | Sửa metadata/preview và kiểm tra đường dẫn; download bỏ tracking. Còn phát hiện/giới hạn cần xem chi tiết. |
| 176 | `DELETE /api/reports/{reportId:int}/uploaded-file` | QL | Response dùng mapper chung; workflow ghi file giữ nguyên. Còn phát hiện/giới hạn cần xem chi tiết. |
| 177 | `GET /api/reports/{reportId:int}/uploaded-file` | SV/QL | Sửa metadata/preview và kiểm tra đường dẫn; download bỏ tracking. Còn phát hiện/giới hạn cần xem chi tiết. |
| 178 | `PUT /api/reports/{reportId:int}/uploaded-file` | QL | Response dùng mapper chung; workflow ghi file giữ nguyên. Còn phát hiện/giới hạn cần xem chi tiết. |
| 179 | `GET /api/reports/{reportId:int}/uploaded-file/download` | SV/QL | Sửa metadata/preview và kiểm tra đường dẫn; download bỏ tracking. Còn phát hiện/giới hạn cần xem chi tiết. |
| 180 | `GET /api/reports/{reportId:int}/uploaded-file/preview` | SV/QL | Sửa metadata/preview và kiểm tra đường dẫn; download bỏ tracking. Còn phát hiện/giới hạn cần xem chi tiết. |
