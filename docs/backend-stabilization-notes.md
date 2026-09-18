# Backend stabilization notes

This note records intentionally limited compatibility decisions from the backend
stabilization pass. It does not define new product behavior.

## ClubService migration compatibility

`AddClubDiscoveryFieldsAndIndexes` makes the current EF model authoritative for
`Clubs.ScheduleLabel`, `Clubs.IsRecruiting`, and the current query indexes. Its
`Up` migration uses additive, conditional SQL because older deployments may
already contain the two columns and two indexes created by the former startup
schema upgrader. Existing rows and user-edited club data are preserved. The
startup upgrader was removed after the migration took ownership of these objects.
Its `Down` path is intentionally non-destructive because the migration cannot
distinguish objects created by an older upgrader from objects it created itself.

## Remaining database initialization debt

- AuthService, ClubService, ReportService, ExportService, and AdminService use EF
  migrations at startup.
- FinanceService and NotificationService still use `EnsureCreated`; FinanceService
  also has a raw compatibility upgrader. Moving these services to migrations needs
  a separately planned data migration and is not safe to fold into this repair.
- ReportService still has a raw compatibility upgrader for schema not yet fully
  represented by its migration history.

## Transactional outbox coverage

ReportService and FinanceService register the shared transactional outbox and use
`AddOutboxMessage` for their primary report and budget workflow events. Remaining
direct publication paths include ClubService club-created events, ActivityService
activity events, ExportService completion/failure events, and scheduled
ReportService reminder events. These gaps should be assessed individually before
changing delivery semantics; this stabilization pass does not rewrite them.

## Authorization decision still open

Current authorization behavior is preserved: `ADMIN`, `SYSTEM_ADMIN`, and
`STUDENT_AFFAIRS_ADMIN` remain distinct roles. AdminService's policy that actually
requires `ADMIN` is named `AdminOnly`; it was not broadened to `SYSTEM_ADMIN`.
Consolidating or redefining those roles requires an approved product/RBAC decision.
