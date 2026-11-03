# C02 activity audience validation

- Baseline: cuongnk/implement-api-contract at 0a638e80d6ca789309e79afd09d92c8c7eb9f932; differs from last successful deployed revision 16dc05ef8da48f00ff2c2dad5280efaf22b93866 only by auth contract documentation.
- IsInternal is authoritative for activity read eligibility/candidates, registration, session sources, check-in and shared experience eligibility. Retained existing account, campus, membership cutoff, publication, timing, capacity and persistence checks.
- Activity Eligibility response is derived; stored column/request compatibility and independent recruitment eligibility remain.
- GitNexus 1.6.12 query/context/impact ran before source edits. ResolveAsync and ToResponse risk HIGH; unresolved delegate callers were confirmed through route registration/text references. Session fixture impact UNKNOWN was reviewed against its direct test calls. detect-changes --scope all ran on staged source candidate before this commit: 8 files, 15 symbols, 17 affected processes, critical aggregate risk. No partial/truncated result flags reported.
- Candidate validation: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41605159159 — successful; 784/784 stabilization tests with SQL Server. Earlier runs exposed missing test setup and obsolete fixture audience declarations; both corrected, not bypassed.
- Local executor unavailable. Tests ran on GitHub Actions.
- Deployment and authenticated online checks must be recorded separately; do not treat this source commit as live verification.


## Release validation and production resource blocker
- Release commit: 0d38e9ed1af75455ce4bf26bdff75bef047756a0.
- Full solution validation passed: 130 ClubReportHub.Tests + 85 AdminService.IntegrationTests + 784 Backend.StabilizationTests = 999 passed, zero failures/skips. Formatting, vulnerability scan, Compose validation and all eight EF migration-model checks passed.
- Backend CI (including production Admin image build) succeeded: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41605909431
- Production deployment remains in progress at the time of this checkpoint: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41605909513 . Do not describe the API source fix as live yet.
- Read-only production diagnostics at 09:08 UTC: root filesystem 48 GB, 100% used, zero available; swap 5955/5959 MB used. Original API containers still had their previous uptime; existing Recommendation__Enabled=true confirmed.
- Cache-only recovery: pruned unused old build cache, then remaining unused build cache. No image/volume/application-data/secret cleanup was performed. Latest disk check 09:15 UTC: root 98%, 1.4 GB available; swap still 5959/5959 MB. Diagnostic Docker disk-usage query timed out under pressure; this is separate from the deployment result.
- Recovery evidence: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41609027199 and https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41609216363 .
- Public online verification waits for the exact release to deploy successfully: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41607378579 . It checks actual public health and anonymous authorization boundaries, not authenticated production registration.
- Cloud blocker notification task creation was confirmed; delivery to a phone remains unknown. No SMS/push delivery claim.
- Temporary automatic deployment trigger restored to the original manual workflow. This does not cancel the already-running release.


## Final deployment — completed 2026-11-03 09:46 UTC
- Production ActivityService source: 0d38e9ed1af75455ce4bf26bdff75bef047756a0.
- Actual running image verified: sha256:245fc67564528c4b57b4d2e277c1555a778409a8adcb886ba293d55a88c3ba64.
- Successful existing deployment workflow: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41613702644 . No extra workflow file was needed for the final deployment.
- Built release and rollback images on CI from exact revisions 0d38e9ed1af75455ce4bf26bdff75bef047756a0 and pre-change 0a638e80d6ca789309e79afd09d92c8c7eb9f932. The prior running image metadata could not be tagged, so rollback uses the explicitly built pre-change source image.
- Only activity-service was recreated, using the current Compose project/configuration. Required configuration variables were preserved from actual running container settings without logging their values or creating/rotating secrets. Other services were not restarted by this scoped rollout. Recommendation__Enabled=true confirmed afterward.
- Online verification passed against https://clubreporthub.duckdns.org: /health, /api/health, /api/auth/health all 200; anonymous /api/v2/activities/feed and /api/v2/activities/1/sessions both 401.
- This is health/authorization-boundary verification. Authenticated production student registration was not exercised; registration persistence and eligibility were tested in the validated 999-test source suite.
- All temporary agent-created workflow files and the source-staging JSON were removed from both fix/activity-internal-registration-20261103 and fix/activity-audience-production-20261103. Verified only backend-ci.yml, backend-validate.yml, deployment.yml remain. Historical Actions runs remain as validation evidence.
- Original deployment.yml restored to manual-only after successful rollout. No extra automatic deployment trigger remains.
