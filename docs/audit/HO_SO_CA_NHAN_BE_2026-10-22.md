# Bàn giao BE: hồ sơ cá nhân mở rộng

Chỉ sửa BE, đối chiếu canonical self-profile và self-profile-extensions của FE tại `3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42`. Tái sử dụng GET/PATCH `/api/users/me/profile`; không tạo writer cho user bất kỳ.

## Phần đã triển khai

- Thêm `profile.headline` (trim, tối đa 160), `personal.dateOfBirth` (ngày thực `yyyy-MM-dd`, không ở tương lai theo ngày Việt Nam) và `personal.address` (trim, tối đa 300). Headline không nhận ký tự điều khiển; address cho xuống dòng địa chỉ, không nhận các control khác.
- Bỏ field giữ nguyên; null xóa các field mới. Headline trống/null đọc lại là `""`; ngày sinh/địa chỉ đã xóa đọc lại null. Ngày sinh/địa chỉ là thông tin tự khai, không phải học vụ đã được xác nhận.
- GET self trả editableFields gồm ba field mới, cùng năm field cũ. Cần GUID expectedVersion của profile khi PATCH; version cũ 409, request không có field chỉnh sửa 400. Idempotency-Key vẫn tùy chọn như API cũ; nếu dùng, gửi lại cùng payload/key trả receipt và luôn kiểm tra account hiện tại.
- PATCH giới hạn 32 KiB, JSON trùng tên field kể cả khác hoa/thường bị từ chối; field lạ không thành dữ liệu được phép. Identity/roles/email/campus/studentCode/major/year không cho tự sửa. Tài khoản inactive/locked không đọc/ghi self profile qua endpoint này.
- Ba field mới chỉ xuất hiện ở self projection. Internal user-profile projection cho service/manager giữ subset cũ; không có headline, ngày sinh, địa chỉ hoặc dữ liệu học vụ. Không thêm danh bạ công khai.

Ví dụ PATCH (lấy version thật từ GET):

```json
{"expectedVersion":"<profile-guid>","profile":{"headline":"Điều phối CLB"},"personal":{"dateOfBirth":"2004-02-29","address":"Địa chỉ tự khai"}}
```

## Học vụ chỉ đọc từ roster

GET/PATCH self trả thêm `academic` độc lập với version profile:

```json
{"availability":"AVAILABLE","studentCode":"SE100","source":"IMPORT","semesterKey":"<semester-guid>","semesterId":"FALL2026","rosterRevisionId":"<revision-guid>","sourceVersion":"<roster-guid>","major":null,"year":null,"majorAvailability":"NO_AUTHORITATIVE_SOURCE","yearAvailability":"NO_AUTHORITATIVE_SOURCE"}
```

Ví dụ thể hiện schema, không tạo dữ liệu mẫu. Mã sinh viên phải đến từ snapshot roster thực, đối chiếu userId **và email trường hiện tại**, đúng campus/account/actor; không suy từ email/MSSV để tạo ngành, năm học hoặc campus. `academic` không nằm trong editableFields.

- Dùng đúng một Semester active trong Admin. Không có hoặc nhiều hơn một kỳ trả SEMESTER_UNCONFIGURED/SEMESTER_AMBIGUOUS, không tự đoán theo tháng/code.
- Roster chưa có: ROSTER_UNAVAILABLE; roster có revision nhưng thiếu user: NOT_IN_ROSTER. Email/campus đã đổi sau import: IDENTITY_CHANGED, không trả mã cũ.
- Roster row inactive: ROSTER_INACTIVE; thông tin mã và nguồn vẫn được gắn nhãn. Nonstudent: NOT_APPLICABLE. Không có school identity chuẩn: NO_CANONICAL_SCHOOL_IDENTITY.
- Admin/roster lỗi: AUTHORITY_UNAVAILABLE, các trường nguồn không có dữ liệu; phần text self đã lưu vẫn đọc được. Không cấu hình client trong host test/host riêng: UNCONFIGURED.
- Ngành/năm học chưa có cột/nguồn học vụ đã chốt: luôn null và NO_AUTHORITATIVE_SOURCE; không nhận client tự khai làm dữ liệu chính thức.

Nội bộ thêm `GET /api/internal/academic-profiles/me` ở Admin: cần bearer và internal workflow token, user lấy từ principal. Gateway không mở route này cho browser. Auth kiểm tra userId của envelope trước khi gắn vào self response. Projection học vụ được đọc **sau** transaction PATCH/receipt, tránh gọi ngược vào Auth khi transaction đang giữ khóa profile và tránh cache mã học vụ trong receipt cũ.

## Ảnh đại diện và ảnh bìa cơ bản

Đã thêm `profile.avatarUrl` và `profile.coverImageUrl` vào GET/PATCH self và editableFields. Bỏ field giữ nguyên; null xóa đúng ảnh. Hai field này cũng không đi vào projection nội bộ cho quản lý/thành viên khác.

1. POST `/api/media/upload-intents` với kind `student-avatar` hoặc `student-cover`, ownerId là ID tài khoản hiện tại dạng chuỗi, contentType PNG/JPEG/WebP và sizeBytes >0, <=5 MiB. Server đối chiếu principal và account active/unlocked; không dùng ownerId client để cấp quyền.
2. PUT bytes lên uploadUrl do server ký; không gửi app bearer token cho R2. Giữ publicUrl trong receipt intent.
3. PATCH canonical self profile với expectedVersion và `profile.avatarUrl` hoặc `profile.coverImageUrl` là publicUrl vừa được cấp. Server tra đúng actor/kind/owner, kiểm tra expiry, object bytes/MIME/size/dimensions, rồi gắn URL bản verified bất biến. GET sau lưu trả URL verified, khác staging URL.

Không nhận URL tùy ý, blob/data URL, ảnh của người khác hoặc lấy avatar intent gắn sang cover. Storage lỗi/timeout trả 503, dữ liệu sai 400, nguồn không khớp/hết hạn hoặc version cũ 409; transaction thất bại không lưu profile dở. Retry receipt không verify/attach hai lần và vẫn kiểm tra account hiện tại. Các kind CLB/hoạt động vẫn kiểm tra quyền CLB hiện hành; mở self media không cấp quyền chỉnh ảnh CLB.

Auth migration bổ sung `20261022161555_SelfProfileOwnedImages`: hai cột URL nullable, giữ các trường text/private cũ. Nhóm media có 7 test SQL bổ sung (cùng 12 test text/private là 19); storage fixture dùng decoder thật để kiểm tra ảnh và mô phỏng immutable copy, không gọi R2 live. Có regression đỏ trước sửa cho timeout. R2/CORS/signing origin/delivery và cleanup object bỏ dở chưa được nghiệm thu live; không đánh Done toàn bộ media/resources vì luồng ảnh cơ bản đã có.

## Migration và kiểm thử

Auth migration `20261022152518_SelfProfilePrivateExtensions` thêm ba cột nullable vào UserProfiles, giữ dữ liệu text/phone/version cũ. SQL Server test khởi tạo schema trước và hồ sơ cũ bằng cột cũ, nâng cấp rồi xác nhận đọc/ghi field mới. Không áp migration lên database dùng chung.

Kiểm tra cuối sau self media: **882 passed, 0 failed, 0 skipped** toàn solution (123 ClubReportHub, 85 Admin integration, 674 Backend stabilization). Nhóm liên quan gồm 12 test mở rộng hồ sơ, 15 test roster/học vụ và 7 test self media. Build Release -warnaserror đạt 0 warning/error; format CI đầy đủ và EF model/migration của Activity/Admin/Auth/Club đều đạt; có TRX/XPlat coverage. Có regression đỏ trước sửa cho request field mới, giới hạn và atomic validation; SQL upgrade bảo toàn hồ sơ cũ.

## Phần chưa đánh hoàn thành

- Cosmetics/layout, application-logo/club-shape và activity resources còn scope riêng; ảnh đại diện/ảnh bìa cơ bản đã có flow ở trên.
- Ngành/năm học chính thức cần nguồn học vụ/import được chốt. Không biến giá trị localStorage thành dữ liệu chính thức.
- FE vẫn cần chuyển consumer và xử lý trạng thái availability; không sửa UI trong đợt này. Không tự nhập dữ liệu local cũ vào server.
- Governance đang chờ quy tắc chuyển thủ quỹ legacy sang kỳ; tính điểm vẫn tạm hoãn theo yêu cầu chủ dự án.
