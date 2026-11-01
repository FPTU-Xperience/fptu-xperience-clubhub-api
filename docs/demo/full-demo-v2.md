# FullDemo v2 operator runbook

This is a synthetic showcase dataset for the existing ClubHub deployment. It adds 120 clearly labelled demo actors, enriches 12 empty catalog clubs, and keeps the existing student identity and all other accounts intact. It does not invoke the legacy destructive seeder.

## Run

Build `src/Tools/FullDemoDataSeeder/FullDemoDataSeeder.csproj` with .NET 8. When publishing the console tool, use `dotnet publish -c Release -p:ErrorOnDuplicatePublishOutputFiles=false` to accommodate the referenced services' shared appsettings filenames. Supply the eight `ConnectionStrings__Auth`, `Club`, `Activity`, `Admin`, `Report`, `Finance`, `Notification`, and `Export` variables through a private environment file. Do not commit credentials or the journal.

Required operator settings:

| Setting | Meaning |
| --- | --- |
| `FullDemo__ReferenceDate` | Pinned `yyyy-MM-dd`; defaults to the current date in UTC+7. Reuse the pinned date on subsequent runs. |
| `FullDemo__JournalPath` | Private absolute JSON journal path; retain it with the deployment. |
| `FullDemo__ApiBaseUrl` | Gateway address reachable from the tool. |
| `FullDemo__AssetDirectory` | Absolute path to `assets/full-demo-v2`. |
| `FullDemo__AssetBaseUrl` | Public URL serving that asset directory. |
| `FullDemo__Password` | 12–128 characters, used only to create the new synthetic accounts. Keep the original value for resuming. |

```sh
dotnet FullDemoDataSeeder.dll plan
dotnet FullDemoDataSeeder.dll seed
dotnet FullDemoDataSeeder.dll verify
```

`plan` is the default and uses SQL reads only. It never calls migrations, schema upgraders, HTTP endpoints or SaveChanges, and does not write a journal. Pending migrations are a preflight failure. `DemoData__ResetAll=true` is rejected in every environment.

Before `seed`, back up all eight databases with COPY_ONLY and CHECKSUM, verify the backup files, and rehearse their restoration under separate database names. Deploy compatible service assemblies and apply reviewed migrations first. Preserve the existing Docker volumes and integration stream.

Configure explicit `Workspace__SemesterAliases__<semester-guid>__Year` and `__Term` entries for the authoritative semester IDs (for example `2026` and `FA`). Keep the server configuration in the deployment's `shared/academic-aliases.env`; the workflow copies it into the Admin service env file on each release. Academic calendars are then created through the normal versioned API and timetable blocks retain its projected version.

For editable club workspaces, also configure `__ReportPeriod` (for example `FALL2026`), `__EditableFromUtc` and `__EditableUntilUtc`. Bound the edit window by the authoritative semester start/end; Year/Term alone does not enable workspace editing.

## Assets and files

Serve the committed asset directory at the configured public URL. All 60 PNG/PDF assets are synthetic. The 12 PDFs contain two readable pages each. Copy their bytes to the Report service's persistent volumes before seeding:

* `documents/guide-NN.pdf` → `/app/attachments/full-demo-v2/guide-NN-K.pdf`, for `K=0,1,2,3`.
* The same PDF → `/app/report-uploads/full-demo-v2/report-NN-K.pdf`, for `K=0,1,2,3`.

`NN` runs from `01` to `12`. The tool records checksums and real sizes. Contribution evidence uses the normal private upload endpoint. Six PDF/XLSX export requests go through the normal worker and must yield downloadable files with valid signatures before the run succeeds.

## Ownership and domain behavior

Demo logins use the supported email/password endpoint. Usernames start with `fulldemo.` and email addresses end in `@fpt.edu.vn`; no email or Google identity is fabricated or sent. Staff roles and campuses are distinct. Treasurer access comes from approved club membership.

Existing manager assignments are never replaced. The selected club IDs are pinned in the journal. School rosters are previewed and committed through the normal versioned workflow, retaining existing rows. The existing Gmail account is excluded from school-roster import.

Historical showcase records are labelled as demo fixtures. Policy approval, activity-application review, publication, recruitment, staff assignment, contribution submission/review, invitations, quest completion and export generation use the actual service endpoints. Action-based scoring keeps the product's deferred scoring contract; the tool never fabricates action points or legacy XP ledger entries. Recommendations remain derived by the existing pipeline rather than manually inserted Mongo documents.

## Resume and recovery

The journal stores per-module checkpoints, generated keys, row digests and before-images of modified rows. Each direct SQL phase has its own database transaction; there is no cross-database atomicity claim. Completed phases are preserved on rerun, including subsequent human reviews, attendance changes and applications.

A `prepared` checkpoint means interruption at the commit boundary. The tool stops before attempting another write: reconcile the receipt against SQL and the verified backup before deciding how to resume. Never mark a checkpoint committed based on elapsed time alone. The exclusive seed lock is acquired before reloading its mutable journal; an existing reserved-namespace account without a creation receipt is rejected before writes.

Recovery must stay within recorded demo keys and verify row digests before modifying them. API/worker observations supplement EF receipts, and roster/benchmark before-images are retained. These are operator evidence, not permission to automatically delete cross-service history: this tool deliberately has no blanket rollback command. Preserve records changed by a person and use the domain's reversal/adjustment workflows for canonical ledger history. The dataset includes one explicit, bounded contribution reversal through the real adjustment endpoint. Never clear a shared Redis stream or restore all production databases as a blanket undo of a demo run.

## Demo accounts and limitations

The operator handout, generated outside Git, lists the password and role/campus-specific account emails. `fulldemo.admin01@fpt.edu.vn` is the demo administrator; `fulldemo.manager01@fpt.edu.vn` manages the first pinned club, and `fulldemo.student001@fpt.edu.vn` is a student showcase account. The existing Gmail account retains student role, credentials, Google linkage, campus and security version.

Gift inventory and governance previews that the frontend stores in localStorage remain browser-specific. Database seeding does not turn those previews into shared server features.
