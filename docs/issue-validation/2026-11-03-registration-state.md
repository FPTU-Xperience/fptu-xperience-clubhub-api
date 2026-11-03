# Persisted activity registration state — 2026-11-03

## Behavior
- StudentActivityItem gains additive bool IsRegistered. No constructor signature, database schema or registration persistence changes.
- Authenticated feed/detail populate it from ActivityParticipants filtered by the current user ID, using the existing registration query.
- UI renders "Đã đăng ký" after a successful registration and again on revisiting/reloading detail. It never infers registration from CanRegister=false.
- UI removes successful server-confirmation sentence and keeps both club links in their original identity header; registration alone occupies a separate row.

## Graph and tests
- Source: a25f6ef42e2fb6557fed5fd54157530ece6b58d5
- Graph investigation: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41671021988
- Candidate precommit validation: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41671439399
- Fresh query/context/impact for ReadAsync: LOW, three direct callers. StudentActivityItem name was ambiguous (record/constructor, UNKNOWN); resolved with exact Record UID and exact context/impact before candidate application: LOW.
- Text search confirmed constructors and references; only additive property added. Index reports process budget limits and four unresolved receiver sites around ReadAsync; not complete coverage.
- detect-changes --scope all before source commit: three changed files/symbols, low risk; no partial/truncated flags on this result.
- Two candidate regression cases passed, no skips: PUBLISHED and LEGACY. A different user's participation leaves isRegistered false; inserting current user's registration makes fresh detail/feed true and disables canRegister.
- Full CI: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41671908565
- 1014 tests passed (130+85+799), zero failed/skipped. Formatting/dependency/Compose checks, Admin image and eight migration consistency checks passed.

## Deployment
- https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41671954570
- Only ActivityService rolled out, with existing runtime settings and explicit known-source rollback image.
- Image: sha256:72b8e7463a638b7beeb415b956c274a5862f653e553e2985910463217c989dcc
- Production health, API health, auth health returned 200; anonymous feed/session endpoints returned 401.
- UI validation/deployment: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/actions/runs/41671227513
- UI: 312 tests and 12 built/online browser scenarios at 1440/1024/390, including submit then reload retaining "Đã đăng ký", no confirmation paragraph, original club navigation and schedule.
- Browser API requests are isolated fixtures. No real student production registration or authenticated production detail read performed.
- Execution used GitHub Actions; local environment unavailable.
- Existing deployment workflow restored manual-only; no new workflow files.
