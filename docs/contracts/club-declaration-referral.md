# Club referral for external declarations

Students submit the existing canonical declaration payload to
`POST /api/v1/declarations`, optionally adding `referringClubId`.
This is an external/real-world declaration, not a club activity/session
contribution. `clubId` in the intake payload remains null. The author must be
an active, unlocked current member of the referring club; the stored campus
comes from the authenticated student, not a requested campus.

| Action | Route | State after success |
| --- | --- | --- |
| Check current club authority | GET `/api/v1/declarations/clubs/{clubId}/access` | `canProcess`, `canManagePermissions` |
| Read club queue | GET `/api/v1/declarations/clubs/{clubId}` | Server-scoped page, total and totalPages |
| Club endorsement / changes | POST `.../clubs/{clubId}/{id}/club-review` | `APPROVED` / `CHANGES_REQUESTED` at the club |
| Forward endorsed revision | POST `.../clubs/{clubId}/{id}/forward` | `ctsvSubmissionStatus=SUBMITTED` |
| Campus final decision | Existing POST `/api/v1/student-affairs/declarations/{id}/review` | Approved, rejected or revision requested |
| Author revision | Existing PATCH `/api/contributions/{id}` | Resets club review to PENDING and forwarding to NOT_SUBMITTED |

Club actions require `expectedVersion`, a 5–1000 character reason and an
Idempotency-Key. The club-review decision is `ENDORSE` or `REQUEST_CHANGES`.
All actions update the contribution version, append a revision/audit and notify
the author. They do not issue XP. Endorsement alone does not enter the CTSV queue.

The owner, current approved vice president, current Secretary group members and
members explicitly delegated `club.declarations.process` through governance can
process referrals. Club authority is read live at mutation time; removing the
assignment, expiring the term or locking an account removes access to both
records and private evidence. The declarations tab links to the existing real
governance editor; it has no separate local role-permission policy.

An author cannot endorse or forward their own declaration. A final reviewer
must be CTSV in the student's campus or System Admin, and cannot be the author,
club endorser or forwarder. Generic verification grants cannot bypass this
boundary, including detail/evidence reads and correction routes. Final approval
uses the existing approved policy and immutable XP ledger. Replay cannot award
twice. Reject/return decisions do not create XP effects.

The club list filters by club, status, semester/unassigned and title/message/
student search before stable pagination. The CTSV queue supports `viaClub=true`
or `false`; forwarded referrals are filtered by campus and reviewer independence
before paging. Direct declarations and club activity contributions retain their
existing workflows.

Migration `ClubDeclarationReferral` adds five nullable columns to Contributions.
Existing records are not converted or rescored. Additive columns allow the
previous application image to remain a rollback option.

Validation: `ClubDeclarationReferralTests`, existing campus declaration tests,
and `Governance_DeclarationAuthorityUsesCurrentSecretaryOrExplicitDelegationAndRevokesImmediately`,
plus both UI regression/build and browser suites. Isolated browser fixtures do
not establish real-account production acceptance.
