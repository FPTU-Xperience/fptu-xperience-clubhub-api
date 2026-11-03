# Public registration window correction — 2026-11-03

- Production read-only evidence: public activity 1055 (FU-IGO) and seven other scheduled published public activities have RegistrationOpensAt/RegistrationClosesAt NULL. The participant write endpoint treats an absent bound as unrestricted, but student reads required both comparisons to be true and hid registration.
- Student ReadAsync now treats each null bound as unrestricted, matching RegisterParticipant. Explicit opening/closing times, capacity, existing registration, publication, exact membership/campus eligibility and Scheduled status remain enforced. No production activity data was changed.
- Pre-edit GitNexus query/context/impact ran: ReadAsync has 3 direct callers (feed, detail, internal candidate dispatch), LOW risk. Index reported global process discovery limits; direct endpoint references and write-window guards were checked explicitly. Staged detect-changes completed: 2 files/2 symbols, LOW risk, no partial/truncated change-result flag.
- Before source commit, 56 focused read/write/eligibility tests passed, including 8 new null/future/closed/full/cancelled window cases. Evidence: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41618010907 .
- Full source validation and deployment are separate gates to record after completion.
- Cleanup now covers GitHub Actions registry/history, not only YAML: all 6 temporary workflow types disabled and temporary runs removed, while 3 original workflows were retained. Evidence: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41617408150 . Individual removed online run IDs return 404.


## Final validation and deployment
- Full source CI https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41618878807 passed: 130 + 85 + 792 = 1007 tests, zero failures/skips; quality, production Admin image build and eight migration/model checks passed.
- Source be3ea55d9d95d2efead675fdcebbc2c469732cc7 deployed successfully using existing deployment.yml: https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/41619735743 .
- Actual running ActivityService image: sha256:d7f4779660e015848465798928b83490ceaf8820c67f87c514750ba690ef199f. Only ActivityService was rolled out, preserving actual runtime settings. Pre-change rollback image built from source 0d38e9ed1af75455ce4bf26bdff75bef047756a0. Recommendation__Enabled=true confirmed.
- Online /health, /api/health, /api/auth/health returned 200; anonymous protected student feed/session endpoints returned 401. Production registration with a real student account has not been executed.
- Actions cleanup additionally verified against the actual public GitHub Actions HTML: 3 original workflow names present; all 6 temporary workflow names absent. ACTIONS_NAVIGATION_OK from verify-actions-sidebar job in the successful deployment run above.
- Original API manual deployment workflow restored after success. No temporary workflow files or automatic deploy trigger remain.
