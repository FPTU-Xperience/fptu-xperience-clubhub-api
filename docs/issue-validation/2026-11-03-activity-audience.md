# C02 activity audience validation

- Baseline: cuongnk/implement-api-contract at 0a638e80d6ca789309e79afd09d92c8c7eb9f932; differs from last successful deployed revision 16dc05ef8da48f00ff2c2dad5280efaf22b93866 only by auth contract documentation.
- IsInternal is authoritative for activity read eligibility/candidates, registration, session sources, check-in and shared experience eligibility. Retained existing account, campus, membership cutoff, publication, timing, capacity and persistence checks.
- Activity Eligibility response is derived; stored column/request compatibility and independent recruitment eligibility remain.
- GitNexus 1.6.12 query/context/impact ran before source edits. ResolveAsync and ToResponse risk HIGH; unresolved delegate callers were confirmed through route registration/text references. Session fixture impact UNKNOWN was reviewed against its direct test calls. detect-changes --scope all ran on staged source candidate before this commit: 8 files, 15 symbols, 17 affected processes, critical aggregate risk. No partial/truncated result flags reported.
- Candidate validation: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41605159159 — successful; 784/784 stabilization tests with SQL Server. Earlier runs exposed missing test setup and obsolete fixture audience declarations; both corrected, not bypassed.
- Local executor unavailable. Tests ran on GitHub Actions.
- Deployment and authenticated online checks must be recorded separately; do not treat this source commit as live verification.
