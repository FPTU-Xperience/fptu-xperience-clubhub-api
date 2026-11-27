# Raw-point funding and versioned index

Funding means raw experience points. Cash, bank transactions and taxes are outside
this contract (SRS BR-01–BR-05 / OS-02). Club and campus pools are separate. A pool
is identified by club ID (zero for campus), campus and canonical semester code.

## Opening and closing

Admin owns grants, reservations and immutable funding entries. Activity owns the
opening source and closing evidence. A funded opening requires a positive budget,
an approved effective period policy and explicit source attributes. The current
campus reviewer authorizes a synchronous internal reservation before Activity
creates the funded execution; opening itself never awards XP.

The request carries both the current source revision/content hash and the actual
reviewer's bearer token plus the service token. Admin reads Activity's internal
source proof and independently verifies the exact scope, revision and policy.
Reservation retry keys derive from the opening source/revision. If Activity fails
after Admin commits a reservation, funds stay held. Explicit source reconciliation
binds a confirmed execution or refunds an abandoned/changed/expired opening.
An unavailable dependency is never evidence of abandonment.

Closing approval releases immutable attendance/duty candidates. Funded candidates
stay pending with zero XP. Individual review routes cannot bypass settlement.
The complete pending set must be decided with every current candidate version;
all expected generated candidates must have arrived. Self review, missing results,
invalid source/policy or insufficient budget posts no partial XP. XP effects and
their linked raw-point debits share one Admin SQL transaction. Settlement releases
the unused reservation. Cancellation reverses awarded effects before refunding
the remaining reservation exactly once. Existing unfunded activities are not
retroactively charged or rescored.

An independent campus reviewer may explicitly increase an open execution's budget
from the same pool's available points. Both reservation and account versions,
current source proof, sufficient balance and a 5–1000-character reason are required.
The increase appends a new ledger entry; it does not rewrite the opening snapshot.
Settled/refunded budgets cannot increase through this operation.

## HTTP surface

- `GET /api/v1/raw-funding/{clubId}?semesterId=...&campus=...&page=...`
  returns reconciled balances, current account version, paged ledger/reservations
  and live capabilities. `total` and `reservationTotal` are independent counts.
- `POST /api/v1/raw-funding/{clubId}/grants`: semester, expected account version,
  positive amount, reason; campus staff only, active semester.
- `GET /api/v1/raw-funding/reservations/{id}`: closing state, pending-message
  count, candidate versions/eligibility and current settlement capability.
- `POST .../reservations/{id}/settle`: expected reservation version, reason and
  one current-version APPROVE/REJECT decision for every pending candidate.
- `POST .../reservations/{id}/increase-budget`: expected reservation/account
  versions, positive four-decimal amount and reason.
- `POST .../reservations/{id}/reconcile`: expected reservation version and reason.
- Campus drafts use `POST /api/activities/campus/{id}/funding-approval` before
  publication. Funded campus activities use the common independent closing-report
  workflow instead of the old self-finalized report route.

All writes require `Idempotency-Key`. Private evidence is still authenticated and
scoped to the current reviewer. Ledger entries are append-only; a mismatch with
the balance projection returns an explicit reconciliation error, never zeroes.

## Policy boundary

New policy approvals record `ERI_MEAN_PIELOU_LINEAR_W_V2`:
`W = 1 + 0.30 * clamp(S7 / T7, 0, 1)`. All seven positive thresholds are required.
The six core axes supply D/J; the seventh changes W. Historical snapshots without
a formula version remain `ERI_MEAN_PIELOU_SATURATED_V1`; they retain their original
M computation. The response exposes W only for a linear-policy snapshot. No
historical policy or contribution is silently rewritten.

## Release and evidence

Deploy Admin/schema support before Activity begins producing funded sources;
deploy Gateway for the new route family, then the two UIs. Full SQL CI, source
proof integration and browser fixtures are automated evidence. They do not replace
the separately deferred acceptance run using real production accounts.
