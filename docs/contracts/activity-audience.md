# Activity audience contract — 2026-11-03

`ClubActivity.IsInternal` is the authority for activity audience:
- true: approved members of the exact club, with membership established by the existing activity/session cutoff.
- false: active, unlocked users in the club's campus; club membership is not required.

This classification applies to student discovery eligibility, recommendation candidates, self-registration, registered session sources, check-in and experience source eligibility. It does not waive publication, registration/attendance windows, capacity, duplicate-registration, registration-before-attendance or verified-experience requirements.

The stored activity `Eligibility` column and legacy request members remain for schema/client compatibility. Runtime authorization no longer branches on them. Activity responses derive `eligibility` as CLUB_MEMBER/CAMPUS_PUBLIC from IsInternal, so stale persisted values cannot contradict the response. Old clients must change IsInternal (or the supported visibility adapter), rather than expecting activity Eligibility alone to change audience. No migration, backfill or production data rewrite is introduced.

Recruitment `ActivityRecruitment.Eligibility` (SCHOOL_STUDENTS/CLUB_MEMBERS) is a separate policy for positions/assignments and remains in use. This change does not implement recruitment approval or grant roles.

Regression coverage includes public activities with stale CLUB_MEMBER eligibility; internal activities with stale CAMPUS_PUBLIC eligibility; nonmember registration persistence; cross-campus denial; approved membership and existing session-cutoff scenarios.

Validation must be recorded in docs/issue-validation/2026-11-03-activity-audience.md. Build/CI success is separate from production deployment and authenticated online verification.
