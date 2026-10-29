# Activity audience and publication

`ClubActivity.IsInternal` is the canonical audience flag. It is independent of lifecycle (`Scheduled/Completed/Cancelled`) and publication (`IsPublished` for application executions, publication status for older records).

The authenticated discovery feed, student detail and recommendation candidates share this audience predicate:

- `isInternal:false`: the activity's club belongs to the student's current canonical campus.
- `isInternal:true`: the student currently has an Approved membership in the club. A membership in another campus still qualifies.
- Management permission by itself does not count as student membership. Management reads retain their existing authorization.

ClubService remains the membership/campus authority. ActivityService reads the current AuthService account and ClubService scope, rather than trusting UI filters or a student-supplied campus parameter. Only active clubs supplied by that authority can be selected.

Application executions must be published. Standalone published/closed records are readable; legacy records keep their historical implicit publication but now follow the same audience predicate. Unpublished executions, drafts, cancelled/completed/expired activities do not enter the current feed. Recommendation candidates additionally require future registration opportunities, eligibility, an open registration window and capacity. An approved radar policy enables XP personalization; without it, recommendations can use the explicit basic eligible-upcoming mode.

## Writes

- Activity create/update DTOs accept `isInternal`. Older clients may still send `discoveryVisibility`; explicit `isInternal` wins. Writes keep the alias synchronized as CLUB_ONLY/CAMPUS_PUBLIC.
- Activity application input defaults to `isInternal:true`; approval copies it to the resulting activity. Responses return the flag.
- `PATCH /api/activities/{id}/visibility` accepts `{isInternal, expectedVersion, reason}` and an Idempotency-Key. Current club management/review permission is required. The flag is explicit/required, versions prevent stale updates, and the before/after decision is audited. This operation does not publish the activity.

Migration `20261029035522_ActivityInternalVisibility` adds a non-null bit defaulting to true, then copies existing CAMPUS_PUBLIC decisions to false. It does not publish records or rewrite membership/policy data.

## Python recommendation snapshots

The optional activity input `isInternal` is supported by rule SIMPLE_CONTENT_V2. Public activities require matching campus; internal activities require current membership and may cross campus. Old 1.0 snapshots without the flag retain their original public-catalog semantics. The enabled exporter sends it explicitly with the owning club's campus. Python/MongoDB processed delivery performs live candidate rechecks; missing-policy basic mode is supported by both enabled and baseline endpoint configurations.

## Verification and limits

ActivityService and ClubHub UI builds pass. An HTTP fixture invoking the implemented feed/detail/candidate endpoints verifies the exact audience matrix, hidden drafts/unpublished executions, expired records and roster-free DTOs. The Python suite passes 46 tests; the focused UI suite passes 28 tests. EF reports no pending model changes. These are source/fixture checks; the new migration and backend have not been deployed or verified against production data.
