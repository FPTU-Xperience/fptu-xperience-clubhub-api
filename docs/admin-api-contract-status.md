# Admin FE API contract status

This status is for the backend contract compared with `API_1.md` (22 September
2026). The comparison is based on source routes and local tests, not a live
production deployment or an end-to-end Admin FE run. The implementation is on
`codex/admin-api-contract-gap`, fast-forwarded onto `382cefd` on
24 September 2026; its API changes are still uncommitted.

## Implemented route coverage

The 34 method/path pairs that were absent in the original 121-entry route
inventory are now declared. This is route coverage only: it does not imply that
all example IDs, request fields, roles, response shapes, or workflows in
`API_1.md` are supported. Existing numeric IDs and persisted domain states
remain the source of truth. Key safeguards in the new routes include:

- Deleting a user or deadline disables it rather than removing history; deleting
  an activity cancels it; deleting an eligible report archives it while retaining
  audit, attendance, finance references, and stored files.
- Adding a club member creates a pending invitation. The invited user must
  personally submit their membership information and consent before approval.
- Each account keeps one actor role. Removing its sole role switches it to
  `CLUB_MEMBER`; the final active `ADMIN` cannot be removed or reassigned.
- Proposal and settlement creation remains `Submitted`. Their `submit` routes
  acknowledge that state, and `review` records a note without approval or a
  ledger entry. Manual transactions are reviewer-only adjustments with a reason,
  idempotency key, and a separate audit record.

## Explicitly unsupported or not yet authoritative

| Area | Current boundary | Required to complete it |
| --- | --- | --- |
| CTSV / SYSTEM_ADMIN Google sign-in | Google actor policy admits `ADMIN`, `CLUB_MANAGER`, and `CLUB_MEMBER` only. Existing authorization policies referencing other roles do not make those roles sign-in capable. | Approved identity source, provisioning, and role policy. |
| Student-by-semester export | Export requests require a real report ID; no authoritative student/semester export dataset exists here. | Dataset, period mapping, file format, and access scope. |
| Student code, club founding date, single manager ID | Do not fabricate these fields from unrelated rows or sample IDs in `API_1.md`. | Authoritative schema/source and mapping rules. |
| Ratings and anomalies | No approved source or computation rule for the example values. | Product-approved definitions and data provenance. |
| KPI weight administration | The existing KPI endpoint computes from persisted report/activity data using fixed rules; those constants are not an approved configurable KPI-weight source. | Approved rules and ownership for changing weights. |
| Admin FE persistence | A backend route alone does not replace FE-local state or prove the page calls it. | Separate FE integration and browser verification against this contract. |

The API should return a clear validation/authorization error where a request
cannot be fulfilled by the real model; it must not return a fake success or
invent student, rating, KPI, or anomaly data. The sample string IDs in
`API_1.md` are examples, not a migration away from numeric domain IDs.

## Validation boundary

Local unit/integration tests and Compose configuration checks validate the
code paths exercised by those tests. The new manual-adjustment audit table
requires its EF migration before that endpoint can be used against an existing
database. No production migration or deployment is performed as part of this
API work. Disposable local SQL Server 2022 tests now verify the Admin and
Finance migrations, including Finance's active-settlement unique index.
End-to-end Admin FE calls against production remain unverified.
