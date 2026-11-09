# Student preferences, schedule and wallet — BE contract v1

Status: proposed implementation contract, requested 2026-10-27. No endpoints, tables or migrations are implemented by this document. Source baseline: API `bbf7367`, ClubHub UI `fd20ae1`. Existing source behavior is explicitly distinguished below.

Database specification: [student-data-schema-v1.md](student-data-schema-v1.md). Related UI [consistency audit](../../../fptu-xperience-clubhub-ui/docs/student-data-consistency-audit-2026-10-27.md), [schedule contract](../../../fptu-xperience-clubhub-ui/docs/contracts/study-schedule-api.md), [D07 credit lifecycle](../../../fptu-xperience-clubhub-ui/docs/contracts/club-contributions-points-api.md), [D08 redemption](../../../fptu-xperience-clubhub-ui/docs/contracts/profile-cover-rewards-api.md).

This contract owns the new student persistence and wallet requirements. Existing schedule HTTP behavior remains compatible. Earlier browser-only onboarding/200 sample-point provisions are temporary UI behavior, never a production persistence or credit source.

## 1. Data authority

| Aggregate | Owner/database | Authority and boundary |
| --- | --- | --- |
| Student preferences/onboarding | AuthService/AuthDbContext | Own-user editable preferences, declared major/cohort and server completion receipt. |
| Canonical personal profile | AuthService/AuthDbContext | Existing display name, contacts, birth date, biography and image references; not duplicated in preferences. |
| Official academic identity | AdminService roster + account identity | Campus, student code and roster provenance remain read-only to students. |
| Student schedule | AuthService/AuthDbContext | Existing private schedule aggregate migrated to requested table names. |
| Academic calendar/semester | AdminService/AdminDbContext | Official semester GUID/code and campus calendar; never a browser catalog. |
| Experience/XP | AdminService/AdminDbContext | Existing verified experience ledger, policy and corrections; not spendable credit. |
| Club credit wallet/redemption | ClubService/ClubDbContext | New own-user, per-club credit ledger and inventory, in the same DB transaction. |

`userId` always comes from the authenticated principal. Keep existing positive integer user/club IDs; new aggregate IDs and opaque concurrency versions are GUIDs. Instants are UTC ISO-8601; schedule dates use `Asia/Ho_Chi_Minh`. Versions in examples are placeholders, not client-issued values. A missing aggregate uses the empty GUID version and no fabricated balance/grant; GET does not create rows.

No cross-service SQL foreign keys, shared database writes, or distributed transaction is required. Cross-service authority uses authenticated internal clients; durable side effects use source outbox + deduplicated consumer inbox. Internal headers alone are not sufficient trust without the existing service-authentication policy.

## 2. Common HTTP and consistency rules

- All public routes use Gateway and the existing bearer API client. Own preferences/schedule/wallet reads return `Cache-Control: private, no-store`; no public arbitrary-student variant.
- Revalidate active/unlocked actor and resource capabilities at commit. A club manager cannot edit another student's preferences or schedule, debit their wallet arbitrarily, or equip their cosmetics.
- Mutations require `Idempotency-Key`, body hash and durable response receipt, scoped by actor + method + canonical route + key, minimum 24-hour replay retention. Same key/body replays the original receipt even after the expected version changes; changed body is `409 IDEMPOTENCY_CONFLICT`. Permanent business uniqueness survives receipt expiry.
- Partial PATCH: missing fields preserve values, documented nullable fields accept null; reject unknown fields and attempted identity/authority writes. Validate the whole command before changing anything. Concurrency GUIDs are server-generated EF concurrency tokens; never use last-write-wins.
- Error JSON: `{code,message,traceId,errors:{field:[message]}}`. 400 invalid body, 401 missing session, 403 forbidden field/action, 404 nonexistent scope, 409 stale/state/conflict, 503 missing dependency. No false zero or local success on 503.
- Successful writes return canonical state/receipt. FE discards stale requests after account/period changes, invalidates affected query keys and broadcasts account-scoped invalidation without private payloads. Other tabs/devices refetch on focus; conflicts require refetch and explicit retry.

## 3. Student preferences and onboarding

### Ownership and vocabulary

`student-preferences` is the sole source for preferences and onboarding state. Allowed stable interest IDs v1: `technology`, `design`, `business`, `arts`, `sports`, `community`, `languages`, `leadership`. `customInterests` contains trimmed student labels. `declaredAcademic.major` and `cohortLabel` are self-declared, never written into `academic` roster fields. Official campus/student code are displayed with source availability and cannot be edited here.

`syncTimetable` records a preference only; it neither imports a timetable nor claims an external integration exists. Store no email, password, token, avatar blob, wallet amount or official XP in this aggregate.

### GET /api/users/me/preferences — new

Returns 200 for an eligible student, including an unsaved empty draft:

```json
{
  "userId": 42,
  "version": "<preferences GUID or empty GUID>",
  "schemaVersion": 1,
  "onboarding": {"status":"DRAFT","completedVersion":null,"completedAt":null},
  "declaredAcademic": {"major":null,"cohortLabel":null,"provenance":"SELF_DECLARED"},
  "interestIds": [],
  "customInterests": [],
  "syncTimetable": false,
  "updatedAt": null
}
```

Authenticated staff who are not students bypass student onboarding; this endpoint returns `403 STUDENT_REQUIRED` for them. Determine student eligibility from authoritative account state, not email parsing in FE.

### PATCH /api/users/me/preferences — new draft/edit

Body `{expectedVersion, declaredAcademic?:{major?,cohortLabel?}, interestIds?, customInterests?, syncTimetable?}`; return full GET DTO. Draft may be incomplete. Major nullable 1..120 after trimming, cohort nullable 1..30; empty optional text normalizes to null. IDs must be allowlisted; lists must be arrays, deduplicated, total at most 30 labels, custom label 1..100; canonical ID labels cannot be duplicated as custom interests. Invalid supplied values fail instead of being silently dropped. No-op returns current canonical state without rotating versions.

A completed onboarding remains completed when preferences change later, including clearing interests. The completion receipt is a historical fact, not a repeated test of present-day interests. Do not redirect a completed student back to onboarding just because their interests changed.

### POST /api/users/me/onboarding/complete — new atomic command

```json
{
  "expectedPreferencesVersion":"<GET preferences version>",
  "expectedProfileVersion":"<GET profile version>",
  "onboardingVersion":1,
  "preferences":{
    "declaredAcademic":{"major":"Kỹ thuật phần mềm","cohortLabel":"K20"},
    "interestIds":["technology","design","community"],
    "customInterests":[],
    "syncTimetable":false
  },
  "profile":{"displayName":"Nguyễn An"},
  "personal":{"dateOfBirth":"2005-05-17","phoneNumber":"0901234567","address":null}
}
```

For first v1 completion require a declared major and at least three distinct interests across canonical/custom lists. Cohort/contact fields are optional. `profile`/`personal` are optional partial patches using existing canonical self-profile validation and editable-field allowlist, except `profile.interests` is forbidden here because preferences already carry them. Reuse media ownership verification if image fields are allowed; do not accept raw URLs as newly verified uploads.

Within one AuthService transaction: lock/recheck preferences + profile versions, validate all input, write preferences and canonical profile changes, set first completion timestamp/version server-side, persist idempotency response. Return `{preferences:<GET DTO>, profile:<canonical profile DTO>}`. Any invalid contact, stale version or dependency failure leaves both aggregates and completion unchanged. Preserve first `completedAt`; later preference edits use PATCH. A repeated complete command with a different key on already completed v1 returns `409 ONBOARDING_ALREADY_COMPLETED`; same-key replay returns the original receipt. Unsupported onboarding version is `400 ONBOARDING_VERSION_UNSUPPORTED`.

Preferences errors: `STALE_PREFERENCES`, `STALE_PROFILE`, `ONBOARDING_INCOMPLETE`, `PREFERENCE_FIELD_FORBIDDEN`, `INVALID_INTEREST`. Do not mark FE complete or navigate away on an ambiguous transport failure; replay the same command/key or GET authoritative completion.

### Existing profile compatibility — required extension

`GET /api/users/me/profile` retains `academic` as official identity and adds `preferencesVersion` and `declaredAcademic` separately. `profile.interests` becomes a compatibility projection of preferences (stable IDs mapped to the v1 labels, then custom labels). It is not a second persisted copy. Internal member profile projection uses the same canonical mapping.

Existing `PATCH /api/users/me/profile` with `profile.interests` must write preferences in the same AuthService transaction; extend its command with required `expectedPreferencesVersion` for that field. Missing version: `400 PREFERENCES_VERSION_REQUIRED`. Other profile patches remain compatible. Every preference change affecting projected interests/declared academic data rotates the profile version too; every profile-interest patch rotates both versions. This closes cross-tab stale-write holes without allowing two independently writable interest lists.

Keep existing `CanonicalUserProfile.InterestsJson` only for migration/backfill until cutover, then remove it. FE and internal callers must migrate together before enforcing the new preference version. Preserve `campusCode` in the auth projection; do not replace authenticated identity with declared fields.

## 4. Student schedule

Existing API is implemented. Keep these routes and response shapes:

| Method/path (root `/api/users/me/study-schedule/{year}/{term}/{block}`) | Contract |
| --- | --- |
| GET root | Own block `{year,term,block,version,calendarVersion,calendar,semesterKey,semesterId,campusCode,availability,canWrite,entries}`. |
| PUT root/{week}/{day}/{slotId} | `{subject,room,expectedVersion,expectedCalendarVersion}` -> saved cell + new versions. |
| DELETE root/{week}/{day}/{slotId} | Version query parameters -> 204; refetch GET before next edit. |
| POST root/bulk-import | `{entries,mode:"skip"|"replace",expectedVersion,expectedCalendarVersion}` -> full cells/counts/new versions. |

Do not add a parallel `/student-schedule` HTTP alias. Physical `student-schedule` replaces `StudyScheduleBlocks`; `student-schedule-cell` replaces `StudyScheduleCells` through a data-preserving migration. Same user/period/block has exactly one aggregate. Semester/calendar authority stays in AdminService; no duplicated writable semester or calendar table in AuthService.

Preserve existing wire naming: schedule `semesterKey` is the official GUID and `semesterId` is the official code (see current shared `AcademicCalendarReference`). API alias year 2000..2100, term SP/SU/FA, block 10W/3W. Explicit deployment-managed alias mapping resolves the official semester; never derive it from code text or browser time. If existing UI prose reverses GUID/code labels, implementation/shared DTO takes precedence.

10W weeks 1..10 and slots 1..5; 3W weeks 1..3 and slots 1..7; day mon..sun. Unique cell `(scheduleId,week,day,slotNumber)`. Subject trimmed 1..120; room trimmed 0..80. Bulk operation is all-or-nothing and preserves existing skip/replace count semantics. Empty schedules may be virtual; first write uses empty GUID expectedVersion. Validate calendar before creating a real header.

Retain `SCHEDULE_CHANGED`, `CALENDAR_CHANGED`, `SEMESTER_READ_ONLY`, missing/ambiguous semester and calendar-unavailable errors. Inactive-period own reads remain possible; new writes require active authoritative calendar. Calendar changes never move or delete cells; re-render dates from the new source and require its version before further writes. FE must label this as a student-maintained schedule, not a registrar-verified timetable.

Open the student's canonical active semester using the existing semester/calendar catalog, not `new Date()` heuristics. One failed source read must not show an empty successful schedule. Historical calendar revision retention is a separate display concern; the last-used version is retained for write audit, not a second calendar authority.

## 5. Club credit wallet

### Resource and reads — new

One wallet for `(userId,clubId,unit=CLUB_CREDIT)`, continuous across semesters. Campus is inherited from the issuing club; semesters tag earning transactions and reports, never reset the wallet. Stored units are integer credits; no decimal, XP, radar percentage or currency conversion. JSON amounts/counters are safe integers: absolute values <= 9007199254740991. Enforce those bounds on every mutation and reject overflow atomically; database reconciliation sums with decimal(38,0).

`GET /api/clubs/{clubId}/wallet/me`:

```json
{
  "walletId":null,"userId":42,"clubId":7,"unit":"CLUB_CREDIT",
  "balance":0,"earnedTotal":0,"revokedTotal":0,"spentTotal":0,
  "version":"<empty GUID>","lastEntrySequence":0,"asOf":"<UTC>",
  "canRedeem":true,"unavailableReason":null
}
```

Virtual zero is allowed only after a successful authoritative DB/eligibility read proves no wallet exists. A storage/authority failure returns 503, never zero. `canRedeem` describes account/club eligibility, not whether any particular reward is affordable. Active approved members can redeem; a former member may read their own retained wallet/receipts but cannot redeem. Nonmembers with no wallet/history get 403. Never expose arbitrary member balances through this own endpoint.

`GET /api/clubs/{clubId}/points/me` projects the same wallet/version. Optional `semesterId` filters only period counters `{semesterId,earnedInPeriod,revokedInPeriod}`; `balance` always remains the lifetime spendable balance and is labeled as such. Resolve GUID/code via the official semester catalog, not raw string equality.

`GET /api/clubs/{clubId}/wallet/me/ledger?page=1&pageSize=20` returns `{items,page,pageSize,totalItems,totalPages,snapshotSequence}`; each item `{id,sequence,type,amount,balanceAfter,sourceType,sourceId,semesterId,occurredAt,recordedAt,reason,correlationId}`. First page captures `snapshotSequence`; subsequent pages must supply it to filter immutable entries `sequence <= snapshotSequence`, ordered sequence DESC. `/points/me/ledger` uses this same implementation and DTO. The snapshot belongs to the same account/club and cannot exceed current sequence.

### Earning credits — internal command, not a student mint endpoint

Trusted approved task/activity credit policy publishes an immutable event with `{eventId,sourceNamespace,awardId,userId,clubId,semesterId,policyVersion,occurredAt,sourceRevision}`. ClubService authenticates source authority, validates the referenced approved award and derives the integer amount from the approved credit-policy snapshot. XP approval alone does not grant spending credit. No browser command accepts arbitrary user/amount or imports sample wallet balances.

Consumer transaction: deduplicate event + permanent award identity, revalidate issuing club and grant eligibility, create/lock wallet, append one CREDIT, update balance/counters/version/sequence, commit durable inbox receipt/outbox. Duplicate event with changed content is a conflict, not success. Different event IDs for the same award still credit once. Downstream notification failure does not undo a committed credit. Until receipt exists, upstream shows `PENDING`; failed delivery is retried/dead-lettered, never reported `CREDITED` without receipt.

Corrections append `REVERSAL` referencing the original credit; amount is derived from that original entry and may reverse it only once. A corrected award uses a distinct approved revision with a new award identity after reversing the old one. Preserve immutable originals and reasons. A reversal after spending may make balance negative; existing legitimate redemptions remain, further redemption is blocked until balance recovers. This is the v1 debt rule, replacing any assumption that every balance must be nonnegative.

### Redemption — new

`POST /api/clubs/{clubId}/rewards/{rewardId}/redeem`, body `{expectedRewardVersion,expectedWalletVersion}`, requires active approved membership and an Idempotency-Key. Server derives price and grants from published reward version; forbid `amount`, `price`, `userId`, `balance`, `grants` in request.

One ClubService SQL transaction locks/rechecks wallet + reward/membership, verifies issuer/version/publication, checks existing entitlements, sufficient balance, appends DEBIT if price > 0, writes redemption receipt and missing cosmetic grants, rotates wallet/inventory versions and persists response/outbox. Unique `(userId,rewardId)` ensures reusable rewards are acquired once even with different concurrent keys. Reward definitions/grants are immutable once published; an altered offer gets a new reward ID. Existing owned components of a bundle do not change its published price; receipt grants only missing components. Full existing ownership returns `outcome:ALREADY_OWNED` without charge. A free claim creates a receipt/grant, no zero-amount debit; negative debt balances still block claims under v1.

Return `{receiptId,outcome:"REDEEMED"|"ALREADY_OWNED",rewardId,chargedAmount,grants,balance,walletVersion,inventoryVersion}`. Same-key replay returns the exact original response before stale-version rejection after current authorization checks. A duplicate acquisition with a new key returns `ALREADY_OWNED`, `chargedAmount:0`, `grants:[]`, the original receipt ID for that reward and current own wallet/inventory versions. If all components were acquired from other rewards and this reward has no receipt, return `receiptId:null`; do not fabricate a receipt or a debit. Never mutate historical price/grants to make an old receipt look newly redeemed. Receipt history retains its original charge; this response's chargedAmount describes this command only.

`GET /api/clubs/{clubId}/rewards/redemptions/me` paginates immutable receipts; `GET /api/users/me/cosmetics` returns canonical owned grants and inventory version across clubs. Reward catalog/entitlement state and this wallet must share ClubDbContext transaction ownership. Physical gifts/stock/fulfillment are outside v1.

Errors: `WALLET_CHANGED`, `REWARD_CHANGED`, `INSUFFICIENT_CREDITS`, `WALLET_IN_DEBT`, `REWARD_NOT_PUBLISHED`, `MEMBERSHIP_INELIGIBLE`, `CREDIT_SOURCE_CONFLICT`, `WALLET_UNAVAILABLE`. Initial real balance is zero; there is no production 200-credit seed. UI remains unavailable or explicitly preview-only until these operations are implemented and verified.

### Invariants

- `balance = SUM(ledger.amount)`; CREDIT positive, DEBIT/REVERSAL negative. Wallet sequence increases once per nonzero ledger effect. `earnedTotal = SUM(CREDIT)`, `spentTotal = -SUM(DEBIT)`, `revokedTotal = -SUM(REVERSAL)`; balance equals earned minus spent minus revoked.
- Each entry is immutable with a unique wallet sequence and permanent source identity. Totals and insert commit together. Reconciliation detects differences; it never fixes them by rewriting ledger entries.
- Balance reads/counters use one consistent snapshot; parallel credits/debits/reversals serialize on wallet row or fail/retry via concurrency token. Multi-aggregate operations lock resources in a deterministic order.
- Only an explicit future refund contract can credit a redemption back. A debit cannot be disguised as an XP adjustment or edited by a club manager.

## 6. Experience consistency compatibility

No new student-XP table is needed. Reuse existing verified contribution ledger. FE must pass one selected official semester to list/summary/radar and distinguish unfiltered history from scored PERIOD/CUMULATIVE. Bind summary/radar to the same `asOf`, semester, policy and ledger revision; mismatch triggers reload/unavailable. Do not silently use CUMULATIVE without its approved policy. Lists are current records unless a snapshot contract is explicitly implemented; never claim arbitrary list pages are an atomic historical ledger snapshot. Approved/corrected/revoked experience invalidates affected student reads, but does not automatically credit/debit the spendable wallet.

## 7. Rollout and required acceptance

1. Add preferences schema and backfill server interests; migrate schedule table names with IDs/versions preserved. Deploy additive reads before switching clients.
2. Implement atomic preferences/profile completion and profile projections. Update FE gate to server receipt and remove local onboarding authority. No automatic import of stale local profile/onboarding values; a user-confirmed import must still use current expected versions.
3. Implement wallet ledger, receipts, catalog/inventory persistence, authenticated award consumer and Gateway routes. Keep real redemption disabled until end-to-end checks pass. Never migrate browser sample balances/ownership.
4. Switch FE to canonical semester/refresh rules, then retire duplicate legacy columns only after caller compatibility is verified. Deploy migrations once per owning service; no EF EnsureCreated against production.

Required BE tests: own vs other user/campus/club; nonstudent/onboarding eligibility; unavailable official academic source; simultaneous preferences/profile edits; completion rollback on bad personal input; same-key replay/different-body conflict; schedule data-preserving upgrade, alias/calendar changes and concurrent first writes; ledger conservation, duplicate event/different event same award, concurrent credit/debit/reversal, negative debt and zero-price claim, double redemption with distinct keys, rollback after debit before grant, old member read vs redemption denial; paged ledger snapshot stability. Gateway tests must prove exact route/verb ownership and no management fallback.

Required FE/live acceptance: finish onboarding on device A and read on B; clear browser storage; switch account during pending read; change semester; approve/correct/revoke experience while Profile is open; concurrent schedule edits; re-open wallet after redemption; retry a lost receipt response; ensure sample data never appears as real credit. Passing source/build tests does not replace authenticated deployed acceptance.
