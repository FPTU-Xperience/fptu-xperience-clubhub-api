# Kế hoạch hoàn thiện API BE theo contract FE mới

> Yêu cầu: nghiên cứu kỹ, lên plan hoàn thiện API còn thiếu và sửa lỗi cần thiết; **chỉ BE, chưa triển khai**.
> Evidence: BE `b066f96b9fd0ea304e7c498f6ddcc569b2c21ebf`; FE main `3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42` ngày 19/10/2026.
> GitNexus: refresh `node .gitnexus/run.cjs analyze --index-only --pdg`, runner 1.6.12 có identity hash khớp; 31.221 nodes/68.513 edges/585 flows. MCP từng báo cache behind 1 commit; current source vẫn là authority.
> Evidence provenance schema 2; global dirty digest `78e5c6f29d412d898b4121c3945ee8bf167fd487ea0eb6475977fc3d9a0b0323`; 43 cited paths sorted, chỉ loại trừ chính `docs/plans/2026-10-21-gitnexus-plan-backend-contract-completion.md`.
> Bảo toàn ba thay đổi tài liệu audit có trước plan và thay đổi local FE. Các tên file NEW bên dưới là đề xuất tạo mới, không phải source đã tồn tại.

## 1. Mục tiêu và phạm vi

Hoàn thiện các API BE để FE tích hợp bằng dữ liệu và quyền thật, sửa các lệch DTO/luồng nghiệp vụ cần thiết, có migration tương thích và kiểm thử theo actor. Chia thành các batch có thể nghiệm thu riêng, không gộp tất cả vào một lần sửa lớn.

[verified] Mốc audit hiện có 16 operation có consumer FE nhưng thiếu route BE, 21 tài liệu nghiệp vụ cần đối chiếu. 16 không phải tổng mọi API tương lai: có extension trên route cũ, API optional và luồng chưa chốt URL. Xem [audit chi tiết](../audit/DOI_CHIEU_BE_CONTRACT_FE_2026-10-21.md).

Không sửa ứng dụng FE; khi cần thay wire contract sẽ cập nhật tài liệu BE và ghi rõ tương thích cho người làm FE. Không xóa legacy/optional. Không thay hệ thống report/export/finance hoặc canonical profile/ledger đang có nếu chỉ cần bổ sung.

## 2. Hành vi hiện tại đã xác minh

| Vấn đề | Căn cứ source | Hệ quả cho plan |
| --- | --- | --- |
| [verified] Manager GET chọn ClubResponse không có Version/CoverImageUrl/Schedule | `ClubEndpoints.cs:270`, `ClubContracts.cs:48`, `ClubMappers.cs:13` | P01 thêm fields vào read và mapper; test phải lấy version từ HTTP |
| [verified] Nullable image PUT ghi lại cả logo/cover; omitted thành null | `ClubPublicImageEndpoints.cs:13-31` | P01 đổi parsing có presence; giữ wire URL và expectedVersion |
| [verified] Join lấy một bản ghi theo club/user, tái sử dụng rejected/deleted | `MembershipEndpoints.cs:110-140`; unique mọi lịch sử tại `ClubDbContext.cs:72` | P04 phải đổi toàn bộ active predicate, index và history, không chỉ thêm withdraw route |
| [verified] Invite dùng tên/ID client; consent/approve chưa consume roster kỳ | `MemberManagementEndpoints.cs:64`, `MembershipEndpoints.cs:63/185` | P03 authoritative roster trước; P04 revalidate ba thời điểm |
| [verified] Review và auto attendance ghi AWARD ngay | `VerifiedContributionEndpoints.cs:262`, `AdminExperienceEventHandler.cs:93` | Action report mode phải chặn cả hai nhánh và các nhánh upgrade/adjustment |
| [verified] Canonical writer tính tier/role/scale/result và phân bổ raw ledger | `VerifiedExperienceService.cs:66-112` | Không gọi writer cũ với basePoints do FE gửi; thêm typed trusted source adapter |
| [verified] Capability hiện tại dựa bool CanManage/CanManageFinance/CanView | `ClubAccessClient.cs:17-30` | P05 migration quyền theo action, giữ compatibility fields |
| [verified] Student detail thiếu full source, GET sessions đòi club-view/reviewer | `StudentExperienceSources.cs:16`, `ActivitySessionEndpoints.cs:68` | P02 safe source projection, giữ management/challenge riêng |

[verified] Nghiên cứu thêm FE pinned main cho thấy `ActivityApplicationPage.jsx` kiểm tra `validReceipt` với positive integer `id`, **integer `version`** và finite `expectedRawXp`. Vì vậy application cũng nằm trong ADR-01, không chỉ staffing và publication.

## 3. Kiến trúc và quyền sở hữu dữ liệu đề xuất

| Service | Giữ authority hiện tại | Phần mới đề xuất |
| --- | --- | --- |
| AuthService | Account/campus/self profile, public-media signing/verified image | Profile personal/media/equipped layout; self timetable; kiểm tra entitlements qua Club internal client |
| AdminService | Semester, approved scoring policy, verifier assignments, canonical XP effects/allocations | Roster revisions/import; calendar linked semester; typed action award consumer/receipt và nguồn +1 chỉ sau policy được duyệt |
| ClubService | Club/membership/owner/treasurer/semester plans | Governance, active application history, eligibility projection; tasks + CLUB_CREDIT wallet + catalog + entitlements + receipts trong cùng DB |
| ActivityService | Activity/session/registration/check-in/source | Applications/actions/staffing/recruitment/completions; closing reports; approved raw budget + unique award source/outbox; resources |
| Report/Export | Báo cáo CLB/đầu-cuối kỳ, file/export/PDF và luồng cũ | Tích hợp capability và source link khi cần; không coi report tag hiện tại là action settlement |
| Notification/Gateway | Recipient-safe list/read, forwarding/auth/rate limit | Consume outbox mới, dedupe; routing mới chính xác trước catch-all |

[inferred] Giữ các service hiện có, không thêm microservice. Tập trung local invariants vào một DbContext, dùng outbox/inbox/receipt cho side effect liên service. Không dùng transaction giả qua nhiều database.

Luồng action đề xuất:

```text
Application draft/revision → submit → SA review
  → một execution activity chưa published + approved action/raw-budget snapshot
  → publish → assignments/recruitment → execution/evidence
  → independent completion verification → PENDING_RAW_XP
  → immutable closing report → final authorized approval
  → Activity DB transaction: decision + budget spend + unique award source + outbox
  → Admin inbox/typed award transaction: canonical effects + allocations + receipt outbox
  → Activity receipt: CONFIRMED hoặc FAILED/PENDING thật
```

Raw XP budget nằm ở ActivityService để approval/report/spend/source award cùng transaction. Sổ XP cá nhân duy nhất vẫn ở AdminService. Khi Admin unavailable, source approval có thể committed nhưng trạng thái XP vẫn pending; API không được nói đã cộng điểm chính thức. Tái thử và đối soát bằng receipt/source ID, không giảm budget rồi gọi HTTP award trực tiếp.

## 4. GitNexus: phạm vi ảnh hưởng cần giữ trong triển khai

- [graph] `context(ToResponse, ClubMappers.cs)` + `impact(upstream,maxDepth:3)` trả **HIGH**, 4 direct callers: `GetClubById`, `CreateClub`, `UpdateClub`, `AssignManager`. [verified] Source còn có method-group ở managed club reads; P01 kiểm tra cả GET managed/manager list và response create/update/appointment. Không chỉ sửa public GET.
- [graph] `impact(ClubAccessSnapshot record UID, upstream,maxDepth:3)` trả **CRITICAL**, 56 affected; d=1 gồm `GetMyAccessAsync` và `FetchAccessFromApiAsync`. Một truy vấn d=1 riêng đã xác nhận đủ 2 phụ thuộc. [verified] Caller đọc cache/deserializes `/api/clubs/me/access`; P05 phải kiểm tra Report/Finance/Activity/Export/Notification/media trước khi cắt sang capability.
- [graph] `impact(AppendEffectAsync,upstream,maxDepth:3)` trả **CRITICAL**, 5 d=1: `ReviewAsync`, `ReviewUpgradeAsync`, `AdjustAsync`, `AttendanceAsync`, `CancelAsync`; d=2 gồm legacy declaration bridge và event dispatcher. [verified] Từng d=1 được source/literal scan xác nhận; P07 phải account tất cả, đặc biệt QR/manual auto-award.
- [graph] `query(ApiOperations ExecuteAsync...)` tìm `ApiOperationReceipt.cs:40-125` và SQL rollback test. [verified] Có SERIALIZABLE local transaction, response receipt/fingerprint, execution strategy và rollback; foundation này được tái dùng, không viết lại.
- [graph] `query(CanManageMembershipsAsync...)` tìm owner/admin helper và treasurer concurrency tests. [verified] D06 là thay authorization model liên service, không là một dropdown role đơn lẻ.

Giới hạn: graph process extraction báo bỏ entrypoint/callee theo budget; MCP từng báo behind dù local metadata đúng HEAD. Các count graph dùng để chọn vùng test, không dùng làm bằng chứng không có caller. CodeGraph đã được audit trước đó: thư mục có nhưng chưa có index thực tế, không tự index.

## 5. PDG và các thứ tự xử lý phải bảo toàn

| Slice thực chạy | Kết quả chọn lọc | Ràng buộc triển khai |
| --- | --- | --- |
| [graph] controls `ClubPublicImageEndpoints.cs`, 14/14 edges | Ownership → club exists → active/version → verify logo → verify cover → write hai field/version | [verified] Parsing mới giữ đầy đủ guards; chỉ mutate field có mặt; mọi lỗi phải rollback |
| [graph] controls `JoinClub`, bounded 18/28 edges | `existing != null` → invitation-consent branch hoặc rejected/deleted branch line 125 → overwrite metadata line 127 | [verified] Reapply phải tạo history record mới và chọn active row; không revive/ghi đè rejected/withdrawn row |
| [graph] flows `AppendEffectAsync`, variable `after`, 1 edge | Parameter → tier score/weights/calculation snapshot/effective attributes | [verified] Action base-point approval không tương đương legacy tier calculation; typed adapter và policy version riêng |

[verified] `ApiOperations.ExecuteAsync` phải được gọi sau current resource authorization, kể cả replay; receipt trả trước callback check stale resource. Vì vậy retry cùng key/payload trả response gốc hợp lệ, nhưng quyền hiện tại vẫn được kiểm tra. Thay request image cần fingerprint giữ khác biệt omitted/null.

[inferred] Mọi workflow mới giữ trật tự: scope/identity → input/state/version → local DB transaction/CAS → immutable history/receipt/outbox → commit → async receipt. External object/file writes có thể thành orphan khi DB save thất bại; cleanup chỉ xóa object không còn reference.

[inferred] API mới dùng paged envelope chuẩn và stable error DTO `message/code/traceId/errors`, giữ các field tương thích cũ. Dùng route-scoped response normalization/typed errors cho batch mới; nếu cần đổi shared ApiOperations thì phải có impact riêng trước edit và regression cho key/state errors. Không đổi mọi legacy error trong một refactor ngoài phạm vi. Receipt retention tối thiểu 24 giờ; replay vẫn kiểm tra current authorization, không tự xóa receipt sớm để cho cùng key chạy lại.

## 6. Thiết kế thay đổi theo nhóm

### 6.1 Sửa F01/F02 và source reads (P01/P02)

- [inferred] `ClubResponse` thêm non-positional init properties `version`, `coverImageUrl`, `schedule`; `ToResponse` map cùng source với public detail. Giữ constructor và fields private hiện hữu, không gửi roster ra public DTO. P01 không cần DB migration vì source entity đã có dữ liệu.
- [inferred] `MapClubPublicImageEndpoints` nhận presence-aware request (`JsonElement` hoặc Optional DTO có converter), whitelist field + reject duplicate/unknown, required GUID version. Omitted giữ nguyên, explicit null clear, both fields vẫn hỗ trợ client full-state cũ; chỉ expectedVersion không có thay đổi trả validation error. Đây là BE compatibility được ghi lại trong contract.
- [inferred] Student detail thêm `sessions/sourceContexts` đã lọc principal hoặc read con tại student prefix. Chọn DTO trong P00; không expose internal URL/token, challenge, geofence quản trị hay roster. Bao gồm đúng source/session/activity/policy versions, allowed action, tier/scale/allocation khi là legacy source. Historical eligible registered student vẫn dùng được source đã hoàn thành; cancelled source chặn ghi mới.
- [inferred] Club workspace-semesters và invitation-semesters là hai projection của **cùng** Admin Semester; echo GUID/code và alias/reportPeriod rõ. Approved-policy catalog là club-safe read, không nới `BackofficeUser`. Unavailable catalog trả 503 có code, không tạo current semester từ browser clock.

### 6.2 Roster và membership/history (P03/P04)

Schema mới đề xuất: `SemesterRosterRevision`, `RosterImportPreview`, `RosterEntry`, carry-forward provenance ở Admin; campus/semester/current revision/version, canonical user ID và student-code matching authority theo Q-03. Import không tạo User. Preview lưu normalized rows/hash/errors/diff/expiry; commit xác nhận base revision, actor/campus, expectedVersion và idempotency key trong một transaction.

Membership bổ sung source APPLICATION/INVITATION/UNKNOWN, semester key, roster revision khi mời, invitation/consent/withdraw actors/times và immutable transition/history. Giữ GUID version. Source cũ chỉ backfill từ evidence đáng tin; không suy diễn false consent là một invitation đã xác nhận.

- [verified] Unique club+user hiện áp dụng mọi lịch sử. [inferred] Đổi thành filtered active uniqueness cho Pending/Approved chưa soft-delete; nếu provider cần cột ActiveSlot thì model/query/constraint phải cùng predicate và test cả SQL Server/SQLite.
- [inferred] Withdraw đúng chủ, APPLICATION+Pending, canonical WITHDRAWN response; không dùng manager DELETE/Rejected. Callback stale/decision races bị 409, retry thành công replay cùng receipt. Reapply tạo ID mới/giữ answers/decisions cũ. Invitation decline/expiry không tự gộp vào withdraw.
- [inferred] Audit tất cả query theo club/user: join, invite, owner bootstrap/transfer, treasurer và internal scope, không đổi global query filter thành active-only làm mất history. Own generic membership read vẫn giữ lịch sử; active workspace access chỉ dùng active approved row. Inbox bảy ngày có thể FE lọc như hiện tại; dedicated paged inbox chỉ thêm nếu cần và phải filter trước paging.
- [inferred] Invite lấy canonical target identity, student actor/account/campus và selected roster revision từ server. Lookup exclusion dùng đúng active predicate; kiểm tra lại khi gửi, consent và approve. Outbox cùng invitation transaction; notification fail không rollback lời mời đã committed. Không đòi email channel nếu chưa có requirement.

### 6.3 Governance (P05)

Schema đề xuất: term theo `(club,Semester.Id)`; immutable revision; stable group IDs; assignment effective intervals; change sets/per-person before-after capability snapshots; BQL report submission snapshot; protected vice nomination/appointment. Default groups khởi tạo rỗng, không cấp grant tự động.

Union chỉ các group assignment còn hiệu lực, approved membership/account và scope hợp lệ. Report submission không kích hoạt/revoke quyền. Owner transfer dùng workflow đã có; vice chỉ có quyền sau CTSV approval và effective date. Custom permission không tạo leadership/platform privilege. Treasurers đếm unique people/duty qua cả legacy/custom groups, tối đa 2 và owner không đồng thời treasurer theo policy hiện hành.

`ClubAccessSnapshot` thêm capabilities/accessVersion/governanceRevision/validUntil/protected leadership. **Không** đặt IsManager=true cho mọi BQL; các boolean là projection tương thích đã định nghĩa. Áp dụng per-action checks ở Club/Activity/Report/Finance/Export/Notification/media, cùng canonical finance recipients; không để independent legacy writer cấp lại quyền bị revoke. Sensitive writes đọc authority hiện tại/bypass cache và version; outage fail closed. Expiry được đánh giá theo server clock ngay cả worker chưa chạy. Distributed authority race/linearization phải có test và semantics rõ, không hứa cache invalidation đồng bộ tuyệt đối qua nhiều DB.

### 6.4 Application/publication/staffing (P06/P07A)

- [inferred] Dossier lưu draft/revisions/review decisions; actions có stable rule ID, clientId, versioned proposed base/role/scale/count caps/verification mode. Server tính estimates bằng policy projection; estimate là dự toán, không award. Publish/approve bị chặn nếu thiếu approved policy. Numeric defaults phải được Q-01 chốt.
- [inferred] Application public API giữ 4 method/path FE đang gọi. Application ID/version là positive JS-safe integer; expectedRawXp finite decimal serialized number, không giả 0 khi dependency/policy tính dự toán unavailable. Submit giữ revision immutable; request-revision tạo revision mới.
- [inferred] SA decision URL **mới đề xuất** `POST /api/activity-applications/{id}/reviews` với APPROVE/REJECT/REQUEST_REVISION, reason, expectedVersion và key. Scope campus/club + non-self final review; unique decision/source revision. Approve transaction cùng Activity DB tạo một execution activity chưa published, immutable approved actions/policy/source snapshot và raw-budget record. SourceApplicationId có unique constraint.
- [inferred] `ClubActivity` thêm server-owned scoring mode LEGACY_VERIFIED/ACTION_REPORT và source link. Existing rows backfill LEGACY_VERIFIED; không đổi điểm lịch sử. Action activity cấm generic writer đổi approved actions/blueprint. GUID activity version vẫn tồn tại; application/staffing/publication có các integer CAS theo wire của chúng, increment checked và không vượt JS safe integer.
- [inferred] Publication chỉ approved action source: bool/isPublished + publicationVersion, không map hide sang CLOSE. P00 chốt published deep-link/cancel/đã có participant; ưu tiên giữ self history và thông báo trạng thái thay vì xóa. Discovery/recommendation lọc trên server.
- [inferred] Staffing/recruitment một aggregate authoritative, 9 method/path đúng FE; assignment/position gắn approved action, không cấp office hay reviewer quyền. Last seat reserve dưới transaction+constraint; identical joins không tạo duplicate active participation. Eligible assignees là scoped active source, không trường directory công khai.
- [inferred] Bổ sung completion submission/evidence/occurrence workflow **đề xuất** nếu chưa có contract: người thực hiện submit, independent verifier verify/reject/revision, typed version và immutable revisions. Staff assignment đơn thuần không phải bằng chứng hoàn thành. Routes/payload bổ sung phải ghi manifest trước khi tính là feature hoàn chỉnh.

### 6.5 Final report/raw-budget settlement/canonical XP (P07B)

Resource mới đề xuất: `GET/POST /api/activities/{id}/closing-reports`, `GET/PATCH .../{reportId}`, `POST .../{reportId}/submit`, `POST .../{reportId}/reviews`. Đây là BE-owned proposal, chưa có reviewer FE; không coi URL này là contract đã được chốt trước đó.

Freeze snapshot actions/occurrences/users/evidence/verified counts/policy/budget version khi submit; mọi thay đổi source sau preview phải 409 hoặc làm revision mới. Approval cuối chỉ actor/policy được Q-01 xác nhận; cấm tự duyệt effort của mình. Rejected/revision không ghi official XP.

Activity transaction ghi immutable report decision + per-user/action/occurrence unique award source + raw budget spend + outbox + receipt. Index/caps kiểm tra tổng raw spend/occurrences trên server với decimal precision, không lấy amount từ request. Có khóa source chung ngăn attendance/manual/action cùng effort được award hai lần; Q-01 chốt mapping nhiều action/session, không dùng chỉ mỗi session key cho toàn bộ action độc lập.

Admin action consumer xác thực typed immutable approved source/policy, inbox eventId và unique business source award ID; tạo canonical contribution/effects/allocations + receipt outbox trong cùng transaction. Add calculation discriminator/snapshot cho ACTION_REPORT, không ép các giá trị vào tier. Có thể dùng typed low-level writer mới; `AppendEffectAsync` tiếp tục làm legacy wrapper giữ nguyên công thức. Student ledger/history/radar/report đọc cùng canonical effects; update calculation rendering/count/mastery proof có discriminator để không deserialize action như tier attributes.

**Chống bypass bắt buộc:** event AttendanceAsync gặp ACTION_REPORT chỉ ghi attendance/pending completion, không AWARD; generic contribution review/upgrade/adjustment không nhận source action để cộng tier. Check cả QR và manual attendance. Cancel trước award invalidate pending; cancel sau award dùng audited compensating effects, không xóa history. Budget refund/correction cần policy, không tự làm ngân sách tái sử dụng khi revoke.

### 6.6 Tasks/CLUB_CREDIT/rewards (P08)

Task credit và action recruitment là hai resource khác nhau. ClubService giữ task/submission/revision/review, private attachment metadata, wallet + immutable CREDIT/DEBIT/REVERSAL ledger, catalog/version/publication windows, redemption receipts và entitlements. `(club,user)` một wallet; `points/me` và `wallet/me` cùng projection, không hai số dư.

Task approve derive walletPoints từ published immutable policy, non-self reviewer, unique award source và outbox. Nếu cùng DB wallet ghi credit atomic; nếu dùng async exposure phải truthful creditStatus/receipt. Earned total dùng cho leaderboard khác balance sau redemption. Badges có configured rule/source, không sample counters. Reversal/debt theo Q-05 trước live credit.

Redemption trong một Club DB transaction: current active membership + issuer/catalog availability/version + server price + ownership + wallet CAS → debit once + bundle grants + receipt + outbox. Zero-price/zero-balance hợp lệ; đã sở hữu không charge lại; partial bundle theo rule được chốt. Retire/tombstone chặn claim mới nhưng giữ asset/grants lịch sử. Scheduling so sánh server UTC interval ngay cả worker chậm, manual override clear schedule có version.

GET `/api/users/me/cosmetics` nếu route đến ClubService phải có Gateway exact override; Auth canonical equip kiểm tra real grant qua authenticated internal call. Ownership cross-club reusable theo issuer provenance, không cho manager equip người khác. Automatic reward worker chỉ consume finalized authorized result có immutable resultVersion, unique grant key. Contest catalog/final ranking/physical fulfillment chưa có authority thì ghi deferred, không mint bằng client rule/date.

### 6.7 Media, profile, resources và timetable (P09/P10)

Media owner kinds riêng cho new/revised application logo, self avatar/cover và club shape. Self identity từ principal; application ID không là club ID. Giữ verified immutable object pattern hiện có; cleanup staged/unreferenced object sau reference check. Chốt Q-04 trước khi server nhận shape SVG/vector assets; browser sanitization không là authority.

Activity resources có upload-intent → direct PUT → server verify/quarantine → commit metadata, resource capabilities và per-resource version; PUBLIC/CLUB không lẫn URL. Private file delivery current author/reviewer scope, không public object URL. Có metadata PATCH, detach policy/cleanup và source binding; repeated commit không attach cùng intent sang owner khác. Chốt rõ DELETE private evidence resource thay vì đoán “same resource” là `/download`.

Self extensions phân biệt personal writable và academic/identity read-only có source authoritative. Giữ 5 field hiện tại, GUID expectedVersion, omitted/null semantics; bổ sung headline/date/address/media/layout chỉ sau whitelist/schema/source rõ. Equip phần cần inventory hoàn tất sau P08; P09 media/private attachment foundation đi trước P08, tránh dependency vòng.

Academic calendar do AdminService sở hữu theo Semester/campus/alias, Gateway `/api/academic-calendar`; timetable self-only ở AuthService theo year/term/block/week/day/slot. 10W rồi 3W, dùng local calendar dates/slot IDs; bulk template skip/replace toàn block atomic, CAS block/cell; đổi calendar không xóa lịch riêng. Không dùng activity sessions làm timetable.

## 7. Thứ tự triển khai và điều kiện dừng từng batch

Không batch nào được đánh dấu hoàn thành khi mới thêm skeleton route. P00 ghi chính sách chưa chốt là gate đúng batch; không giữ các bug fix độc lập chờ mọi quyết định của toàn dự án.

| Batch | Phụ thuộc | Việc triển khai | Điều kiện nghiệm thu |
| --- | --- | --- | --- |
| **P00 — Chốt wire contract và policy có ảnh hưởng nghiệp vụ** | — | Lập operation manifest BE, phân loại required/optional/proposed; ghi ADR version, media omitted/null, scoring mode, award/final approver, roster O-04 và asset policy. Đặc tả DTO/errors/paging/idempotency/OpenAPI examples cho từng batch trước khi viết handler. | Mỗi operation có actor, owner service, state machine, version type, source authority và test scenario. Quyết định chưa có căn cứ được giữ OPEN; batch không phụ thuộc vẫn triển khai được. |
| **P01 — Sửa lỗi đang chặn tích hợp: DTO quản lý CLB và cập nhật ảnh** | P00 | Thêm init properties Version/CoverImageUrl/Schedule vào ClubResponse và mapper; sửa image PUT phân biệt omitted với null, giữ ảnh không gửi lên; validate unknown/duplicate field và required version. Không sửa FE. | GET manager → lấy HTTP version → PATCH text/PUT từng ảnh → reload thành công; bỏ field giữ nguyên, null clear, stale 409; member/cross-club bị chặn. Không cần migration cho các cột đã có. |
| **P02 — Safe semester/policy catalogs và source context cho sinh viên** | P00 | Dùng Admin Semester GUID/code làm authority; thêm club workspace-semesters/invitation-semesters và approved-policy projection qua internal client có scope. Bổ sung student-safe session/source read vào detail có quyền, kể cả campus nonmember đã đăng ký và lịch sử hoàn tất. | Manager không cần quyền backoffice vẫn chọn được học kỳ/policy hợp lệ; mapping year/term/reportPeriod rõ. Sinh viên campus nonmember lấy đúng source/version để nộp đóng góp; không nhận challenge/roster/evidence người khác. |
| **P03 — Roster theo học kỳ: preview, commit, revision và carry-forward** | P02, P00 | AdminService lưu roster/import preview/revision/campus/provenance; preview CSV/JSON theo schema được chốt, canonical identity lookup qua Auth; commit expectedVersion+key atomic; protected internal lookup và paged admin roster. | Phân biệt chưa import, explicit empty, carry-forward; không tạo tài khoản ngầm. Preview stale/expired/wrong-campus bị từ chối; import lỗi không thay roster. Có migration nâng cấp và audit. |
| **P04 — Membership: metadata, rút đơn/reapply và lời mời đúng eligibility** | P02, P03 | Thêm source/semester/rosterRevision/inviter/consent/withdraw/audit; giữ GUID version có sẵn. Lưu lịch sử không ghi đè, đổi uniqueness sang active Pending/Approved. Thêm withdraw; nâng invite, consent, approve và eligible search với live authority/outbox. | Một active record/user/club dù join/invite/withdraw/approve đua nhau; retry withdraw trả receipt cũ; reapply có ID mới và giữ đơn cũ. Manager không consent hộ; invitation không auto-approve. Source UNKNOWN không tự quảng cáo quyền withdraw. |
| **P05 — Governance đa nhóm và quyền có scope xuyên service** | P02, P04 | Term/group/assignment intervals/revisions/change sets/per-person history/immutable BQL submissions; catalog và union capability; protected owner transfer reuse và vice nomination/review. Tích hợp read/write capabilities, treasurer duties và access invalidation đồng thời ở service liên quan. | Báo cáo BQL không cấp quyền; đổi nhóm/revoke/expiry ảnh hưởng quyền đúng thời điểm; custom group không tự cấp leadership/CTSV; một người được nhiều nhóm nhưng tối đa hai người giữ treasurer duty. Giữ DTO compatibility, không biến mọi BQL thành IsManager. |
| **P06 — Hồ sơ tạo hoạt động, duyệt và publication** | P02, P05, P00 | ActivityService dossier/revision/action proposals/review history, server estimates, submit; SA review resource đề xuất. Approve tạo đúng một activity chưa publish và approved raw budget/snapshots. Bổ sung bool/integer publication API cho action activities; bảo toàn lifecycle legacy. | 4 API dossier và publication hiện FE cần hoạt động; draft/revision chỉ sửa đúng trạng thái; approve/retry không sinh hai activity; publication theo source approval, actor và integer CAS. CLOSE không được dùng như unpublish. |
| **P07 — Staffing/recruitment → completion pending → final report → XP** | P06, P05, P00 | 9 staffing/recruitment APIs; assignment không cấp quyền; private completion evidence và occurrence identity. Closing report immutable snapshot, review/final approval, raw budget transaction + award outbox. Admin typed action consumer ghi canonical effect/allocations, inbox và receipt. | Last-seat không vượt capacity; không self-verify; completion VERIFIED chưa cộng XP. Approval report tiêu raw budget một lần, source award/correction/reversal không trùng qua QR/manual/review/replay. Status PENDING/CONFIRMED/FAILED phản ánh receipt thật. |
| **P09 — Media/resources và self-profile extensions** | P01, P02, P05, P00 | Bổ sung owner kinds application-logo/student-avatar/student-cover/club-shape theo policy đã chốt; resources upload/commit/update/private delivery/cleanup; self profile personal fields được phép và read-only academic projection. Equip ownership hoàn tất sau P08, không tạo dependency vòng. | Public/private storage tách scope; intent bound owner/actor/kind, object verification và expired/replay/cross-owner tests. Reload giữ dữ liệu server. Academic/identity không cho client tự sửa; manager không sửa personal profile người khác. |
| **P08 — Task, club-credit wallet, rewards và cosmetics inventory** | P05, P09, P00 | ClubService task/submission/review/private evidence, credit ledger/wallet, earned-versus-balance summaries, leaderboard/badges, reward catalog/publication/redeem/inventory/receipts. Atomic debit+grant cùng ClubDbContext; self cosmetics projection và Auth equip validation qua internal authority. | Một wallet dùng chung points/me và wallet/me; CLUB_CREDIT không là raw XP hay VNĐ. Free/bundle/owned/retry/concurrent redeem đúng, không debit hai lần. Outbox grant có receipt; reward retire không xóa quyền đã sở hữu. Không import sample balance/grants. |
| **P10 — Academic calendar và timetable cá nhân** | P02, P00 | Admin calendar linked canonical Semester/campus/year-term alias; self study-schedule per block/week/day/slot và bulk template import atomic. Validate 10W trước 3W, timezone/date semantics, expected versions và account isolation. | 6 method/path calendar/schedule có persistence; thao tác tuần không ghi đè tuần khác; import skip/replace và concurrent manual edit an toàn; đổi calendar không xóa timetable. |
| **P11 — Nghiệm thu BE, migration và bàn giao contract** | P01, P02, P03, P04, P05, P06, P07, P08, P09, P10 | Sau mỗi batch chạy test liên quan; cuối cùng full solution Release/CI, SQL migration upgrade, real Gateway auth, dependency/outbox recovery và storage acceptance có cấu hình. Cập nhật status manifest/audit/hướng dẫn BE cho FE bằng bằng chứng, không sửa UI. | 16 missing consumer operations được xác minh ở host/Gateway, required scope không còn missing/partial chưa giải thích. Migrations và tests đạt; live checks chưa chạy phải có trạng thái riêng. Không ghi hoàn thành chỉ vì route có mặt. |

P07 chia hai increment: A — read/assignment/recruitment/completion pending; B — final report/award/receipts. A chưa là completion của toàn action contract. P09 attachment/media foundation đứng trước P08 vì task evidence cần nó; các phần này cũng có thể làm sau P05 trong khi P06/P07 đang được chốt. P09 nghiệm thu media/personal fields độc lập; inventory-backed equip được nghiệm thu ở cuối P08, không tạo dependency vòng và không phải chờ tất cả điểm mới sửa ảnh.

**P12 — tùy chọn, giữ trong backlog:** durable pins, public directory/per-club opt-in, custom registration forms, physical stock/fulfillment, contest catalog và office succession/point-reset rollover. Chỉ triển khai sau khi selected scope và contract đủ rõ; không xóa hoặc đánh dấu cancelled.

### 7.1 Mapping 16 operation đã thiếu sang batch

| ID | Method/path | Batch | Ghi chú |
| --- | --- | --- | --- |
| M01 | `POST /api/clubs/me/membership-applications/{}/withdraw` | P04 | Sinh viên |
| M02 | `GET /api/clubs/{}/eligible-members` | P04 | Quản lý CLB |
| M03 | `GET /api/activity-applications/{}` | P06 | Quản lý CLB, CTSV/Admin |
| M04 | `POST /api/activity-applications` | P06 | Quản lý CLB |
| M05 | `PATCH /api/activity-applications/{}` | P06 | Quản lý CLB |
| M06 | `POST /api/activity-applications/{}/submit` | P06 | Quản lý CLB |
| M07 | `PATCH /api/activities/{}/publication` | P06 | Quản lý CLB |
| M08 | `GET /api/activities/{}/staffing` | P07 | Quản lý CLB, Sinh viên, Người xác minh |
| M09 | `POST /api/activities/{}/assignments` | P07 | Quản lý CLB |
| M10 | `POST /api/activities/{}/assignments/{}/cancel` | P07 | Quản lý CLB |
| M11 | `POST /api/activities/{}/action-completions/{}/verify` | P07 | Người xác minh |
| M12 | `POST /api/activities/{}/recruitment` | P07 | Quản lý CLB |
| M13 | `PATCH /api/activities/{}/recruitment` | P07 | Quản lý CLB |
| M14 | `PATCH /api/activities/{}/recruitment/status` | P07 | Quản lý CLB |
| M15 | `POST /api/activities/{}/recruitment/positions/{}/join` | P07 | Sinh viên |
| M16 | `GET /api/clubs/{}/activity-recruitments` | P07 | Quản lý CLB, Sinh viên |

### 7.2 Bao phủ toàn bộ 21 tài liệu nghiệp vụ

| Contract | Batch/phạm vi |
| --- | --- |
| implemented/canonical-self-profile-api.md | P01/P09; giữ subset 5 trường đã có |
| implemented/club-public-profile-api.md | P01 |
| implemented/notification-summary.md | P04/P07/P08/P11; reuse list/read |
| implemented/student-experience-reads-api.md | P02/P07/P11 |
| implemented/verified-experience-operations-api.md | P02/P07/P11; giữ legacy |
| activity-action-staffing-api.md | P06/P07 |
| activity-profile-resources-api.md | P09 |
| club-contributions-points-api.md | P08 |
| club-governance-groups-api.md | P05 |
| club-member-invitations-api.md | P03/P04 |
| club-navigation-applications-api.md | P04; pins optional |
| my-clubs-membership-api.md | P04; composed read giữ nguyên |
| profile-cover-rewards-api.md | P08/P09 |
| public-image-api.md | P01/P09 |
| public-member-directory-api.md | OPTIONAL P12 |
| self-profile-extensions-api.md | P09 |
| semester-student-roster-api.md | P03 |
| study-schedule-api.md | P10 |
| workspace-semester-context-api.md | P02 |
| experience-source-projections-api.md | P02 |
| event-registration-extensions-api.md | CONDITIONAL P12 |

## 8. Chiến lược kiểm thử

[verified] Baseline thực chạy ở lượt audit ngay trước plan: 737 pass, 0 fail, 3 skipped, tổng 740; không chạy lại trong lượt chỉ lập kế hoạch này. Ba skip là migration Admin/Finance thiếu connection env; nhóm ClubManager SQL LocalDB có chạy. Không coi baseline này đã cover API mới.

Thêm regression F01/F02 trước thay implementation; lấy version qua HTTP, không trực tiếp từ DbContext. Dùng SQL Server cho uniqueness/CAS/transaction/outbox/migration proof; SQLite/TestServer cho isolated HTTP actor/error contract. InMemory không đủ để chứng minh concurrent safety.

| Test file | Trạng thái | Scenario bắt buộc |
| --- | --- | --- |
| `tests/Backend.StabilizationTests/CrossServiceWorkflowAuthorizationTests.ClubManager.cs` | existing | GET manager → use response version → PATCH/PUT → GET; preserve public/private DTO boundaries; Only logo sent → old cover unchanged; only cover sent → logo unchanged; explicit null clear; Cross-club/revoked owner/stale version and replay after revocation denied |
| `tests/Backend.StabilizationTests/ClubAuthorizationRegressionTests.cs` | existing | Invited student must personally consent before manager approval; Forged ID/name/semester, locked/nonstudent/wrong-campus and roster changed after search rejected |
| `tests/Backend.StabilizationTests/ClubDataIntegrityConcurrencyTests.cs` | existing | join/invite/approve/withdraw races → at most one active membership; history retained; Treasurer limit still two unique people through specialist/custom groups |
| `tests/Backend.StabilizationTests/ClubManagerGatewayTests.cs` | existing | Missing 16 routes actually select owning service before catch-all; New workspace/policy/academic/self cosmetics roots do not hit wrong service; verbs and auth policies match |
| `tests/Backend.StabilizationTests/ContractCompletionMembershipTests.cs` | NEW — proposed | withdraw retry + wrong key/payload; stale approve after withdraw; reapply ID differs and old answers intact; UNKNOWN source cannot withdraw; current metadata version retained; inactive club/privacy tests |
| `tests/Backend.StabilizationTests/SemesterRosterContractTests.cs` | NEW — proposed | CSV invalid/unmatched/duplicate/oversize preview → no live writes; stale/expired preview commit; explicit empty versus no import; campus carry-forward and audit |
| `tests/Backend.StabilizationTests/GovernanceContractTests.cs` | NEW — proposed | Content → Event → ordinary in one term reconstructs exact history; BQL report does not grant; Future/expired/revoked assignments; owner/vice protection; union/custom leadership bypass; two-treasurer concurrent grant; Delayed invalidation and stale cache cannot authorize sensitive writes; dependency outage fails closed |
| `tests/Backend.StabilizationTests/ActivityActionContractTests.cs` | NEW — proposed | Receipt ID/version positive JS-safe integer; version increases; stale and duplicate submit/review; Approve creates one unpublished activity; hide differs from CLOSE and deep-link policy applied; Last seat with two joins → exactly one succeeds; self/cross-campus verifier denied; Completion verify → zero canonical award; final report approval → one budget spend and one eventual canonical source award; QR/manual/generic review cannot mint action XP; duplicate/out-of-order/cancel events do not resurrect award; Crash after local approval before publish; consumer crash before ack; retry produces same receipts and exact decimals |
| `tests/Backend.StabilizationTests/ClubCreditRewardContractTests.cs` | NEW — proposed | Two concurrent redemptions/keys cannot overspend; debit+bundle grant rollback together; Zero credit/zero price works; fully owned item never charged twice; partial ownership follows agreed price rule; Earned total differs from balance; reversal after spend follows confirmed debt policy; Publication schedule/manual override/retire boundary; existing entitlements remain usable; no sample credits |
| `tests/Backend.StabilizationTests/ProfileMediaResourcesContractTests.cs` | NEW — proposed | Student own avatar/cover versus another owner; application logo not coerced to club ID; Wrong bytes/MIME/size, expired intent, cross-activity reuse, R2 failure → no attachment; referenced media not cleaned; CLUB file never in public DTO; title/link validation/quarantine; reload uses canonical metadata; Forbidden academic/identity/cross-user edits; equip requires real owned entitlement |
| `tests/Backend.StabilizationTests/StudyScheduleContractTests.cs` | NEW — proposed | Student cannot write calendar, admin wrong campus cannot write; alias mapping absent/ambiguous fails; Week/block/user isolation; 10W invalid slots, date order; bulk skip/replace and manual version conflict atomic |

Áp dụng bảng actor cho mỗi mutation: unauthenticated, student self/other, member pending/approved/removed, manager đúng/sai CLB, specialist đúng/sai capability, SA đúng/sai campus, revoked/expired verifier, self-review. Recheck sau change of authority và dependency failure. Key giống/payload đổi phải 409; key khác không được vượt unique business source/capacity/balance.

[verified] `.github/workflows/backend-validate.yml` có restore, scoped format, Release build `-warnaserror`, full solution test/coverage, SQL env, EF pending-model check và compose checks. Dùng commands dưới trong môi trường đủ prerequisites; placeholder `<changed-service.csproj>` phải thay bằng service thật và dotnet-ef 8.0.18 như CI:

```powershell
dotnet restore ClubReportHub.sln
dotnet build ClubReportHub.sln --configuration Release --no-restore -warnaserror
dotnet test ClubReportHub.sln --configuration Release --no-build --logger trx --collect:"XPlat Code Coverage" --results-directory ./TestResults
dotnet ef migrations has-pending-model-changes --project <changed-service.csproj> --startup-project <changed-service.csproj> --configuration Release --no-build
git diff --check
```

Sau mỗi batch dùng filter/test project liên quan, cuối chuỗi chạy full gates một lần ở tip. Không đổi golden/baseline lặp lại mỗi intermediate commit. Gateway acceptance phải exercise real ASP.NET/YARP route selection/auth trước catch-all; live R2 cần signing/PUT/CORS/verify/attach/reload có cấu hình, không suy ra từ decoder unit test. Không cần sửa FE để test BE.

## 9. Rủi ro, migration và tương thích

| Vùng | Mức/ảnh hưởng | Biện pháp |
| --- | --- | --- |
| Mapper DTO | [graph] HIGH; 4 direct callers và method-group reads | Additive init properties, test manager/private/public và create/update/manager appointment responses |
| Shared access | [graph] CRITICAL; d1 GetMyAccessAsync/FetchAccessFromApiAsync; nhiều service dùng bool | Capability migration có action catalog, source version, deny unknown; preserve legacy fields; kiểm tra all consumers trong P05 |
| XP writer | [graph] CRITICAL; 5 direct callers | Typed action mode, no generic mint bypass; preserve legacy review/upgrade/adjustment/attendance/cancel behavior; canonical immutable ledger |
| Membership history | [verified] Existing one-row lookup và all-history unique | Expand metadata → backfill audit → sửa readers/writers → đổi active constraint → bật reapply/withdraw; không xóa old rows |
| Distributed side effects | [inferred] Outage/replay/out-of-order | One local transaction, immutable source IDs, inbox/outbox+receipt, pending/failure codes và reconciliation job; không claim final khi chưa ack |
| Numeric policy | [verified] Contract proposals và +1 conflict còn mở | Approved version + recorded basis/environment; Q gates; không sửa historical awards hoặc auto-approve parameters |
| Media/private data | [verified] Kind scopes và file policies khác nhau | Verified object owner binding; separate public/private delivery; byte/decode/quarantine; no cross-user profile edits |

Migration theo expand/backfill/constraint/cutover. Mỗi service có migration riêng, old schema upgrade tests và idempotent SQL review; không drop dữ liệu/column/old writer trước cutover. Membership active constraint và treasurer slot constraints phải chạy đúng hai provider. Existing action mode default explicit LEGACY_VERIFIED, không phát lại thưởng cũ. New action/int versions seeded deterministically chỉ cho new resources; không convert GUID lịch sử.

Telemetry cho nghiệp vụ mới: correlation/source/receipt IDs, source kind/policy version, pending age, inbox lag, retry/failure code, duplicate-source conflicts, budget/wallet invariant violations; không log token, contact riêng, file bytes, passwords hay signed upload URL. Có reconciliation báo cáo source-award pending nhưng chưa được canonical receipt; retry không chạy lại mutation bằng key mới.

Rollback ứng dụng bằng tắt entrypoint mới và giữ reads/history/migrations additive; xử lý tiền/điểm đã commit bằng compensating operation có audit. Không rollback database bằng xóa ledger. Trước code edit phải impact current symbol; trước commit khi được yêu cầu phải detect_changes và xử lý partial/truncated result.

## 10. File dự kiến thay đổi

File dưới đây đã xác định vai trò; tên NEW chỉ là proposed organization, executor có thể tách nhỏ theo repo conventions. Source verification chỉ áp dụng cho symbol/file hiện có, không khẳng định file NEW tồn tại.

| File | Symbol | Trách nhiệm |
| --- | --- | --- |
| `src/Services/ClubService/Contracts/ClubContracts.cs` | ClubResponse, ClubMembershipResponse, ClubAccessResponse | P01/P04/P05: additive fields; preserve current constructor compatibility |
| `src/Services/ClubService/Mappers/ClubMappers.cs` | ToResponse, ToMembershipResponseWithClub | P01/P04: canonical public metadata/version, source and capability projection |
| `src/Services/ClubService/Endpoints/ClubPublicImageEndpoints.cs` | MapClubPublicImageEndpoints | P01: explicit field presence, null clear, request fingerprint and version checks |
| `src/Services/ClubService/Endpoints/MembershipEndpoints.cs` | JoinClub, ApproveMembership | P04: live eligibility, new historical application rows, active uniqueness |
| `src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs` | InviteClubMember | P04: derive canonical target and semester roster, source outbox |
| `src/Services/ClubService/Endpoints/ClubEndpoints.cs` | GetClubById, GetAccessSummary | P01/P04/P05: response compatibility, current memberships and scoped capabilities |
| `src/Services/ClubService/Data/ClubDbContext.cs` | ClubDbContext | P03-P09: entities/indexes/constraints/receipts/outbox; wallet+grant one DB |
| `src/Shared/ClubReportHub.Shared/Auth/ClubAccessClient.cs` | ClubAccessSnapshot, GetMyAccessAsync, FetchAccessFromApiAsync | P05: additive capability/revision metadata and current authority checks |
| `src/Services/ClubService/Extensions/AuthorizationExtensions.cs` | CanManageMembershipsAsync | P05: action-specific policy integration; do not globally make specialist a manager |
| `src/Services/ActivityService/Models/ClubActivity.cs` | ClubActivity | P06/P07: immutable source application link, scoring mode, int aggregate/publication versions |
| `src/Services/ActivityService/Data/ActivityDbContext.cs` | ActivityDbContext | P06/P07/P09: dossier/actions/staffing/completion/report/raw budget/evidence/resource persistence |
| `src/Services/AdminService/Data/AdminDbContext.cs` | AdminDbContext | P03/P07: roster revisions; canonical action award inbox/effects; append-only guards |
| `src/Services/AdminService/Data/VerifiedExperienceModel.cs` | VerifiedExperienceModel | P07: unique source award receipt and compatible typed calculation snapshot |
| `src/Services/AdminService/Services/VerifiedExperienceService.cs` | AppendEffectAsync | P07: preserve tier wrapper; add controlled writer for prevalidated action award values |
| `src/Services/AdminService/Services/AdminExperienceEventHandler.cs` | AttendanceAsync, CancelAsync | P07: route action mode to pending/compensation; prevent immediate legacy award bypass |
| `src/Services/AdminService/Endpoints/VerifiedContributionEndpoints.cs` | ReviewAsync, ReviewUpgradeAsync, AdjustAsync | P07: reject legacy tier writes against action-owned source; keep legacy/external behavior |
| `src/Services/AuthService/Endpoints/CanonicalProfileEndpoints.cs` | CanonicalProfileEndpoints | P09/P08: strict self extensions, ownership verification, preserve field whitelist |
| `src/Services/AuthService/Endpoints/PublicMediaEndpoints.cs` | PublicMediaEndpoints | P09: distinct owner kinds, positive-owner/application/self authorization |
| `src/Services/AuthService/Services/PublicMediaStorage.cs` | PublicMediaStorage | P09: retain verified immutable objects; kind-specific media policy and cleanup |
| `src/Gateway/ApiGateway/yarp.json` | routing configuration | P02-P10: new prefixes and exact precedence; /users/me/cosmetics must not fall into Auth catch-all |
| `src/Services/ClubService/Endpoints/WorkspaceContextEndpoints.cs` | NEW — proposed | P02: workspace-semesters/invitation-semesters/approved-policy catalog |
| `src/Services/AdminService/Endpoints/SemesterRosterEndpoints.cs` | NEW — proposed | P03: preview/commit/student-roster + protected internal projection |
| `src/Services/ClubService/Endpoints/MyMembershipApplicationEndpoints.cs` | NEW — proposed | P04: self withdrawal/history; preserve current membership IDs |
| `src/Services/ClubService/Services/GovernanceService.cs` | NEW — proposed | P05: authoritative revision/capability union/history |
| `src/Services/ClubService/Endpoints/GovernanceEndpoints.cs` | NEW — proposed | P05: canonical D06 routes and protected appointments |
| `src/Services/ActivityService/Endpoints/ActivityApplicationEndpoints.cs` | NEW — proposed | P06: dossier draft/submit/review and source activity creation |
| `src/Services/ActivityService/Endpoints/ActivityStaffingEndpoints.cs` | NEW — proposed | P07: 9 staffing/recruitment routes |
| `src/Services/ActivityService/Services/ActionReportSettlementService.cs` | NEW — proposed | P07: immutable closing report/decision/raw budget/award outbox |
| `src/Services/AdminService/Services/ActionExperienceEventHandler.cs` | NEW — proposed | P07: typed event → canonical XP ledger + receipt |
| `src/Services/ClubService/Services/ClubCreditRewardService.cs` | NEW — proposed | P08: same-DB wallet/debit/grants/receipts and schedules |
| `src/Services/ActivityService/Endpoints/ActivityResourceEndpoints.cs` | NEW — proposed | P09: gallery/upload/commit/private download |
| `src/Services/AuthService/Endpoints/StudyScheduleEndpoints.cs` | NEW — proposed | P10: self timetable; academic calendar remains Admin authority |
| `docs/contracts/be-operation-manifest.json` | NEW — proposed | P00/P11: BE status/owner/method/path/contract scope and proof |

Ngoài bảng: model/contracts/migration/Program registration theo module mới và shared integration events có version, test files ở §8. Trước thêm entity/event kiểm tra index/source hiện tại; không đoán filename hoặc xóa abstraction cũ. Host mapping/Gateway và internal auth là một phần batch, không để đến cuối mới phát hiện route không tới được service.

## 11. Context có thể dùng để triển khai tiếp

Pack pin cả commit và dirty/untracked evidence; audit hai file mới chưa commit cũng có hash layer riêng. Executor kiểm tra lại digest bằng helper trước khi sửa, không cần khám phá lại toàn repo. Phần máy đọc bên dưới có thể thu gọn khi đọc tài liệu.

<details>
<summary>Implementation context pack — JSON</summary>

```json
{
  "implementation_context": {
    "task_summary": "BE-only completion of updated FE contracts and necessary compatibility/business fixes; planning only.",
    "acceptance_criteria": [
      "All 16 missing consumer operations mapped and implemented by their owning batch",
      "F01-F06 resolved within selected scope, with HTTP-path regression proof",
      "Required contract groups covered; optional/unapproved work explicitly gated",
      "Single authoritative raw ledger, single credit wallet, no double awards/debits",
      "Migrations upgrade real prior schemas and preserve history; CI/service/Gateway checks green",
      "BE contract statuses backed by test/proof, source-versus-live acceptance separated"
    ],
    "evidence_provenance": {
      "schema_version": 2,
      "head_commit": "b066f96b9fd0ea304e7c498f6ddcc569b2c21ebf",
      "generated_plan_path": "docs/plans/2026-10-21-gitnexus-plan-backend-contract-completion.md",
      "global_dirty_digest": {
        "algorithm": "sha256",
        "canonicalization": "gitnexus-evidence-provenance-v2 NUL-framed UTF-8 records",
        "value": "78e5c6f29d412d898b4121c3945ee8bf167fd487ea0eb6475977fc3d9a0b0323"
      },
      "cited_path_manifest": [
        {
          "path": ".github/workflows/backend-validate.yml",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:c1ebce867d7a7d386bec9da8dff1eb51af0f5345c54af7b77335cdf13d434c8e",
          "index_digest": "sha256:c1ebce867d7a7d386bec9da8dff1eb51af0f5345c54af7b77335cdf13d434c8e",
          "worktree_digest": "sha256:c1ebce867d7a7d386bec9da8dff1eb51af0f5345c54af7b77335cdf13d434c8e",
          "untracked_digest": "absent"
        },
        {
          "path": "AGENTS.md",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:544c9e742271d91067cc19111a6d536b07f391dd43d5d74f3dca601f84c4d52b",
          "index_digest": "sha256:544c9e742271d91067cc19111a6d536b07f391dd43d5d74f3dca601f84c4d52b",
          "worktree_digest": "sha256:544c9e742271d91067cc19111a6d536b07f391dd43d5d74f3dca601f84c4d52b",
          "untracked_digest": "absent"
        },
        {
          "path": "docs/audit/DOI_CHIEU_BE_CONTRACT_FE_2026-10-21.json",
          "object_kind": {
            "head": "absent",
            "index": "absent",
            "worktree": "absent",
            "untracked": "regular"
          },
          "state": "untracked",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "absent",
          "index_digest": "absent",
          "worktree_digest": "absent",
          "untracked_digest": "sha256:9e73c988284f72bd9a7200f9e768fd694d884b9850aa88dc84471c02e9b6e3fe"
        },
        {
          "path": "docs/audit/DOI_CHIEU_BE_CONTRACT_FE_2026-10-21.md",
          "object_kind": {
            "head": "absent",
            "index": "absent",
            "worktree": "absent",
            "untracked": "regular"
          },
          "state": "untracked",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "absent",
          "index_digest": "absent",
          "worktree_digest": "absent",
          "untracked_digest": "sha256:ba7ddde6b21647080f222ba08729ea71f4e6e13668f6abcd69e4bc47c54344de"
        },
        {
          "path": "src/Gateway/ApiGateway/yarp.json",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:c829069c4eeb473a3d29d56eaf292989e982b515b878186998f72e1b6063802e",
          "index_digest": "sha256:c829069c4eeb473a3d29d56eaf292989e982b515b878186998f72e1b6063802e",
          "worktree_digest": "sha256:c829069c4eeb473a3d29d56eaf292989e982b515b878186998f72e1b6063802e",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ActivityService/Data/ActivityDbContext.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:fda04695d40b02e934b67c598a412d8fcc2c29d0d846fa31095e64fdc1ad7d27",
          "index_digest": "sha256:fda04695d40b02e934b67c598a412d8fcc2c29d0d846fa31095e64fdc1ad7d27",
          "worktree_digest": "sha256:fda04695d40b02e934b67c598a412d8fcc2c29d0d846fa31095e64fdc1ad7d27",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ActivityService/Endpoints/ActivityLifecycleEndpoints.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:061a70393cf02698ed16b4c6c929666df36eca1b8eac6342858425d4ff4c4646",
          "index_digest": "sha256:061a70393cf02698ed16b4c6c929666df36eca1b8eac6342858425d4ff4c4646",
          "worktree_digest": "sha256:061a70393cf02698ed16b4c6c929666df36eca1b8eac6342858425d4ff4c4646",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ActivityService/Endpoints/ActivitySessionEndpoints.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:f784728cc9724f3bde8173f9c912192c957c509ea05d8223093bef9f65e62cbb",
          "index_digest": "sha256:f784728cc9724f3bde8173f9c912192c957c509ea05d8223093bef9f65e62cbb",
          "worktree_digest": "sha256:f784728cc9724f3bde8173f9c912192c957c509ea05d8223093bef9f65e62cbb",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ActivityService/Endpoints/StudentActivityEndpoints.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:84a34060d71e73ba3a948115100da8b5d5c5e4062b9b63a307f13dd6d4793d47",
          "index_digest": "sha256:84a34060d71e73ba3a948115100da8b5d5c5e4062b9b63a307f13dd6d4793d47",
          "worktree_digest": "sha256:84a34060d71e73ba3a948115100da8b5d5c5e4062b9b63a307f13dd6d4793d47",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ActivityService/Models/ClubActivity.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:53ceadde388b0e2efc9cb3283dcb4f10de0ff556bedabad42400160ffb296634",
          "index_digest": "sha256:53ceadde388b0e2efc9cb3283dcb4f10de0ff556bedabad42400160ffb296634",
          "worktree_digest": "sha256:53ceadde388b0e2efc9cb3283dcb4f10de0ff556bedabad42400160ffb296634",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/AdminService/Contracts/SemesterContracts.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:aede85c52f595bf9ebdee7acf7abfa21de773b668953392bbd4e2cc9d96e3399",
          "index_digest": "sha256:aede85c52f595bf9ebdee7acf7abfa21de773b668953392bbd4e2cc9d96e3399",
          "worktree_digest": "sha256:aede85c52f595bf9ebdee7acf7abfa21de773b668953392bbd4e2cc9d96e3399",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/AdminService/Data/AdminDbContext.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:5e6e77c7fca859514319e97c69b04ae8720f3c6c5fe8a64fb5ed2da2895a4f2f",
          "index_digest": "sha256:5e6e77c7fca859514319e97c69b04ae8720f3c6c5fe8a64fb5ed2da2895a4f2f",
          "worktree_digest": "sha256:5e6e77c7fca859514319e97c69b04ae8720f3c6c5fe8a64fb5ed2da2895a4f2f",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/AdminService/Data/VerifiedExperienceEntities.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:fee7c9e3780d0b23f3fff0ec0943802b900d4050aface40832b12d64e54828d1",
          "index_digest": "sha256:fee7c9e3780d0b23f3fff0ec0943802b900d4050aface40832b12d64e54828d1",
          "worktree_digest": "sha256:fee7c9e3780d0b23f3fff0ec0943802b900d4050aface40832b12d64e54828d1",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/AdminService/Data/VerifiedExperienceModel.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:a79d5cea98224dc09f7a106f312b913b7ed972e126f814b2b3feda01c3224a73",
          "index_digest": "sha256:a79d5cea98224dc09f7a106f312b913b7ed972e126f814b2b3feda01c3224a73",
          "worktree_digest": "sha256:a79d5cea98224dc09f7a106f312b913b7ed972e126f814b2b3feda01c3224a73",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/AdminService/Endpoints/PrivateEvidenceEndpoints.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:accf0219624505418354df010e53c530bc550632f1f41e770641713c63452a46",
          "index_digest": "sha256:accf0219624505418354df010e53c530bc550632f1f41e770641713c63452a46",
          "worktree_digest": "sha256:accf0219624505418354df010e53c530bc550632f1f41e770641713c63452a46",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/AdminService/Endpoints/SemesterEndpoints.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:14184fe4474b88a26896295de6237c7428d4417c0317fa33a90344470529b634",
          "index_digest": "sha256:14184fe4474b88a26896295de6237c7428d4417c0317fa33a90344470529b634",
          "worktree_digest": "sha256:14184fe4474b88a26896295de6237c7428d4417c0317fa33a90344470529b634",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/AdminService/Endpoints/VerifiedContributionEndpoints.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:1cf34ba7f021a3a096441ffcbf5208a0fb0facb0b3370297f23746f67f351c93",
          "index_digest": "sha256:1cf34ba7f021a3a096441ffcbf5208a0fb0facb0b3370297f23746f67f351c93",
          "worktree_digest": "sha256:1cf34ba7f021a3a096441ffcbf5208a0fb0facb0b3370297f23746f67f351c93",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/AdminService/Services/AdminExperienceEventHandler.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:6dec04fe71d0f20b952c6407c20af8088c0e1ed17c5dd902145a62b518659c10",
          "index_digest": "sha256:6dec04fe71d0f20b952c6407c20af8088c0e1ed17c5dd902145a62b518659c10",
          "worktree_digest": "sha256:6dec04fe71d0f20b952c6407c20af8088c0e1ed17c5dd902145a62b518659c10",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/AdminService/Services/StudentExperienceReadService.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:0d64261ec3c4035081e218f733284c530d01d5f8ea2e98668c7bbcaba857574c",
          "index_digest": "sha256:0d64261ec3c4035081e218f733284c530d01d5f8ea2e98668c7bbcaba857574c",
          "worktree_digest": "sha256:0d64261ec3c4035081e218f733284c530d01d5f8ea2e98668c7bbcaba857574c",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/AdminService/Services/VerifiedExperienceService.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:8afd6948f825896fd090e877437b9fea7c6fdc44afa7ace7be481599335232b8",
          "index_digest": "sha256:8afd6948f825896fd090e877437b9fea7c6fdc44afa7ace7be481599335232b8",
          "worktree_digest": "sha256:8afd6948f825896fd090e877437b9fea7c6fdc44afa7ace7be481599335232b8",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/AuthService/Endpoints/CanonicalProfileEndpoints.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:fa52675c0a40f17a23094ddf5c56d814a4470e130bfe827e56476079837b9241",
          "index_digest": "sha256:fa52675c0a40f17a23094ddf5c56d814a4470e130bfe827e56476079837b9241",
          "worktree_digest": "sha256:fa52675c0a40f17a23094ddf5c56d814a4470e130bfe827e56476079837b9241",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/AuthService/Endpoints/PublicMediaEndpoints.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:4cf69ba08a2b1e0722d94150d720cbff0e56c72522dfcbb5013d5001a30aceeb",
          "index_digest": "sha256:4cf69ba08a2b1e0722d94150d720cbff0e56c72522dfcbb5013d5001a30aceeb",
          "worktree_digest": "sha256:4cf69ba08a2b1e0722d94150d720cbff0e56c72522dfcbb5013d5001a30aceeb",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/AuthService/Services/PublicMediaStorage.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:d2c50538c3d6178eb48517504e54998c7f703f334cd4cf95d83da6309a7261ba",
          "index_digest": "sha256:d2c50538c3d6178eb48517504e54998c7f703f334cd4cf95d83da6309a7261ba",
          "worktree_digest": "sha256:d2c50538c3d6178eb48517504e54998c7f703f334cd4cf95d83da6309a7261ba",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ClubService/Contracts/ClubContracts.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:d9e28ca10784aca2898c78e16056a871fc1b69f3a91c37256f9fddb911a104df",
          "index_digest": "sha256:d9e28ca10784aca2898c78e16056a871fc1b69f3a91c37256f9fddb911a104df",
          "worktree_digest": "sha256:d9e28ca10784aca2898c78e16056a871fc1b69f3a91c37256f9fddb911a104df",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ClubService/Data/ClubDbContext.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:4aca16b384b8dd03d0ea87dd47dff193fbfda295e1fe2b318a3be67e5c2038c5",
          "index_digest": "sha256:4aca16b384b8dd03d0ea87dd47dff193fbfda295e1fe2b318a3be67e5c2038c5",
          "worktree_digest": "sha256:4aca16b384b8dd03d0ea87dd47dff193fbfda295e1fe2b318a3be67e5c2038c5",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ClubService/Endpoints/ClubEndpoints.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:ff3c4b2a58e12ed13a30f8a21d2d9d8c585a9177b42614651f682c3d5b377a2c",
          "index_digest": "sha256:ff3c4b2a58e12ed13a30f8a21d2d9d8c585a9177b42614651f682c3d5b377a2c",
          "worktree_digest": "sha256:ff3c4b2a58e12ed13a30f8a21d2d9d8c585a9177b42614651f682c3d5b377a2c",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ClubService/Endpoints/ClubPublicImageEndpoints.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:cd19a6ffa75dc7a96e4818f4ca204d7635cd0057b9d51fe6067302c6d3d71c28",
          "index_digest": "sha256:cd19a6ffa75dc7a96e4818f4ca204d7635cd0057b9d51fe6067302c6d3d71c28",
          "worktree_digest": "sha256:cd19a6ffa75dc7a96e4818f4ca204d7635cd0057b9d51fe6067302c6d3d71c28",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ClubService/Endpoints/ManagerEndpoints.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:ca6c31cb92e7c8a02454a29e33764bda9bfcb17a6b201ba2fe037c6c08980a88",
          "index_digest": "sha256:ca6c31cb92e7c8a02454a29e33764bda9bfcb17a6b201ba2fe037c6c08980a88",
          "worktree_digest": "sha256:ca6c31cb92e7c8a02454a29e33764bda9bfcb17a6b201ba2fe037c6c08980a88",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:2c1e2fdf30853e8b9cdc77861fd5d72d6f53c0520a4e480157ebe12e545354ec",
          "index_digest": "sha256:2c1e2fdf30853e8b9cdc77861fd5d72d6f53c0520a4e480157ebe12e545354ec",
          "worktree_digest": "sha256:2c1e2fdf30853e8b9cdc77861fd5d72d6f53c0520a4e480157ebe12e545354ec",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ClubService/Endpoints/MembershipEndpoints.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:8866b5a453894e903ddc3d520d03db1ab5154067bdacc126bc8f9a46048c3587",
          "index_digest": "sha256:8866b5a453894e903ddc3d520d03db1ab5154067bdacc126bc8f9a46048c3587",
          "worktree_digest": "sha256:8866b5a453894e903ddc3d520d03db1ab5154067bdacc126bc8f9a46048c3587",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ClubService/Extensions/AuthorizationExtensions.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:e28ceed8c8eefd9234068b0500cd79a060c49b67eadbd09f523c87f7c88738cb",
          "index_digest": "sha256:e28ceed8c8eefd9234068b0500cd79a060c49b67eadbd09f523c87f7c88738cb",
          "worktree_digest": "sha256:e28ceed8c8eefd9234068b0500cd79a060c49b67eadbd09f523c87f7c88738cb",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ClubService/Mappers/ClubMappers.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:9a6573b2c36f52312b282281b35ef8371f5a79d987fc511558e1120972ee9f2a",
          "index_digest": "sha256:9a6573b2c36f52312b282281b35ef8371f5a79d987fc511558e1120972ee9f2a",
          "worktree_digest": "sha256:9a6573b2c36f52312b282281b35ef8371f5a79d987fc511558e1120972ee9f2a",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ClubService/Models/ClubMembership.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:974cfa173eee3db8a863af8fa4fc2cd8695cd7d17f00d3b8b21ef4aa952b5cd5",
          "index_digest": "sha256:974cfa173eee3db8a863af8fa4fc2cd8695cd7d17f00d3b8b21ef4aa952b5cd5",
          "worktree_digest": "sha256:974cfa173eee3db8a863af8fa4fc2cd8695cd7d17f00d3b8b21ef4aa952b5cd5",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ClubService/Models/ClubWorkflowStatuses.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:46ca7287df532d9d1439a6183af58471849a935b96c9f6d7f06b7860e6567c1f",
          "index_digest": "sha256:46ca7287df532d9d1439a6183af58471849a935b96c9f6d7f06b7860e6567c1f",
          "worktree_digest": "sha256:46ca7287df532d9d1439a6183af58471849a935b96c9f6d7f06b7860e6567c1f",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Services/ClubService/Services/ClubMemberQuery.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:1b797e1104f517efe4c3ba4a3ad49564794d704d2d6d02fbf2c0975dc102370a",
          "index_digest": "sha256:1b797e1104f517efe4c3ba4a3ad49564794d704d2d6d02fbf2c0975dc102370a",
          "worktree_digest": "sha256:1b797e1104f517efe4c3ba4a3ad49564794d704d2d6d02fbf2c0975dc102370a",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Shared/ClubReportHub.Shared/Auth/ClubAccessClient.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:100af314fdcf27d1aca1c5e8403376441d74c0de37acf74d65b925153193380a",
          "index_digest": "sha256:100af314fdcf27d1aca1c5e8403376441d74c0de37acf74d65b925153193380a",
          "worktree_digest": "sha256:100af314fdcf27d1aca1c5e8403376441d74c0de37acf74d65b925153193380a",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Shared/ClubReportHub.Shared/Data/ApiOperationReceipt.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:3e84228a2a410c25a21c23cba83b839ba23de5c42e36aaf6acc8255040507c65",
          "index_digest": "sha256:3e84228a2a410c25a21c23cba83b839ba23de5c42e36aaf6acc8255040507c65",
          "worktree_digest": "sha256:3e84228a2a410c25a21c23cba83b839ba23de5c42e36aaf6acc8255040507c65",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Shared/ClubReportHub.Shared/Experience/MediaAttachmentClient.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:dff3d0ab700ddb7b24bc0afa6e4df77147e23fff45a38e335702350b07a1b2a0",
          "index_digest": "sha256:dff3d0ab700ddb7b24bc0afa6e4df77147e23fff45a38e335702350b07a1b2a0",
          "worktree_digest": "sha256:dff3d0ab700ddb7b24bc0afa6e4df77147e23fff45a38e335702350b07a1b2a0",
          "untracked_digest": "absent"
        },
        {
          "path": "src/Shared/ClubReportHub.Shared/Experience/StudentExperienceSources.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:c5db4ea9dbcf6d634c1f673f12159dfc75fe60b4ed9ad42e8ee5db859b01b352",
          "index_digest": "sha256:c5db4ea9dbcf6d634c1f673f12159dfc75fe60b4ed9ad42e8ee5db859b01b352",
          "worktree_digest": "sha256:c5db4ea9dbcf6d634c1f673f12159dfc75fe60b4ed9ad42e8ee5db859b01b352",
          "untracked_digest": "absent"
        },
        {
          "path": "tests/Backend.StabilizationTests/ClubAuthorizationRegressionTests.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:3d27d9124861ecbf2bfd1d8f42ad8cb570ecc0b4dc8629bcdd799dc54a512684",
          "index_digest": "sha256:3d27d9124861ecbf2bfd1d8f42ad8cb570ecc0b4dc8629bcdd799dc54a512684",
          "worktree_digest": "sha256:3d27d9124861ecbf2bfd1d8f42ad8cb570ecc0b4dc8629bcdd799dc54a512684",
          "untracked_digest": "absent"
        },
        {
          "path": "tests/Backend.StabilizationTests/ClubDataIntegrityConcurrencyTests.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:04bb1800919d885e625f5ad99312be587440deff498c32694e55a3e681b29da0",
          "index_digest": "sha256:04bb1800919d885e625f5ad99312be587440deff498c32694e55a3e681b29da0",
          "worktree_digest": "sha256:04bb1800919d885e625f5ad99312be587440deff498c32694e55a3e681b29da0",
          "untracked_digest": "absent"
        },
        {
          "path": "tests/Backend.StabilizationTests/ClubManagerGatewayTests.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:c23f7523b8415979b48485331838d889a80e1f1a24664294cd7c25f51113cdaa",
          "index_digest": "sha256:c23f7523b8415979b48485331838d889a80e1f1a24664294cd7c25f51113cdaa",
          "worktree_digest": "sha256:c23f7523b8415979b48485331838d889a80e1f1a24664294cd7c25f51113cdaa",
          "untracked_digest": "absent"
        },
        {
          "path": "tests/Backend.StabilizationTests/CrossServiceWorkflowAuthorizationTests.ClubManager.cs",
          "object_kind": {
            "head": "regular",
            "index": "regular",
            "worktree": "regular",
            "untracked": "absent"
          },
          "state": "clean",
          "rename_from": null,
          "rename_to": null,
          "head_digest": "sha256:5a4e407487bf4d1d19ebd7b32712ed177d18da4fc8c1f0bf586b89e153e4e83f",
          "index_digest": "sha256:5a4e407487bf4d1d19ebd7b32712ed177d18da4fc8c1f0bf586b89e153e4e83f",
          "worktree_digest": "sha256:5a4e407487bf4d1d19ebd7b32712ed177d18da4fc8c1f0bf586b89e153e4e83f",
          "untracked_digest": "absent"
        }
      ]
    },
    "primary_symbols": [
      {
        "symbol": "ToResponse",
        "file": "src/Services/ClubService/Mappers/ClubMappers.cs",
        "lines": "13-49",
        "role": "Read/write DTO compatibility; HIGH"
      },
      {
        "symbol": "MapClubPublicImageEndpoints",
        "file": "src/Services/ClubService/Endpoints/ClubPublicImageEndpoints.cs",
        "lines": "16-35",
        "role": "Presence-aware image changes and CAS"
      },
      {
        "symbol": "JoinClub",
        "file": "src/Services/ClubService/Endpoints/MembershipEndpoints.cs",
        "lines": "63-161",
        "role": "Consent/reapply/history and active uniqueness"
      },
      {
        "symbol": "AppendEffectAsync",
        "file": "src/Services/AdminService/Services/VerifiedExperienceService.cs",
        "lines": "66-112",
        "role": "Canonical XP effects; CRITICAL"
      },
      {
        "symbol": "ClubAccessSnapshot",
        "file": "src/Shared/ClubReportHub.Shared/Auth/ClubAccessClient.cs",
        "lines": "17-30",
        "role": "Governance compatibility/authority projection; CRITICAL"
      }
    ],
    "related_symbols": [
      {
        "symbol": "GetClubById",
        "relationship": "Selects manager versus public DTO",
        "relevance": "ClubEndpoints.cs"
      },
      {
        "symbol": "ToMembershipResponseWithClub",
        "relationship": "Existing GUID membership version",
        "relevance": "ClubMappers.cs"
      },
      {
        "symbol": "InviteClubMember",
        "relationship": "Canonical target/roster validation",
        "relevance": "MemberManagementEndpoints.cs"
      },
      {
        "symbol": "ApproveMembership",
        "relationship": "Consent and live eligibility",
        "relevance": "MembershipEndpoints.cs"
      },
      {
        "symbol": "CanManageMembershipsAsync",
        "relationship": "Existing owner/admin boundary",
        "relevance": "AuthorizationExtensions.cs"
      },
      {
        "symbol": "GetMyAccessAsync",
        "relationship": "Direct ClubAccessSnapshot dependent; cache/bypass",
        "relevance": "ClubAccessClient.cs"
      },
      {
        "symbol": "FetchAccessFromApiAsync",
        "relationship": "Direct ClubAccessSnapshot dependent; deserialization",
        "relevance": "ClubAccessClient.cs"
      },
      {
        "symbol": "ReviewAsync",
        "relationship": "Direct ledger caller, block action bypass",
        "relevance": "VerifiedContributionEndpoints.cs"
      },
      {
        "symbol": "ReviewUpgradeAsync",
        "relationship": "Direct ledger caller, preserve legacy upgrades",
        "relevance": "VerifiedContributionEndpoints.cs"
      },
      {
        "symbol": "AdjustAsync",
        "relationship": "Direct ledger caller, typed corrections",
        "relevance": "VerifiedContributionEndpoints.cs"
      },
      {
        "symbol": "AttendanceAsync",
        "relationship": "Direct ledger caller, do not auto-award action mode",
        "relevance": "AdminExperienceEventHandler.cs"
      },
      {
        "symbol": "CancelAsync",
        "relationship": "Direct ledger caller, append compensation",
        "relevance": "AdminExperienceEventHandler.cs"
      },
      {
        "symbol": "ExecuteAsync",
        "relationship": "Serializable transaction, receipt and rollback reuse",
        "relevance": "ApiOperationReceipt.cs"
      },
      {
        "symbol": "StudentActivityItem",
        "relationship": "Safe source context extension",
        "relevance": "StudentExperienceSources.cs"
      },
      {
        "symbol": "ClubDbContext",
        "relationship": "Membership uniqueness/governance/wallet schemas",
        "relevance": "ClubDbContext.cs"
      },
      {
        "symbol": "AdminDbContext",
        "relationship": "Append-only canonical XP/inbox/roster",
        "relevance": "AdminDbContext.cs"
      },
      {
        "symbol": "ActivityDbContext",
        "relationship": "Application/action/source transactions",
        "relevance": "ActivityDbContext.cs"
      },
      {
        "symbol": "CanonicalProfileEndpoints",
        "relationship": "Self-only fields/equip",
        "relevance": "CanonicalProfileEndpoints.cs"
      },
      {
        "symbol": "PublicMediaEndpoints",
        "relationship": "Owner-kind intent boundary",
        "relevance": "PublicMediaEndpoints.cs"
      },
      {
        "symbol": "StudentExperienceReadService",
        "relationship": "One canonical ledger and formula compatibility",
        "relevance": "StudentExperienceReadService.cs"
      }
    ],
    "execution_path": [
      "Current principal/campus/club and authority validation",
      "Validate field presence/state/version/approved policy",
      "Execute local mutation through ApiOperations with receipt and outbox",
      "Commit immutable source decision and budget/grant source in owning DB",
      "Consume typed source event idempotently in target ledger DB",
      "Publish canonical receipt; read DTO displays real pending/final status"
    ],
    "pdg_constraints": [
      {
        "description": "Image write is guarded by current ownership, active club, version and both requested verifications; only successful branch persists fields/version.",
        "affected_statements": [
          "src/Services/ClubService/Endpoints/ClubPublicImageEndpoints.cs:20",
          "src/Services/ClubService/Endpoints/ClubPublicImageEndpoints.cs:25",
          "src/Services/ClubService/Endpoints/ClubPublicImageEndpoints.cs:27",
          "src/Services/ClubService/Endpoints/ClubPublicImageEndpoints.cs:31"
        ],
        "implementation_consequence": "Presence-aware parsing must preserve checks and rollback; null and omitted cannot hash identically."
      },
      {
        "description": "JoinClub branch line 125 reuses a rejected/deleted row and overwrites its answers/review metadata.",
        "affected_statements": [
          "src/Services/ClubService/Endpoints/MembershipEndpoints.cs:113",
          "src/Services/ClubService/Endpoints/MembershipEndpoints.cs:125",
          "src/Services/ClubService/Endpoints/MembershipEndpoints.cs:127"
        ],
        "implementation_consequence": "Create a new application row for reapply; add active-only predicate and migrate unique index before enabling history."
      },
      {
        "description": "AppendEffectAsync consumes after.Tier/Role/Scale/Result and allocates by after.PillarWeights; it is not an arbitrary action raw-point writer.",
        "affected_statements": [
          "src/Services/AdminService/Services/VerifiedExperienceService.cs:66",
          "src/Services/AdminService/Services/VerifiedExperienceService.cs:72"
        ],
        "implementation_consequence": "Use typed server-validated action settlement, preserve legacy wrapper and audit correction/reversal paths."
      }
    ],
    "architectural_patterns": [
      {
        "pattern": "Serializable mutation + idempotent response",
        "example_location": "src/Shared/ClubReportHub.Shared/Data/ApiOperationReceipt.cs:40",
        "usage_guidance": "Authorize before every replay; same DB receipt/outbox; CAS and DB uniqueness remain required."
      },
      {
        "pattern": "Source event → canonical ledger → recognition receipt",
        "example_location": "src/Services/AdminService/Services/AdminExperienceEventHandler.cs:16",
        "usage_guidance": "Reuse bounded inbox/outbox pattern for action awards, with new calculation kind and no legacy automatic award bypass."
      },
      {
        "pattern": "Verified immutable public image object",
        "example_location": "src/Services/AuthService/Services/PublicMediaStorage.cs:49",
        "usage_guidance": "Do not attach mutable staging object or trust arbitrary client URL."
      }
    ],
    "files_to_modify": [
      {
        "file": "src/Services/ClubService/Contracts/ClubContracts.cs",
        "symbols": [
          "ClubResponse",
          "ClubMembershipResponse",
          "ClubAccessResponse"
        ],
        "intended_change": "P01/P04/P05: additive fields; preserve current constructor compatibility"
      },
      {
        "file": "src/Services/ClubService/Mappers/ClubMappers.cs",
        "symbols": [
          "ToResponse",
          "ToMembershipResponseWithClub"
        ],
        "intended_change": "P01/P04: canonical public metadata/version, source and capability projection"
      },
      {
        "file": "src/Services/ClubService/Endpoints/ClubPublicImageEndpoints.cs",
        "symbols": [
          "MapClubPublicImageEndpoints"
        ],
        "intended_change": "P01: explicit field presence, null clear, request fingerprint and version checks"
      },
      {
        "file": "src/Services/ClubService/Endpoints/MembershipEndpoints.cs",
        "symbols": [
          "JoinClub",
          "ApproveMembership"
        ],
        "intended_change": "P04: live eligibility, new historical application rows, active uniqueness"
      },
      {
        "file": "src/Services/ClubService/Endpoints/MemberManagementEndpoints.cs",
        "symbols": [
          "InviteClubMember"
        ],
        "intended_change": "P04: derive canonical target and semester roster, source outbox"
      },
      {
        "file": "src/Services/ClubService/Endpoints/ClubEndpoints.cs",
        "symbols": [
          "GetClubById",
          "GetAccessSummary"
        ],
        "intended_change": "P01/P04/P05: response compatibility, current memberships and scoped capabilities"
      },
      {
        "file": "src/Services/ClubService/Data/ClubDbContext.cs",
        "symbols": [
          "ClubDbContext"
        ],
        "intended_change": "P03-P09: entities/indexes/constraints/receipts/outbox; wallet+grant one DB"
      },
      {
        "file": "src/Shared/ClubReportHub.Shared/Auth/ClubAccessClient.cs",
        "symbols": [
          "ClubAccessSnapshot",
          "GetMyAccessAsync",
          "FetchAccessFromApiAsync"
        ],
        "intended_change": "P05: additive capability/revision metadata and current authority checks"
      },
      {
        "file": "src/Services/ClubService/Extensions/AuthorizationExtensions.cs",
        "symbols": [
          "CanManageMembershipsAsync"
        ],
        "intended_change": "P05: action-specific policy integration; do not globally make specialist a manager"
      },
      {
        "file": "src/Services/ActivityService/Models/ClubActivity.cs",
        "symbols": [
          "ClubActivity"
        ],
        "intended_change": "P06/P07: immutable source application link, scoring mode, int aggregate/publication versions"
      },
      {
        "file": "src/Services/ActivityService/Data/ActivityDbContext.cs",
        "symbols": [
          "ActivityDbContext"
        ],
        "intended_change": "P06/P07/P09: dossier/actions/staffing/completion/report/raw budget/evidence/resource persistence"
      },
      {
        "file": "src/Services/AdminService/Data/AdminDbContext.cs",
        "symbols": [
          "AdminDbContext"
        ],
        "intended_change": "P03/P07: roster revisions; canonical action award inbox/effects; append-only guards"
      },
      {
        "file": "src/Services/AdminService/Data/VerifiedExperienceModel.cs",
        "symbols": [
          "VerifiedExperienceModel"
        ],
        "intended_change": "P07: unique source award receipt and compatible typed calculation snapshot"
      },
      {
        "file": "src/Services/AdminService/Services/VerifiedExperienceService.cs",
        "symbols": [
          "AppendEffectAsync"
        ],
        "intended_change": "P07: preserve tier wrapper; add controlled writer for prevalidated action award values"
      },
      {
        "file": "src/Services/AdminService/Services/AdminExperienceEventHandler.cs",
        "symbols": [
          "AttendanceAsync",
          "CancelAsync"
        ],
        "intended_change": "P07: route action mode to pending/compensation; prevent immediate legacy award bypass"
      },
      {
        "file": "src/Services/AdminService/Endpoints/VerifiedContributionEndpoints.cs",
        "symbols": [
          "ReviewAsync",
          "ReviewUpgradeAsync",
          "AdjustAsync"
        ],
        "intended_change": "P07: reject legacy tier writes against action-owned source; keep legacy/external behavior"
      },
      {
        "file": "src/Services/AuthService/Endpoints/CanonicalProfileEndpoints.cs",
        "symbols": [
          "CanonicalProfileEndpoints"
        ],
        "intended_change": "P09/P08: strict self extensions, ownership verification, preserve field whitelist"
      },
      {
        "file": "src/Services/AuthService/Endpoints/PublicMediaEndpoints.cs",
        "symbols": [
          "PublicMediaEndpoints"
        ],
        "intended_change": "P09: distinct owner kinds, positive-owner/application/self authorization"
      },
      {
        "file": "src/Services/AuthService/Services/PublicMediaStorage.cs",
        "symbols": [
          "PublicMediaStorage"
        ],
        "intended_change": "P09: retain verified immutable objects; kind-specific media policy and cleanup"
      },
      {
        "file": "src/Gateway/ApiGateway/yarp.json",
        "symbols": [
          "routing configuration"
        ],
        "intended_change": "P02-P10: new prefixes and exact precedence; /users/me/cosmetics must not fall into Auth catch-all"
      },
      {
        "file": "src/Services/ClubService/Endpoints/WorkspaceContextEndpoints.cs",
        "symbols": [
          "NEW — proposed"
        ],
        "intended_change": "P02: workspace-semesters/invitation-semesters/approved-policy catalog"
      },
      {
        "file": "src/Services/AdminService/Endpoints/SemesterRosterEndpoints.cs",
        "symbols": [
          "NEW — proposed"
        ],
        "intended_change": "P03: preview/commit/student-roster + protected internal projection"
      },
      {
        "file": "src/Services/ClubService/Endpoints/MyMembershipApplicationEndpoints.cs",
        "symbols": [
          "NEW — proposed"
        ],
        "intended_change": "P04: self withdrawal/history; preserve current membership IDs"
      },
      {
        "file": "src/Services/ClubService/Services/GovernanceService.cs",
        "symbols": [
          "NEW — proposed"
        ],
        "intended_change": "P05: authoritative revision/capability union/history"
      },
      {
        "file": "src/Services/ClubService/Endpoints/GovernanceEndpoints.cs",
        "symbols": [
          "NEW — proposed"
        ],
        "intended_change": "P05: canonical D06 routes and protected appointments"
      },
      {
        "file": "src/Services/ActivityService/Endpoints/ActivityApplicationEndpoints.cs",
        "symbols": [
          "NEW — proposed"
        ],
        "intended_change": "P06: dossier draft/submit/review and source activity creation"
      },
      {
        "file": "src/Services/ActivityService/Endpoints/ActivityStaffingEndpoints.cs",
        "symbols": [
          "NEW — proposed"
        ],
        "intended_change": "P07: 9 staffing/recruitment routes"
      },
      {
        "file": "src/Services/ActivityService/Services/ActionReportSettlementService.cs",
        "symbols": [
          "NEW — proposed"
        ],
        "intended_change": "P07: immutable closing report/decision/raw budget/award outbox"
      },
      {
        "file": "src/Services/AdminService/Services/ActionExperienceEventHandler.cs",
        "symbols": [
          "NEW — proposed"
        ],
        "intended_change": "P07: typed event → canonical XP ledger + receipt"
      },
      {
        "file": "src/Services/ClubService/Services/ClubCreditRewardService.cs",
        "symbols": [
          "NEW — proposed"
        ],
        "intended_change": "P08: same-DB wallet/debit/grants/receipts and schedules"
      },
      {
        "file": "src/Services/ActivityService/Endpoints/ActivityResourceEndpoints.cs",
        "symbols": [
          "NEW — proposed"
        ],
        "intended_change": "P09: gallery/upload/commit/private download"
      },
      {
        "file": "src/Services/AuthService/Endpoints/StudyScheduleEndpoints.cs",
        "symbols": [
          "NEW — proposed"
        ],
        "intended_change": "P10: self timetable; academic calendar remains Admin authority"
      },
      {
        "file": "docs/contracts/be-operation-manifest.json",
        "symbols": [
          "NEW — proposed"
        ],
        "intended_change": "P00/P11: BE status/owner/method/path/contract scope and proof"
      }
    ],
    "tests": [
      {
        "file": "tests/Backend.StabilizationTests/CrossServiceWorkflowAuthorizationTests.ClubManager.cs",
        "kind": "existing",
        "scenarios": [
          "GET manager → use response version → PATCH/PUT → GET; preserve public/private DTO boundaries",
          "Only logo sent → old cover unchanged; only cover sent → logo unchanged; explicit null clear",
          "Cross-club/revoked owner/stale version and replay after revocation denied"
        ]
      },
      {
        "file": "tests/Backend.StabilizationTests/ClubAuthorizationRegressionTests.cs",
        "kind": "existing",
        "scenarios": [
          "Invited student must personally consent before manager approval",
          "Forged ID/name/semester, locked/nonstudent/wrong-campus and roster changed after search rejected"
        ]
      },
      {
        "file": "tests/Backend.StabilizationTests/ClubDataIntegrityConcurrencyTests.cs",
        "kind": "existing",
        "scenarios": [
          "join/invite/approve/withdraw races → at most one active membership; history retained",
          "Treasurer limit still two unique people through specialist/custom groups"
        ]
      },
      {
        "file": "tests/Backend.StabilizationTests/ClubManagerGatewayTests.cs",
        "kind": "existing",
        "scenarios": [
          "Missing 16 routes actually select owning service before catch-all",
          "New workspace/policy/academic/self cosmetics roots do not hit wrong service; verbs and auth policies match"
        ]
      },
      {
        "file": "tests/Backend.StabilizationTests/ContractCompletionMembershipTests.cs",
        "kind": "NEW — proposed",
        "scenarios": [
          "withdraw retry + wrong key/payload; stale approve after withdraw; reapply ID differs and old answers intact",
          "UNKNOWN source cannot withdraw; current metadata version retained; inactive club/privacy tests"
        ]
      },
      {
        "file": "tests/Backend.StabilizationTests/SemesterRosterContractTests.cs",
        "kind": "NEW — proposed",
        "scenarios": [
          "CSV invalid/unmatched/duplicate/oversize preview → no live writes",
          "stale/expired preview commit; explicit empty versus no import; campus carry-forward and audit"
        ]
      },
      {
        "file": "tests/Backend.StabilizationTests/GovernanceContractTests.cs",
        "kind": "NEW — proposed",
        "scenarios": [
          "Content → Event → ordinary in one term reconstructs exact history; BQL report does not grant",
          "Future/expired/revoked assignments; owner/vice protection; union/custom leadership bypass; two-treasurer concurrent grant",
          "Delayed invalidation and stale cache cannot authorize sensitive writes; dependency outage fails closed"
        ]
      },
      {
        "file": "tests/Backend.StabilizationTests/ActivityActionContractTests.cs",
        "kind": "NEW — proposed",
        "scenarios": [
          "Receipt ID/version positive JS-safe integer; version increases; stale and duplicate submit/review",
          "Approve creates one unpublished activity; hide differs from CLOSE and deep-link policy applied",
          "Last seat with two joins → exactly one succeeds; self/cross-campus verifier denied",
          "Completion verify → zero canonical award; final report approval → one budget spend and one eventual canonical source award",
          "QR/manual/generic review cannot mint action XP; duplicate/out-of-order/cancel events do not resurrect award",
          "Crash after local approval before publish; consumer crash before ack; retry produces same receipts and exact decimals"
        ]
      },
      {
        "file": "tests/Backend.StabilizationTests/ClubCreditRewardContractTests.cs",
        "kind": "NEW — proposed",
        "scenarios": [
          "Two concurrent redemptions/keys cannot overspend; debit+bundle grant rollback together",
          "Zero credit/zero price works; fully owned item never charged twice; partial ownership follows agreed price rule",
          "Earned total differs from balance; reversal after spend follows confirmed debt policy",
          "Publication schedule/manual override/retire boundary; existing entitlements remain usable; no sample credits"
        ]
      },
      {
        "file": "tests/Backend.StabilizationTests/ProfileMediaResourcesContractTests.cs",
        "kind": "NEW — proposed",
        "scenarios": [
          "Student own avatar/cover versus another owner; application logo not coerced to club ID",
          "Wrong bytes/MIME/size, expired intent, cross-activity reuse, R2 failure → no attachment; referenced media not cleaned",
          "CLUB file never in public DTO; title/link validation/quarantine; reload uses canonical metadata",
          "Forbidden academic/identity/cross-user edits; equip requires real owned entitlement"
        ]
      },
      {
        "file": "tests/Backend.StabilizationTests/StudyScheduleContractTests.cs",
        "kind": "NEW — proposed",
        "scenarios": [
          "Student cannot write calendar, admin wrong campus cannot write; alias mapping absent/ambiguous fails",
          "Week/block/user isolation; 10W invalid slots, date order; bulk skip/replace and manual version conflict atomic"
        ]
      }
    ],
    "verification_commands": [
      "dotnet restore ClubReportHub.sln",
      "dotnet build ClubReportHub.sln --configuration Release --no-restore -warnaserror",
      "dotnet test ClubReportHub.sln --configuration Release --no-build --logger trx --collect:\"XPlat Code Coverage\" --results-directory ./TestResults",
      "dotnet test tests/Backend.StabilizationTests/Backend.StabilizationTests.csproj --no-restore --filter \"FullyQualifiedName~ClubManager|FullyQualifiedName~ClubAuthorization|FullyQualifiedName~ClubDataIntegrity\"",
      "dotnet ef migrations has-pending-model-changes --project <changed-service.csproj> --startup-project <changed-service.csproj> --configuration Release --no-build",
      "git diff --check"
    ],
    "risks": [
      "HIGH: ClubMappers.ToResponse has four graph direct callers plus method-group callers; use additive properties and verify every response path.",
      "CRITICAL: ClubAccessSnapshot has 56 graph impacted symbols to depth 3 and crosses Report/Finance/Activity/Export/Notification/media; preserve booleans and migrate action checks deliberately.",
      "CRITICAL: AppendEffectAsync has five direct callers; auto attendance/review/upgrade/adjustment/cancel can otherwise bypass delayed action awards.",
      "Removing membership all-history uniqueness changes Find/Single semantics; review join/invite/manager bootstrap/owner transfer/treasurer/roster before accepting migration.",
      "Cross-service award/grant is eventual: no success claim before receipt; duplicate/out-of-order/cancel/retry must be deterministic.",
      "Approved formula/numeric policy, file policy and optional scope cannot be invented from UI examples.",
      "Contract FE is pinned remote main, while local FE branch differs; do not checkout or overwrite local changes."
    ],
    "assumptions": [
      "The execution branch still includes BE b066f96 and cited bytes: verify provenance/diff before implementation.",
      "FE remains pinned 3ffd00ed; revalidate exact applicationPayload/validReceipt/staffing/publication if friend changes contract.",
      "No approved missing product-baseline repo was available: resolve Q-01..Q-06 using approved documents/product owner, not browser mock data.",
      "Actual internal service credentials, R2 and SQL test endpoints are available only at acceptance: verify configuration presence without logging secrets."
    ],
    "open_questions": [
      {
        "id": "Q-01",
        "question": "Approve final authority, action formula/base-point policies/caps/category allocation, raw budget limits and amendment/reversal/refund rules. Do not publish or mint on unapproved proposal.",
        "gate": "Before award-enabled P06/P07"
      },
      {
        "id": "Q-02",
        "question": "Resolve official +1/linear versus existing saturated ERI policy, PERIOD/CUMULATIVE and rollover. Keep versioned existing formula; changing it requires approved policy + regression set.",
        "gate": "Before new index policy/rollover"
      },
      {
        "id": "Q-03",
        "question": "Roster CSV identity schema O-04, timing/carry-forward, student canonical code/source and explicit empty semantics, campus/global scope.",
        "gate": "Before P03 import parser is accepted"
      },
      {
        "id": "Q-04",
        "question": "Final shape/FRAME types: PNG-only transparent 1MiB versus PNG/SVG/vectorized 5MiB; safe asset DTO, ownership and moderation. Never trust browser sanitizer alone.",
        "gate": "Before P08/P09 shape publication"
      },
      {
        "id": "Q-05",
        "question": "Task reversal negative-balance/debt policy, bundle partial ownership/prices and auto grant result authority. No arbitrary conversion raw XP → CLUB_CREDIT.",
        "gate": "Before P08 live credits/redemption"
      },
      {
        "id": "Q-06",
        "question": "Directory opt-in, durable pins, custom registration forms, physical stock/fulfillment, contest result catalog, office succession and point-reset rollover. Preserve as optional/needs-spec, do not delete.",
        "gate": "P12 follow-up"
      }
    ],
    "avoid": [
      "Do not repeat full repository discovery; start from pinned audit and this plan.",
      "Do not modify FE code or overwrite package-lock.json.",
      "Do not silently change existing GUID APIs to integers or use CLOSE as unpublish.",
      "Do not award action XP in generic contribution/attendance paths before final report.",
      "Do not create a second canonical XP ledger or second spendable wallet.",
      "Do not treat task credit, raw XP budget, Admin quest XP and VNĐ as the same unit.",
      "Do not turn specialist/Host/Staff labels into manager/CTSV/verifier authority.",
      "Do not import browser sample wallets/grants/governance into production.",
      "Do not delete deferred scope or implement unapproved numerical policy as fact.",
      "Run impact before each code symbol edit and detect_changes before any requested commit."
    ],
    "external_contract_reference": {
      "repository": "fptu-xperience-clubhub-ui",
      "commit": "3ffd00ed03a5b8db942ed3ac3b7b6dee098d9f42",
      "audit_manifest": "docs/audit/DOI_CHIEU_BE_CONTRACT_FE_2026-10-21.json",
      "additional_source_verified": "src/pages/v2/activity-application/ActivityApplicationPage.jsx: validReceipt requires positive integer application ID/version"
    },
    "batch_dependencies": [
      {
        "id": "P00",
        "title": "Chốt wire contract và policy có ảnh hưởng nghiệp vụ",
        "depends": [],
        "work": "Lập operation manifest BE, phân loại required/optional/proposed; ghi ADR version, media omitted/null, scoring mode, award/final approver, roster O-04 và asset policy. Đặc tả DTO/errors/paging/idempotency/OpenAPI examples cho từng batch trước khi viết handler.",
        "exit": "Mỗi operation có actor, owner service, state machine, version type, source authority và test scenario. Quyết định chưa có căn cứ được giữ OPEN; batch không phụ thuộc vẫn triển khai được."
      },
      {
        "id": "P01",
        "title": "Sửa lỗi đang chặn tích hợp: DTO quản lý CLB và cập nhật ảnh",
        "depends": [
          "P00"
        ],
        "work": "Thêm init properties Version/CoverImageUrl/Schedule vào ClubResponse và mapper; sửa image PUT phân biệt omitted với null, giữ ảnh không gửi lên; validate unknown/duplicate field và required version. Không sửa FE.",
        "exit": "GET manager → lấy HTTP version → PATCH text/PUT từng ảnh → reload thành công; bỏ field giữ nguyên, null clear, stale 409; member/cross-club bị chặn. Không cần migration cho các cột đã có."
      },
      {
        "id": "P02",
        "title": "Safe semester/policy catalogs và source context cho sinh viên",
        "depends": [
          "P00"
        ],
        "work": "Dùng Admin Semester GUID/code làm authority; thêm club workspace-semesters/invitation-semesters và approved-policy projection qua internal client có scope. Bổ sung student-safe session/source read vào detail có quyền, kể cả campus nonmember đã đăng ký và lịch sử hoàn tất.",
        "exit": "Manager không cần quyền backoffice vẫn chọn được học kỳ/policy hợp lệ; mapping year/term/reportPeriod rõ. Sinh viên campus nonmember lấy đúng source/version để nộp đóng góp; không nhận challenge/roster/evidence người khác."
      },
      {
        "id": "P03",
        "title": "Roster theo học kỳ: preview, commit, revision và carry-forward",
        "depends": [
          "P02",
          "P00"
        ],
        "work": "AdminService lưu roster/import preview/revision/campus/provenance; preview CSV/JSON theo schema được chốt, canonical identity lookup qua Auth; commit expectedVersion+key atomic; protected internal lookup và paged admin roster.",
        "exit": "Phân biệt chưa import, explicit empty, carry-forward; không tạo tài khoản ngầm. Preview stale/expired/wrong-campus bị từ chối; import lỗi không thay roster. Có migration nâng cấp và audit."
      },
      {
        "id": "P04",
        "title": "Membership: metadata, rút đơn/reapply và lời mời đúng eligibility",
        "depends": [
          "P02",
          "P03"
        ],
        "work": "Thêm source/semester/rosterRevision/inviter/consent/withdraw/audit; giữ GUID version có sẵn. Lưu lịch sử không ghi đè, đổi uniqueness sang active Pending/Approved. Thêm withdraw; nâng invite, consent, approve và eligible search với live authority/outbox.",
        "exit": "Một active record/user/club dù join/invite/withdraw/approve đua nhau; retry withdraw trả receipt cũ; reapply có ID mới và giữ đơn cũ. Manager không consent hộ; invitation không auto-approve. Source UNKNOWN không tự quảng cáo quyền withdraw."
      },
      {
        "id": "P05",
        "title": "Governance đa nhóm và quyền có scope xuyên service",
        "depends": [
          "P02",
          "P04"
        ],
        "work": "Term/group/assignment intervals/revisions/change sets/per-person history/immutable BQL submissions; catalog và union capability; protected owner transfer reuse và vice nomination/review. Tích hợp read/write capabilities, treasurer duties và access invalidation đồng thời ở service liên quan.",
        "exit": "Báo cáo BQL không cấp quyền; đổi nhóm/revoke/expiry ảnh hưởng quyền đúng thời điểm; custom group không tự cấp leadership/CTSV; một người được nhiều nhóm nhưng tối đa hai người giữ treasurer duty. Giữ DTO compatibility, không biến mọi BQL thành IsManager."
      },
      {
        "id": "P06",
        "title": "Hồ sơ tạo hoạt động, duyệt và publication",
        "depends": [
          "P02",
          "P05",
          "P00"
        ],
        "work": "ActivityService dossier/revision/action proposals/review history, server estimates, submit; SA review resource đề xuất. Approve tạo đúng một activity chưa publish và approved raw budget/snapshots. Bổ sung bool/integer publication API cho action activities; bảo toàn lifecycle legacy.",
        "exit": "4 API dossier và publication hiện FE cần hoạt động; draft/revision chỉ sửa đúng trạng thái; approve/retry không sinh hai activity; publication theo source approval, actor và integer CAS. CLOSE không được dùng như unpublish."
      },
      {
        "id": "P07",
        "title": "Staffing/recruitment → completion pending → final report → XP",
        "depends": [
          "P06",
          "P05",
          "P00"
        ],
        "work": "9 staffing/recruitment APIs; assignment không cấp quyền; private completion evidence và occurrence identity. Closing report immutable snapshot, review/final approval, raw budget transaction + award outbox. Admin typed action consumer ghi canonical effect/allocations, inbox và receipt.",
        "exit": "Last-seat không vượt capacity; không self-verify; completion VERIFIED chưa cộng XP. Approval report tiêu raw budget một lần, source award/correction/reversal không trùng qua QR/manual/review/replay. Status PENDING/CONFIRMED/FAILED phản ánh receipt thật."
      },
      {
        "id": "P09",
        "title": "Media/resources và self-profile extensions",
        "depends": [
          "P01",
          "P02",
          "P05",
          "P00"
        ],
        "work": "Bổ sung owner kinds application-logo/student-avatar/student-cover/club-shape theo policy đã chốt; resources upload/commit/update/private delivery/cleanup; self profile personal fields được phép và read-only academic projection. Equip ownership hoàn tất sau P08, không tạo dependency vòng.",
        "exit": "Public/private storage tách scope; intent bound owner/actor/kind, object verification và expired/replay/cross-owner tests. Reload giữ dữ liệu server. Academic/identity không cho client tự sửa; manager không sửa personal profile người khác."
      },
      {
        "id": "P08",
        "title": "Task, club-credit wallet, rewards và cosmetics inventory",
        "depends": [
          "P05",
          "P09",
          "P00"
        ],
        "work": "ClubService task/submission/review/private evidence, credit ledger/wallet, earned-versus-balance summaries, leaderboard/badges, reward catalog/publication/redeem/inventory/receipts. Atomic debit+grant cùng ClubDbContext; self cosmetics projection và Auth equip validation qua internal authority.",
        "exit": "Một wallet dùng chung points/me và wallet/me; CLUB_CREDIT không là raw XP hay VNĐ. Free/bundle/owned/retry/concurrent redeem đúng, không debit hai lần. Outbox grant có receipt; reward retire không xóa quyền đã sở hữu. Không import sample balance/grants."
      },
      {
        "id": "P10",
        "title": "Academic calendar và timetable cá nhân",
        "depends": [
          "P02",
          "P00"
        ],
        "work": "Admin calendar linked canonical Semester/campus/year-term alias; self study-schedule per block/week/day/slot và bulk template import atomic. Validate 10W trước 3W, timezone/date semantics, expected versions và account isolation.",
        "exit": "6 method/path calendar/schedule có persistence; thao tác tuần không ghi đè tuần khác; import skip/replace và concurrent manual edit an toàn; đổi calendar không xóa timetable."
      },
      {
        "id": "P11",
        "title": "Nghiệm thu BE, migration và bàn giao contract",
        "depends": [
          "P01",
          "P02",
          "P03",
          "P04",
          "P05",
          "P06",
          "P07",
          "P08",
          "P09",
          "P10"
        ],
        "work": "Sau mỗi batch chạy test liên quan; cuối cùng full solution Release/CI, SQL migration upgrade, real Gateway auth, dependency/outbox recovery và storage acceptance có cấu hình. Cập nhật status manifest/audit/hướng dẫn BE cho FE bằng bằng chứng, không sửa UI.",
        "exit": "16 missing consumer operations được xác minh ở host/Gateway, required scope không còn missing/partial chưa giải thích. Migrations và tests đạt; live checks chưa chạy phải có trạng thái riêng. Không ghi hoàn thành chỉ vì route có mặt."
      }
    ],
    "missing_operation_assignment": [
      {
        "method": "POST",
        "route": "/api/clubs/me/membership-applications/{}/withdraw",
        "consumer": "withdrawMyMembershipApplication",
        "actors": [
          "Sinh viên"
        ],
        "status": "NO_BE_ROUTE",
        "scope": "current_facade",
        "contract": "my-clubs-membership-api.md",
        "work_package": "P04"
      },
      {
        "method": "GET",
        "route": "/api/clubs/{}/eligible-members",
        "consumer": "searchEligibleClubMembers",
        "actors": [
          "Quản lý CLB"
        ],
        "status": "NO_BE_ROUTE",
        "scope": "current_facade",
        "contract": "club-member-invitations-api.md",
        "work_package": "P04"
      },
      {
        "method": "GET",
        "route": "/api/activity-applications/{}",
        "consumer": "getActivityApplication",
        "actors": [
          "Quản lý CLB",
          "CTSV/Admin"
        ],
        "status": "NO_BE_ROUTE",
        "scope": "current_facade",
        "contract": "activity-action-staffing-api.md",
        "work_package": "P06"
      },
      {
        "method": "POST",
        "route": "/api/activity-applications",
        "consumer": "createActivityApplication",
        "actors": [
          "Quản lý CLB"
        ],
        "status": "NO_BE_ROUTE",
        "scope": "current_facade",
        "contract": "activity-action-staffing-api.md",
        "work_package": "P06"
      },
      {
        "method": "PATCH",
        "route": "/api/activity-applications/{}",
        "consumer": "updateActivityApplication",
        "actors": [
          "Quản lý CLB"
        ],
        "status": "NO_BE_ROUTE",
        "scope": "current_facade",
        "contract": "activity-action-staffing-api.md",
        "work_package": "P06"
      },
      {
        "method": "POST",
        "route": "/api/activity-applications/{}/submit",
        "consumer": "submitActivityApplication",
        "actors": [
          "Quản lý CLB"
        ],
        "status": "NO_BE_ROUTE",
        "scope": "current_facade",
        "contract": "activity-action-staffing-api.md",
        "work_package": "P06"
      },
      {
        "method": "PATCH",
        "route": "/api/activities/{}/publication",
        "consumer": "setActivityPublication",
        "actors": [
          "Quản lý CLB"
        ],
        "status": "NO_BE_ROUTE",
        "scope": "current_facade",
        "contract": "activity-action-staffing-api.md",
        "work_package": "P06"
      },
      {
        "method": "GET",
        "route": "/api/activities/{}/staffing",
        "consumer": "getActivityStaffing",
        "actors": [
          "Quản lý CLB",
          "Sinh viên",
          "Người xác minh"
        ],
        "status": "NO_BE_ROUTE",
        "scope": "direct_consumer",
        "contract": "activity-action-staffing-api.md",
        "work_package": "P07"
      },
      {
        "method": "POST",
        "route": "/api/activities/{}/assignments",
        "consumer": "ActivityStaffingPanel.assignments",
        "actors": [
          "Quản lý CLB"
        ],
        "status": "NO_BE_ROUTE",
        "scope": "direct_consumer",
        "contract": "activity-action-staffing-api.md",
        "work_package": "P07"
      },
      {
        "method": "POST",
        "route": "/api/activities/{}/assignments/{}/cancel",
        "consumer": "ActivityStaffingPanel.cancel",
        "actors": [
          "Quản lý CLB"
        ],
        "status": "NO_BE_ROUTE",
        "scope": "direct_consumer",
        "contract": "activity-action-staffing-api.md",
        "work_package": "P07"
      },
      {
        "method": "POST",
        "route": "/api/activities/{}/action-completions/{}/verify",
        "consumer": "ActivityStaffingPanel.verify",
        "actors": [
          "Người xác minh"
        ],
        "status": "NO_BE_ROUTE",
        "scope": "direct_consumer",
        "contract": "activity-action-staffing-api.md",
        "work_package": "P07"
      },
      {
        "method": "POST",
        "route": "/api/activities/{}/recruitment",
        "consumer": "ActivityStaffingPanel.createRecruitment",
        "actors": [
          "Quản lý CLB"
        ],
        "status": "NO_BE_ROUTE",
        "scope": "direct_consumer",
        "contract": "activity-action-staffing-api.md",
        "work_package": "P07"
      },
      {
        "method": "PATCH",
        "route": "/api/activities/{}/recruitment",
        "consumer": "ActivityStaffingPanel.editRecruitment",
        "actors": [
          "Quản lý CLB"
        ],
        "status": "NO_BE_ROUTE",
        "scope": "direct_consumer",
        "contract": "activity-action-staffing-api.md",
        "work_package": "P07"
      },
      {
        "method": "PATCH",
        "route": "/api/activities/{}/recruitment/status",
        "consumer": "ActivityStaffingPanel.recruitmentStatus",
        "actors": [
          "Quản lý CLB"
        ],
        "status": "NO_BE_ROUTE",
        "scope": "direct_consumer",
        "contract": "activity-action-staffing-api.md",
        "work_package": "P07"
      },
      {
        "method": "POST",
        "route": "/api/activities/{}/recruitment/positions/{}/join",
        "consumer": "ActivityStaffingPanel.joinPosition",
        "actors": [
          "Sinh viên"
        ],
        "status": "NO_BE_ROUTE",
        "scope": "direct_consumer",
        "contract": "activity-action-staffing-api.md",
        "work_package": "P07"
      },
      {
        "method": "GET",
        "route": "/api/clubs/{}/activity-recruitments",
        "consumer": "QuestsTab",
        "actors": [
          "Quản lý CLB",
          "Sinh viên"
        ],
        "status": "NO_BE_ROUTE",
        "scope": "direct_consumer",
        "contract": "activity-action-staffing-api.md",
        "work_package": "P07"
      }
    ]
  }
}
```

</details>

## 12. Giả định, quyết định mở và phần hoãn

Các lựa chọn Selected là đề xuất kỹ thuật để BE tương thích FE hiện tại; các Q là quyết định nghiệp vụ cần bằng chứng trước batch có tác động điểm/quyền/dữ liệu. Không cần chờ Q-01 để sửa P01.

| ID | Trạng thái | Quyết định/câu hỏi | Gate |
| --- | --- | --- | --- |
| ADR-01 | Selected BE compatibility | New action application/staffing/publication use positive JS-safe integer version with CAS; old APIs retain GUID. FE application validReceipt also demands integer, not only staffing/publication. | P06/P07 |
| ADR-02 | Selected BE compatibility | Image PUT supports field presence: omitted preserves, null clears; retain URL payload and owner-bound verification. Record this change explicitly in BE contract. | P01 |
| ADR-03 | Proposed architecture | ActivityService owns dossiers/actions/completions/closing report/raw budget/local award source. AdminService remains sole canonical raw experience ledger; typed outbox/inbox/receipt, no distributed DB transaction. | P06/P07 |
| ADR-04 | Proposed architecture | ClubService owns task credit wallet, rewards, receipts and cross-club reusable entitlements in one DB transaction; Auth owns equipped profile and checks entitlement authority. No new microservice. | P08/P09 |
| Q-01 | OPEN business gate | Approve final authority, action formula/base-point policies/caps/category allocation, raw budget limits and amendment/reversal/refund rules. Do not publish or mint on unapproved proposal. | Before award-enabled P06/P07 |
| Q-02 | OPEN business gate | Resolve official +1/linear versus existing saturated ERI policy, PERIOD/CUMULATIVE and rollover. Keep versioned existing formula; changing it requires approved policy + regression set. | Before new index policy/rollover |
| Q-03 | OPEN business gate | Roster CSV identity schema O-04, timing/carry-forward, student canonical code/source and explicit empty semantics, campus/global scope. | Before P03 import parser is accepted |
| Q-04 | OPEN business gate | Final shape/FRAME types: PNG-only transparent 1MiB versus PNG/SVG/vectorized 5MiB; safe asset DTO, ownership and moderation. Never trust browser sanitizer alone. | Before P08/P09 shape publication |
| Q-05 | OPEN business gate | Task reversal negative-balance/debt policy, bundle partial ownership/prices and auto grant result authority. No arbitrary conversion raw XP → CLUB_CREDIT. | Before P08 live credits/redemption |
| Q-06 | OPEN scope | Directory opt-in, durable pins, custom registration forms, physical stock/fulfillment, contest result catalog, office succession and point-reset rollover. Preserve as optional/needs-spec, do not delete. | P12 follow-up |

[assumed] BE/FE chưa đổi sau snapshot; executor xác minh HEAD/cited bytes và operation manifest trước khi code. [assumed] Policy approvals và external source documents chưa tìm được trong sibling doc repo; mọi numeric/default rule phải được đối chiếu bản đã duyệt, không dựa tên branch hoặc UI mock. [assumed] Hạ tầng live và test credentials sẽ có ở acceptance; nếu thiếu, tách trạng thái chưa kiểm chứng, không giả success.

Giữ các nhóm optional trong P12 với nhãn bỏ qua/tạm hoãn rõ và reason. Không làm lại login/canonical text/notification list/semester CRUD/owner transfer/verified experience report đã có. Không mở manager personal-profile PUT đang chủ đích trả 403. Không thêm mobile/backend surface hay refactor deployment ngoài yêu cầu này. Không pull/checkout FE hoặc sửa lockfile của người dùng.

Giới hạn bằng chứng: bound PDG JoinClub chỉ lấy slice 18/28, không phải whole-function proof; C# dynamic/DI/method-group và async cross-service luôn cần current source và HTTP tests. Local graph refresh có warning truncation; không có claim graph là đầy đủ. Test pass trước plan không là production acceptance của các đề xuất chưa triển khai.

## 13. Definition of Done

- [ ] P01/F01/F02 có HTTP regression: manager đọc được version/cover/schedule; đổi một ảnh không mất ảnh còn lại.
- [ ] 16 operation M01–M16 có handler thật, DTO đúng FE pinned contract, đúng actor/service/Gateway; không chỉ tồn tại declaration.
- [ ] Các required contract groups có persistence/migration/host registration/validation/auth/state/version/idempotency/paging/error và test evidence. Các optional/proposed có trạng thái riêng, không xóa.
- [ ] Roster/membership đúng campus/current identity; invitation cá nhân consent; withdrawal và reapply giữ lịch sử, unique active state dưới race.
- [ ] Governance union/revocation/expiry/protected leadership/duty constraints áp dụng xuyên service; report BQL không cấp quyền; các direct access consumers đã được account.
- [ ] Action completion chưa award; final report approval đúng final authority, budget invariant, unique source/canonical XP receipt và correction/reversal. Legacy/external/QR/manual không hồi quy hoặc bypass.
- [ ] CLUB_CREDIT wallet/debit/grants atomic và độc lập raw XP/VNĐ; không có demo grant/credit migration. Equip self và media owner/file/private policy đúng.
- [ ] Timetable/calendar alias/10W–3W/cell isolation/bulk atomic có server persistence.
- [ ] Full BE CI gates/migration upgrade/concurrency/outbox/dependency/Gateway tests đạt. Live R2/deployment checks có evidence hoặc nhãn pending, không ghi Done thay phần chưa chạy.
- [ ] Bàn giao BE operation manifest/audit/request-response examples cho người làm FE; ghi source-complete và live-accepted riêng. Source FE và thay đổi local được giữ nguyên.

Đợt bắt đầu được đề xuất là **P01**, sau đó P02; P00 chỉ cần chốt phần tương thích của hai batch này để sửa lỗi độc lập trước. Các batch trao điểm/ngân sách/quyền mới chỉ được bật sau Q tương ứng được xác nhận.
