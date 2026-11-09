# C02/C04 — legacy registration capability follow-up, 2026-11-03

## Production evidence
- Reported route: https://fptux-clubhub-ui.pages.dev/v2/activities/4.
- Read-only SQL confirmed ID 4, Software Engineering Weekly Lab, IsInternal=true, PublicationStatus=LEGACY, Status=Scheduled, registration bounds NULL, start 2026-09-15 and end 2026-12-15.
- Existing RegisterParticipant writer permits authorized LEGACY scheduled registration. ReadAsync instead required PUBLISHED for canRegister.
- Fix makes the read capability match that existing writer. Internal membership/approval/join-date eligibility and public campus scope remain authoritative. No endpoint, persistence mechanism or XP award invented. Legacy remains excluded from recommendation candidates by the existing candidate query.

## Source and validation
- Source commit: 0d3d9d3957160b68bbc6dc1be184f885153ef71e.
- Graph/candidate validation: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41661205130
- Fresh GitNexus query/context and upstream ReadAsync impact before source edit: three direct callers (DetailAsync, FeedAsync, endpoint mapping), LOW risk.
- Index reports process-discovery budget limits and four unresolved receiver call sites for ReadAsync. This is not a claim of complete graph coverage. Text review of reader/writer complemented the graph.
- Partial test class impact returned UNKNOWN; this change adds a new test method in a new partial file and does not alter existing test methods. Existing fixtures/class declarations were inspected as text; no claim of unused symbols.
- detect-changes --scope all before source commit: 2 changed files, ReadAsync and enclosing class, low risk; no partial/truncated flag on the detect-changes result. Index-level limits above still apply.
- Candidate student tests: 29 pass, four SQL-only tests skipped in the candidate job. These SQL cases were subsequently included in full CI.
- Full CI: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41661730469
- 1012 tests passed (130 + 85 + 797), zero skipped/failed. Formatting, dependency scan, Compose validation, Admin image build and all eight EF model/migration consistency checks passed.
- Five new regression cases: public/internal scheduled legacy, already registered, completed, cancelled.

## Deployment and online checks
- Deployment: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41661928499
- ActivityService image: sha256:87968c9329d5b89e101c1ddcff329b971bd45a54f6b802c6aba87c123eba1e47
- Targeted rollout only ActivityService using existing runtime configuration and known-source rollback image. No migration or account mutation.
- Production health /health, /api/health and /api/auth/health returned 200.
- Anonymous feed and session endpoints returned 401; recommendation enabled setting remained true.
- UI evidence: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-ui/actions/runs/41661114349 and /actions/runs/41661629941; 310 tests, actual public/member reader routes on desktop/mobile, plus eight exact deployed /v2/activities/4 browser scenarios with every API call intercepted.

## Limits
Execution environment unavailable; checks ran on GitHub Actions, not the user's local machine. No real student session was available to verify that account's actual canRegister or create a production registration. Online browser UI checks used isolated API fixtures and never sent the synthetic token to the API.

Existing deployment workflow restored to manual-only after verification; no new workflow files.
