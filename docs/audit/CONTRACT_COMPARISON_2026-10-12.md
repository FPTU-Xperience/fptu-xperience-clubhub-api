# Đối chiếu backend ClubHub với tài liệu FPTU Xperience

Ngày phân tích: **12/10/2026**. Đây là báo cáo phân tích mã nguồn, không phải biên bản nghiệm thu triển khai.

**Kết luận:** Backend hiện có nền tảng quản lý CLB, membership, hoạt động/điểm danh, báo cáo, ngân sách/quyết toán và hạ tầng vận hành. Backend **chưa đáp ứng đầy đủ phạm vi FPTU Xperience trong doc**. Các miền nghiệp vụ cốt lõi về trải nghiệm sinh viên — XP 6+1, rubric/normalization, ledger đóng góp, học kỳ/rollover, portfolio và đổi quà — chưa có triển khai tương ứng trong source được kiểm tra.

## 1. Phạm vi và nguồn đối chiếu

| Nguồn | Phiên bản / cách sử dụng |
| --- | --- |
| Backend `fptu-xperience-clubhub-api` | HEAD `f8ed7ef`, commit ngày 25/09/2026; working tree không có thay đổi tracked trước phân tích. |
| Doc `../fptu-xperience-doc` | HEAD `cf71b04`, commit ngày 16/09/2026. Đã đọc toàn bộ 13 file trong `documents/`, README và ghi chú mentor. |
| `feature-requests/` của doc | Tám file hiện đều rỗng, không cung cấp contract bổ sung. Nội dung yêu cầu nằm trong `documents/`. |
| Đặc tả chức năng | `screen-flows.md`, semester/member/org/activity/point/goods/history và hai tài liệu import/normalization. |
| Bàn giao giao diện | `UI_DESIGN_HANDOFF.md` và `AI_CONTEXT.md`: mô tả mock `/demo`, quy tắc phạm vi và đề xuất tích hợp. Không coi thao tác mock là chức năng backend đã làm. |
| Contract nội bộ backend | `API_ENDPOINTS.md`, `docs/admin-api-contract-status.md`, `docs/remediation/API_CONTRACT_CHANGES.md`, DTO và route thực tế. |

Doc hiện **không cung cấp một OpenAPI/JSON Schema đã chốt** cho toàn bộ sản phẩm. Mục 11 của handoff nói rõ các tên thao tác là đề xuất logic, không phải endpoint đang tồn tại. Vì vậy báo cáo đối chiếu yêu cầu chức năng, dữ liệu, quyền và chuyển trạng thái; không tự sáng tác đường dẫn API rồi kết luận backend thiếu đường dẫn đó.

`API_1.md` được nhắc trong kế hoạch backend cũ nhưng không có trong hai repo đang kiểm tra. Báo cáo này không chứng nhận đủ 121 method/path của tài liệu ngoài repo đó. Con số 34 route bổ sung trong status cũ cũng không được dùng làm tỷ lệ hoàn thành sản phẩm.

GitNexus được gắn với đúng repository; index ở commit `33bf7fe`, **chậm hơn HEAD 27 commit**. Đã dùng query/context và trace luồng tạo hoạt động, sau đó kiểm tra source hiện tại để xác nhận. Lệnh cập nhật index được thử nhưng không hoàn thành trong phiên và đã dừng. CodeGraph MCP không truy cập được index tại đường dẫn Windows này dù thư mục `.codegraph` hiện diện. Các kết luận bên dưới dựa trên source hiện tại, không dựa riêng vào graph cũ.

## 2. Kiến trúc hiện có và phần có thể tái sử dụng

Backend là solution .NET 8 gồm Gateway YARP, AuthService, ClubService, ActivityService, ReportService, FinanceService, ExportService, NotificationService, AdminService và KpiGrpcService; có shared library và DemoDataSeeder.

Các phần có thể tái sử dụng khi triển khai contract mới:

- Auth xác minh Google ID token, kiểm tra email allowlist trong DB, liên kết Google subject, cấp JWT/refresh token.
- Membership và assignment quản lý theo từng CLB; endpoint `/api/clubs/me/access` cung cấp quan hệ của actor tại từng CLB.
- CRUD hoạt động, đăng ký, check-in, điểm danh quản lý và thống kê tham gia.
- Workflow báo cáo, file đính kèm, export từ snapshot báo cáo; finance có proposal, settlement và giao dịch tiền.
- Hạ tầng Redis Streams, outbox, audit, background jobs, rate limiting và JWT freshness.

Không tái sử dụng KPI CLB hoặc giao dịch tiền như thể chúng là XP cá nhân hay ví đổi quà: chủ thể, đơn vị, nguồn ghi nhận và quy tắc nghiệp vụ khác nhau.

## 3. Ma trận yêu cầu so với source

“Có nền tảng” nghĩa là tìm thấy triển khai liên quan trong source; chưa đồng nghĩa đã kiểm chứng deployment. “Một phần” nghĩa là thiếu dữ liệu, quyền hoặc bước nghiệp vụ. “Chưa thấy” đã được kiểm tra qua graph, inventory model/DTO/route, DbContext/migration snapshot và tìm kiếm source; không chỉ suy từ kết quả graph rỗng.

| ID | Yêu cầu trong doc | Đánh giá | Hiện trạng / phần còn thiếu |
| --- | --- | --- | --- |
| R01 | University identity, onboarding sở thích/chuyên ngành/thời khóa biểu | Một phần | Có Google login + allowlist. Chưa có tích hợp danh tính trường, onboarding interests/major/timetable hay roster sinh viên theo học kỳ đang được kiểm tra thực sự. |
| R02 | Đổi ngữ cảnh CLB, quyền manager A không lan sang B | Có nền tảng | Có manager assignment, approved membership, `/me/managed`, `/me/memberships`, `/me/access`; các thao tác hoạt động kiểm tra đúng `ClubId`. Cần bổ sung permission cho vai trò mới và tài nguyên theo kỳ. |
| R03 | Khám phá CLB và hoạt động công khai | Một phần | Có directory/detail DTO và bộ lọc CLB. Route vẫn yêu cầu đăng nhập; hoạt động chỉ hiện trong các CLB actor được xem, chưa có `IsPublic`/public event projection. |
| R04 | Xin tham gia, duyệt/từ chối, rút đơn, đóng tuyển | Một phần | Có join/review/roster và kiểm tra consent. Chưa có rút đơn chính chủ; `JoinClub` không kiểm tra `IsRecruiting`. Form chỉ gửi lý do theo mock không đủ validation backend. |
| R05 | Danh bạ cộng đồng cho member và quản lý roster cho BOD | Một phần | API members/detail hiện dùng quyền quản lý membership. Member thường bị từ chối; chưa có DTO danh bạ cộng đồng tối giản riêng. Phạm vi thông tin member được xem còn cần chốt. |
| R06 | Org chart: chủ tịch, phó chủ tịch, thủ quỹ, quản kho | Một phần | Có manager assignment và membership `MEMBER`/`TREASURER`, hiển thị `CLUB_OWNER`. Chưa có phó chủ tịch, quản kho, cơ cấu ban hoặc permission tương ứng. |
| R07 | Tạo hoạt động theo kỳ, loại trải nghiệm, hạn mức XP, sức chứa | Một phần | Có title, lịch, địa điểm, trạng thái, nguồn report. Chưa có `SemesterId`, phân loại 6+1, hạn mức XP, capacity hoặc cờ public trong model hoạt động. |
| R08 | Đăng ký/hủy đăng ký, check-in bằng mã/phiên; mục tiêu dài hạn QR động/vị trí | Một phần | Có đăng ký idempotent và check-in theo thời gian/ngày Việt Nam. Chưa có hủy đăng ký, kiểm sức chứa, phiên do manager mở/đóng, mã/QR/vị trí, yêu cầu đã đăng ký hoặc ghi XP sau check-in. |
| R09 | Quest và xét đóng góp: minh chứng, lý do, cấm tự duyệt | Chưa thấy | Chưa có Quest, ContributionSubmission, hàng đợi xác nhận đóng góp hoặc transaction ghi điểm. Text `Contributions` trong membership không phải bản ghi đóng góp đã xác minh. |
| R10 | Self-reported experience: chuyển BOD/CTSV, minh chứng, tối đa 3 pending và 10/kỳ | Chưa thấy | Chưa có entity, route, bộ đếm giới hạn hoặc cơ chế phân công người duyệt. Báo cáo CLB hiện tại có chủ thể khác. |
| R11 | XP 6+1, rubric theo loại CLB, hệ số giờ/vai trò/category, phiên bản cấu hình | Chưa thấy | KPI hiện xếp hạng CLB theo báo cáo/hoạt động/participants, dùng trọng số cố định. Không có XP sinh viên, rubric engine hay normalization parameters theo kỳ. |
| R12 | Ledger đóng góp append-only, verified source, điều chỉnh/thu hồi truy vết | Chưa thấy | Có audit/outbox và sổ giao dịch finance nhưng chưa có Verified Contribution Ledger. Chưa có khóa chống ghi điểm lặp theo nguồn đóng góp. |
| R13 | Semester management, khóa kỳ, rollover/snapshot/reset | Chưa thấy | `ReportingDeadline` chỉ có `Period`, `DueDate`, `IsActive`. Không có semester entity, trạng thái kỳ, job rollover, snapshot hoặc guard chặn ghi vào kỳ lưu trữ. |
| R14 | Lịch sử thành viên theo học kỳ, vai trò/XP tại thời điểm chốt | Một phần | Có lịch sử hoạt động và thời gian assignment. Chưa có member snapshot theo kỳ, Season XP hay vai trò được chốt; dữ liệu roster hiện tại không thay thế lịch sử kỳ. |
| R15 | Hai kho quà, ví theo nguồn CLB/trường, threshold, đổi/trao quà | Chưa thấy | Không có Goods/Inventory/Redemption/XPWallet. Chưa có transaction giữ tồn và số dư, chống đổi món cuối cùng hoặc chống retry trừ điểm hai lần. |
| R16 | Portfolio private/public, Seasonal/Permanent Web Chart, export CV | Chưa thấy | UserSummary là identity cơ bản; membership có thông tin từng CLB. Chưa có profile headline/about, ledger cá nhân, biểu đồ 6+1 hoặc export portfolio. Export hiện bắt buộc `ReportId`. |
| R17 | Recommendation/cold-start tránh trùng lịch học | Chưa thấy | Có filter category/search/recruiting, chưa có recommendation engine, student interests hoặc timetable. Filter danh mục không chứng minh cá nhân hóa. |
| R18 | Coursera/EduNext/FAP webhook; CTSV tạo đơn/import Excel/CSV, auto-verify | Chưa thấy | Chưa có provider integration, webhook, import experience, mapping MSSV hoặc nguồn trusted auto-approval. DemoDataSeeder không phải luồng import nghiệp vụ. |
| R19 | CTSV dashboard engagement, rubric, semester, anomaly/integrity | Một phần | Có role CTSV, identity endpoint và quyền review CLB/report/finance. AdminService chủ yếu có `/me` và audit; chưa có các miền cấu hình/analytics XP trong doc. |
| R20 | System Admin identity integrations, ledger integrity, monitor jobs | Một phần | Có hạ tầng audit, health/rate limit/jobs; chưa có portal/API quản lý tích hợp và integrity của XP ledger. `SYSTEM_ADMIN` tồn tại như role legacy, chưa nằm trong danh sách role được Google login. |
| R21 | Báo cáo, tài chính tùy chọn và thông báo | Có nền tảng, cần mapping | Backend có workflow sâu hơn mock. Phải map DTO/trạng thái/quyền; transaction tiền không phải điểm. Chưa có `financeEnabled` theo CLB như demo và chưa có event thông báo XP/quà. |

## 4. Chênh lệch cần xử lý trước khi nối frontend

### C01 — Quy tắc điểm và học kỳ chưa có miền dữ liệu tương ứng

Doc yêu cầu điểm theo **user + CLB + semester + loại trải nghiệm + nguồn xác nhận**. Source KPI chỉ tổng hợp theo CLB/period từ report và dùng công thức:

`approvedReports × 50 + activities × 5 + participants × 0.1 − rejectedReports × 10 − overdueReports × 20`.

Nguồn: [KpiEndpoints.cs](../../src/Services/ReportService/Endpoints/KpiEndpoints.cs#L29), công thức tại dòng 131; yêu cầu: [point-distribute-system.md](../../../fptu-xperience-doc/documents/point-distribute-system.md), [point-normalization-layer.md](../../../fptu-xperience-doc/documents/point-normalization-layer.md).

Hệ quả: không thể nối màn Seasonal/Permanent Web Chart, ledger và reward shop vào `/api/kpis/leaderboard` rồi xem đó là đáp ứng contract. Cần chốt mô hình experience/ledger/semester/wallet trước khi mở rộng DTO.

### C02 — Check-in chưa đáp ứng điều kiện nghiệp vụ trong handoff

[CheckIn](../../src/Services/ActivityService/Endpoints/ActivityWriteEndpoints.cs#L191) lấy actor từ JWT, kiểm membership và thời gian hoạt động, chống trùng theo ngày. Đây là nền tảng tốt nhưng handler không kiểm tra đăng ký trong `ActivityParticipants`, mã check-in, phiên mở/đóng hoặc vị trí; request có body còn bị từ chối. Sau lưu attendance không có bước ghi XP.

Handoff dòng 168 và 293 yêu cầu đã đăng ký, hoạt động live, phiên mở, đúng mã và ledger một lần. Screen flow dài hạn còn yêu cầu QR động và vị trí. Không nên gọi endpoint hiện tại là QR check-in hoàn chỉnh.

### C03 — Chủ CLB chưa sửa được giới thiệu/mở tuyển theo handoff

`PUT /api/clubs/{id}` được bảo vệ bằng `StudentAffairsAdministration`, chỉ nhận `ADMIN`/`STUDENT_AFFAIRS_ADMIN`. Chủ CLB thông thường không qua được policy dù có assignment đúng CLB.

Nguồn: [ClubEndpoints.cs](../../src/Services/ClubService/Endpoints/ClubEndpoints.cs#L60), [JwtServiceCollectionExtensions.cs](../../src/Shared/ClubReportHub.Shared/Auth/JwtServiceCollectionExtensions.cs#L120); handoff dòng 188–190.

Cần một thao tác giới hạn field cho chủ đúng CLB; không mở quyền sửa toàn bộ DTO quản trị để hỗ trợ một màn settings.

### C04 — Đóng tuyển chưa chặn join ở server; rút đơn chưa được hỗ trợ

[JoinClub](../../src/Services/ClubService/Endpoints/MembershipEndpoints.cs#L62) chỉ tìm CLB đang active rồi validate hồ sơ; không kiểm `IsRecruiting`. Gửi HTTP trực tiếp vẫn có thể tạo pending khi UI đã đóng tuyển.

`DELETE /api/clubs/memberships/{id}` là thao tác manager/admin đưa membership sang `Inactive`; không phải rút đơn pending chính chủ. Các trạng thái membership hiện có `Pending/Approved/Rejected/Inactive`, chưa có `Withdrawn`.

Nguồn: [MembershipEndpoints.cs](../../src/Services/ClubService/Endpoints/MembershipEndpoints.cs#L391), [ClubWorkflowStatuses.cs](../../src/Services/ClubService/Models/ClubWorkflowStatuses.cs#L3); handoff dòng 288–289.

### C05 — Form xin tham gia trong mock không khớp validation backend

Mock chỉ nhập lý do. Backend yêu cầu thêm họ tên, ngày sinh, giới tính, email, số điện thoại và hai xác nhận quy định/tham gia; thiếu các trường này trả lỗi.

Nguồn: [JoinClubRequest](../../src/Services/ClubService/Contracts/ClubContracts.cs#L171), [ValidateJoinRequest](../../src/Services/ClubService/Extensions/ValidationExtensions.cs#L91).

Cần thống nhất UX intake hoặc nguồn dữ liệu hồ sơ thật; không tự điền thông tin giả để vượt validation.

### C06 — API roster quản lý chưa phục vụ trang cộng đồng của member

[ListClubMembers](../../src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs#L136) và `GetMemberDetails` đều gọi `CanManageMembershipsAsync`. Hàm này chỉ cho reviewer trường hoặc manager assignment đúng CLB. Member thường không được đọc.

Không nên bỏ guard khỏi endpoint hiện tại vì DTO chi tiết chứa thông tin cá nhân. Cần chốt phạm vi member được thấy rồi xây projection/community DTO tương ứng. Handoff cũng ghi đây là quyết định còn mở.

### C07 — Response hoạt động chưa tách dữ liệu quản lý khỏi dữ liệu member

List/detail hoạt động cho mọi actor có `CanView` gọi cùng [ToResponse](../../src/Services/ActivityService/Endpoints/ActivityEndpointHelpers.cs#L19), trả toàn bộ participants và attendances, gồm tên, trạng thái, ghi chú và người điểm danh. Chưa có nhánh projection riêng cho member chỉ cần trạng thái của mình như handoff mục 11.

Đây là chênh lệch phạm vi dữ liệu cần chốt, không phải bằng chứng user ngoài CLB đã vượt quyền. Guard theo CLB vẫn hiện diện. Nên tách DTO manager/member thay vì chỉ ẩn cột ở frontend.

### C08 — Lịch sử tham gia hiện tại chưa phải lịch sử học kỳ bất biến

[ClubMemberDetailResponse](../../src/Services/ClubService/Contracts/ClubContracts.cs#L303) chứa activity history và tỷ lệ tham gia, không có semester snapshot hay XP đã chốt. Membership lưu role hiện tại; assignment có mốc thời gian nhưng chưa cung cấp snapshot roster/role/XP cuối kỳ.

Lọc hoạt động theo thời gian hoặc `Report.Period` không tạo ra member history theo contract. Cần read model snapshot và policy chỉ đọc cho kỳ đã đóng.

## 5. Sai lệch của tài liệu API hiện tại

Đã xác nhận các điểm sau bằng route/DTO/handler hiện tại; bảng này không phải inventory đầy đủ mọi API:

| Tài liệu | Source hiện tại | Hệ quả khi FE làm đúng theo tài liệu |
| --- | --- | --- |
| `GET /api/clubs/my-memberships` | Route là `GET /api/clubs/me/memberships` ở ClubEndpoints dòng 35. | Gọi sai route. |
| Club list/detail được ghi `Public / Auth` | Gateway dùng `Default`, service dùng `BusinessAccess`; không có `AllowAnonymous`. | Khách chưa đăng nhập không đọc được. “Public DTO” không đồng nghĩa truy cập anonymous. |
| `PUT /api/clubs/{id}` ghi `Admin, ClubManager` | Policy `StudentAffairsAdministration`. | Chủ CLB nhận từ chối quyền. |
| Members/detail ghi cho `ClubMember, Manager` | Handler yêu cầu quyền quản lý membership. | Member thường bị từ chối. |
| Join example thiếu ngày sinh/giới tính/consent | Validation yêu cầu các trường này. | Example request không đủ để tạo đơn. |
| Membership review gửi `reviewNote` | DTO `ReviewClubMembershipRequest` có field `Note`. | Ghi chú có thể không được bind/lưu như FE kỳ vọng. |
| Activities dùng query `fromUtc`, `toUtc`, `includeStats` | Handler list nhận `from`, `to`; không có tham số `includeStats`. | Bộ lọc thời gian theo tên doc không được áp dụng. |
| Export gửi `{ "format": "Pdf", "reportId": 1 }` | DTO/handler đọc `ExportType`, bắt buộc report thật; giới hạn role tạo export. | Payload `format` không cung cấp `ExportType`; không hỗ trợ member export CV. |
| Status doc nói CTSV chưa Google sign-in được | HEAD đã thêm `StudentAffairsAdmin` vào `GoogleActorRoles`. | Tài liệu status lỗi thời ở phần CTSV; `SYSTEM_ADMIN` vẫn chưa được admit. |

Nguồn: [API_ENDPOINTS.md](../../API_ENDPOINTS.md), [admin-api-contract-status.md](../admin-api-contract-status.md), [ActorAccountPolicy.cs](../../src/Services/AuthService/Services/ActorAccountPolicy.cs#L8), [ExportContracts.cs](../../src/Services/ExportService/Contracts/ExportContracts.cs#L3), [ExportEndpoints.cs](../../src/Services/ExportService/Endpoints/ExportEndpoints.cs#L98), [ActivityEndpoints.cs](../../src/Services/ActivityService/Endpoints/ActivityEndpoints.cs#L39).

Không nên thay backend theo mọi example một cách máy móc: một số khác biệt xuất phát từ bảo vệ dữ liệu và invariant hiện có. Cần chốt contract rồi sửa doc/adapter hoặc endpoint có phạm vi rõ ràng.

## 6. Các quyết định doc chưa thống nhất

| Quyết định | Mâu thuẫn / điểm chưa chốt |
| --- | --- |
| Reset ví | `semester-management.md` và `point-distribute-system.md` nói điểm đổi quà/Season XP reset mỗi kỳ. Handoff dòng 267–269 dùng ví CLB tích lũy qua kỳ và không reset khi đổi kỳ; tài liệu cũng nói đây chỉ là quy tắc demo cần xác nhận. |
| Hai kho hay chỉ ví CLB | Đặc tả points/goods yêu cầu cả kho trường và kho CLB; mock/handoff chủ yếu mô tả kho CLB. Phải xác định phần nào nằm trong release đầu. |
| Cache toàn bộ nội bộ | `screen-flows.md`/`profile-joining-history.md` đề xuất load toàn bộ data quản trị vào localStorage. Handoff mới yêu cầu scope server, DTO theo quyền, cache theo user/CLB/kỳ và xóa khi logout. Không dùng mô tả cũ làm lý do cấp dữ liệu quản trị cho member. |
| Identity | Screen flow mô tả SSO trường; handoff mô tả Google + danh sách sinh viên DB; backend hiện dùng allowlist `Users`. Đoạn kiểm tra roster theo semester trong GoogleSignInService chỉ là comment, chưa thực thi. |
| Biên hệ số giờ | Normalization ghi dưới 2 giờ ×1, 2–8 giờ ×1.5 nhưng ví dụ hoạt động 2 giờ dùng ×1. Các mốc 8/20 giờ cũng cần xác định biên; cơ chế Work Experience chưa có công thức hoàn chỉnh. |
| Ngưỡng đổi quà | 30 ngày/50 XP hoặc 3 tasks là các ví dụ, không phải giá trị cấu hình đã được phê duyệt. |
| Chức danh và role | Doc dùng Student/System Admin/BOD nhiều chức danh; backend dùng `CLUB_MEMBER`, `ADMIN`, một global actor role và assignment/membership theo CLB. Cần mapping chính thức; nhiều vai trò tại các CLB không đòi hỏi nhiều global JWT role. |
| Quyền xem hồ sơ | Handoff còn mở câu hỏi member xem danh bạ nào, ai duyệt đóng góp của chủ nhiệm, ai mở lại kỳ, chính sách hoàn quà và truy cập finance. Cần quyết định trước API ghi. |

## 7. Thứ tự triển khai đề xuất

Đây là đề xuất từ phân tích, chưa phải thay đổi đã thực hiện.

1. **Chốt contract và release đầu:** thống nhất semester/XP/wallet, vai trò, visibility, ngưỡng, biên normalization và scope MVP. Xuất OpenAPI cùng example response/error từ backend, cập nhật API_ENDPOINTS và status bị lỗi thời.
2. **Hoàn thiện tích hợp CLB hiện có:** map ID số/trạng thái, intake hồ sơ, guard đóng tuyển, rút đơn, settings giới hạn cho manager; tách community/activity DTO theo quyền; hủy đăng ký/capacity/session check-in nếu thuộc MVP.
3. **Xây semester và ledger trải nghiệm:** experience 6+1, contribution/evidence, phân công reviewer, anti-spam, rubric version và transaction ghi điểm idempotent. Tách KPI CLB khỏi XP cá nhân.
4. **Xây đọc lịch sử và portfolio:** snapshot roster/role/leaderboard, rollover chạy lại an toàn, khóa ghi kỳ cũ; private/public profile và export CV từ dữ liệu đã xác minh.
5. **Xây reward:** catalog/tồn kho CLB và trường, wallet theo nguồn, redeem/fulfill/refund theo chính sách; chống âm tồn và double-spend.
6. **Xây integrations/analytics:** trusted webhook/import có xác thực và chống trùng, recommendation/timetable, anomaly/integrity khi có dữ liệu và quy tắc đáng tin cậy.

Nên giữ service hiện có nếu phù hợp và bổ sung miền nghiệp vụ thiếu; chưa có căn cứ từ báo cáo này để viết lại toàn bộ solution hoặc quyết định phải thêm một microservice cho mỗi feature.

## 8. Tiêu chí nghiệm thu cần bổ sung

- Manager A/member B không quản lý B; thay clubId/resourceId/userId không vượt scope.
- Đóng tuyển chặn join tại server; rút đơn chỉ cho chủ pending; duyệt lặp không tạo membership trùng.
- Request join theo contract có đủ dữ liệu hợp lệ và consent; FE không báo thành công trước response thật.
- Hủy đăng ký/capacity/session/token check-in đúng trạng thái; retry check-in không ghi attendance hoặc XP hai lần.
- Member chỉ nhận dữ liệu được phép; manager DTO không bị dùng làm DTO public/member.
- Self-report kiểm 3 pending/10 kỳ dưới concurrency; trusted import bypass đúng nguồn, không bypass bằng flag từ client.
- XP giữ loại 6+1, rubric version, giờ/vai trò/weight và verified source; cấm tự duyệt, note bắt buộc.
- Rollover chạy lại không cộng/trừ lặp; kỳ đóng chỉ đọc; lịch sử vai trò/XP không đổi khi sửa membership hiện tại.
- Hai request đổi món cuối cùng không âm stock; retry redeem không double-spend; trao quà không trừ điểm lần hai.
- Đổi quà không làm mất điểm thành tích; wallet CLB không dùng điểm CLB khác; reset/hết hạn tuân theo quyết định đã chốt.
- Profile tổng hợp chính chủ qua nhiều CLB; public DTO không lộ thông tin private; export CV dùng nguồn đã xác minh.
- Google allowlist/roster, revoke permission và refresh token có test theo policy đã chốt; role CTSV hoạt động trên luồng thật.

## 9. Kiểm chứng trong phiên này và giới hạn

Đã đọc tài liệu, đối chiếu route/DTO/model/permission và handler trọng yếu, xem migration snapshots, đọc các test contract hiện có và trace GitNexus cho luồng tạo hoạt động. Tìm kiếm source không thấy miền XP/semester/reward/profile/integration tương ứng; các từ xuất hiện trong fixture/comment không được tính là feature.

Đã thử:

```powershell
dotnet test ClubReportHub.sln -c Release --no-restore --logger 'trx;LogFilePrefix=contract-audit-20261012' --results-directory TestResults/contract-audit-20261012
```

Lệnh **dừng trước khi chạy test** với `NETSDK1127` do không giải quyết được targeting packs. `project.assets.json` của shared project yêu cầu reference packs **8.0.31**, trong khi bộ .NET 8 reference packs được liệt kê trong `C:/Program Files/dotnet/packs` là **8.0.28**. Cần restore môi trường phụ thuộc phù hợp rồi chạy lại; báo cáo không suy lỗi source từ lỗi môi trường này.

Các số liệu `287 passed` và “100% completed” trong README/CHECKPOINT là bằng chứng lịch sử cho kế hoạch remediation cũ. Chúng không xác nhận lần chạy hiện tại hoặc phạm vi FPTU Xperience mới. Chưa kiểm tra DB/deployment thật hay frontend E2E trong phiên này.

Không sửa mã ứng dụng, không migrate/seed DB, không deploy hoặc commit. Artifact được tạo trong phiên là báo cáo này.
