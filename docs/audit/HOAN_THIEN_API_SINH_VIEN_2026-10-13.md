# Hoàn thiện API Sinh viên — BE — cập nhật 14/10/2026

Nhánh: feat/student-api-completion. Giữ nguyên công việc Quản lý CLB đang có. Không sửa mã FE; nhãn và hướng dẫn contract FE đã đồng bộ ngày 14/10. Chưa triển khai production.

Kết quả: 15 operation bổ sung đã hoàn thiện; Sinh viên 32/32 operation COMPLETE trong danh sách đối chiếu. [Bảng trạng thái hiện hành 14/10](TRANG_THAI_API_BE_CHO_FE_2026-10-14.md) thay cho kết luận tiến độ 13/10.

## Phạm vi đã triển khai

15 operation còn thiếu/chưa hoàn thiện trong kiểm kê 13/10:

| ID | Route | Service | Nội dung |
| --- | --- | --- | --- |
| API-005 | GET /api/v2/activities/feed | Activity | Search/filter/paging, quyền xem campus/member, display-safe projection, server clock và next session. |
| API-006 | GET /api/v2/activities/{activityId} | Activity | Deep link trực tiếp có cùng kiểm tra visibility, không roster/minh chứng. |
| API-007 | GET /api/v2/recommendations/activities | Admin | Quy tắc low-axis, eligible candidates, deterministic ranking qua nhiều trang, căn cứ policy/radar. |
| API-010 | GET /api/users/me/contributions | Admin | Canonical + legacy riêng nhãn, dedup mirror, SQL paging/count, calculation hiện trạng. |
| API-011 | GET /api/contributions/{id} | Admin | Detail có calculationBreakdown, ledgerEffects/delta/allocation/revision/policy/verification. |
| API-017 | GET /api/users/me/experience-summary | Admin | Tổng toàn bộ DB, count tách points, availability/snapshot của từng nguồn. |
| API-018 | GET /api/users/me/activity-history | Activity | Self-only, cross-activity/session history, registered/verified/revoked và legacy. |
| API-023 | GET /api/v1/declarations/me | Admin | Self normalized paged declarations, không lộ hồ sơ sinh viên khác. |
| API-026 | GET /api/v1/declarations/me/radar | Admin | Net ledger theo kỳ và cumulative approved policy riêng, six axes + plusOne. |
| API-027 | GET /api/v1/quests | Admin | Catalog published có criteria, paging tùy query; GET không tự seed. |
| API-028 | GET /api/v1/quests/me | Admin | Self participation, tiến độ derived từ công sức verified; phản ánh proof bị revoke. |
| API-029 | GET /api/v1/quests/{id} | Admin | Criteria/version/policy và counts; draft không công khai, không roster. |
| API-030 | POST /api/v1/quests/{id}/join | Admin | Canonical profile, published/campus/deadline/source eligibility, frozen criteria, dedup/retry. |
| API-031 | POST /api/v1/quests/{id}/complete | Admin | Principal only, đã join, CAS/version, trusted contribution proof; completion history bất biến, zero extra points. |
| API-032 | POST /api/clubs/{id}/join | Club | Canonical identity từ Auth, active/unlocked/campus/consent, transaction/retry/uniqueness. |

17 operation Sinh viên đã hoàn thành trước đó tiếp tục được kiểm thử hồi quy. API hỗ trợ nội bộ không tính thêm vào mốc 74 operation. Alias /api/quests áp dụng cùng bảo vệ với /api/v1/quests.

## Lưu ý tích hợp FE

### Đóng góp và khai báo

GET /api/users/me/contributions và GET /api/v1/declarations/me:

- Query: semesterId=canonicalCode, status, pillar, page>=1, pageSize=1..100.
- Response: items/page/pageSize/total/totalItems/totalPages.
- **declarations/me hiện trả envelope, thay array cũ**. FE phải đọc items.
- sourceNamespace=CONTRIBUTION cho canonical, LEGACY_DECLARATION cho lịch sử cũ.
- Legacy có isAuthoritative=false, semesterId=null, legacyRawPointsPreview riêng; không cộng nó vào điểm xác thực.
- Filter semester chỉ áp dụng nguồn có kỳ xác định. Không tự gán kỳ cho legacy.
- Detail của legacy vẫn dùng route declarations cũ; không gọi /contributions/{legacyId} để coi nó là canonical.

Detail canonical giữ contribution/revisions/decisions/roleUpgradeRequests và bổ sung calculationBreakdown/ledgerEffects.
Effect mới có immutable calculation snapshot. Effect cũ được dựng lại khi revision, policy gốc, tổng bút toán và phân bổ khớp hoàn toàn; provenance=RECONSTRUCTED_FROM_IMMUTABLE_LEDGER_AND_REVISION. Nếu không đủ căn cứ hoặc formula chưa hỗ trợ, calculationAvailable=false; không dùng policy hiện tại để giả cách tính lịch sử.
RecognitionStatus REVOKED được thể hiện đúng, không tiếp tục hiển thị RECORDED.

### Radar/tổng quan

Theo kỳ: GET /api/v1/declarations/me/radar?semester=FA26.
Tích lũy: GET /api/v1/declarations/me/radar?scope=CUMULATIVE.

- PERIOD cần semester hợp lệ hoặc đúng một active semester; không fallback FA26.
- CUMULATIVE không nhận đồng thời semester.
- Có scope/semesterId/policyVersion/formulaVersion/contributionFormulaVersion/ledgerRevision/asOf/version.
- radarAxes đúng sáu trụ cột, plusOne là RealWorldWork riêng; pillars bảy phần vẫn giữ để compatibility.
- Cumulative cộng điểm thô net đã ghi, chuẩn hóa bằng approved cumulative policy; không cộng X đã bão hòa theo từng kỳ.
- Legacy chỉ có legacyPreviewCount, không vào điểm chính thức.
- Policy chưa duyệt: 503 SCORE_POLICY_UNAPPROVED. P01–P06 vẫn cần cấu hình/căn cứ thống nhất thực tế; test fixture không phải phê duyệt production.
- GET /api/users/me/experience-summary?semesterId=FA26 tách recognizedContributionCount khỏi rawExperiencePoints.
- Attendance/clubs source lỗi: count=null + availability=false, không fake0.
- asOf lịch sử được hỗ trợ bởi ledger/radar; membership/attendance hiện trạng chưa có historical projection, summary gắn HISTORICAL_SNAPSHOT_UNSUPPORTED và null cho hai nguồn khi yêu cầu asOf. Không khẳng định atomic snapshot xuyên nhiều service.

Admin approval hiện hỗ trợ Scope PERIOD/CUMULATIVE và EffectiveAt trong body của route approval đã có. Cumulative scope không được dùng làm policy xác thực nguồn activity.

### Khám phá/gợi ý/lịch sử

Feed/detail yêu cầu đăng nhập. discoveryVisibility mặc định CLUB_ONLY, có thể chọn CAMPUS_PUBLIC qua authoring BE của manager.
Dữ liệu đã có không tự đổi thành public trong migration. Quyền xem và quyền đăng ký tách biệt.

Feed: clubId/search/status=UPCOMING|LIVE/semesterId/page/pageSize; mặc định50, tối đa100.
Item có id/club info/title/description/coverImageUrl/startTime/endTime/location/status/nextAction/isEligible/canRegister/version/nextSessionId/sessionVersion/policyVersion/pillarWeights.
nextAction là gợi ý trình bày; action đăng ký/điểm danh vẫn kiểm tra lại quyền. Không có participants, attendance của người khác, email/phone, private evidence hoặc QR secret.

Recommendation: limit=1..6, semesterId hoặc scope. sourceLabel=LOW_AXIS_RULE, ruleVersion=LOW_AXIS_UNROUNDED_START_ID_V1.
Trục thấp tính từ X chưa làm tròn; tie theo CoreSix rồi startTime rồi activity ID. Không dùng +1 làm trục thứ bảy. Có recommendationId/rank/targetPillar/reason/radarSnapshot/sourceAsOf/scopeVersion.
Không có candidate => items=[]; dependency lỗi =>503. GET không mint điểm hoặc bắt buộc telemetry.

History: page/pageSize/semesterId/verificationStatus. Envelope items/page/pageSize/totalItems/totalPages/asOf.
Giữ activity/session/attendance versions, contributionId và sourceNamespace. Legacy chưa có session vẫn UNVERIFIED, không suy Present thành verified.

### Nhiệm vụ

Create/update của backoffice bổ sung criteria:
targetPillar (CoreSix), eligibleActivityIds (1..100), policyVersion (approved PERIOD),
minimumVerifiedContributions, minimumAllocatedPoints, eligibleSessionIds tùy chọn, ruleVersion=LOW_AXIS_QUEST_V1.
Update criteria cần expectedVersion; criteria/semester/campus/deadline đã được join không sửa trực tiếp.

Catalog/mine mặc định giữ array có giới hạn trang đầu để compatibility; gửi page/pageSize để nhận envelope.
Catalog có X-Total-Count. GET không tự tạo nhiệm vụ demo.
Với Sinh viên đã đăng nhập, GET catalog mặc định xếp nhiệm vụ phù hợp theo trục thấp, lọc campus/kỳ/deadline/nguồn hợp lệ và loại nhiệm vụ đã chọn trước khi phân trang. Có targetPillar/eligibleActivityIds/policyVersion/rank/reason/ruleVersion/radarSnapshot/sourceLabel=LOW_AXIS_RULE. recommended=false đọc catalog thông thường với sourceLabel=CATALOG. Khách chưa đăng nhập gọi recommended=true nhận401. Radar/policy/nguồn unavailable trả lỗi rõ, không fallback thành danh sách giả được cá nhân hóa.
rewardXp/reward là LEGACY_DISPLAY_ONLY; extraPointsAwarded=false.
completedCountBasis=RECORDED_COMPLETION_HISTORY; số này là lịch sử completion đã ghi, không phải bút toán điểm.

Join chỉ principal, lấy identity canonical, đóng băng criteria/period/campus/deadline vào participation.
Hoạt động đã đăng ký vẫn được dùng cho join dù đủ chỗ/đóng đăng ký; buổi nguồn phải còn phía trước. eligibleSessionIds được resolve đúng buổi, kể cả buổi sau của chuỗi. Criteria/session selector rỗng hoặc trùng bị từ chối. Quest đổi version trong lúc kiểm tra nguồn =>409 STALE_QUEST.
Complete body: expectedVersion của participation, linkedContributionIds tùy chọn, note.
note tối đa500 ký tự, khớp schema SQL. Complete kiểm tra nguồn Activity hiện tại cả trước idempotency replay; không chờ inbox hủy nguồn mới ngăn completion. Nguồn unavailable =>503 và khi đọc mine có progressAvailable=false/effectiveStatus=UNAVAILABLE. Không sửa các completion record cũ.
StudentUserId legacy có thể cùng principal; giá trị khác =>403, không có chức năng complete cho người khác.
Chưa join =>409 QUEST_NOT_JOINED. Chưa có verified proof phù hợp =>409 QUEST_PROOF_REQUIRED.
Pending/rejected/revoked/legacy/khác tác giả/khác source/kỳ/trụ cột không đủ proof.
Idempotency-Key được hỗ trợ; nên gửi cho join/complete để retry an toàn.
Bằng chứng bị revoke phải đọc effectiveStatus=REQUIRES_REVALIDATION. Status/CompletedAt cũ và completion record được giữ cho lịch sử.
Không có ledger effect phát sinh từ join/complete; điểm thuộc contribution nguồn.

### Gia nhập CLB

POST /api/clubs/{id}/join vẫn giữ route.
Name/email/phone trong body không được ghi đè danh tính Auth. FullName/Email/canonical phone được resolve từ server.
Nhập reason, category-specific additionalInfo và consent. DOB/Gender/Address là thông tin đơn nếu cần, không ép nộp lại để tạo điểm.
Tài khoản inactive/locked hoặc khác campus bị chặn; Auth chưa cấu hình/lỗi trả503.
Idempotency-Key tùy chọn; pending/approved hiện có không tạo thêm record. Rejected/deleted có quy trình gửi lại.

## Migration và môi trường

- Admin: 20261013142006_StudentExperienceCompletion; 20261013145324_StudentCompletionDefaults.
- Activity: 20261013142006_StudentActivityDiscovery.
- Giữ migration Quản lý CLB trước đó. Additive schema, không backfill legacy thành điểm verified.
- Security:InternalWorkflowToken phải cùng giá trị giữa service; nội bộ không gọi qua FE.
- Auth/Club/Activity base URLs phải được cấu hình; không dùng dependency outage để giả số liệu.
- Chưa áp dụng migration lên DB thật của người dùng. Kiểm thử SQL dùng DB riêng có prefix task-owned và được cleanup.

## Kiểm thử

Ngày 14/10/2026: toàn solution 740/740 test passed, 0 failed, 0 skipped: Shared 123, Admin integration 85, Backend stabilization 532. Có 16 test Sinh viên mới, gồm bốn test SQL Server. Kết quả TRX và coverage lưu ở TestResults/StudentFinal_2026-10-14.

Release build với -warnaserror: 0 warning, 0 error. Tất cả format gate của CI qua. EF has-pending-model-changes qua cho 8 service; model khớp migrations. Dependency vulnerability scan không báo package có lỗ hổng ở nguồn NuGet hiện tại. git diff --check qua.
Các ca mới phủ net period/cumulative, legacy, >100 contribution summary, source outage,
recommendation vượt trang100, self quest/proof/replay/revoke/no extra points,
privacy/paging/deep links/recurring sessions/history và SQL Server queries/migrations.
Các ca bổ sung phủ quest đổi version trong lúc resolve nguồn, nguồn hủy trước khi inbox cập nhật, outage trước replay, mixed foreign proof, reconstruction delta lịch sử, nhiệm vụ xếp hạng trước paging, session selector cho buổi sau và counter version đổi dù count giữ nguyên.
Fixture lời mời dùng hồ sơ Auth canonical, giữ consent của chính sinh viên.
Toàn bộ luồng Manager dùng chung phải tiếp tục qua; không tắt test hoặc thay production auth bằng demo fallback.

## Ngoài phạm vi

Mã FE chưa sửa; tài liệu contract đã đồng bộ trạng thái BE. API-033 read version của manager và các operation Verifier/Admin khác vẫn giữ trạng thái cũ.
Wallet/redemption/leaderboard/FAP/timetable/ML/public student profile/withdraw application tiếp tục BỎ QUA hoặc deferred theo contract.

