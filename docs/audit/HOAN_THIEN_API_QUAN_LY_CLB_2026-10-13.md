# Kết quả triển khai BE cho Quản lý CLB — 13/10/2026

## Phạm vi và cách đối chiếu

Triển khai theo phiếu đăng ký v1.4, cơ chế điểm FPTU Xperience và contract đã điều chỉnh, dựa trên bản kiểm kê `TONG_HOP_API_CHUA_HOAN_THANH_THEO_ACTOR_2026-10-12.md` tại commit `80818266`.

**24 operation chính của Quản lý CLB đã có triển khai.** Đối chiếu lại DTO để bàn giao FE ngày 13/10: **23 hoàn thành, API-033 còn thiếu read version cho manager**; PUT ảnh CLB có cùng điểm thiếu. `GET /api/clubs/{id}` trả ClubFullResponse cho manager chưa có version, dù write endpoints/public DTO đã có version. Cần bổ sung metadata đọc để FE lấy expectedVersion lần đầu; không tự sinh phiên bản. Xem [bảng trạng thái hiện hành](TRANG_THAI_API_BE_CHO_FE_2026-10-13.md) cho cả 74 operation và phần còn thiếu theo actor. Có thêm API dùng chung cho xác minh, minh chứng, tài khoản nội bộ, hồ sơ chính chủ, ảnh công khai và policy. Báo cáo này không khẳng định hoàn thành toàn bộ các actor.

Theo yêu cầu mới nhất, công việc nghiệm thu tập trung vào BE. Những thay đổi FE đã có được giữ lại; việc xây dựng các màn hình FE mới không thuộc phạm vi bàn giao này. Bản kiểm kê ngày 12/10 vẫn được giữ làm mốc lịch sử.

## 24 operation chính

| ID kiểm kê | Operation | Kết quả triển khai |
| --- | --- | --- |
| API-033 | `PATCH /api/clubs/{clubId}/public-profile` | Sửa từng trường công khai, category lưu trong DB, lịch có semester/ngày/khung 30 phút; kiểm tra owner hiện tại và expectedVersion; chặn sửa trạng thái, owner và ảnh qua route này. |
| API-034 | `POST /api/activities/{activityId}/sessions` | Tạo buổi đơn/lịch lặp theo giờ Việt Nam, ngày loại trừ, occurrence ID ổn định; dời cửa sổ điểm danh theo từng buổi; chống tạo lặp và kiểm tra version. |
| API-035 | `GET /api/activities/{activityId}/sessions` | Trả các buổi cùng version, semester, attendancePolicy và snapshot nguồn điểm. |
| API-036 | `PATCH /api/activities/{activityId}/sessions/{sessionId}` | Sửa buổi đúng quyền và version; bảo vệ buổi đã có lịch sử điểm danh. |
| API-037 | `POST /api/activities/{activityId}/sessions/{sessionId}/close` | Đóng buổi theo trạng thái hợp lệ; lưu lịch sử, version và lý do. |
| API-038 | `POST /api/activities/{activityId}/sessions/{sessionId}/cancel` | Hủy có lý do/audit/outbox; thu hồi attendance và đảo điểm bằng bút toán mới. |
| API-039 | `GET /api/activities/{activityId}/sessions/{sessionId}/attendance-challenge` | QR ký HMAC, đổi mỗi 30 giây, ràng buộc activity/session/version; trả serverTime và hết hạn; chỉ manager đúng CLB được phát. |
| API-040 | `GET /api/clubs/{clubId}/semester-plans/{semesterId}` | Đọc kế hoạch kỳ đúng scope, canonical semester code và version. |
| API-041 | `PUT /api/clubs/{clubId}/semester-plans/{semesterId}` | Một kế hoạch hiện hành cho mỗi CLB/kỳ; mục tiêu không âm; CAS/version, revision history và idempotency. |
| API-042 | `GET /api/clubs/{clubId}/experience-statistics?semesterId=...` | Tính toàn dữ liệu: hoạt động/buổi, người duy nhất, lần đầu toàn dự án, vai trò hiệu lực, net raw points từng trụ cột và tiến độ mục tiêu. |
| API-043 | `POST /api/clubs/{clubId}/experience-reports` | Chốt snapshot thống kê đã đối chiếu planVersion/ledgerRevision; trả 202 và job PDF lưu trong DB. |
| API-044 | `GET /api/clubs/{clubId}/experience-reports/{reportId}/download` | Tải PDF thực tế sau khi hoàn thành; private/no-store; kiểm tra quyền quản lý hiện tại, kể cả file đã tạo trước đó. |
| API-045 | `GET /api/activities` | Dữ liệu quản lý có publication/semester/version/capacity/cấu hình điểm và điểm danh; người không quản lý chỉ nhận dữ liệu cá nhân được phép. |
| API-046 | `GET /api/activities/{id}` | Chi tiết gồm cấu hình nguồn đã công bố; không lộ danh sách người khác cho thành viên thông thường. Các occurrence được lấy qua API-035. |
| API-047 | `POST /api/activities` | Tạo draft đúng CLB, lấy tên CLB từ authority; giữ cấu hình đăng ký, semester và điểm để công bố sau khi có policy hợp lệ. |
| API-048 | `PUT /api/activities/{id}` | UPDATE/PUBLISH/CLOSE có version và idempotency; PUBLISH yêu cầu policy đã duyệt và snapshot; bảo vệ dữ liệu đã có lịch sử. |
| API-049 | `DELETE /api/activities/{id}` | Hủy hoạt động mới bằng expectedVersion/reason/key; giữ lịch sử, hủy các buổi và phát sự kiện đảo điểm. |
| API-050 | `PATCH /api/activities/{id}/complete` | Hoàn thành khi hoạt động kết thúc và các buổi đã được xử lý; version, lý do, audit; không tự cộng điểm. |
| API-051 | `GET /api/clubs/{clubId}/members` | Hồ sơ hiện hành lấy từ Auth; applicationSnapshot được tách riêng. Thống kê không khả dụng trả available=false và số liệu null. |
| API-052 | `GET /api/clubs/{clubId}/members/{memberId}` | Hồ sơ hiện hành, hồ sơ gia nhập lịch sử và lịch sử tham gia phân trang; cùng quyền quản lý CLB. |
| API-053 | `PUT /api/clubs/{clubId}/members/{memberId}` | Giữ route tương thích và trả 403 `PERSONAL_PROFILE_SELF_ONLY` khi yêu cầu sửa hồ sơ cá nhân người khác. Tự sửa dùng canonical self profile. |
| API-054 | `GET /api/clubs/{clubId}/activities/{activityId}/attendance` | Trả dữ liệu theo session, verification/recognition/version và nguồn; kiểm tra đúng CLB/hoạt động. |
| API-055 | `PUT /api/clubs/{clubId}/activities/{activityId}/attendance/{memberId}` | Xác nhận theo buổi với version/lý do/minh chứng, capability attendance.verify và no-self; ghi outbox cho ledger. |
| API-056 | `PUT /api/clubs/{clubId}/activities/{activityId}/attendance` | Kiểm tra toàn bộ batch trước khi ghi, gồm no-self từng người; source/session/row versions và cùng giao dịch. |

## API xác minh dùng chung

Quyền sở hữu CLB không tự cấp quyền xác minh. Manager chỉ dùng các API sau khi có `VerificationAssignment` còn hiệu lực cho đúng source và capability.

| Operation | Nghiệp vụ |
| --- | --- |
| `GET /api/verifications/me/queue` | Queue contribution, upgrade và manual attendance theo assignment; hỗ trợ EXTERNAL/REAL_WORLD nếu được phân công; loại hồ sơ của chính reviewer. |
| `GET /api/activities/{activityId}/contributions` | Đọc danh sách của activity đúng CLB được phân công, kể cả khi danh sách chưa có dòng nào. |
| `POST /api/contributions/{id}/reviews` | APPROVE/REJECT/REQUEST_REVISION; version, reason, audit, immutable decision và ledger effect nguyên tử. |
| `POST /api/contributions/{id}/role-upgrade-requests/{requestId}/reviews` | Duyệt upgrade theo version; giữ policy/vector/tier/scale gốc; chỉ ghi phần điểm chênh lệch. |
| `POST /api/contributions/{id}/adjustments` | CORRECT/REVOKE đúng capability; append lịch sử; không sửa/xóa ledger cũ hoặc tăng lại điểm của nguồn đã hủy. |
| `POST /api/activities/{activityId}/sessions/{sessionId}/manual-attendance-requests/{requestId}/review` | Duyệt điểm danh thủ công bằng minh chứng; PENDING cho đến khi Activity xác nhận nguồn và Admin ghi ledger. |
| `GET /api/v1/student-affairs/declarations` | Giữ queue khai báo cũ, bổ sung giới hạn assignment/no-self cho nguồn canonical. |
| `POST /api/v1/student-affairs/declarations/{id}/review` | Adapter cho khai báo ngoài CLB/+1 mới, dùng chung contribution revision/decision/ledger. Khai báo CLB cũ chưa có nguồn xác thực bị chặn để tránh tạo điểm tùy ý. |

API hỗ trợ đã triển khai: upload/download minh chứng riêng tư; contribution submit/detail/revise và upgrade request; my contributions/ledger; manual request submit/list; internal register/login; GET/PATCH `/api/users/me/profile`; verification assignment; approved experience policy; public image upload intent/attachment binding; PUT ảnh CLB và cover activity; GET trạng thái job PDF.

Đăng ký/đăng nhập nội bộ dùng `POST /api/auth/internal/register` và `POST /api/auth/internal/login`. Đây là tên route đã chọn cho chức năng mà kiểm kê ban đầu đề xuất `/api/auth/register` và `/api/auth/login`; không tạo session store thứ hai. Register yêu cầu username, email, password 12–128 ký tự và displayName; role được server gán. API profile canonical vẫn là `/api/users/me/profile`.

## Quy tắc dữ liệu và tính điểm

- Một công sức CLB được định danh bằng user + activity + session. Loại minh chứng/contributionKind không tạo thêm công sức. QR, manual và contribution review cùng đi vào nguồn ledger này.
- QR hợp lệ chỉ ghi nhận Participant theo blueprint công bố. Nâng vai trò/kết quả phải có quyết định riêng và chỉ cộng delta.
- Điểm chưa xác minh không vào ledger. Outbox, inbox eventId, unique source/revision/effect và idempotency cùng chống trùng khi replay/race/restart.
- Ledger, allocation, revision, decision và approved policy được bảo vệ append-only qua cả SaveChanges đồng bộ/bất đồng bộ. Correction/reversal giữ lịch sử gốc.
- Policy dùng đúng môi trường, kỳ và snapshot đã duyệt P01–P06; không tự duyệt tham số dự án. Policy mới không thay snapshot của đóng góp cũ.
- Mục tiêu bằng 0 trả ratio=null. Lần đầu là lần đầu trong dữ liệu dự án, xét cả các nguồn ngoài CLB/+1, không tuyên bố là dữ liệu toàn trường.
- Attendance/declaration/XP cũ vẫn được giữ. Attendance legacy chưa xác minh không tự thành nguồn điểm. Bản khai báo được adapter mới tạo có một contribution canonical; các projection mirror không được cộng lại trong radar hoặc thống kê.
- Recognition qua nhiều service có độ trễ outbox/consumer. API trả PENDING/RECORDED/FAILED thay vì báo đã có điểm trước khi service nhận xác nhận. Báo cáo chốt snapshot riêng và không thay đổi sau correction về sau.
- Mất quyền/assignment bị thu hồi thì replay hoặc tải lại phải kiểm tra quyền hiện tại. Manager không sửa hồ sơ cá nhân của người khác.

## Kiểm thử

**Kết quả chạy cuối: 724/724 kiểm thử BE đạt, 0 thất bại, 0 bỏ qua.** Trong đó AdminService.IntegrationTests: 85, Backend.StabilizationTests: 516, ClubReportHub.Tests: 123. Nhóm riêng Quản lý CLB có 51 trường hợp, gồm 16 trường hợp định tuyến Gateway.

Build `Release --no-restore -warnaserror`: 0 warning, 0 error. Năm bước `dotnet format --no-restore --verify-no-changes` của CI cho Admin/Auth/Club và hai test project đều đạt; bước Shared theo danh sách include của CI cũng đạt. `docker compose config --quiet` đạt với biến cấu hình kiểm thử. `git diff --check` không có lỗi whitespace.

GitNexus đã được cập nhật và phân tích lại cả file sửa lẫn file code mới, bằng Git index riêng phục vụ review, không thay staging hiện tại: 122 file, 929 symbol và 291 flow bị ảnh hưởng. Mức phạm vi ảnh hưởng là CRITICAL vì có thay đổi xuyên service và thư viện dùng chung. Các luồng transaction/idempotency, hủy/đảo điểm, review/assignment, radar, quyền tài khoản và member statistics đã được kiểm tra cùng bộ test hồi quy. Graph còn giới hạn ở một số lời gọi qua interface/DI và receiver chưa suy ra kiểu; không coi số caller thiếu là bằng chứng an toàn.

Các nhóm kiểm thử có trong source:

| Nhóm | Kịch bản chính |
| --- | --- |
| ClubManager profile/plan/membership | Exact owner, revoked owner, trường bị cấm, CAS, canonical semester, lịch sử, replay, xóa thành viên, bảo vệ owner. |
| Attendance/session | QR sai/hết hạn/khác scope, geofence, bản ghi trùng, hủy sau check-in, lịch lặp và cửa sổ từng buổi theo giờ Việt Nam. |
| Experience verification | Pending không có điểm, assignment/no-self, replay, upgrade delta, private evidence và thu hồi quyền tải, external declaration/radar không đếm mirror. |
| SQL Server integrity | Migration cả 5 DB, nâng dữ liệu legacy, unique source sau reload, last-seat race, append-only và rollback sau SaveChanges của handler. |
| Statistics/PDF | 125 người, first-time toàn dự án, target 0, ledgerRevision đổi khi dữ liệu căn cứ đổi; PDF thực tế, snapshot bất biến, job hết lease được phục hồi và current owner download. |
| Gateway | ASP.NET/YARP chọn đúng cluster cho 16 đường dẫn, gồm các route mới nằm trong prefix cũ; transport được thay bằng handler kiểm thử. |
| Hồi quy | Toàn bộ các test project của BE, gồm tài khoản, phân quyền, Redis retention, migrations và tính điểm hiện có. |

SQL tests tạo các database tên ngẫu nhiên thuộc phạm vi kiểm thử và xóa đúng database đó sau khi chạy; không dùng database nghiệp vụ làm dữ liệu test. Policy/đóng góp giả chỉ tồn tại trong fixture kiểm thử.

Chạy lại trên Windows với LocalDB instance kiểm thử đã tạo:

```powershell
$env:ADMIN_SERVICE_TEST_CONNECTION_STRING = 'Server=(localdb)\ClubManagerApiTests;Database=master;Integrated Security=True;TrustServerCertificate=True'
$env:FINANCE_SERVICE_TEST_CONNECTION_STRING = $env:ADMIN_SERVICE_TEST_CONNECTION_STRING
dotnet build ClubReportHub.sln --configuration Release --no-restore -warnaserror
dotnet test ClubReportHub.sln --configuration Release --no-build
```

Nếu chưa có instance `ClubManagerApiTests`, tạo/chạy instance LocalDB đó trước; có thể dùng SQL Server khác bằng cách thay hai connection string trên. CI dùng SQL Server riêng thay cho LocalDB. Có thể chạy riêng các kịch bản mới bằng `dotnet test tests/Backend.StabilizationTests/Backend.StabilizationTests.csproj --filter "FullyQualifiedName~ClubManager_"`.

## Cấu hình để chạy thật

1. Chạy SQL Server và Redis; áp dụng 5 migration dưới đây. Các service có cấu hình Development trỏ đến port local trong launchSettings. Nếu chọn port khác, override `Services__...__BaseUrl` tương ứng.
2. Cấu hình cùng `Security__InternalWorkflowToken` mạnh cho Auth, Club, Activity, Admin và Export; Activity có `Attendance__SigningKey` riêng, tối thiểu 32 byte. Không commit secret. Compose nhận `INTERNAL_WORKFLOW_TOKEN` và `ATTENDANCE_SIGNING_KEY`; workflow deployment đã chuyển tiếp các secret này.
3. Tạo semester thực tế, benchmark P01–P06 và thực hiện approval có căn cứ bằng actor được quyền. Chưa được duyệt thì công bố/ghi điểm bị chặn; fixture kiểm thử không thay cho approval dự án.
4. Phân công owner CLB và assignment xác minh đúng actor/source/capability. Cấp owner không đồng nghĩa cấp attendance.verify/contribution.review/experience.adjust.
5. Upload ảnh công khai cần R2 Endpoint/Bucket/AccessKeyId/SecretAccessKey/PublicOrigin thật và CORS bucket cho origin FE khi sử dụng. Chưa có cấu hình thì media trả lỗi cấu hình có kiểm soát. Trong đợt này chưa upload vào bucket R2 thật hoặc triển khai lên máy chủ.

| Service | Migration |
| --- | --- |
| Club | `20261012162104_ClubManagerProfilesAndSemesterPlans` |
| Activity | `20261012162110_ClubManagerActivitySessions` |
| Admin | `20261012162116_VerifiedExperienceLedger` |
| Auth | `20261012162122_InternalAccountsProfilesAndMedia` |
| Export | `20261012162127_ClubExperiencePdfReports` |

Các chức năng đã đánh dấu BỎ QUA vẫn giữ nguyên phạm vi: tài chính/ví/đổi quà/cosmetic, roster trường/FAP/OJT/SSO bắt buộc, timetable, full specialist consoles, leaderboard cạnh tranh, ML/anomaly automation. Không dùng chúng làm điều kiện để hoàn thành actor Quản lý CLB.
