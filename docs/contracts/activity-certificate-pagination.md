# Activity certificate pagination

`GET /api/activities/certificates/me` and
`GET /api/activities/certificates/club/{clubId}` accept `page` (default `1`,
valid range `1..10000`) and return `{ items, hasMore }`.

Each page contains all certificates issued to the authorized recipient for one
activity. Activities are ordered by descending activity ID; certificates within
an activity are ordered by certificate ID. An out-of-range page returns an empty
`items` array and `hasMore: false`. An invalid page returns HTTP 400 with
`INVALID_CERTIFICATE_PAGE`.

Recipient filtering happens before pagination. Personal pages contain only the
authenticated student's certificates. Club pages retain the existing club access
check and contain only certificates issued to that club.

Previously, pages contained twelve individual certificates, which could split
one activity across pages. Clients should render the returned activity group and
use `hasMore` to enable the next page, without assuming twelve items per page.
The response fields and authorization contract are unchanged.

The one-activity limit bounds the inline badge/frame payload while keeping the
activity complete. New activities allow at most thirty certificate definitions,
with each PNG limited to 90 KiB. No existing certificates are rewritten.

Regression coverage: `ActivityActionContractTests.CertificatePagesKeepEveryDefinitionForAnActivityTogetherAndScopeBeforePaging`
tests both endpoints with thirteen certificates in one activity, a second
activity, an unrelated recipient, the last/empty pages, and invalid input.
