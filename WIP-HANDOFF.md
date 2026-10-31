# WIP handoff — 2026-10-28

This checkpoint is intentionally WIP for continuing on another computer. Do not assume production is running this checkout.

## Repositories and branches

- API: `FPTU-Xperience/fptu-xperience-clubhub-api`, branch `cuongnk/implement-api-contract`.
- ClubHub UI: `FPTU-Xperience/fptu-xperience-clubhub-ui`, branch `cuongnk21/integrate-api-email-login`.
- Recommendation (rcm): `FPTU-Xperience/recommendation-engine`, branch `main` (the only existing branch).

All modified/untracked source files in these three worktrees are included in WIP commits on these branches. No new PR or branch is required. On the other computer, fetch and check out each branch. The current machine's local Git HEADs may precede the remote commits because publishing used the GitHub Git API; source files were preserved locally.

## Implemented

- Server-backed onboarding, timetable version handling, verified profile/club/activity images and modal layout fixes from the preceding checkpoint.
- Python rcm private feed/read/invalidation APIs, asynchronous durable Mongo queue, generation/lease fencing, expiry checks, result indexes and retention.
- .NET source export, periodic reconciliation and transactional source-change outbox events across Auth/Club/Activity/Admin; Redis invalidation consumer wakes refresh.
- Public .NET activities/clubs recommendation endpoints read precomputed rcm results and recheck live access/availability. Rcm/Mongo remain private, separate Compose stack on the API host/network.
- Combined API + pinned rcm deployment, independent rcm workflow, immutable release directories, health checks, rollback and synthetic feed-to-result smoke.

## Last verified revision

API `675e8a3b8483ac4f6f962699964d7a1140debbf8` and rcm `c9e79e3ee5c59d63625299114f2e76c056b59697` passed combined deployment validation:

https://github.com/FPTU-Xperience/fptu-xperience-clubhub-api/actions/runs/40551731488

975 backend tests passed (130 + 85 + 760), EF model/migration checks passed, quality checks passed; rcm's 55 tests (including real Mongo) and image build passed. Earlier red runs predate the fixture/outbox and Mongo timestamp fixes; do not rerun those old revisions.

## Deployment status and immediate next steps

That run failed at **Provision and deploy recommendation stack** with `invalid mount path: 'noexec' mount path must be absolute`. YAML parsed the unquoted tmpfs option list as three mounts. The rcm WIP checkpoint quotes the entire `/tmp:size=16m,noexec,nosuid` entry. This fix has not been redeployed.

The Mongo container and stable `fptux-recommendation-mongo` volume were provisioned. The Python container did not start. API source copying/rollout was skipped, so production .NET remains on its previous release. Preserve the generated host `mongo.password` and the Mongo volume; do not initialize a new password or run `down -v`.

This API WIP also wires AuthService to `redis:6379` and waits for Redis health. Auth now publishes an outbox and must not use the localhost Redis default in a container. This Compose-only fix is not yet validated by a deployment.

1. Check the WIP commits' CI results and validate both Compose files.
2. Read rcm `docs/service-deployment.md` and review the two Compose corrections.
3. Run API **Deploy to Ubuntu VPC** from `cuongnk/implement-api-contract`, `deploy_rcm=true`, with `rcm_ref` set to the new rcm WIP commit SHA. An Actions rerun of the failed job would still use the old broken rcm SHA; launch a new workflow instead.
4. Confirm rcm synthetic smoke, API service health and actual exporter/result freshness. No successful full production rollout has been claimed.

## Functional gaps to finish

- Interest/major taxonomy mappings and per-club pillar metadata need configuration; unmapped data remains unknown. Club recruitment uses the existing live ClubService flag. Clubs without valid pillar metadata are excluded.
- Activity sessions are conservatively treated as required because no optional/required flag exists yet. Legacy activities without published sessions are excluded. Activities whose core-six pillar weights do not sum to one are excluded; review normalization if mixed plus-one allocations must participate.
- Schedule knownness requires both timetable blocks with matching calendar versions; otherwise schedule is explicitly unknown.
- Current exporter scans the active semester's active accounts serially. Measure throughput and refine fan-out before larger workloads.
- V1 reranks with deterministic rules. Wallet feature export, impression/click/dismiss ingestion and ML training are not implemented.
- Configure/test encrypted off-host Mongo backups and monitoring before treating the new deployment as complete.

GitNexus runner was absent; manual impact analysis continued under the user's earlier approved exception.
