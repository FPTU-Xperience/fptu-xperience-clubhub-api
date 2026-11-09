# Recommendations runtime diagnosis (2026-10-29)

## Implemented request path

The ClubHub discovery page calls GET `/api/v2/recommendations/activities?limit=6&scope=PERIOD`. Gateway routes this to AdminService. When Recommendation:Enabled is false, the baseline implements LOW_AXIS_UNROUNDED_START_ID_V1 in C#. The remote changes integrated before release add an enabled Python/MongoDB pipeline with snapshot export, invalidation and live eligibility rechecks. Missing policy uses explicit basic mode in either configuration; approved-policy processed delivery retains its existing service-failure behavior. The engine now uses SIMPLE_CONTENT_V2 for internal/public audience filtering.

## Reasons for missing results

1. The handler first reads the student's radar. PERIOD without semesterId requires exactly one active semester; otherwise HTTP 400 `SEMESTER_REQUIRED`. This happens before requesting candidates.
2. That scope/environment needs an effective approved experience policy for XP personalization. Previously a missing policy produced HTTP 503 `SCORE_POLICY_UNAPPROVED`. The accompanying fix returns basic eligible-upcoming suggestions with `mode:BASIC`, `personalizationAvailable:false`, `personalizationUnavailableReason:SCORE_POLICY_UNAPPROVED`, rule `ELIGIBLE_UPCOMING_V1` and null radar snapshots. Incompatible approved policies still produce `SCORE_POLICY_UNSUPPORTED`.
3. AdminService calls ActivityService's internal candidate endpoint using the internal workflow token. Network/auth/configuration/response failures produce HTTP 503 `RECOMMENDATION_SOURCE_UNAVAILABLE`. A scope change during pagination produces `RECOMMENDATION_SOURCE_CHANGED`.
4. Successful candidate reads must still pass all activity conditions: published discovery record, `isInternal` audience (same-campus public or internal of a current member club), matching semester, future next session/activity, open registration window, remaining capacity, eligibility and not already registered. LEGACY/DRAFT records are not automatically registration candidates. See `activity-visibility.md`.
5. Ranking discards candidates with no positive weight on any core radar axis. An empty eligible/rankable set returns HTTP 200 with `available:true, items:[]`; this is distinct from service failure.

The UI previously rendered a generic connection error for most failed requests, including business/configuration errors. The accompanying UI change retains the API error code and shows a specific explanation for missing/unsupported policy, missing semester or unavailable/changing candidates. Do not activate a semester, approve a scoring policy, publish legacy activities or weaken eligibility merely to force a nonempty result.

## Verification

The separate Python suite passed 44/44 tests on 2026-10-29. This verifies source behavior, not production integration. The tests first hit Windows sandbox tempfile AccessDenied; running the same suite outside that boundary passed.

The user's authenticated Network response confirms HTTP 503 `SCORE_POLICY_UNAPPROVED` with message `This scope requires an approved policy for the configured environment.` The handler stops in radar policy lookup, before candidates/ranking. The exact missing/mismatched semester, scope, environment or policy effective time requires a read of the deployed policy configuration; this response alone does not identify which dimension is misconfigured.

## Basic mode fix

Recommendations are advisory: when the only missing prerequisite is an approved XP policy, the endpoint now requests the same authorized registration candidates and orders them by upcoming start time and ID. This mode does not require positive pillar weights, does not label a target pillar and does not invent a radar/policy snapshot. All existing public/internal, campus, membership, semester, registration and capacity filters remain upstream in ActivityService.

When a matching approved policy is available, the existing LOW_AXIS_UNROUNDED_START_ID_V1 path is retained with `mode:PERSONALIZED` and `personalizationAvailable:true`. Other read errors, invalid semesters, upstream failures and authority-version changes remain errors. Empty eligible catalogs return a valid empty list. The UI explicitly labels basic suggestions while personalization is unavailable.

Official radar/summary/scoring/ledger operations still require approved policy. This change does not create or approve policies and does not award XP. The policy-approval endpoint still requires the agreed P01-P06 parameters, environment and approval basis.

AdminService build and a focused HTTP regression harness cover basic mode, explicit metadata, stable time/ID ordering, canonical semester aliases, invalid semesters, empty candidates, unavailable sources, untouched policy/ledger data, retained personalized mode and rejection of unsupported approved policies. These are source/fixture checks; the backend change has not been deployed.
