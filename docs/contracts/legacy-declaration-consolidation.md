# Consolidated declaration intake

All new submissions on `/api/v1/declarations` use the canonical contribution
workflow: configured semester, occurred date, explicit attributes and 1–10 owned
private evidence files. The obsolete URL-only intake no longer creates an
unversioned `SelfDeclaration`. Canonical decisions use the existing current-version
review and immutable XP ledger. Legacy PUT/review paths cannot create new decisions
or update historical raw-point previews.

Historical records remain readable under their existing ID and campus/ownership
boundaries. Their response explicitly marks `isAuthoritative=false` and
`sourceNamespace=LEGACY_DECLARATION`. Historical approvals/rejections are read-only;
they are not retrospectively rescored or assigned an inferred semester.

A pending direct external record can be upgraded by its author with
`POST /api/v1/declarations/{id}/upgrade`. The input is
`{expectedLegacyVersion, submission}`; the submission has the same fields as a new
canonical declaration. The response supplies a fingerprint on the historical GET.
The endpoint rechecks live account/campus authority, private evidence ownership,
semester/date eligibility and the unchanged historical fingerprint inside the
transaction. It creates one canonical pending contribution under the same ID,
retains the original creation timestamp and embeds the prior record in its source
snapshot. No XP is awarded by conversion. Retry uses the original Idempotency-Key.

Pending historical club-session records require the real activity contribution
workflow; they cannot be relabelled as an external effort. All existing history
and contribution effects remain intact. ClubHub supplies the author upgrade form;
Admin presents old records as awaiting author completion or read-only history.

Automated evidence covers ownership, staff-campus boundaries, stale fingerprints,
foreign evidence, retired intake/review denial, same-ID replay and unchanged
historical radar. Browser fixtures include upload reuse and key reuse after a
failed upgrade request. Production-account acceptance remains a separate step.
