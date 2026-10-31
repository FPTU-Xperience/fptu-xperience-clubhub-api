# Đánh giá ERD và database BE — 26/10/2026

> **Đã có bản triển khai và kiểm chứng sau audit:** [27/10/2026](TRIEN_KHAI_ERD_DATABASE_2026-10-27.md). Nội dung dưới giữ kết quả baseline trước sửa (91 bảng); ERD liên kết hiện đã cập nhật model sau sửa.

**Kết luận: nền tảng có thể tiếp tục dùng cho đồ án, nhưng chưa thể coi là đã tối ưu hoặc đủ toàn vẹn dữ liệu.** Quan hệ lõi, nhiều unique index, transaction/outbox và snapshot lịch sử được làm khá tốt. Các mục ưu tiên là migration/schema thực tế, xung đột khi duyệt đơn, ràng buộc hồ sơ CLB và liên kết file báo cáo. Không cần thêm tầng/framework để giải quyết những việc này.

Đợt này chỉ đánh giá, tạo tài liệu và chạy kiểm chứng trên database SQL tạm; **không sửa production C#, schema đang dùng, FE hay quy tắc tính điểm**. Các thay đổi refactor từ đợt trước được giữ nguyên. Cả 32 file C# của đợt đó có hash không đổi.

## 1. Bằng chứng và phạm vi

- Đọc model EF, cấu hình tách riêng, migration, handler/query và test có sẵn của 8 service. CodeGraph không truy cập được index; GitNexus được dùng để tìm DbContext/luồng và đối chiếu source. Index C# khớp, chỉ có drift ở 3 tài liệu cuối đợt trước.
- Export trực tiếp EF design-time model và generate migration SQL, không cần kết nối production. Cả 8 model báo `HasPendingModelChanges = false`.
- Tạo 8 database SQL Server LocalDB mới, chạy toàn bộ migration, đối chiếu **sự tồn tại của cột** trước/sau upgrader đúng thứ tự startup. Xóa các database tạm sau kiểm tra.
- Tái hiện 4 phản ví dụ bằng persistence/helper trên SQL Server tạm. Đây không phải 4 test HTTP; không tuyên bố đã chạy lại toàn bộ 946 test của đợt 25/10.
- Chưa có execution plan, số dòng/dung lượng và benchmark production. Vì vậy index đề xuất là ứng viên theo hình dạng query, chưa phải kết luận đã đo được độ trễ.

[ERD theo từng database](ERD_EF_DATABASE_2026-10-26.md) · [Data dictionary/model JSON](ERD_EF_DATABASE_2026-10-26.json) · [Kết quả SQL và số liệu](KIEM_CHUNG_DATABASE_2026-10-26.json).

## 2. Quy mô thực tế

Một SQL Server instance đang chứa 8 database theo service. Các ID xuyên database là logical reference qua API/event; thiếu FK vật lý ở chỗ đó không tự động là sai. Phân tách database hiện tại làm tăng việc đồng bộ và vận hành; nên giữ ranh giới dữ liệu rõ và hoàn thiện các ràng buộc trước khi mở rộng thêm service.

| Service | Bảng EF | FK | Index không tính PK | Unique index | Migration |
|---|---:|---:|---:|---:|---:|
| Auth | 9 | 6 | 11 | 8 | 9 |
| Club | 12 | 8 | 28 | 13 | 17 |
| Activity | 19 | 20 | 34 | 14 | 6 |
| Admin | 31 | 16 | 55 | 19 | 14 |
| Report | 8 | 4 | 14 | 5 | 7 |
| Finance | 5 | 2 | 11 | 4 | 5 |
| Export | 5 | 1 | 8 | 2 | 4 |
| Notification | 2 | 0 | 3 | 0 | 1 |
| **Tổng** | **91** | **57** | **164** | **65** | **63** |

91 bảng không đồng nghĩa có 91 thực thể nghiệp vụ phức tạp: số này bao gồm history, revision, ledger, allocation, import, outbox, idempotency và job. Không đánh giá chuẩn hóa hoặc độ phức tạp chỉ bằng số bảng. Inventory không tính bảng hệ thống migration/Hangfire. Chưa thấy một ERD đầy đủ hiện hành trong tài liệu source; sơ đồ đính kèm được tạo từ EF để tránh vẽ quan hệ không tồn tại.

## 3. Các lỗi/thiếu sót nên sửa trước

### DB-01 — P1: helper migration có thể báo thành công khi schema chưa được tạo

[DatabaseStartupExtensions.cs:51](../../src/Shared/ClubReportHub.Shared/Data/DatabaseStartupExtensions.cs#L51) bắt SQL error 1801/2714 rồi chỉ cần `CanConnectAsync = true` là `return`. Kết nối được database không chứng minh migration đã hoàn tất. Với database có object trùng nhưng thiếu bảng/cột hoặc thiếu migration history, helper có thể bỏ qua lỗi thật.

**Đã tái hiện:** tạo DB chỉ có Roles rồi gọi helper Auth; hàm trả bình thường, bảng Users vẫn không tồn tại, còn 9 migration pending. Không chạy host production nên không khẳng định mọi service chắc chắn sẽ vượt qua tất cả bước seed/readiness sau đó.

**Cách chỉnh:** riêng lỗi tạo database do cạnh tranh có thể retry có kiểm soát; object already exists trong migration phải xác minh history/schema hoặc fail để thực hiện baseline/reconcile rõ ràng. Không nuốt lỗi chỉ bằng phép thử kết nối. Thêm test partial/legacy schema và test cùng lúc khởi động.

### DB-02 — P2: Report migration chưa tự tạo đủ schema của model

Chạy đủ 7 migration Report trên DB trống vẫn thiếu **12 cột**: 6 cột liên kết tài chính/xuất hoạt động của Reports và 6 cột preview của ReportUploadedFiles. Cả model/snapshot vẫn báo không pending. Insert bằng model sau `MigrateAsync` đã trả lỗi SQL `Invalid column name`; sau [ReportSchemaUpgrader.cs:10](../../src/Services/ReportService/Data/ReportSchemaUpgrader.cs#L10) thì kiểm tra đủ cột đạt. [Program.cs:160](../../src/Services/ReportService/Program.cs#L160) hiện gọi cả hai bước, nên đây là phụ thuộc thật của startup, không phải khẳng định startup bình thường luôn lỗi.

7 service khác không thiếu cột trong kiểm tra tương tự. Đã kiểm tra column coverage, chưa khẳng định mọi kiểu SQL/index/constraint đều đã đối chiếu vật lý.

**Cách chỉnh:** thêm migration reconcile có điều kiện để giữ được DB đã được upgrader bổ sung cột; không phát hành `AddColumn` vô điều kiện làm lỗi môi trường cũ. Đưa schema về một nguồn migration có version, rồi thu hẹp compatibility upgrader. Test database mới chỉ chạy migration và kiểm tra schema/model thực tế; `has-pending-model-changes` không thay cho test này.

### DB-03 — P1: đơn CLB chưa được bảo vệ trước quyết định duyệt/từ chối đồng thời

ClubCreationApplication, ClubDisbandRequest, ClubOwnershipTransfer không có concurrency token hoặc update có điều kiện trên trạng thái. Các handler đọc Submitted/Pending, sau đó ghi kết quả; bên reject không nhất thiết sửa Club nên token của Club không bảo vệ được quyết định.

Căn cứ: [DisbandEndpoints.cs:186](../../src/Services/ClubService/Endpoints/DisbandEndpoints.cs#L186), [reject:231](../../src/Services/ClubService/Endpoints/DisbandEndpoints.cs#L231); [ApplicationEndpoints.cs:253](../../src/Services/ClubService/Endpoints/ApplicationEndpoints.cs#L253); [TransferEndpoints.cs:237](../../src/Services/ClubService/Endpoints/TransferEndpoints.cs#L237).

**Đã tái hiện trên SQL:** hai DbContext cùng đọc Pending; approve lưu đơn Approved và CLB inactive; reject từ bản đọc cũ vẫn lưu thành công. Kết quả cuối: **đơn Rejected nhưng CLB đã giải thể**. Chưa chạy race HTTP hoặc tái hiện riêng cho cả ba loại đơn; finding cho creation/transfer được xác nhận theo cùng kiểu đọc/ghi và model không có CAS.

**Cách chỉnh:** thêm version/rowversion trên từng loại đơn hoặc conditional update `WHERE Id=... AND Status='Pending'`; quyết định và side effect lưu cùng transaction, stale request trả 409. Transaction đơn thuần ở isolation mặc định không thay thế kiểm tra trạng thái/version. Thêm SQL race approve/reject cho cả ba loại.

### DB-04 — P2: thiếu unique constraint cho đơn thành lập CLB đang chờ của cùng sinh viên

[ApplicationEndpoints.cs:166](../../src/Services/ClubService/Endpoints/ApplicationEndpoints.cs#L166) chỉ kiểm tra `Any` trước insert; [UpdateApplication:219](../../src/Services/ClubService/Endpoints/ApplicationEndpoints.cs#L219) đưa NeedsRevision về Submitted mà không kiểm tra một đơn Submitted khác. [ClubDbContext.cs:136](../../src/Services/ClubService/Data/ClubDbContext.cs#L136) chỉ tạo index thường `(RequesterUserId, Status)`.

Ngoài race submit, có đường tuần tự: đơn A NeedsRevision → tạo đơn B Submitted → resubmit A. **Đã xác nhận SQL cho phép hai dòng Submitted cùng RequesterUserId** qua các mutation này; chưa gọi HTTP trong kiểm chứng.

**Cách chỉnh:** filtered unique trên RequesterUserId với Status=Submitted (đúng quy tắc handler hiện tại); xử lý conflict 409 cho cả submit/resubmit. Kiểm tra và giải quyết bản ghi trùng trước migration. Chưa tự mở rộng unique sang NeedsRevision vì đó là quyết định nghiệp vụ khác.

### DB-05 — P2: file minh chứng có thể trỏ chi tiết báo cáo đã bị xóa

[ReportAttachment.cs:8](../../src/Services/ReportService/Models/ReportAttachment.cs#L8) có ReportDetailId nhưng model chỉ FK attachment tới Report. [UpdateReport:255](../../src/Services/ReportService/Endpoints/ReportCrudEndpoints.cs#L255) xóa và tạo lại toàn bộ details, trong khi giữ attachments.

**Đã tái hiện trên SQL theo cùng pattern lưu:** tạo detail A và attachment trỏ A; thay details; A bị xóa nhưng attachment vẫn giữ ID cũ. Kiểm tra detail khi upload không bảo vệ lần sửa sau đó.

**Cách chỉnh:** quy định giữ ID khi sửa, hoặc clear/rebind liên kết attachment trong cùng transaction; dọn orphan cũ, thêm nullable FK phù hợp. Composite FK `(ReportId,ReportDetailId)` bảo vệ thêm việc detail thuộc đúng report. Dùng NoAction/Restrict và xử lý xóa có chủ ý; tránh multiple cascade paths từ Report qua detail/attachment.

### DB-06 — P2, cần chốt hành vi: thông báo theo role dùng trạng thái đọc chung

[Notification.cs:11](../../src/Services/NotificationService/Models/Notification.cs#L11) có một IsRead cho cả notification. [MarkAsRead:95](../../src/Services/NotificationService/Endpoints/NotificationEndpoints.cs#L95) và read-all cập nhật dòng chung. Consumer thực sự tạo broadcast cho ClubManager/ClubMember và các role khác.

Với broadcast, A đọc thì B cùng role cũng mất unread. Đây là giới hạn model hiện tại; test NotificationReadContractTests còn cố ý giữ shared marker. Nếu sản phẩm dùng “thông báo chưa đọc của tôi”, cần `NotificationReadReceipt(NotificationId,UserId,ReadAtUtc)` có composite PK hoặc fan-out riêng từng recipient. Không cần FK xuyên DB tới Auth. Nếu shared acknowledgement là chủ ý, phải ghi rõ contract/UI; không gọi là lỗi phân quyền.

## 4. Gia cố quan hệ trong cùng database

- **Activity:** FK ActivityId và SessionId độc lập chưa bảo vệ “session thuộc cùng activity”; staffing/completion có các tuple tương tự. [ActivityDbContext.cs:95](../../src/Services/ActivityService/Data/ActivityDbContext.cs#L95). Cân nhắc composite FK `(ActivityId,SessionId)` tới `(ActivityId,Id)`, giữ SessionId nullable cho legacy. Handler đang chọn theo cả activity/session nên đây là gap DB, chưa chứng minh API tạo attendance sai.
- **Admin ledger:** Effect.RevisionId và Decision.RevisionId là revision nội bộ nhưng thiếu FK bảo vệ revision thuộc đúng contribution. [VerifiedExperienceModel.cs:42](../../src/Services/AdminService/Data/VerifiedExperienceModel.cs#L42). Cân nhắc composite `(ContributionId,RevisionId)` sau kiểm tra lịch sử. **Không thêm FK máy móc cho TargetRevisionId:** upgrade dùng upgrade-request ID, revoke dùng nonce; nên mô tả target đa loại hoặc chuyển TargetKind/TargetId khi chỉnh model. CurrentRevisionId còn có vòng insert contribution/revision, cần thiết kế nullable/staged insert trước.
- Các bảng điểm/ledger đang giữ frozen snapshot và append-only guards. Gia cố quan hệ không đồng nghĩa chốt công thức điểm; phần chính sách điểm vẫn hoãn.

## 5. Tối ưu có giá trị, theo thứ tự

1. **Tải minh chứng sau khi kiểm tra quyền:** [PrivateEvidenceEndpoints.cs:56](../../src/Services/AdminService/Endpoints/PrivateEvidenceEndpoints.cs#L56) đọc cả Bytes (tới 10 MB) trước khi xác minh. Nên đọc metadata/owner trước, kiểm tra quyền rồi mới lấy Bytes; request bị 403 không cần kéo BLOB. Đây là thay đổi nhỏ, không cần đổi storage.
2. **Phân trang tại SQL:** [MemberActivityStatisticsService.cs:97](../../src/Services/ActivityService/Services/MemberActivityStatisticsService.cs#L97) tải toàn lịch sử/attendance rồi Skip/Take ở RAM. Tách aggregate tổng và SQL page, chỉ lấy attendance của trang. Giữ mẫu số toàn lịch sử trong thống kê.
3. **Index family refresh:** query revoke ở [RefreshTokenService.cs:277](../../src/Services/AuthService/Services/RefreshTokenService.cs#L277) lọc FamilyId + RevokedAtUtc nhưng [AuthDbContext.cs:72](../../src/Services/AuthService/Data/AuthDbContext.cs#L72) chưa có index dẫn đầu FamilyId. Ứng viên: `(FamilyId,RevokedAtUtc)` hoặc filtered FamilyId khi RevokedAtUtc IS NULL, chọn sau đo workload.
4. **Index export của người dùng:** [ExportEndpoints.cs:50](../../src/Services/ExportService/Endpoints/ExportEndpoints.cs#L50) lọc RequestedByUserId và sort CreatedAtUtc; ứng viên `(RequestedByUserId,CreatedAtUtc DESC)`. Không thêm nhiều biến thể Status nếu chưa có query phổ biến chứng minh cần.
5. **Quan hệ evidence dùng để truy vấn quyền nên có bảng liên kết:** `EvidenceRefsJson.Contains(...)` hiện quét các JSON nguồn/revision/request; khi tăng dữ liệu, thêm bảng source-evidence và index EvidenceId, vẫn giữ JSON snapshot nếu cần. Không chuẩn hóa mọi JSON lịch sử một cách máy móc.
6. **Retention và claim job:** có lịch dọn refresh token hết hạn/receipt/outbox/notification/job theo chính sách; ExportGenerationJob legacy nên có conditional claim/version giống PDF worker mới nếu cần chống trùng thực thi. Chưa tái hiện duplicate job trong audit này. PDF/BLOB lưu SQL vẫn chấp nhận được ở dung lượng nhỏ của đồ án.

Không có execution plan/benchmark nên chưa khẳng định các index đề xuất sẽ nhanh hơn bao nhiêu. 164 index gồm index quan hệ/unique; không tự xóa chỉ vì số lượng lớn.

## 6. Những phần nên giữ

- PK/FK trong cùng aggregate và cascade có kiểm soát; Restrict ở lịch sử/ledger và các nguồn cần giữ chứng cứ.
- Club có unique active-manager, pending disband/transfer và treasurer slots; attendance có filtered unique riêng legacy/session; application execution 1–1 và history theo version.
- Finance dùng decimal(18,2), unique filtered settlement chỉ ở Submitted/Approved; proposal version bảo vệ các mutation settlement trong transaction. Không sửa thành unique vô điều kiện khiến mất lịch sử bị từ chối.
- Snapshot tên CLB/người duyệt/policy/source và PDF snapshot phục vụ lịch sử; không thay toàn bộ bằng join tới dữ liệu hiện tại.
- Outbox EventId, receipt/idempotency, lease/concurrency ở các worker mới là cần thiết cho xử lý lặp; không xóa để giảm số bảng.
- Một SQL Server instance chứa 8 database phù hợp deployment hiện tại. Nếu muốn đơn giản hóa kiến trúc sâu hơn, cần một kế hoạch riêng cùng chi phí chuyển đổi; các lỗi ưu tiên ở trên xử lý được ngay trong ranh giới hiện có.
- Deployment đã có backup và RESTORE VERIFYONLY trước thay release. Đó chưa phải một lần diễn tập phục hồi dữ liệu đầy đủ. Về vận hành, compose đang dùng tài khoản sa cho các service; nên tách login/user giới hạn quyền theo database nếu triển khai ngoài môi trường demo.

## 7. Thứ tự chỉnh đề xuất

**Đợt 1 — tính đúng đắn và triển khai:** DB-01, DB-02, DB-03; sau đó DB-04 và DB-05. Mỗi migration phải có kiểm tra dữ liệu đang tồn tại, tránh thất bại vì duplicate/orphan hoặc cột raw-upgrader đã thêm trước đó.

**Đợt 2 — hành vi người dùng và hiệu năng:** chốt unread cá nhân (DB-06); metadata-before-BLOB, SQL paging và hai index ứng viên; kiểm tra SQL/plan trước và sau. Gia cố composite FK cùng bộ negative SQL test.

**Đợt 3 — vận hành và dung lượng:** retention, claim legacy export, role SQL riêng và restore drill. Không gộp thay đổi công thức điểm vào các bước này.

Audit đã hoàn tất; các phát hiện ở trên là danh sách cần sửa, không được coi là đã triển khai. Source C# và database production giữ nguyên trong đợt 26/10.
