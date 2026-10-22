# Đối chiếu BE với contract FE mới — 21/10/2026

**Kết luận:** BE có nền tảng Sinh viên/Quản lý CLB đã triển khai theo phạm vi cũ, nhưng **chưa đáp ứng đầy đủ contract FE main mới**. Xác minh được **16 operation đang có consumer FE nhưng chưa có route BE** trong tập kiểm tra facade + staffing/recruitment. Ngoài ra còn các gói đề xuất chưa làm và ít nhất hai lệch DTO/read-write cần sửa trước khi xác nhận tích hợp trang CLB.

Đây là audit, không triển khai API hay sửa ứng dụng FE. Tài liệu này là mốc hiện hành cho **contract FE 3ffd00ed**; báo cáo ngày 14/10 giữ làm lịch sử, không dùng con số “Sinh viên 32/32” hoặc “64/74” để kết luận toàn bộ phạm vi mới đã xong.

## 1. Mốc source và cách đọc kết quả

| Hạng mục | Mốc đã kiểm tra |
| --- | --- |
| FE contract mới nhất sau fetch | `origin/main` — `3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42`, commit 19/10/2026 |
| FE working tree hiện mở | `update-contract` — `7faf84cb`; giữ nguyên thay đổi local `package-lock.json` |
| BE working HEAD và origin/main | `main` — `b066f96b9fd0ea304e7c498f6ddcc569b2c21ebf`, commit 15/10/2026; sạch trước audit |
| Đọc contract | 16 tài liệu nghiệp vụ active + 5 subset trong `implemented/`; đọc thêm conventions/lifecycle/inventory/proposal/QA |
| Nguồn bằng chứng | Endpoint, DTO, mapper, DbContext/migration, Program/Gateway, FE call site được ghim commit và test thực chạy |
| Phạm vi kiểm thử | Full solution BE; chưa kiểm tra browser/gateway/R2 đang deploy |

FE đang mở có `be-api-status.md` cũ, nhưng file đó đã bị thay thế trên main bởi index/lifecycle và `implemented/`. Audit đọc snapshot main bằng `git show`, không checkout/pull đè nhánh đang làm.

- **SOURCE_SUBSET_READY:** phần ghi rõ có source triển khai; không bao trùm extensions và không có nghĩa deploy đã đạt.
- **PARTIAL / CONTRACT_MISMATCH:** có nền tảng/route nhưng chưa đủ DTO, nghiệp vụ hoặc chuỗi read-write.
- **NO_BE_IMPLEMENTATION / NO_BE_ROUTE:** chưa có resource/nghiệp vụ hay method/path tương ứng.
- **OPTIONAL_DEFERRED / CONDITIONAL_NOT_SPECIFIED:** chưa làm, nhưng không tự tính là API MVP bắt buộc.

Script trích route độc lập tái hiện **285 declaration**, trong đó **269 public API declaration** sau bỏ internal/non-API; giữ các alias, **0 receiver chưa resolve**. Đây là source extractor, không phải OpenAPI/reflection hoặc xác nhận runtime. GitNexus được refresh local đúng HEAD; MCP còn báo cache chậm một commit và graph có flow bị cắt nên mọi nhận định quan trọng đều đối chiếu source hiện tại. CodeGraph chưa có index thực tế nên không dùng kết quả rỗng để kết luận.

## 2. Số liệu có thể dùng để lập việc

| Tập kiểm tra | Kết quả | Ý nghĩa |
| --- | --- | --- |
| Facade `src/services/api.js` | 113 wrapper = 104 có declaration + 2 composed + 7 thiếu | 104 không đồng nghĩa 104 API hoàn thiện |
| Consumer staffing/recruitment ngoài facade | Thêm 9 method/path thiếu | Scanner chỉ facade bỏ sót nhóm này |
| Hợp của hai tập thiếu trên | **16 operation riêng biệt** | Đã bỏ trùng theo HTTP method + normalized path |
| Toàn bộ đề xuất trong tài liệu | Còn nhiều hơn 16 | Có API chưa được FE gọi, alias/optional và luồng chưa chốt URL; không đưa tổng giả chính xác |

Không cộng số theo actor thành tổng chung vì một read có thể dùng cho nhiều actor. Không tính selection/inbox read tùy chọn như thiếu bắt buộc khi FE đã compose được từ API hiện có.

## 3. Những điểm cần xử lý trước khi ghi “hoàn thành”

### F01 · P1 — GET chi tiết CLB cho chính chủ thiếu version, ảnh bìa và lịch cấu trúc

GetClubById chọn ToResponse cho manager/SA, trong khi ClubResponse không có Version, CoverImageUrl hoặc Schedule. Chỉ ToPublicDetailResponse có các trường này. ClubPageTab đọc chính GET đó rồi gửi club.version khi lưu. Vì vậy tải bằng đúng tài khoản quản lý không cung cấp đủ dữ liệu để lưu text/ảnh theo contract; không thể xác nhận toàn bộ implemented/club-public-profile-api.md là Done.

**Hướng xử lý:** Bổ sung cùng version/cover/schedule vào projection quản lý và mapper, giữ dữ liệu private đúng quyền. Kiểm thử GET bằng manager → lấy version từ HTTP → PATCH text/PUT ảnh → reload; không lấy version trực tiếp từ DbContext trong test tích hợp này.

**Căn cứ:** [ClubEndpoints.cs:270](../../src/Services/ClubService/Endpoints/ClubEndpoints.cs), [ClubContracts.cs:48](../../src/Services/ClubService/Contracts/ClubContracts.cs), [ClubMappers.cs:117](../../src/Services/ClubService/Mappers/ClubMappers.cs), [ClubPageTab.jsx:114](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/src/pages/v2/club-workspace-page/tabs/ClubPageTab.jsx#L114), [CrossServiceWorkflowAuthorizationTests.ClubManager.cs:29](../../tests/Backend.StabilizationTests/CrossServiceWorkflowAuthorizationTests.ClubManager.cs).

### F02 · P1 — Đổi một ảnh CLB có thể làm mất ảnh còn lại

ClubPageTab gửi {[field]:url,expectedVersion}; facade gửi nguyên object. ClubPublicImagesRequest là full-state gồm LogoUrl/CoverImageUrl nullable. Field vắng được bind null; MediaAttachmentClient.VerifyAsync(null, current, ...) trả null thành công rồi handler ghi cả hai cột. Khi đã có version hợp lệ và ảnh mới được xác minh, đổi logo riêng có thể xóa cover; đổi cover riêng có thể xóa logo. F01 hiện còn có thể chặn sớm thao tác ở màn hình này.

**Hướng xử lý:** Chốt một wire contract: FE gửi cả hai giá trị cho PUT hoặc BE phân biệt omitted với explicit null trong partial update. Vì phạm vi hiện tại chỉ audit, chưa sửa bên nào. Bổ sung test đổi riêng từng ảnh, chủ động clear null, stale version và retry.

**Căn cứ:** [ClubPageTab.jsx:114](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/src/pages/v2/club-workspace-page/tabs/ClubPageTab.jsx#L114), [api.js:360](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/src/services/api.js#L360), [ClubPublicImageEndpoints.cs:13](../../src/Services/ClubService/Endpoints/ClubPublicImageEndpoints.cs), [MediaAttachmentClient.cs:14](../../src/Shared/ClubReportHub.Shared/Experience/MediaAttachmentClient.cs), [ClubPublicImageEndpoints.cs:31](../../src/Services/ClubService/Endpoints/ClubPublicImageEndpoints.cs).

### F03 · P1 — Luồng mời thành viên có route nhưng chưa kiểm tra sinh viên/roster học kỳ

InviteClubMember xác thực quyền quản lý, CLB hoạt động, role member và chống trùng; sau đó lưu userId/fullName từ request. Không resolve tài khoản/roster/campus học kỳ, không đọc semesterId. Metadata source/semester/roster revision và outbox thông báo mời chưa có. Đây là khoảng thiếu có thể quan sát trong source, chưa kết luận đã khai thác trên môi trường deploy.

**Hướng xử lý:** Làm roster authoritative và lookup nội bộ trước; kiểm tra lại khi mời, sinh viên đồng ý và manager duyệt. Giữ nguyên yêu cầu cá nhân đồng ý trước khi duyệt; bổ sung source/audit/version và outbox.

**Căn cứ:** [MemberManagementEndpoints.cs:64](../../src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs), [MemberManagementEndpoints.cs:109](../../src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs), [ClubWorkflowStatuses.cs:3](../../src/Services/ClubService/Models/ClubWorkflowStatuses.cs).

### F04 · P1 — Luồng cộng điểm hiện tại khác cơ chế action chờ báo cáo kết thúc

ReviewAsync hiện gọi AppendEffectAsync loại AWARD ngay khi approve đóng góp; calculator dùng tier/role/scale/result. Action contract mới yêu cầu completion VERIFIED chỉ có pendingRawXp, sau final report mới award và trừ raw budget. Chưa có application/action/assignment/completion/final-report settlement persistence; không được coi raw ledger hiện tại là đã đáp ứng luồng này.

**Hướng xử lý:** Chốt quyền duyệt cuối, công thức và cách cùng tồn tại/migrate với verified contribution trước khi bật award action; thiết kế unique source xuyên attendance/manual/action và transactional raw budget. Không thay công thức hoặc cộng thêm một lần chỉ để khớp màn hình.

**Căn cứ:** [VerifiedContributionEndpoints.cs:262](../../src/Services/AdminService/Endpoints/VerifiedContributionEndpoints.cs), [VerifiedExperienceService.cs:72](../../src/Services/AdminService/Services/VerifiedExperienceService.cs), [activity-action-staffing-api.md:52](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/activity-action-staffing-api.md#L52).

### F05 · P2 — Sinh viên ngoài CLB thiếu projection nguồn để gửi đóng góp của hoạt động campus-public

StudentActivityItem có nextSessionId/version/policy/pillarWeights nhưng không có effortTier/scale/full scoring snapshot. GET sessions đòi CanView của CLB hoặc reviewer. Sinh viên cùng campus chưa là thành viên có thể đủ quyền đăng ký/check-in nhưng không lấy được toàn bộ source context qua đường FE hiện tại. Không mở internal endpoint ra browser để bù thiếu.

**Hướng xử lý:** Bổ sung projection đã lọc quyền cho sinh viên vào detail/session context; giữ challenge và roster ngoài response đó. Test campus nonmember → đăng ký → check-in → lấy source → gửi contribution.

**Căn cứ:** [StudentExperienceSources.cs:16](../../src/Shared/ClubReportHub.Shared/Experience/StudentExperienceSources.cs), [ActivitySessionEndpoints.cs:68](../../src/Services/ActivityService/Endpoints/ActivitySessionEndpoints.cs).

### F06 · P2 — Membership đã có GUID version; vẫn thiếu source/capabilities/semester/withdrawal

ClubMembershipResponse có Version và mapper đã gán ConcurrencyToken. Vì vậy câu trong bảng Required backend changes của club-navigation-applications-api.md nói không có version là sai với source hiện tại. Những thiếu hụt source/canWithdraw/canAcceptInvitation/clubCode/logoUrl/semester và trạng thái WITHDRAWN vẫn đúng.

**Hướng xử lý:** Khi chỉnh contract, giữ và tái sử dụng GUID version hiện có; chỉ bổ sung metadata và lifecycle còn thiếu. Bổ sung predicate active-record thống nhất cho join/invite/reapply; không dùng manager DELETE làm rút đơn.

**Căn cứ:** [ClubContracts.cs:72](../../src/Services/ClubService/Contracts/ClubContracts.cs), [ClubMappers.cs:162](../../src/Services/ClubService/Mappers/ClubMappers.cs), [club-navigation-applications-api.md:42](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/club-navigation-applications-api.md#L42).

## 4. 16 operation thiếu có consumer hiện tại

Các path dưới đây được chuẩn hóa: `{}` là một ID; bỏ query string. “Có consumer” nghĩa có code gọi và điều kiện kích hoạt, không khẳng định mọi nút đang bật trong production. Mutation thiếu phải tiếp tục hiển thị unsupported/error đúng thực tế.

| # | HTTP | Path | Actor/chức năng |
| --- | --- | --- | --- |
| 1 | POST | `/api/clubs/me/membership-applications/{}/withdraw` | Sinh viên; `withdrawMyMembershipApplication` |
| 2 | GET | `/api/clubs/{}/eligible-members` | Quản lý CLB; `searchEligibleClubMembers` |
| 3 | GET | `/api/activity-applications/{}` | Quản lý CLB, CTSV/Admin; `getActivityApplication` |
| 4 | POST | `/api/activity-applications` | Quản lý CLB; `createActivityApplication` |
| 5 | PATCH | `/api/activity-applications/{}` | Quản lý CLB; `updateActivityApplication` |
| 6 | POST | `/api/activity-applications/{}/submit` | Quản lý CLB; `submitActivityApplication` |
| 7 | PATCH | `/api/activities/{}/publication` | Quản lý CLB; `setActivityPublication` |
| 8 | GET | `/api/activities/{}/staffing` | Quản lý CLB, Sinh viên, Người xác minh; `getActivityStaffing` |
| 9 | POST | `/api/activities/{}/assignments` | Quản lý CLB; `ActivityStaffingPanel.assignments` |
| 10 | POST | `/api/activities/{}/assignments/{}/cancel` | Quản lý CLB; `ActivityStaffingPanel.cancel` |
| 11 | POST | `/api/activities/{}/action-completions/{}/verify` | Người xác minh; `ActivityStaffingPanel.verify` |
| 12 | POST | `/api/activities/{}/recruitment` | Quản lý CLB; `ActivityStaffingPanel.createRecruitment` |
| 13 | PATCH | `/api/activities/{}/recruitment` | Quản lý CLB; `ActivityStaffingPanel.editRecruitment` |
| 14 | PATCH | `/api/activities/{}/recruitment/status` | Quản lý CLB; `ActivityStaffingPanel.recruitmentStatus` |
| 15 | POST | `/api/activities/{}/recruitment/positions/{}/join` | Sinh viên; `ActivityStaffingPanel.joinPosition` |
| 16 | GET | `/api/clubs/{}/activity-recruitments` | Quản lý CLB, Sinh viên; `QuestsTab` |

Nguồn: facade inventory tái chạy ngày audit, `activity-staffing.js`, `ActivityStaffingPanel.jsx`, `QuestsTab.jsx` tại commit FE đã ghim; đối chiếu toàn bộ route endpoint BE. Activity staffing có GET aggregate + 7 mutation và GET recruitment theo CLB. POST verify cần grant xác minh độc lập, không mặc định manager được tự xác minh.

Riêng SA approve/reject/request-revision application và báo cáo chốt action **chưa có URL chốt trong contract**. Chưa đưa những lệnh đó vào số 16, nhưng chúng là phần nghiệp vụ còn thiếu của chuỗi hoàn chỉnh. Gateway chưa có mapping riêng cho `/api/activity-applications`; những path staffing nằm dưới catch-all activity vẫn cần handler BE. Có catch-all không có nghĩa có API.

## 5. Tình trạng theo actor

| Actor | Nền tảng đã có để tái sử dụng | Phần thiếu/chưa hoàn thiện theo contract mới |
| --- | --- | --- |
| Sinh viên | Canonical profile 5 trường; own membership/access; consent; discovery/recommendation; registration/check-in; evidence/contribution/history/ledger/radar; notification | Rút đơn/reapply/source metadata; safe contribution source cho campus nonmember; action recruitment/join; hồ sơ mở rộng/media/cosmetics; task và club-credit wallet; timetable. Pins là preference nâng cấp; public directory là optional |
| Quản lý CLB | Membership list/detail/review/remove; owner/treasurer foundations; public text writer; basic image attach; activity/session/attendance; semester plan/statistics/PDF; ReportService/export hiện hữu | F01/F02; invitation eligibility/roster; safe semester/policy catalogs; activity application/action/staffing/publication; final report/raw budget; governance đa nhóm; resource gallery; tasks/credit/rewards |
| Người xác minh được phân công | Queue và scoped assignment, contribution/manual attendance reviews, upgrade/adjustment; không tự duyệt | Action-completion verification/pending state và final action report chưa có; cần safe projection/authority đã chốt. Chức danh trong CLB không thay grant |
| CTSV/Admin | Semester CRUD, account/admin foundations, experience policy approval/grants, existing club applications/owner transfer/report/finance approval | Roster import/revision/carry-forward; duyệt hồ sơ action và budget/final report; governance leadership bổ sung/rollover. +1/formula, O01–O14 cần quyết định sản phẩm |
| Thủ quỹ/quyền tài chính | FinanceService proposal/settlement/transaction và owner/treasurer giới hạn hiện tại | Tích hợp canonical duty/capabilities khi D06 triển khai. Finance VNĐ không đại diện raw XP budget hoặc CLUB_CREDIT; không suy ra phải xóa finance |

Thành viên BQL/Host/Staff là assignment/capability có scope, không tự tạo actor toàn cục hay quyền CTSV. Các actor cũ được đánh dấu bỏ qua trong phạm vi v1.4 không tự quyết định phạm vi main mới; phải xem lifecycle và quyết định sản phẩm tương ứng.

## 6. Rà từng tài liệu nghiệp vụ hiện hành

“Có nền tảng” trong bảng này không đồng nghĩa resource đề xuất đã có implementation. `implemented/` được ghi rõ trong tên file; governance proposal không đếm thêm một feature trùng với API contract.

| Contract | Trạng thái audit BE | Đã có | Còn thiếu/giới hạn |
| --- | --- | --- | --- |
| implemented/[canonical-self-profile-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/implemented/canonical-self-profile-api.md) | SOURCE_SUBSET_READY | GET/PATCH canonical 5 trường, principal, GUID version, validation; đồng bộ private member projection. | Chỉ displayName/about/skills/interests/phone. Phần mở rộng ở tài liệu riêng; cần live acceptance. |
| implemented/[club-public-profile-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/implemented/club-public-profile-api.md) | PARTIAL_CONTRACT_MISMATCH | PATCH text/schedule, validate category/semester, stale-version, persistence. | F01: GET cho manager thiếu version/cover/schedule; chưa thể xác nhận tích hợp xong. |
| implemented/[notification-summary.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/implemented/notification-summary.md) | SOURCE_SUBSET_READY | Notification list/read, recipient filtering, persistence và Gateway. | Chưa kiểm tra live delivery; event mời/đổi quà mới chưa có. |
| implemented/[student-experience-reads-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/implemented/student-experience-reads-api.md) | SOURCE_SUBSET_READY | Feed/detail/recommendation/summary/radar/contributions/ledger/history có handler và routes. | F05 source projection; khác biệt công thức/+1 và live acceptance vẫn mở; raw points không phải ví. |
| implemented/[verified-experience-operations-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/implemented/verified-experience-operations-api.md) | SOURCE_SUBSET_READY | Private evidence, declaration/contribution/review/upgrade/adjustment, scoped assignments/policy, sessions/manual attendance, plans/statistics/PDF. | Chỉ cơ chế hiện có; không bao gồm final action report, raw budget hay wallet; F04/F05. |
| [activity-action-staffing-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/activity-action-staffing-api.md) | NO_BE_IMPLEMENTATION | Tái sử dụng activity/session/verified ledger và internal authority hiện có. | Thiếu application 4 API, publication toggle, staffing 9 API; thiếu decisions/final report/budget. Integer version khác GUID; chính sách vẫn là proposal. |
| [activity-profile-resources-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/activity-profile-resources-api.md) | PARTIAL | Cover attach có; generic authorized activity detail có. | Thiếu public activity projection, resource/gallery/capabilities, upload-intent/commit/PATCH/detach và private delivery. |
| [club-contributions-points-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/club-contributions-points-api.md) | NO_BE_IMPLEMENTATION | Có verified raw contribution và private evidence dùng làm nền tảng. | Thiếu task/submission/review/credit correction, wallet/ledger/leaderboard/badges; không dùng Admin quest/Finance tiền thay thế. |
| [club-governance-groups-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/club-governance-groups-api.md) | NO_BE_IMPLEMENTATION | Membership, owner assignments/transfer, treasurer rule và verifier grants hiện có. | Thiếu term/group/multi-assignment/capability union/revisions/history/report snapshots/vice appointment/expiry; source projection và liên dịch vụ. |
| [club-member-invitations-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/club-member-invitations-api.md) | PARTIAL | Tạo Pending invitation, sinh viên consent, manager approve/reject hiện có. | Thiếu roster/eligible-members/invitation-semesters, invite/consent/review revalidation, canonical target, source/audit/outbox (F03). |
| [club-navigation-applications-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/club-navigation-applications-api.md) | PARTIAL | Own access/memberships + directory composition; review timestamps và GUID version đã có. | Thiếu source/action metadata, withdraw/reapply và durable pins; pins là nâng cấp tương lai. F06 sửa nhận định thiếu version. |
| [my-clubs-membership-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/my-clubs-membership-api.md) | PARTIAL | Own memberships/access/directory/join/review hiện có, không cần duplicate read. | Thiếu self withdrawal/WITHDRAWN/history/reapply và metadata; selection/inbox API riêng là tùy chọn. |
| [profile-cover-rewards-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/profile-cover-rewards-api.md) | NO_BE_IMPLEMENTATION | Canonical text và basic club/activity media có. | Thiếu catalog/inventory/wallet/redeem/equip/award-rules/results worker/publication scheduling. Tài sản/credit local không phải grant BE. |
| [public-image-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/public-image-api.md) | PARTIAL | Upload intent + verify + attach cho club-logo/club-cover/activity-cover của owner hiện hữu. | F01/F02; thiếu application-logo/student-avatar/student-cover/club-shape và policy kiểu file; R2/CORS/delivery live chưa kiểm tra. |
| [public-member-directory-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/public-member-directory-api.md) | OPTIONAL_DEFERRED | Public leaders/member count và private member list/detail hiện có. | Opt-in public list và per-club self consent chưa có; chưa cần cho batch bắt buộc, không đánh dấu hủy. |
| [self-profile-extensions-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/self-profile-extensions-api.md) | PARTIAL | 5 trường canonical text/phone đã persist. | Thiếu headline/dob/address/media/academic/layout/consent tùy phạm vi; academic cần nguồn authoritative; manager không sửa hồ sơ người khác. |
| [semester-student-roster-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/semester-student-roster-api.md) | NO_BE_IMPLEMENTATION | Semester CRUD canonical GUID/code và campus policy có. | Thiếu preview/commit/student-roster, revision/carry-forward/empty distinction/internal lookup; schema matching O-04 cần chốt. |
| [study-schedule-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/study-schedule-api.md) | NO_BE_IMPLEMENTATION | Semester authority hiện có; activity schedule là luồng khác. | Thiếu academic-calendar GET/PUT và study-schedule GET/PUT/DELETE/import, persistence, alias semester/campus, version và atomic bulk. |
| [workspace-semester-context-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/workspace-semester-context-api.md) | PARTIAL | Admin semester và semester plans đã có. | Thiếu workspace-semesters safe projection, manager-safe approved policy catalog, alias/reportPeriod/semester attribution thống nhất; không nới quyền backoffice. |
| [experience-source-projections-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/experience-source-projections-api.md) | PARTIAL | Discovery có source IDs/versions/pillars; session management/internal authority có. | F05; thiếu safe source context và manager-safe catalog; không mở challenge/internal service secrets. |
| [event-registration-extensions-api.md](https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/blob/3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42/docs/contracts/event-registration-extensions-api.md) | CONDITIONAL_NOT_SPECIFIED | Participant registration/check-in/session attendance hiện có. | Custom form/answers/withdraw/review là mở rộng có điều kiện; URL/schema/chính sách chưa chốt, không đếm như API đã thiết kế. |

## 7. Các nhóm API đề xuất còn thiếu, ngoài số 16

Đây là backlog theo resource để lập plan; không coi tất cả là scope bắt buộc và không đếm route chưa được chốt. Các suffix viết tắt phải dùng base path từ chính contract.

| Nhóm | API/resource đã được đề xuất nhưng BE chưa có | Lưu ý trước triển khai |
| --- | --- | --- |
| Semester/roster | GET club `workspace-semesters`, `invitation-semesters`, governance `semesters`; Admin POST `roster-imports/preview`, POST `roster-imports/{id}/commit`, GET `student-roster` | Cùng authority Semester GUID/code, không tạo nhiều catalog. Preview/commit atomic, campus, carry-forward và explicit empty |
| My Clubs | POST self membership-application withdraw; tùy chọn GET selection/inbox; tương lai GET/PUT/DELETE self pins | Withdrawal là bắt buộc trong D10; GET dedicated selection/inbox không bắt buộc khi composed read đủ. Có GUID version sẵn |
| Governance | GET permission-catalog/term/eligible-members/revisions/revision/history/report-submissions/submission; POST group/assignment-change/archive/report submission/leadership nomination/approve/reject; PATCH group | Owner transfer reuse; báo cáo BQL không kích hoạt quyền; custom group không cấp leadership; capability union và expiry phải áp dụng liên dịch vụ |
| Tasks/credit | GET/POST tasks, PATCH task, POST publish/close, GET/POST submissions, GET own/detail submission, PATCH revision, POST review/reversal; summary/points/me/ledger/leaderboard/badges; private evidence upload/download/delete | Cùng một club-credit wallet với rewards, không tái dùng raw XP như spendable balance. DELETE evidence đang ghi “same resource”: cần chốt URL rõ thay vì đoán /download |
| Rewards/cosmetics | GET self cosmetics; GET/POST catalog; PATCH reward; POST publish/retire/redeem; GET own receipts/wallet; GET/PUT award-rules; self profile equip fields | Chưa có bảng wallet/inventory/receipt/grants/worker. Schedule/unpublish/delete và settlement reads có yêu cầu nhưng chưa chốt đầy đủ routes. Physical fulfillment deferred |
| Academic schedule | GET/PUT `/api/academic-calendar/{year}/{term}`; GET self study-schedule block; PUT/DELETE slot; POST block import | 10W rồi 3W, self-only, server semester alias, block/cell version + atomic import; activity sessions không thay timetable |
| Activity resources | GET public activity; POST resource upload-intents/commit; PATCH resource metadata; authorized detail resources/capabilities | Cover attach đã có. Detach/delete/private delivery và cleanup cần policy/URL đầy đủ, không bịa thêm route |
| Optional directory | GET club members/public; nếu chọn per-club model thì PATCH self club-directory-consents/{clubId} | Opt-in mặc định false, giữ private roster; không viết cả hai consent model cạnh tranh |
| Existing route extensions | GET/PATCH self profile, GET student detail/safe session context, invite/consent/review, own membership, club detail, image writes | Đây là bổ sung DTO/nghiệp vụ cho route hiện có; không cộng thành route mới cho từng field |
| Action full lifecycle | SA decisions; execution activity/action rule/completion evidence/final report review/raw budget settlement | Contract có ý định nghiệp vụ nhưng chưa đủ URL/authority/numeric policy; phải chốt trước khi tính tổng API hoặc code award |

## 8. Quyết định contract còn mở và phần không nên làm trùng

- D01 — Action XP: basePoints/stacking/caps/authority duyệt cuối, final report và tương thích scoring tier hiện tại. Không coi proposal là policy đã duyệt.
- D02 — Publication và staffing: chọn integer aggregate version riêng hoặc thống nhất GUID qua adapter; giữ CLOSE khác unpublish; chốt khi đã có participant có được ẩn không.
- D03 — Điểm: raw activity budget, raw experience ledger, Admin quest XP, CLUB_CREDIT spendable wallet và VNĐ phải có nguồn/quy đổi riêng được duyệt; +1 tuyến tính so với saturation hiện tại chưa chốt.
- D04 — Roster: schema/identity matching O-04, import/carry-forward, valid-empty và campus; rollover reset điểm/office succession là các nghiệp vụ riêng chưa có contract đủ để đếm API.
- D05 — Media: public-image quy định shape PNG <=1MiB/transparent, reward editor mô tả PNG/SVG <=5MB và vectorization. Chốt một policy server và wire asset DTO trước khi publish shape.
- D06 — Task và recruitment: D07 tasks nhận CLUB_CREDIT và activity-recruitments nhận action pending XP là hai source khác nhau; phải chốt cách cùng hiển thị/quyền, không biến Admin quests thành cả hai.
- D07 — Custom registration/public directory/pins/physical rewards: xác định required/deferred, giữ nguyên phần optional; không cộng toàn bộ vào backlog MVP bắt buộc.
- D08 — Numeric limits, negative wallet reversal debt, award policies và leadership scope còn là proposed defaults. Repo fptu-xperience-doc được FE tham chiếu không hiện diện cạnh source; chưa có căn cứ xác nhận toàn bộ O01–O14 đã được duyệt.
- D09 — Hợp đồng legacy: chốt read projection quản lý và full PUT/partial image (F01/F02); chỉnh câu no version của memberships (F06). Các đoạn historical được lifecycle ghi rõ không tự trở thành yêu cầu hiện hành.

Không triển khai lại notification list/read, canonical text, verified experience ledger, session attendance, owner transfer, semester CRUD và club experience report đã có. Có thể tái sử dụng foundation sau khi kiểm tra scope. `PUT /api/clubs/{clubId}/members/{memberId}` là compatibility route đang từ chối sửa personal profile bằng 403 `PERSONAL_PROFILE_SELF_ONLY`; không “hoàn thiện” bằng cách cho manager sửa hồ sơ cá nhân. `GET /me/selection` hay inbox riêng là lựa chọn tối ưu read, không là hai API bắt buộc mới.

Đồng thời giữ report/export/finance legacy: FE vẫn gọi các route đó. Form báo cáo CLB/đầu kỳ/cuối kỳ dùng reportType/tag hiện có; chưa có sourceActivityId FK để chốt action awards. `CLUB_ACTIVITY:{id}` là traceability convention của FE, không phải backend-enforced activity settlement. Không xóa hệ thống cũ chỉ vì có proposal mới.

## 9. Kiểm thử thực chạy và giới hạn bằng chứng

Lệnh:

```powershell
dotnet test ClubReportHub.sln --no-restore --logger "trx;LogFilePrefix=contract-audit-2026-10-21" --results-directory .test-results/contract-audit-2026-10-21
```

| Project | Đạt | Lỗi | Bỏ qua | Tổng |
| --- | ---: | ---: | ---: | ---: |
| ClubReportHub.Tests | 123 | 0 | 0 | 123 |
| AdminService.IntegrationTests | 84 | 0 | 1 | 85 |
| Backend.StabilizationTests | 530 | 0 | 2 | 532 |
| **Tổng** | **737** | **0** | **3** | **740** |

Exit code 0; solution build được trong lần test này. Ba test bị skip do thiếu ADMIN_SERVICE_TEST_CONNECTION_STRING/FINANCE_SERVICE_TEST_CONNECTION_STRING là kiểm thử migration riêng của Admin/Finance. Các test SQL Server của nhóm ClubManager dùng LocalDB riêng có thực chạy đạt, gồm migrate/reload, source uniqueness, ledger immutability và rollback. Không nên mô tả rằng toàn bộ SQL Server tests đều bị skip.

Test profile hiện tại lấy version trực tiếp từ DbContext trước PATCH nên chưa chứng minh GET manager cung cấp đủ DTO (F01). Test decoder ảnh không chứng minh đã chạy signing/PUT/CORS/attach thật trên R2. Không viết thêm hoặc sửa code/test BE trong audit này; các regression scenario mới nằm trong hướng xử lý của từng finding.

Chưa thực hiện authenticated browser E2E, live Gateway, R2, policy approval trên hệ thống thật hay kiểm tra deployment/migration production. Test suite cũ qua không biến các route chưa tồn tại thành hoàn thành, cũng không xác nhận numeric policy đã được CTSV duyệt.

## 10. Thứ tự xử lý đề xuất cho BE

1. **Sửa lỗi tương thích đang chặn tích hợp:** F01/F02, canonical membership metadata và safe student source context; thêm test đúng chuỗi HTTP theo actor.
2. **Chốt contract trước các thay đổi điểm/quyền:** action formula/final approver/raw budget, GUID hay integer, +1, shape file policy, O-04 và scope optional. Chốt full PUT/partial image bằng một rule duy nhất.
3. **Semester/roster và membership:** safe catalogs, import preview/commit/revision/carry-forward, eligibility lookup, invitation revalidation, withdrawal/reapply và outbox.
4. **Application/action execution:** dossier/revision/decisions → activity chưa publish → staffing/recruitment → completion pending → final report/raw-budget settlement; chống award trùng xuyên source. Bổ sung Gateway cho application.
5. **Governance:** term/groups/capability union/history/report snapshots/protected leadership, cập nhật đồng thời quyền/recipients ở Club/Activity/Report/Finance; scope verifier vẫn độc lập.
6. **Theo scope đã chọn:** resources, task-credit-wallet-rewards, self media/layout/timetable. Public directory/pins/physical rewards chỉ đưa vào khi được xác nhận cần.
7. **Acceptance:** migration upgrade/backfill; actor/campus/club isolation; concurrency/idempotency/outbox/dependency failure; sau đó FE integration với tài khoản thực qua Gateway.

## 11. Dữ liệu kiểm tra và bàn giao

- [JSON đầy đủ](DOI_CHIEU_BE_CONTRACT_FE_2026-10-21.json): commit/hash tài liệu, 113 wrapper với declaration và nhãn nghiệp vụ riêng, 269 declaration BE, 16 call operation thiếu, 21 nhóm contract, findings và kết quả TRX.
- Hash trong manifest là nội dung FE gốc tại commit ghim, không phải file inventory đã tái sinh trong scratch. FE không bị checkout hoặc sửa; BE chỉ thêm tài liệu audit và cập nhật index.
- Số liệu dùng cho snapshot này. Khi một bên đổi commit cần chạy lại đối chiếu; không dùng nhãn Done của tài liệu kiểm kê để đánh dấu cả feature Done.
