# FPTUX API final product requirements

Status: target contract artifact, 2026-10-02. The owner-confirmed registration v1.3, Report 3 SRS draft, and decision register in `capstone-documents` define the final product. [API_ENDPOINTS.md](API_ENDPOINTS.md) and source code document current implementation only. See `fptu-xperience-doc/documents/final-product-baseline.md` for the cross-product summary.

## Server boundary

`fptu-xperience-clubhub-api` is the designated FPTUX API repository for ClubHub web, Admin web and Mobile. The final product has one authoritative server boundary for identity, role decisions, semester roster, clubs, activities, check-in, raw-point ledger, credited wallets, ERI and histories. The existing ClubReportHub services may be reused, but current endpoints do not establish final-product coverage.

| Contract family | Required target behavior |
| --- | --- |
| Identity and authorization | Resolve System Admin, Student Affair (SA), student and club-scoped Club BOD rights on every protected request. A person may hold different offices in different clubs. Active-roster checks apply to nominations and applicable student actions. |
| Clubs and applications | Support club establishment/closure, membership, ordinary president transfer with SA approval, office history, and emergency nomination with student acceptance in My Applications. Never delete a paused club as a side effect. |
| Activity governance | Accept one club dossier containing content, category allocation and a proposed raw-point budget. SA approves, rejects or requests revision and may provide a numeric suggested budget. SA publishes global activities directly. |
| Club bonus | SA grants raw bonus balance. Club BOD may allocate available bonus to an already approved activity without another approval. Keep grant, allocation, release and actor history. |
| Attendance and awards | A valid check-in creates an attendance record and one automatic raw participation award. Club BOD can add distinct raw contribution awards during the activity. Give the student a per-event record history with raw input, credited output and status. |
| Self-declaration | Student chooses exactly one of seven categories and submits effort/evidence, without a proposed amount. Outside-club claims go directly to SA; in-club claims require club membership and Club BOD referral with optional recommended raw amount. SA may edit category and raw amount and is the final gate for both. |
| Semester and roster | SA imports the active roster in Admin; if no new import action occurs, carry forward the previous list. Reset current-period points, activity availability, raw budgets and club bonuses at rollover while preserving lifetime and audit histories. Apply one-semester inactive membership grace and the confirmed president succession/pause rules. |
| Scoring and ERI | Version effective coefficients for six experience categories and convert verified raw awards to credited wallet points per record. Maintain seventh Real-World Work Experience column separately. Compute six-category depth/evenness and draft `ERI = Depth × (0.5 + 0.5 × J) × W`, `W = 1 + 0.30 × min(S7 / T7, 1)`, with `T7 > 0`. Preserve policy and source provenance for each credit. |

## Domain invariants

1. Raw activity-budget consumption equals effective **raw** awards; the conversion into credited wallet points does not affect budget consumption. FinanceService money amounts are a separate domain.
2. Check-in retries, approval retries and bonus allocation retries must not duplicate attendance, award, budget debit or wallet credit. Corrections append linked reversal or adjustment records rather than erasing history.
3. An award from an activity inherits its approved activity category allocation. A separate category selector for Club BOD per award would violate the agreed flow; exact split arithmetic remains O-02.
4. Self-declarations never debit an activity budget or club bonus. Only final SA approval credits the student wallet.
5. A missing student remains an inactive current member for one semester, then leaves current membership after a second consecutive inactive semester. Historic memberships and offices remain queryable.
6. An inactive president gives an active vice president acting authority for one semester, then automatic promotion if still inactive at the following rollover, with an SA notification record. If no active vice president exists, pause the club, mark planned activities **Not Held** for registered students and refund previously allocated bonus exactly once. SA can nominate any **active** student, including a nonmember; club reactivation waits for nominee acceptance.
7. A scoring-policy change does not silently recalculate previously credited records. Period totals reset at rollover; lifetime totals and source records persist.

## Contracts still requiring product decisions

The capstone decision register tracks O-01–O-14: raw-to-wallet arithmetic and rounding, activity category allocation, budget exhaustion, roster timing/import format, vice-president exceptions, application states, bonus expiry and reversals, Not Held notices, nomination timing, self-declaration evidence/appeal, real-world threshold/zero convention, optional features, external data/privacy and measurable quality targets. Do not infer these rules from current code or old demo behavior.

Quests, reward redemption, club-type scoring rubrics, personalized recommendations, portfolio export/sharing, timetable/OJT links and automatic external experience imports are earlier proposals, not confirmed core acceptance criteria for this SOW.
