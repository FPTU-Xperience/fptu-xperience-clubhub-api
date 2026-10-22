# Bàn giao BE: lịch học cá nhân và lịch học kỳ

Đối chiếu `study-schedule-api.md` của FE tại `3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42`, bao gồm phần reconciliation yêu cầu liên kết Semester và version. Chỉ sửa BE. Không đọc/import localStorage hoặc tự chuyển dữ liệu mẫu từ FE vào server.

## API đã có

| Method | Route | Quyền và kết quả |
| --- | --- | --- |
| GET | `/api/academic-calendar/{year}/{term}` | Account hiện tại active/unlocked; lịch của campus hiện tại |
| PUT | `/api/academic-calendar/{year}/{term}` | CTSV cùng campus hoặc Admin; lưu lịch chung và revision bất biến |
| GET | `/api/users/me/study-schedule/{year}/{term}/{block}` | Lịch riêng của principal; toàn bộ tuần của block, đã sắp xếp |
| PUT | `/api/users/me/study-schedule/{year}/{term}/{block}/{week}/{day}/{slotId}` | Tạo/thay đúng một ô; không đổi tuần khác |
| DELETE | Cùng route một ô | Xóa riêng ô đó; 204 nếu chưa có ô, không xóa period/history calendar |
| POST | `/api/users/me/study-schedule/{year}/{term}/{block}/bulk-import` | Weekly template, skip/replace; transaction toàn block |

Thêm `GET /api/internal/academic-calendar/{year}/{term}` chỉ cho service: cần bearer hợp lệ và internal token. Gateway không mở prefix này. Auth dùng projection nội bộ của Admin, không tạo catalog học kỳ thứ hai.

Tất cả mutation cần `Idempotency-Key` 1–100 ký tự ASCII. Khóa gửi lại với cùng request trả receipt; đổi request cùng key trả 409. Account, campus và nguồn học kỳ được kiểm tra trước replay. Không nhận userId từ body để chọn người sở hữu.

## Nguồn học kỳ và campus

Admin là nguồn Semester GUID/code/ngày/active. Cấu hình alias tường minh giống workspace catalog:

```text
Workspace:SemesterAliases:<Semester.Id>:Year = 2026
Workspace:SemesterAliases:<Semester.Id>:Term = FA
```

Không suy ra term/năm từ code, tháng hiện tại hoặc AcademicYear. Không có alias: 404 SEMESTER_UNCONFIGURED. Nhiều Semester cùng alias: 409 SEMESTER_ALIAS_AMBIGUOUS. Kỳ đã biết nhưng chưa cấu hình block trả ba ngày `""`, có version thật; lịch block-local vẫn nhập được nếu kỳ active và ngày nguồn hợp lệ.

Mỗi cấu hình lịch gắn với Semester.Id và campus. GET dùng campus của account Auth hiện tại. Query `campusCode` chỉ cho chọn campus khác khi là Admin; CTSV/sinh viên không vượt campus của mình. Admin có account GLOBAL phải chọn một campus cụ thể; có thể dùng `?campusCode=HAN` ở lịch chung và các API lịch cá nhân. Query này không đổi chủ sở hữu lịch cá nhân.

Ngày là chuỗi local calendar `yyyy-MM-dd`, không chuyển múi giờ. termStartDate phải bằng ngày bắt đầu Semester chính thức. Hai block bắt đầu thứ Hai, termStartDate <= block10StartDate, block3StartDate >= block10StartDate + 70 ngày; ba tuần của block 3W phải kết thúc trong học kỳ. 3W dùng tuần 1–3; 10W dùng tuần 1–10. Slot 1–5 cho 10W, slot 1–7 cho 3W.

## Version và request

GET lịch chung trả các field cũ cùng `semesterKey`, `semesterId`, `campusCode`, `version`, `isActive`, `canWrite`, `availability`. Version là GUID opaque tính từ nguồn Semester và cấu hình hiện hành; lấy nguyên từ GET, kể cả cấu hình đang trống. PUT:

```json
{"termStartDate":"2026-08-30","block10StartDate":"2026-08-31","block3StartDate":"2026-11-09","expectedVersion":"<calendar-version-from-GET>"}
```

Ví dụ chỉ hợp lệ khi ngày Semester và alias thật khớp. Không seed học kỳ từ ví dụ này.

GET lịch cá nhân trả thêm `version` của block và `calendarVersion` nguồn. `version` là Guid.Empty khi chưa có block, khác với version lịch chung. Thêm hai field vào body PUT ô hoặc bulk import:

```json
{"subject":"SWP391","room":"AL-R101","expectedVersion":"<block-version>","expectedCalendarVersion":"<calendar-version>"}
```

Bulk giữ cấu trúc `mode:"skip"|"replace"`, `entries:[{day,slotId,subject,room}]` và hai expected-version trên. Template không có week; mỗi hàng áp dụng cho mọi tuần của block. Count imported/skipped/replaced tính theo ô tuần, không tính theo số hàng template. Response có toàn bộ entries và version block mới. Đọc version này cho lần ghi tiếp theo.

DELETE không có body; gửi query `expectedVersion=<block-guid>&expectedCalendarVersion=<calendar-guid>`. Sau 204, GET lại để lấy version hiện tại cho thao tác mới. Retry cùng Idempotency-Key vẫn trả 204.

`canWrite` của projection nguồn phản ánh khả năng ghi lịch cá nhân; CTSV/Admin vẫn có thể PUT để sửa cấu hình block đã cũ nếu học kỳ còn active.

Version cũ của block hoặc lịch chung trả 409. Mọi ô trong một block cùng dùng version aggregate, nên import và sửa thủ công không được âm thầm ghi đè nhau. Sửa ngày lịch chung chỉ đổi nhãn ngày/version nguồn, không dịch/xóa các ô tuần đã lưu. Semester inactive hoặc cấu hình block cũ không còn hợp lệ khiến ghi mới bị chặn; lịch sử riêng vẫn đọc được.

## Validation và riêng tư

- year 2000–2100; term chính xác SP/SU/FA; day mon…sun; block và tuần/slot đúng phạm vi.
- Subject trim 1–120 ký tự; room trim tối đa 80, thiếu/null thành `""`; không nhận ký tự điều khiển.
- Tối đa 35 hoặc 49 hàng template, không trùng day+slot. JSON trùng field/field lạ, thiếu version, sai kiểu, payload quá lớn đều bị từ chối trước ghi. PUT/calendar tối đa 8 KiB, bulk tối đa 64 KiB.
- Không trả DB/account ID trong entries. Không ghi subject/room vào log nghiệp vụ. User khác không đọc/sửa được lịch riêng; quản lý CLB không có endpoint đọc lịch của sinh viên.
- Token nội bộ sai hoặc response authority không hợp lệ trả 503; không biến sự cố service thành lỗi nhập liệu của sinh viên. Thông tin campus/học kỳ không hợp lệ giữ mã 400/403/404/409 tương ứng.

## Migration và xác minh

- Admin: `20261022143753_AcademicCalendarBySemester`, hai bảng AcademicCalendars và AcademicCalendarRevisions; FK Semester và unique scope/version, lịch sử append-only qua bốn overload SaveChanges.
- Auth: `20261022143758_PrivateStudySchedules`, block riêng theo user/year/term/block và cell unique theo block/week/day/slot. Không thay dữ liệu account/profile cũ.
- Reset demo xóa các cell/block trước Users; không chạy reset trong lượt này. Không áp migration lên môi trường dùng chung.

Kiểm tra cuối: **860 passed, 0 failed, 0 skipped** toàn solution (123 ClubReportHub, 85 Admin integration, 652 Backend stabilization). Nhóm liên quan có 55 test đạt (23 lịch học và 32 định tuyến). Build Release -warnaserror đạt 0 warning/error; format CI đầy đủ và EF model/migration của Activity/Admin/Auth/Club đều đạt; có TRX/XPlat coverage. SQL Server kiểm tra upgrade từ schema trước, hai thao tác cùng version chỉ một bên ghi và giữ dữ liệu account/học kỳ. Regression đỏ trước sửa xác nhận lỗi phân loại credential nội bộ và chọn campus cho Admin GLOBAL.

FE hiện vẫn dùng repository localStorage cho lịch học. Backend source đã có các route trên; chuyển consumer FE, nghiệm thu qua trình duyệt và deploy vẫn chưa thực hiện. Các nhóm governance/profile/media/resources và điểm có trạng thái riêng, không được đánh Done bởi phần lịch học này.
