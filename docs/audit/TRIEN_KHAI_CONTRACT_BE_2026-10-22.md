# Bàn giao BE theo contract FE mới — 22/10/2026

Phạm vi: BE, trên nhánh `feat/backend-contract-completion`. Nguồn yêu cầu là contract FE main `3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42`; BE trước triển khai là `b066f96b9fd0ea304e7c498f6ddcc569b2c21ebf`. Tài liệu này cập nhật tiến độ sau [audit 21/10](DOI_CHIEU_BE_CONTRACT_FE_2026-10-21.md); audit cũ giữ nguyên mốc nguồn. Không sửa FE và không đánh đồng test cục bộ với đã deploy.

> Cập nhật tiếp ngày 22/10: xem [bàn giao hoạt động, phân công và roster](HOAN_THIEN_API_BE_KHONG_TINH_DIEM_2026-10-22.md). Nhóm 16 consumer operation thiếu route ban đầu hiện đã có đủ handler; tính điểm đang hoãn và các nhóm contract khác vẫn có phần chưa hoàn thành. Kết quả 788 test dưới đây là checkpoint trước đợt bổ sung này.

> Bổ sung tiếp: [6 API lịch học và lịch học kỳ](HOAN_THIEN_LICH_HOC_BE_2026-10-22.md) đã có persistence, version và kiểm thử SQL; FE consumer vẫn cần chuyển từ localStorage.

> Bổ sung: [hồ sơ cá nhân mở rộng](HO_SO_CA_NHAN_BE_2026-10-22.md), gồm ba field self-only và học vụ từ roster; các phần chưa có nguồn/media được giữ nhãn riêng.

## Trạng thái từng phần

| Actor / phần | API | Trạng thái BE và giới hạn |
| --- | --- | --- |
| Quản lý CLB | `GET /api/clubs/{id}`, `GET /api/clubs/me/managed` | **Đã sửa F01**: trả `version`, `coverImageUrl`, `schedule`; mapper dùng chung cũng bổ sung dữ liệu cho response tạo/sửa/gán quản lý. Quyền xem danh sách thành viên không được mở rộng. |
| Quản lý CLB | `PUT /api/clubs/{id}/public-images` | **Đã sửa F02**: trường bị bỏ qua giữ nguyên; `null` xóa đúng ảnh được chỉ định. Có version, xác minh media, quyền hiện tại và chống gửi lại khác payload. |
| Quản lý CLB, thành viên đã duyệt, CTSV/Admin có phạm vi | `GET /api/clubs/{clubId}/workspace-semesters` | **Đã có read API**: catalog Admin thật, kiểm tra CLB/campus/quyền hiện tại, mã/GUID/ngày và availability. Không đồng nghĩa governance hay mọi nghiệp vụ theo kỳ đã hoàn thành. |
| Quản lý CLB, CTSV/Admin có phạm vi | `GET /api/clubs/{clubId}/invitation-semesters` | **Đã bổ sung luồng roster**: availability/canInvite dựa trên revision roster thật; có eligible-members và kiểm tra lại khi mời/consent/duyệt. Xem bàn giao mới. |
| Quản lý CLB, CTSV/Admin có phạm vi | `GET /api/clubs/{clubId}/experience-policies?semesterId=...` | **Đã có read API**: nhận code/GUID thật; chỉ policy PERIOD đã duyệt, đúng environment, đã có hiệu lực. Không trả approval basis, người duyệt, tau hay cấu hình quản trị. |
| Sinh viên đã đăng ký hoạt động | `GET /api/v2/activities/{activityId}/sessions?page=1&pageSize=20` | **Đã có projection an toàn cho F05**: phiên/lịch sử/nguồn snapshot, phiên bản và trạng thái của chính người gọi; không trả roster, geofence, QR challenge hay evidence người khác. FE cần chuyển sang route này cho campus nonmember. |
| Sinh viên | `GET /api/clubs/me/membership-applications` | **Đã có API**: lịch sử đơn/lời mời của chính tài khoản, cả hồ sơ đã rút/được lưu trữ; club code/logo, source, version và capability theo principal. |
| Sinh viên | `POST /api/clubs/me/membership-applications/{applicationId}/withdraw` | **Đã có API**: không body; tự rút đơn APPLICATION đang Pending; gửi lại trả cùng trạng thái; giữ câu trả lời và audit actor/thời gian/version. Không dùng để từ chối lời mời hay rời CLB đã tham gia. |
| Sinh viên | `POST /api/clubs/{id}/join` | **Đã sửa nộp lại**: tạo ID mới, liên kết `previousApplicationId`, giữ nhận xét/câu trả lời của đơn trước; consent lấy từ chính sinh viên, vẫn chờ quản lý duyệt. |
| Sinh viên | `GET /api/clubs/me/memberships` | **Đã bổ sung metadata F06**: `clubCode`, `logoUrl`, `source`, `semesterId`, `invitedAt`, `consentedAt`, `canWithdraw`, `canAcceptInvitation`, `version`. `semesterId` còn null khi chưa có căn cứ roster; legacy source là UNKNOWN. |
| Quản lý CLB | `POST /api/clubs/{clubId}/members` | **Đã bổ sung kiểm tra roster**: bắt buộc semesterId; email trường/tài khoản hiện có, actor hợp lệ, revision và account kiểm tra lại ở mời/consent/duyệt; có outbox cho người được mời. Không tự tạo account hoặc auto-approve. |
| Nội bộ | `GET /api/internal/experience/workspace-catalog` | Chỉ service-to-service: cần internal token **và** bearer hợp lệ. FE không được gọi hoặc nhận token này. Không thay policy backoffice của `/api/v1/semesters`. |

Trong 16 operation có consumer nhưng thiếu route tại audit 21/10, withdrawal đã có ở checkpoint trước và 15 operation còn lại đã có handler ở đợt bổ sung hiện tại. **Còn 0 route thiếu trong nhóm này**, không đồng nghĩa toàn bộ contract đã xong. Actor dùng chung operation nên không cộng các hàng thành tổng.

| Actor | Operation có consumer vẫn thiếu route trong nhóm 16 |
| --- | ---: |
| Quản lý CLB | 0 |
| Sinh viên | 0 |
| Người xác minh | 0 |
| CTSV/Admin | 0 |

Danh sách mới, điểm chưa tương thích ở FE, schema roster, migration và giới hạn ở [bàn giao hiện tại](HOAN_THIEN_API_BE_KHONG_TINH_DIEM_2026-10-22.md) và [JSON bàn giao](TRIEN_KHAI_CONTRACT_BE_2026-10-22.json).

## Cách FE dùng các phần đã có

### Sửa thông tin và ảnh CLB

Đọc `version` từ response GET quản lý. Gửi lại `expectedVersion` khi sửa public profile/images. Cập nhật CLB qua PUT quản trị cũ cũng đổi version, để trình sửa đang mở không ghi đè thay đổi mới. Ví dụ chỉ thay logo:

```json
{"logoUrl":"https://<verified-media-url>","expectedVersion":"<club-version>"}
```

Không gửi `coverImageUrl` nghĩa là giữ nguyên cover; gửi `coverImageUrl:null` nghĩa là xóa cover. Có thể gửi cả hai để tương thích client cũ. Body chỉ có version, trùng tên trường, trường lạ hoặc kiểu ảnh không phải string/null trả 400. Version cũ trả 409. Media không xác minh được không lưu nửa chừng. Gửi lại cùng Idempotency-Key/payload trả receipt cũ; omission và null có fingerprint khác nhau. Kiểm tra quyền quản lý vẫn chạy trước replay.

### Chọn học kỳ và policy

Workspace catalog trả `{clubId,items,currentAvailability,asOf,timezone,startInclusive,endInclusive}`. Mỗi item có `id`/`semesterKey` là GUID, `code`/`semesterId`/`invitationCode` là code Admin, label, ngày UTC, isCurrent, editable, canInvite và availability.

Admin là một nguồn duy nhất. Không đoán năm/term/kỳ báo cáo từ code, kể cả code có dạng quen thuộc. Alias tùy chọn đặt tại `Workspace:SemesterAliases:<Semester.Id>:Year`, `Term` (`SP|SU|FA`) và `ReportPeriod`. Không có cấu hình thì trả null và `PARTIAL_OR_UNCONFIGURED`.

Muốn `editable:true`, cần đúng một kỳ active và cấu hình `EditableFromUtc`/`EditableUntilUtc` dưới cùng alias; cửa sổ phải nằm trong ngày của học kỳ và thời điểm gọi nằm trong `[from,until)`. Đây là metadata cửa sổ chung; endpoint ghi từng nghiệp vụ vẫn phải kiểm tra quyền/quy tắc riêng. Thành viên chỉ đọc có editable=false. Nhiều kỳ active trả `currentAvailability:"AMBIGUOUS"`, không tự chọn một kỳ.

`startUtc`/`endUtc` là các mốc Admin được chuyển sang UTC; metadata biên học kỳ hiện là inclusive. Không cấu hình giả ngày hay kỳ báo cáo. Report-period alias không tự thêm semester attribution cho Finance, không tạo governance authority và không reset thành viên/điểm.

Policy read yêu cầu `semesterId` code/GUID; response echo code chuẩn và semesterKey, `items:[{policy,effectiveAt}]`. Không có policy phù hợp trả `availability:"NO_APPROVED_POLICY"`; nguồn Admin hỏng/mất kết nối trả 503, không giả danh sách hợp lệ.

### Nguồn phiên dành cho sinh viên

Route mới chỉ nhận principal; query `userId` không đổi người gọi. Cần account hiện tại active/unlocked, visibility/campus phù hợp và đăng ký tham gia. Hoạt động private hoặc draft không được lộ qua route này. Với CLUB_MEMBER, chỉ các phiên có thời điểm bắt đầu sau lúc được duyệt thành viên mới đủ điều kiện.

Response phân trang có `items`, `totalItems`, `totalPages`, `asOf`, `scopeVersion`. Mỗi item có phiên bản activity/session và `source` dùng cùng bộ dựng snapshot với API nội bộ; có tier/scale/vector/policy đã công bố. Lịch sử phiên hoàn thành vẫn đọc được. Hủy hoạt động/phiên làm source.status=CANCELLED và tắt thao tác mới.

`sourceActions.canCheckIn` phản ánh điều kiện nguồn/cửa sổ hiện tại và cấu hình ký attendance; check-in vẫn cần QR/vị trí hợp lệ ở endpoint ghi. `contribution` và `manualAttendance` là `UNAVAILABLE` hoặc `REQUIRES_SUBMISSION_VALIDATION`: đây không phải cam kết sẽ ghi thành công. AdminService còn kiểm tra duplicate contribution, manual request pending, evidence và policy khi ghi. Không cộng điểm khi đọc nguồn. API quản lý `/api/activities/{id}/sessions` giữ nguyên quyền; FE public/student dùng route v2 mới.

### Rút đơn và nộp lại

Đơn mới tự nộp có source APPLICATION; lời mời mới có source INVITATION. Dữ liệu cũ được migration gán UNKNOWN vì không đủ bằng chứng xác định nguồn; không suy luận chỉ từ cờ consent. `canWithdraw` chỉ true ở response của chính người nộp, source APPLICATION, Pending, chưa lưu trữ. Response quản lý không cấp capability tự rút đơn người khác.

Rút đơn POST không có body, hỗ trợ Idempotency-Key tùy chọn. Response 200 `{applicationId,clubId,status:"WITHDRAWN",version,canWithdraw:false,withdrawnAt}`. Rút lại đơn đã rút trả cùng kết quả; approved/rejected/removed/invitation/UNKNOWN trả 409. Người khác và ID không tồn tại trả 404. Quản lý DELETE vẫn là thao tác quản lý riêng.

Nộp lại tạo ID mới. Hồ sơ trước, câu trả lời và người/thời điểm xét duyệt được giữ lại; mỗi hồ sơ trả version hiện tại riêng, `previousApplicationId` liên kết chuỗi lần nộp. Version là token đồng bộ của bản ghi, không phải số revision nghiệp vụ. Unique index chỉ áp dụng bản ghi chưa lưu trữ, vẫn ngăn hai membership/đơn hiện hành. Lời mời đã đồng ý vẫn Pending cho đến khi quản lý duyệt; không auto-enroll. Đơn Pending cũ có source UNKNOWN chưa được phép tự rút cho đến khi có căn cứ phân loại nguồn.

## Migration và kiểm thử

Migration mới: `20261021140255_MembershipApplicationHistory`. Chạy qua cơ chế EF migration hiện có của ClubService. Bổ sung source/consent/invitation/withdraw metadata, liên kết lần nộp và bảng MembershipActions; giữ nguyên nội dung lịch sử. Upgrade từ schema trước đã có dữ liệu được kiểm tra bằng SQL Server. Không rollback mù về unique index cũ sau khi đã có nhiều bản ghi lịch sử cùng CLB/user.

Đã có regression HTTP bắt lỗi trước sửa cho F01/F02, version của PUT quản trị cũ và nộp lại mất lịch sử. Có test quyền, replay, null/omission, fail-closed, Gateway và SQL Server cho phân trang, migration, concurrent join và approve-versus-withdraw. Audit chặn sửa/xóa qua cả sync/async và overload acceptAllChanges. Demo reset xóa audit trước membership; re-seed chọn bản ghi hiện hành, không hồi sinh/ghi đè hồ sơ lưu trữ.

Kết quả cuối ngày 22/10/2026 trên workspace:

| Bộ kiểm thử Release | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| ClubReportHub.Tests | 123 | 0 | 0 |
| AdminService.IntegrationTests | 85 | 0 | 0 |
| Backend.StabilizationTests | 580 | 0 | 0 |
| **Tổng** | **788** | **0** | **0** |

Restore đạt; build Release `-warnaserror` đạt với 0 warning/0 error; EF xác nhận Club model khớp migration; format verify của AdminService/AuthService/ClubService và hai integration/stabilization test project đều đạt; `git diff --check` đạt. Chạy thêm bộ regression Quản lý/Authorization/DataIntegrity được **73 passed**, không cộng trùng vào tổng 788. Có thu thập XPlat coverage.

Admin/Finance SQL migration tests được bật bằng hai biến `ADMIN_SERVICE_TEST_CONNECTION_STRING` và `FINANCE_SERVICE_TEST_CONNECTION_STRING`, trỏ tới LocalDB test instance. Các test tạo/xóa database tạm theo prefix riêng; không áp migration vào database dùng chung. Không bỏ qua ba test SQL trước đây thiếu biến môi trường.

Các lệnh chính để tái hiện sau khi cấu hình SQL test server:

```powershell
dotnet restore ClubReportHub.sln
dotnet build ClubReportHub.sln --configuration Release --no-restore -warnaserror
dotnet test ClubReportHub.sln --configuration Release --no-build --logger trx --collect:"XPlat Code Coverage"
dotnet ef migrations has-pending-model-changes --project src/Services/ClubService/ClubService.csproj --startup-project src/Services/ClubService/ClubService.csproj --configuration Release --no-build
```

GitNexus dùng distribution 1.6.12 và receipt schema 4 đã kiểm tra theo source hiện tại. Một số quan hệ C# có receiver không xác định hoặc symbol ID rỗng, nên scope được đối chiếu thêm bằng source, đăng ký endpoint, HTTP và SQL regression; không coi một kết quả static rỗng là bằng chứng không ảnh hưởng. Các thay đổi so với thứ tự plan là tách withdrawal/history khỏi roster chưa chốt, bổ sung canonical account check có thể làm độc lập, và sửa version của PUT cũ khi regression phát hiện. Plan gốc được giữ nguyên.

## Phần còn lại giữ CHƯA HOÀN THÀNH

- P03/P04: đã có roster/import/carry-forward/eligible-members và xác nhận lời mời theo email trường hiện có. Notification có outbox; delivery live chưa nghiệm thu.
- P05: governance đa nhóm/quyền xuyên service còn thiếu.
- P06/P07: phần vận hành hồ sơ/publication/staffing/recruitment/completion đã có. Estimates/final-report award/settlement raw budget và mọi tính/cộng/thu hồi điểm mới **tạm hoãn theo yêu cầu chủ dự án**. FE application adapter còn yêu cầu expectedRawXp là số; BE trả null với trạng thái hoãn, cần FE xử lý riêng.
- P08: task → CLUB_CREDIT, wallet/redemption/reward/bundle và reversal/debt còn mở, phụ thuộc quy tắc điểm chưa chốt.
- P09: media owners/shape/FRAME và profile/resources chưa hoàn thành; chuẩn shape PNG/SVG cần chốt. P10 đã có source lịch học/calendar, xem bàn giao riêng; FE/live còn chờ nghiệm thu.
- +1/linear versus saturated ERI, PERIOD/CUMULATIVE, reset/rollover và các mục tùy chọn giữ trạng thái hoãn/cần đặc tả; không xóa.

Plan tổng thể vẫn chưa đạt toàn bộ Definition of Done. Những mục trên không được đổi nhãn thành hoàn thành chỉ vì route cũ hoặc test cũ tồn tại. Test ở workspace không xác nhận đã deploy, đã áp migration trên môi trường dùng chung hay FE đã chuyển consumer.
