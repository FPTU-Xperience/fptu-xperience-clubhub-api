# Triển khai sửa ERD/database BE — hoàn tất 27/10/2026

Đã triển khai 6 nhóm lỗi DB-01–06 cùng các tối ưu độc lập trong source BE. **983/983 test đạt, 0 lỗi, 0 bỏ qua**, gồm **37 case mới**. Phạm vi BE; công thức và chính sách tính điểm tiếp tục hoãn theo yêu cầu. Chưa áp dụng migration lên production, chưa commit/push.

## Những phần đã sửa

| Phát hiện | Kết quả đã kiểm chứng |
|---|---|
| DB-01: khởi tạo schema báo thành công sai | Helper thực hiện lại chính thao tác khi retry và báo lỗi nếu schema/history chưa được reconcile. Kết nối SQL thành công không còn được coi là migration hoàn tất. |
| DB-02: Report thiếu schema | Migration bổ sung có điều kiện 12 cột finance/preview và index BudgetProposalId. Cả 8 DB mới đều đủ cột chỉ bằng MigrateAsync. Giữ upgrader để tương thích các môi trường cũ. |
| DB-03: duyệt/từ chối đồng thời | Ba loại đơn CLB có Guid CAS, tự xoay qua SaveChanges. Sáu HTTP race kiểm tra cả hai quyết định thắng. Request cũ trả 409; transaction rollback cả CLB, owner, membership và outbox. |
| DB-04: đơn thành lập trùng | Filtered unique trên requester khi Status=Submitted; kiểm tra cả submit/resubmit và xử lý 409. Migration chặn dữ liệu trùng cũ, giữ nguyên đơn và lịch sử để xử lý có chủ ý. |
| DB-05: file trỏ detail đã xóa | FK ghép bảo đảm attachment/detail thuộc cùng report. Migration gỡ liên kết sai nhưng giữ file. Khi thay toàn bộ details, attachment được giữ ở cấp report và ReportDetailId trở thành null. |
| DB-06: trạng thái đọc chung theo role | Receipt riêng theo actor, khóa (NotificationId,UserId), FK cascade. Mark/read-all idempotent theo lô 250; broadcast không còn đổi marker đọc chung. |

## Gia cố và tối ưu bổ sung

- Attendance có FK ghép (ActivityId,SessionId), ngăn session của hoạt động khác; giữ SessionId null cho dữ liệu legacy.
- Decision.RevisionId và LedgerEffect.RevisionId phải tồn tại và thuộc đúng contribution. TargetRevisionId vẫn là tham chiếu đa loại, không bị gán FK sai. Không tự viết lại lịch sử ledger.
- Download minh chứng đọc metadata và kiểm tra quyền trước khi lấy Bytes; request 403 không truy vấn BLOB. Hai URL download giữ nguyên.
- Lịch sử hoạt động đếm/aggregate và phân trang tại SQL, chỉ tải attendance của trang. Giữ tổng toàn lịch sử, distinct activity và điều kiện xác minh; sắp xếp theo thời gian rồi Id để ổn định trang. Cancelled được lọc không phân biệt hoa/thường.
- Thêm index RefreshTokens(FamilyId,RevokedAtUtc) và ExportRequests(RequestedByUserId,CreatedAtUtc DESC). Chưa đo benchmark production, không tuyên bố tỷ lệ tăng tốc.

## Contract dành cho FE

Đường dẫn, method và DTO hiện hữu giữ nguyên. Với thông báo, `isRead` giờ thuộc actor hiện tại: A đọc broadcast không làm B mất unread. Trạng thái legacy của thông báo gửi trực tiếp đã đọc được backfill cho owner xác định được.

Marker broadcast cũ không xác định được ai đã đọc nên được reset; mỗi người bắt đầu có unread riêng. Bộ lọc unread được áp dụng trước giới hạn 100 dòng. Receipt không cấp quyền xem; role và phạm vi CLB vẫn được kiểm tra theo cơ chế hiện hữu.

Read-all xử lý từng lô. Nếu lỗi giữa chừng, một số lô có thể đã hoàn thành; retry tiếp tục an toàn.

## Migration mới

| Service | Migration |
|---|---|
| Auth | `20261026090152_IndexRefreshTokenFamilies.cs` |
| Export | `20261026090157_IndexExportsByRequester.cs` |
| Activity | `20261026090202_EnforceAttendanceSessionActivity.cs` |
| Admin | `20261026090207_EnforceContributionRevisionLineage.cs` |
| Club | `20261026090212_ProtectClubRequestReviews.cs` |
| Report | `20261026090217_ReconcileReportSchemaAndAttachmentDetails.cs` |
| Notification | `20261026090221_AddPersonalNotificationReadReceipts.cs` |

Các migration được tạo ngày 26/10; hoàn tất kiểm chứng ngày 27/10. Model hiện tại có **92 bảng, 61 FK, 169 index và 70 migration** trong 8 DbContext. Không tính bảng nội bộ Hangfire hoặc __EFMigrationsHistory.

[ERD hiện tại](ERD_EF_DATABASE_2026-10-26.md) và [model JSON đầy đủ](ERD_EF_DATABASE_2026-10-26.json) đã được xuất lại từ assembly hiện tại.

## Nâng cấp dữ liệu cũ

Trước khi áp dụng vào DB có dữ liệu: backup, chạy [SQL preflight chỉ đọc](../remediation/SQL_PREFLIGHT_ERD_2026-10-27.sql), xử lý các dòng báo lỗi theo quyết định nghiệp vụ rồi mới chạy migration.

| SQL error | Dữ liệu cần kiểm tra |
|---|---|
| 51020 | Nhiều report dùng cùng BudgetProposalId |
| 51031 | Nhiều đơn Submitted của một requester |
| 51041 | Session không thuộc activity của attendance |
| 51042 / 51043 | Revision của ledger effect/decision không tồn tại hoặc thuộc contribution khác |

Không tự xóa đơn hoặc sửa ledger để vượt constraint. Report chỉ tự gỡ liên kết detail không hợp lệ; các file và attachment được giữ nguyên.

Down của Report giữ 12 cột legacy và index BudgetProposal vì model cũ/upgrader đã dùng chúng; không xóa dữ liệu finance/preview có trước. Down của Notification loại bảng receipt; model cũ không biểu diễn được trạng thái đọc riêng của broadcast. Cần backup nếu thực hiện rollback DB. Chưa thực hiện rollback/deploy production.

## Kiểm chứng

- Build toàn solution Release với `-warnaserror`: 0 warning, 0 error.
- Toàn bộ 983 test đạt, không skip. Admin/Finance SQL migration tests được bật kết nối LocalDB. Có 3 file coverage; số lượng file không thể hiện độ phủ đầy đủ.
- 37 case mới: 13 Club workflow, 7 Report schema, 6 Notification receipts, 11 DatabaseSafety. Trong đó 36 case dùng SQL, 1 case dùng InMemory để kiểm tra khác biệt hoa/thường. Case Cancelled đã chạy thất bại trước sửa và đạt sau sửa.
- Hai fixture cũ được cập nhật: kiểm tra danh sách migration đã áp dụng thay vì cố định tên migration cuối; tạo revision thật cho ledger tham chiếu. Các assertion về giá trị, bất biến, rollback và nghiệp vụ vẫn giữ nguyên.
- Kiểm tra model/snapshot đạt ở 8 service. Tạo mới 8 database SQL đều đủ cột sau migration độc lập; Report không còn cần upgrader để tạo 12 cột bị thiếu.
- Định dạng code, diff whitespace và hash source được kiểm tra. Graph được làm mới sau thay đổi cuối; source và test được dùng để đối chiếu các giới hạn dynamic dispatch/process extraction. File C# mới chưa tracked cũng được kiểm tra riêng.

Lượt test đầu có 981/983 đạt; hai fixture trên đã được sửa, chạy riêng đạt, rồi chạy lại toàn suite. Trong quá trình này SDK 10.0.113 bị thay bởi 10.0.114, làm một lượt tool báo mất vstest/targeting pack. Assets đã được restore và các kiểm tra được chạy lại trên SDK hiện có; không thay framework mục tiêu hoặc phiên bản thư viện của BE.

## Các phần còn lại

- Chưa đặt chính sách retention/xóa lịch sử; chưa tự động purge dữ liệu.
- Chưa tách bảng source-evidence để thay việc tìm JSON bằng Contains.
- Chưa chuyển ExportGenerationJob legacy sang CAS/lease.
- Attendance/revision đã có FK ghép; các tuple staffing/completion khác vẫn dựa vào API guards hiện hữu.
- Chưa cấp login SQL riêng, diễn tập restore, load test hoặc áp dụng production. Các vấn đề cache quyền/authority đã ghi ở đợt audit trước vẫn có trạng thái riêng.
- Quy tắc tính điểm tiếp tục hoãn theo yêu cầu của người dùng.

[JSON kiểm chứng](TRIEN_KHAI_ERD_DATABASE_2026-10-27.json) ghi counters, migration, model counts, hash source và giới hạn xác minh. Đây là bàn giao code/migration đã kiểm thử, chưa phải xác nhận triển khai production.
