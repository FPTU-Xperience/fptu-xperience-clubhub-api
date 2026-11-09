# Club credit and reward contract

Approved by the user on 2026-11-04: retain the verified experience ledger, derive **one CLUB_CREDIT per verified XP of the same club**, include existing verified history, and reverse credit by the source delta. Previously spent credit may leave debt; keep receipts and ownership and block further redemption until the debt is repaid. XP/radar are never debited by redemption. No sample-wallet import or money/legacy-budget conversion.

## Authority and storage

- Earning is a credit projection of append-only `ExperienceLedgerEffect` joined to its own user's `VerifiedContribution`, matching club, source type CLUB, nonempty verification and policy IDs. Each effect ID appears once, so retries or repeated reads cannot mint a second credit. `XpLedgerEntries`, raw activity budgets, browser counters and external/+1 contributions are not wallet credit.
- Separate `ClubRewardRedemption` journal records spending; `ClubCreditWallet` stores spending/version for transactional CAS. Reconciliation checks compare its cached spending against immutable debit history. The source ledger revision and wallet version produce the returned opaque wallet version, so a new earning/reversal also invalidates an old redemption quote.
- `balance = positive verified effects - negative verified effects - redeemed amount`. Wallet is lifetime per user/club; graph date range does not reset spending or mix currencies.
- New schema is additive. Cosmetic definitions, ownership, receipts and reward audit records are append-only. Published reward definition/price/grants are immutable; retirement stops new claims while retaining existing ownership.

## Routes

- Authenticated own reads: GET `/api/clubs/{clubId}/wallet/me` and `/points/me` are aliases; `/wallet/me/graph?days=1..365`; `/points/me/ledger?page=&pageSize=`; `/rewards/redemptions/me?page=&pageSize=`. Query `userId` never selects another account. GET `/api/users/me/cosmetics` returns own paged entitlements and immutable definitions.
- Public GET `/api/clubs/{clubId}/rewards?page=&pageSize=` returns published reward metadata and server `DRAFT/SCHEDULED/OPEN/EXPIRED/RETIRED` availability. `manage=true` requires current owner/global Admin and includes drafts. Capabilities separately project management and current member redemption eligibility.
- Owner/global Admin POST `/rewards`, PATCH `/rewards/{rewardId}` (draft only), POST `/publish`, POST `/retire`. Every write uses `Idempotency-Key`; edits/decisions require current version. Basic palette is four HEX colors. SHAPE/FRAME requires a verified `club-reward` PNG/JPEG/WebP upload belonging to this actor and issuing club. BUNDLE references an immutable color and shape from this issuer. Physical fulfillment and SVG/advanced-gradient publication are not silently emulated.
- Member POST `/rewards/{rewardId}/redeem` accepts only `{expectedRewardVersion,expectedWalletVersion}` and `Idempotency-Key`. It revalidates active account/current approved membership, issuer, current window/version and funds in a serializable transaction; no client actor, price or grants are accepted. Different concurrent keys cannot overspend one wallet. Existing reward receipt is returned without another charge. A partially owned bundle charges the displayed full price and grants only missing components; full ownership charges nothing.
- Media intent kind `club-reward` uses existing current-club-manager ownership authorization, quota and decoded-image verification. No raw data URI or arbitrary public asset URL is a trusted entitlement.

## Graph and integration

Graph dates use Asia/Ho_Chi_Minh with an opening balance. Series show cumulative net credit after adjustments, spending and remaining balance; earning is not reduced when spending occurs. Ledger lines carry `VERIFIED_XP_EFFECT` or `REWARD_REDEMPTION` source IDs. All own reads use no-store and retain zero/negative values.

Backend/API tests, CI, deployed UI fixtures and real-account acceptance are separate evidence. Do not treat sample ownership as a server grant or claim real upload/redemption acceptance without a real authorized test.
