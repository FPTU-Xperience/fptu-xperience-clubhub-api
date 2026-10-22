# Bàn giao BE: hoạt động, phân công và roster học kỳ

Ngày 22/10/2026. Source BE, nhánh `feat/backend-contract-completion`. FE giữ nguyên. Đối chiếu contract FE tại `3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42`.

## Phạm vi đã chốt với chủ dự án

- Tiếp tục triển khai BE; không sửa FE.
- **Tạm hoãn tính/cộng/thu hồi điểm mới** vì chưa chốt nghiệp vụ. Không sửa công thức đang chạy, không tạo ví mới và không tự quy đổi XP/credit.
- Roster đối chiếu bằng **email trường của tài khoản Auth đã tồn tại**. Import không tự tạo tài khoản. Mã sinh viên là dữ liệu roster do CTSV cung cấp; không dùng tiền tố mã sinh viên để suy ra campus.

## Trạng thái API

15 operation có consumer nhưng thiếu route tại bảng bàn giao trước đã có handler. Đây là số route, không phải khẳng định toàn bộ mọi đề xuất trong contract đã hoàn thành. Kiểm tra live/deploy và tích hợp FE có trạng thái riêng.

| Actor | Route còn thiếu trong nhóm 15 | Ghi chú |
| --- | ---: | --- |
| Quản lý CLB | 0 | Hồ sơ, công bố, phân công, tuyển người, eligible-members |
| Sinh viên | 0 | Đọc staffing/tuyển người và tự nhận vị trí |
| Người xác minh | 0 | Đọc staffing, xác minh hoàn thành; không cộng điểm |
| CTSV/Admin | 0 | Đọc hồ sơ; có thêm quyết định duyệt và quản trị roster |

Các actor dùng chung một số operation. Không cộng các hàng thành tổng API riêng biệt.

| Method | Route | Trạng thái BE |
| --- | --- | --- |
| GET | `/api/clubs/{clubId}/eligible-members` | Có roster thật, account hiện tại, loại membership/lời mời/manager đang hiện hành, phân trang |
| POST / GET | `/api/activity-applications` | Tạo draft / danh sách theo CLB có quyền |
| GET / PATCH | `/api/activity-applications/{id}` | Đọc / sửa DRAFT hoặc REVISION_REQUESTED bằng version số nguyên |
| POST | `/api/activity-applications/{id}/submit` | Kiểm tra hồ sơ đầy đủ, học kỳ, thời gian và nguồn action trước gửi duyệt |
| POST | `/api/activity-applications/{id}/review` | CTSV đúng campus hoặc Admin; độc lập với người tạo; APPROVED/REJECTED/REVISION_REQUESTED |
| GET | `/api/activity-applications/{id}/history` | Lịch sử nội dung và quyết định không ghi đè |
| PATCH | `/api/activities/{id}/publication` | Bool `isPublished`, version số nguyên riêng; không dùng CLOSE để ẩn |
| GET | `/api/activities/{id}/staffing` | Actions, phân công, tuyển dụng, capabilities theo principal; không lộ danh sách ứng viên trường |
| POST | `/api/activities/{id}/assignments` | Thành viên CLB hợp lệ, action đã duyệt, giới hạn số người |
| POST | `/api/activities/{id}/assignments/{assignmentId}/cancel` | Đánh dấu REMOVED; giữ lịch sử và kết quả đã xác minh |
| POST | `/api/activities/{id}/assignments/{assignmentId}/completions` | Chính người được phân công nộp minh chứng WORK_REVIEW; chưa phát sinh điểm |
| POST | `/api/activities/{id}/assignments/{assignmentId}/attendance-claims` | Tự nộp yêu cầu xác nhận tham dự cho action ATTENDANCE; cần người có attendance.verify độc lập xác minh; chưa phát sinh điểm |
| POST | `/api/activities/{id}/action-completions/{completionId}/verify` | Capability xác minh hiện tại, không tự xác minh, kiểm tra account/membership của người nộp |
| POST / PATCH | `/api/activities/{id}/recruitment` | Tạo / sửa cùng một đợt tuyển, không phá vị trí đã có người |
| PATCH | `/api/activities/{id}/recruitment/status` | OPEN/CLOSED; chỉ mở khi hoạt động được công bố và còn hiệu lực |
| POST | `/api/activities/{id}/recruitment/positions/{positionId}/join` | User lấy từ principal; giữ chỗ cuối và ngăn nhận trùng bằng transaction/version/unique index |
| GET | `/api/clubs/{clubId}/activity-recruitments` | Cùng ID đợt tuyển với staffing; phân trang, status/from/to, visibility và campus |
| POST | `/api/v1/semesters/{identifier}/roster-imports/preview` | Preview CSV/JSON, không thay roster đang dùng |
| POST | `/api/v1/semesters/{identifier}/roster-imports/{importId}/commit` | Commit snapshot đã xem; version, owner/campus, expiry và identity kiểm tra lại |
| GET | `/api/v1/semesters/{identifier}/student-roster` | CTSV đúng campus/Admin; manager không được dùng như danh bạ trường |

Thêm ba route **nội bộ**, không cấp cho browser: Auth `POST /api/internal/semester-roster/accounts/resolve`; Admin `POST /api/internal/semester-roster/lookup` và `GET /api/internal/semester-roster/summaries`. Cần internal workflow token. Gateway không mở các route này cho FE.

## Điểm đang tạm hoãn: lưu ý cho người làm FE

Hồ sơ trả `expectedRawXp:null`, `scoringStatus:"DEFERRED_BY_PRODUCT_OWNER"`, `canAwardPoints:false`. Các giá trị basePoints trong hồ sơ chỉ là đề xuất được lưu lại, không phải chính sách tính điểm được duyệt. Giới hạn độ dài/số lượng là giới hạn kỹ thuật của request, không phải mức thưởng được phê duyệt.

**FE hiện kiểm tra expectedRawXp phải là số; phần adapter này chưa tương thích khi bỏ tính điểm.** FE cần nhận trạng thái chờ chính sách và không hiển thị/null thành điểm 0. BE không trả số giả để vượt validation. Không đánh toàn bộ action contract hoàn thành chỉ vì các route vận hành đã có.

APPROVED chỉ tạo đúng một hoạt động chưa công bố và action rules bất biến. Xác minh completion chỉ đổi trạng thái thành VERIFIED, giữ người/thời gian/version; không ghi raw XP, CLUB_CREDIT, ngân sách hoặc hiệu ứng ledger. API lifecycle cũ không được chỉnh/publish vòng qua hồ sơ action đã duyệt. Legacy attendance/contribution vẫn dùng cơ chế cũ; không dùng nó để tự cấp điểm cho action mới.

## Hợp đồng hồ sơ và phân công

ID hồ sơ/action là integer dương; version hồ sơ/staffing/publication là integer dương. API legacy giữ GUID version. ID recruitment/position/assignment/completion là GUID. Lấy version hiện tại từ GET rồi gửi `expectedVersion` đúng resource. `publicationVersion` khác version staffing; mọi thay đổi publication cũng tăng version staffing.

GET `/api/activities/{id}` và danh sách `/api/activities` giữ `version` GUID cũ; với hoạt động từ hồ sơ mới, bổ sung `isPublished`, `publicationVersion`, `sourceApplicationId`, `publicationCapabilities.canManage` và bỏ field `publicationStatus`. Cách này giúp adapter FE chọn PATCH publication bool thay vì nhánh PUBLISH/CLOSE legacy. Hoạt động legacy vẫn giữ response cũ. Khả năng công bố kiểm tra lại học kỳ active và thời gian nguồn; không chỉ dựa trên quyền ở thời điểm duyệt.

`completions` chỉ nhận WORK_REVIEW, còn `attendance-claims` chỉ nhận ATTENDANCE và luôn có minh chứng. Đây là xác minh thủ công độc lập, không phải QR check-in; không tạo ActivityAttendance hoặc thưởng qua luồng legacy. Người chỉ có contribution.review không được duyệt attendance.

Các mutation hồ sơ/publication/staffing cần `Idempotency-Key` (ASCII 1–100 ký tự). Gửi lại cùng method/path/body/key trả receipt đã commit; đổi body cùng key trả 409. Quyền hiện tại được kiểm tra trước replay. Sửa draft gửi đủ payload theo applicationPayload; trường ngoài DTO/JSON trùng tên bị từ chối. Payload tối đa 256 KiB, tối đa 100 actions/positions.

Chỉ quản lý hiện tại của CLB được ghi; CTSV/Admin đọc và xét duyệt theo campus, không tự duyệt hồ sơ do mình tạo. Public viewer chỉ nhận phân công của chính mình, không nhận eligibleAssignees hoặc minh chứng người khác. Verifier chỉ nhận minh chứng trong capability tương ứng; HOST/STAFF không cấp quyền quản lý, tài chính hoặc xác minh.

Không ẩn hoạt động đã có đăng ký hay phân công đang hiệu lực; trả 409 PUBLICATION_IN_USE. Hủy phân công vẫn giữ completion và lịch sử; nhận lại không xóa giới hạn occurrence. Vị trí đã có người không được xóa/đổi loại hoặc giảm sức chứa xuống dưới số người đã nhận. Thay scope tuyển dụng khi có phân công đang hiệu lực bị từ chối.

## Roster và lời mời

CSV UTF-8 có exact headers `email,studentCode,campusCode,status`, hoặc thêm `userId` cuối. `status` là ACTIVE/INACTIVE; campus dùng HAN/HCM/DAN/CAN/QNH. Tối đa 5 MiB request và 10.000 dòng. JSON tương đương:

```json
{"schemaVersion":1,"rows":[{"email":"student@fpt.edu.vn","studentCode":"SE000000","campusCode":"HAN","status":"ACTIVE"}],"expectedRosterVersion":"00000000-0000-0000-0000-000000000000","mode":"replace"}
```

Đây là ví dụ schema, không tạo tài khoản mẫu. Account phải tồn tại, là actor Sinh viên/Quản lý CLB hợp lệ, có school email, đúng campus; ACTIVE còn yêu cầu active/unlocked. `Roster:SchoolEmailDomains` ở Auth cấu hình domain trường, mặc định `fpt.edu.vn`; không dùng danh sách Google sign-in có gmail làm school roster.

Preview trả importId/baseVersion/errors/canCommit, tồn tại một giờ. Commit cần `expectedRosterVersion` và Idempotency-Key; không cho người khác/campus khác commit preview. Version ban đầu là Guid.Empty. Dòng lỗi, email/code trùng, userId/email/campus không khớp, account không tồn tại hoặc đổi sau preview đều không thay roster thật.

Thiếu roster trả ROSTER_UNAVAILABLE, không thay bằng toàn bộ account active. Roster có revision rỗng là dữ liệu hợp lệ. Khi khởi tạo lười kỳ chưa có roster, chỉ kế thừa revision của kỳ có ngày bắt đầu trước gần nhất trong cùng campus; lưu source revision, source CARRY_FORWARD và actor hệ thống 0. Không đoán học kỳ từ code, không reset điểm hoặc tự cấp membership/office. Import rỗng rõ ràng ghi đè dữ liệu kế thừa và không bị kế thừa lại.

Eligible search yêu cầu search 2–100 ký tự, pageSize 1–50. Kết quả chỉ gồm userId/fullName/studentCode/flags, không email/phone. Roster revision và tài khoản được tra lại lúc mời, đồng ý và duyệt. Có thể gửi `expectedRosterVersion` từ search khi mời để từ chối nguồn đã đổi. Với CLB GLOBAL, manager dùng roster campus của mình; không mở danh bạ toàn trường.

POST `/api/clubs/{clubId}/members` nay bắt buộc `semesterId`; giữ fullName như field tương thích nhưng tên lưu lấy từ Auth. Có thể dùng Idempotency-Key; không tự approve. Lưu riêng InvitedRosterRevisionId/InvitedRosterVersion lúc mời và RosterRevisionId/RosterVersion lần xác nhận gần nhất. Lời mời nguồn INVITATION cũ thiếu semester/campus cần đối soát, trả INVITATION_REQUIRES_RECONCILIATION; không tự đoán. UNKNOWN vẫn giữ UNKNOWN. Consent vẫn Pending đến khi quản lý duyệt.

Lời mời và notification outbox cho đúng người nhận commit cùng transaction. Import có notification outbox cho người import; không có email/push hay kiểm chứng delivery live trong lượt này.

## Migration và kiểm thử

Migration mới trong lượt này:

- ActivityService: `20261022090451_ActivityActionWorkflowWithoutScoring`.
- AdminService: `20261022094258_SemesterStudentRosterEmailIdentity`.
- ClubService: `20261022094304_InvitationRosterValidation` và `20261022100406_InvitationRosterAuditContext`.

Lịch sử/revision/action rule đã duyệt được bảo vệ append-only qua SaveChanges sync/async và các overload. Migration SQL Server đã được kiểm tra với dữ liệu ở schema trước; các test dùng database tạm có prefix riêng. Không áp migration lên môi trường dùng chung trong lượt làm việc.

Kiểm thử cuối trên source hiện tại: **831 passed, 0 failed, 0 skipped**; gồm ClubReportHub.Tests 123, AdminService.IntegrationTests 85 và Backend.StabilizationTests 623. Build Release `-warnaserror` đạt 0 warning/0 error; format whitespace verify cho toàn bộ file C# thay đổi và `git diff --check` đạt. Format đầy đủ theo các project CI (Admin/Auth/Club và hai test project) cùng lệnh EF has-pending-model-changes của Activity/Admin/Club đều đạt. Có TRX và XPlat coverage. Các test SQL nhận `ADMIN_SERVICE_TEST_CONNECTION_STRING` để chạy được cả CI/Linux, chỉ dùng LocalDB làm fallback trên máy phát triển.

Có test SQL Server hai người nhận ghế cuối và hai import thay cùng roster; transaction thua trả 409, không để bản ghi dở. Có regression đỏ trước sửa cho append-only, publication metadata, optional approval reason, manual attendance source và race SQL. Tests kiểm tra EF model/migration khớp và nâng cấp schema có dữ liệu cũ; không áp migration lên database dùng chung.

## Chưa được đánh hoàn thành

- Tính điểm/estimates/final-report award/budget settlement, +1/ERI/reset/rollover và wallet/credit/redemption: **tạm hoãn theo yêu cầu chủ dự án**, không xóa.
- Governance đa nhóm/quyền xuyên service và các mở rộng profile/media/resources: vẫn là backlog của kế hoạch tổng thể. Calendar/timetable đã được bổ sung ở [bàn giao lịch học](HOAN_THIEN_LICH_HOC_BE_2026-10-22.md); FE/live có trạng thái riêng.
- Optional directory/pins/custom forms/physical fulfillment/contest/office succession: giữ nhãn bỏ qua/cần đặc tả theo phạm vi cũ.
- Tích hợp FE, deploy, áp migration shared và delivery/storage live: chưa nghiệm thu.

Definition of Done của toàn bộ kế hoạch contract **chưa đạt**. Bảng route trên chỉ xác nhận phạm vi vận hành; trạng thái điểm bị hoãn và các nhóm backlog còn lại phải luôn hiển thị riêng.
