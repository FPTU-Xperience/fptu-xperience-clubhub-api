# Activity time and lifecycle synchronization

The stored workflow state (`Scheduled`, `Completed`, `Cancelled`) previously changed only on explicit management commands. There was no timer consumer to reconcile elapsed activities or sessions. Legacy activity responses returned the stored state while student discovery derived UPCOMING/LIVE from the next scheduled session and excluded expired schedules. This allowed workspace cards to remain "Sắp diễn ra" indefinitely.

## Time phase and workflow state

GET `/api/activities` and GET `/api/activities/{id}` now include `timeline: { status, startTimeUtc, endTimeUtc, asOf }`. It uses the earliest scheduled session whose end is after the server clock, falling back to parent dates only when there are no sessions. Terminal workflow states take precedence. Consumers use this phase for display, preserving `status` for workflow decisions.

`ActivityLifecycleWorker` runs on service startup and every 30 seconds. A bounded pass reconciles up to 100 activities. Published activities, closed registration, legacy activities and published application executions are eligible. Unpublished drafts remain drafts.

- Close a SCHEDULED session only after both its end and attendance close time have elapsed.
- Complete a Scheduled activity only after its parent end and every non-cancelled session's end/attendance window have elapsed, with no open session left.
- Keep future sessions, including those beyond a stale parent date; do not close an activity early.
- Repeated passes produce no duplicate transitions, audits or events. Restart catches up elapsed records.
- Each parent is rechecked in a serializable transaction on SQL Server; workflow changes, audit and outbox are committed together. Concurrency failures are retried from current data on the next worker pass.

Each automatic transition records AUTO_CLOSE/AUTO_COMPLETE with actor 0 (SYSTEM), time, prior/new states, version and reason. It adds `ActivityLifecycleChangedEvent` under `activity.lifecycle.changed` to the existing transactional outbox. Consumers can subscribe using the existing event bus; no new downstream consumer or browser push subscription is introduced by this change. Normal API reloads show the current timeline.

Temporal completion does not attest participation, approve a closing report, settle action XP or post ledger credits. Existing attendance/XP events retain those responsibilities.

## Postponement and visibility boundaries

There is currently no POSTPONED activity/session state or management endpoint. Elapsed time cannot identify a postponement. A postponement workflow needs an explicit management decision, reason, new schedule (or an explicitly unknown date), audit and notification policy. It is not inferred by this worker.

Publication remains independent. The accompanying audience fix uses `isInternal` and the actual execution publication flag; legacy reads retain their historical visibility within the authorized audience. See `activity-visibility.md`. The lifecycle worker does not publish or change the audience of records.

## Seed evidence

`DemoCatalog` defines `tech-weekly-lab` from reference date +4 to +95 days with weekly meeting days. With the default reference date 2026-09-11, it spans 2026-09-15 through 2026-12-15; the start date alone does not make it expired on 2026-10-29. `tech-cloud-seminar` is a single-day schedule at reference date +22 (2026-10-03). Both are seeded Scheduled. These are source fixtures; actual deployed end dates/publication must be checked independently before changing production data.
