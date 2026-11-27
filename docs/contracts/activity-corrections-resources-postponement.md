# Activity corrections, resources and postponement

Implementation branch: `fix/remaining-debt-20261127`. Authenticated production
acceptance is deferred until the combined debt/gap implementation is ready.

## Closing reports

`POST /api/activities/{id}/closing-report/return` requires current independent
campus review authority, `expectedVersion`, a 5–2000 character `note` and an
idempotency key. Only SUBMITTED reports can become RETURNED. This creates no XP
or certificates. The authoring club may POST the corrected report to the original
submission route with its returned `expectedVersion`. Approval locks the report.
All transitions append CLOSING_REPORT_* session audits with the versioned content.
GET includes the latest 50 events. The first submission time remains the deadline
eligibility time, so a staff-requested correction does not make a timely report
late; final-role edit deadlines remain unchanged.

## File resources

`POST /api/activities/{id}/resources/upload` accepts authenticated multipart fields
`file`, `kind`, `title`, `visibility` and an idempotency key. It validates the
extension, MIME type, signature, name and size. Office ZIP archives are bounded
and exclude VBA/embedded objects; PDFs exclude recognized active-content markers.
These checks are content validation, not a claim of antivirus certification.

Limits: documents 20 MB, images 5 MB, videos 100 MB; 100 attached resources and
500 MB per activity. File contents live in a separate SQL table, outside metadata
and audit projections, so private delivery has no publicly accessible storage URL
or additional volume provisioning dependency. SQL storage/backup capacity must
include this quota. Download uses the authenticated API, live activity/membership/
campus/visibility checks, no-store/nosniff and attachment delivery. Detachment
removes bytes while retaining metadata and immutable audit history. Upload retry
uses the same receipt; no unattached staging files remain. Old provisional
upload-intents return 410 directing clients to atomic multipart upload.

## Profile decorations

`ActivityCertificateDisplay.UserId` is an externally assigned account key, not an
IDENTITY. The migration transaction rebuilds the standalone table and copies all
IDs, versions, assets and positions; it does not remap or rescore records. The
existing expected-version and owned-certificate checks remain. ClubHub prevents
same-tick duplicate writes and reuses an idempotency key for unchanged retries.

## Leadership

Current Chair, approved Vice Chair and current Secretary have operational
leadership authority; Secretary is projected explicitly with term expiry and is
not automatically assigned treasurer duty. Chair/Vice/Secretary and Event members
qualify as HOST candidates. Ordinary eligible members retain STAFF. Owner-only
succession/nomination boundaries remain explicit. Current account, membership,
campus, revocation and term checks still apply.

## Postponement

GET/POST `/api/activities/{id}/postponement` and POST `/resume` use current
organization/campus authority, versions, reasons and idempotency. A scheduled
activity may be postponed before it starts, with no fabricated replacement date.
Attendance, completions or closing reports block moving recorded history. Campus
review confirms future replacement dates in the same active semester. Sessions
shift together, receive new versions and retain their approved scoring policy.
Registration windows remain explicit; resuming does not silently reopen them.

Postponement clears scheduled publication and cancels room/equipment bookings;
new bookings are required. Registered students receive outbox notifications and
can cancel while postponed, including after the old planned start. New
registrations/check-in/manual attendance are closed while postponed. Lifecycle
workers only complete Scheduled activities. History records both schedules and
the reason. There is no XP/certificate effect from postponement or resumption.
