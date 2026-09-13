The c006-7f12ed40ff9a browser run FAILED at the company discard dialog. Final measurement was absent; the poststate source guard rejected capture, and final SQL verification was NOT_RUN. Owned stop/release-check/teardown succeeded. Its evidence and former readiness remain immutable historical records, not this candidate's acceptance.

The allocated-only c006-d861550bfb2e source candidate is also preserved: its CI failed on a PID substring assertion. No live resources were started for that candidate. This revision fixes that assertion with a deterministic folder-substring regression; all lifecycle state and no-kill checks remain unchanged.

## L04 retry source candidate (2026-09-13)

Current frontend #250: 0d20850562d271ea934c04ff8c002679030dfaf2.
Committed frontend+harness diff SHA-256:
311edcb5a7a45ffcda20fc04adbbf16e126347dfe185cfff879824ea4fbc6faa
against base 3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f.
Source-only independent review:
https://github.com/sinangym/GoymenERP/pull/250#issuecomment-5652761056
Body SHA-256: af9a9c17e3e0132aa90d02d264703d14f983f92123a70e315768694750965650.
This review is not browser/SQL acceptance evidence.

New run: c006-c79a094df633; instance: a226ca3a-08b3-4efd-92ec-b22333778557.
External contract: artifacts/cari-acceptance-006/account-status-20260913/c006-c79a094df633-contract.json.
AcceptanceHead and CompiledHead must both identify the final local source.
BackendDependency remains 9df7f40e3aefc5b02ef42a378c0d1aa800a2fe29.
Ports remain API/SQL/UI 25080/25433/25173.
Use the existing bind/setup/start/smoke/publish/stop/release-check/teardown flow
with the new run evidence directory and a fresh root-locked contract.
Do not start live resources before the root lock.

The older L04 section below describes the preserved c006-5b7b30940e9a run,
not current readiness. Its browser journey FAILED; final SQL effects
verification was NOT_RUN because no final measurement was produced.
Stop/release-check/teardown succeeded. Its contract, logs and ENV_READY bytes
are immutable historical evidence and must not be reused for this candidate.
This pin update does not claim a new ENV_READY or browser/SQL pass.
# C006 disposable Cari acceptance environment

## Current Cari L04 source preparation (2026-09-13)

This worktree's Cari constants now select `c006-5b7b30940e9a`, instance
`1bd0f50d-f882-45b6-8d24-175f032ef1d5`, database `GoymenCariC006_5b7b30940e9a`.
Historical sections below describe preserved runs, not current readiness.
Owner worktree: `C:\src\GoymenERP-cari-acceptance-envelope-20260911`.
Base main: `60e584d3255aa034060538f4413dc4aafa051a7c`.
The separately locked Cari API dependency remains `9df7f40e3aefc5b02ef42a378c0d1aa800a2fe29`.
Final acceptance/compiled head must match the actual new commit and local DLLs.

Frontend owner #250: `b9f094c437b4d3be61c9c8e2d1ebc451a8451b6f` in
`C:\src\GoymenERP-claude`; diff base `3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f`.
Raw committed frontend+harness diff: 760095 bytes,
SHA256 `8f7bf0c538dfeffd37adbd0609a707003e0e5aa06bf55dd1a3a6ea7e44f5684b`.
The existing independent source-only review is #250/comment-5649036177;
the publisher binds its exact prose hash, reviewer attribution and current required CI.
It does not represent a human approval or executed browser/SQL test.

External contract: `artifacts/cari-acceptance-006/account-status-20260913/contract.json`.
Backend owns preparation; coordinator receives the actual final source/contract hashes.
Existing contract shape/ports remain API 25080, SQL 25433, UI 25173.
Evidence: `artifacts/cari-acceptance-006/account-status-20260913/c006-5b7b30940e9a`.
The one-node setup/start/smoke/publish/stop/release-check/teardown flow is unchanged.

Smoke creates exactly one Party with A/B roles=0 accounts. Six existing contact API
commands populate A at active v7, B remains active v1. It verifies the existing reader
`company-a-user` can read and receives server 403 for status writes with no SQL effect.
Only the isolated Testing provider advertises `cari.accounts.status.update`, using the
existing permission and detail-parent control contract. Normal providers/CORS are unchanged.

After final source tests, review and CI, run the existing `BackendVerify` command, bind,
setup, start and smoke. Publish using `publish.py --tests-summary <actual-local-summary>
--frontend-review <coordinator-receipt> --github-cli <gh>`. Claude runs only L04 after
validating the published manifest. Then:

```powershell
./tools/cari-acceptance-006/control.ps1 -Action verify-account-status -BrowserMeasurement <actual-l04-measurement.json>
./tools/cari-acceptance-006/control.ps1 -Action stop
./tools/cari-acceptance-006/control.ps1 -Action release-check
./tools/cari-acceptance-006/control.ps1 -Action teardown
```

The typed verifier joins actual page-local requests, original lost 204, delivered 204
with lost GET, fresh reads around historical replay, three durable keys and two audit
transitions. Expected: 4 PUT, 2 mutations, A active v9, 1 replay, 0 NoChange,
0 new Party/account, B unchanged, reader writes 0. SQL evidence is separate from the
browser verdict. At source preparation live ENV/browser/SQL/cleanup are `NOT_RUN`.

## Product U-01/U-02 adapter (PR #251)

The same executable, `setup.ps1`, `control.ps1`, same-handle `Lifecycle`, and tested
runtime DLL validator now also serve the explicitly selected `-Product` profile.
Consumed Cari constants and runs are unchanged. No normal Product provider or CORS
origin is activated. Product uses API/SQL/UI 26080/26433/26173 and only the existing
`tools/product-u01-u02/ENVIRONMENT_CONTRACT.md` three-file contract. The publisher
writes its own worktree; the Product owner copies the exact verified bytes.

The source preparation rejects historical frontend `1c48f609b83caa1118b0b274261678b6857088fa`
for live use because it lacks the origin transport correction. Source capture is
not ENV_READY. Admit the corrected frontend exact head and existing narrow source
pin before any live command. `backendDependency` retains the separately accepted
shared runtime pin; `acceptanceHead` and `compiledHead` bind the final local build.

From `C:\src\GoymenERP-product-owner-boundary-20260912`, after final commit/review/CI:

```powershell
# Existing backend-only entry: complete discovered SDK rows, no browser or SQL claim.
./tools/ci/cari-c02-link.ps1 -Mode BackendVerify -OutputParent C:\src\GoymenERP-product-evidence
# Capture observed source using the final admitted pins and the exact ordinal
# ProductEnvironmentSettings.FixturePaths list. Send the JSON to the Product owner.
# dotnet <existing-tool-dll> product-source <backendDependency> <frontendWorktree> <frontendDiffBase> <fixturePaths...>

# The Product owner assembles accepted-contract.json; coordinator supplies its hash.
# Set these to the REAL returned paths/hash, not example or manifest-derived approval:
$env:GOYMEN_PRODUCT_ACCEPTED_CONTRACT = '<absolute owner accepted-contract.json>'
$env:GOYMEN_PRODUCT_ACCEPTED_CONTRACT_SHA256 = '<coordinator-recorded SHA256>'
$env:GOYMEN_PRODUCT_BACKEND_TESTS_SUMMARY = '<this local BackendVerify summary.json>'
./tools/cari-acceptance-006/setup.ps1 -Product
./tools/cari-acceptance-006/control.ps1 -Product -Action start
./tools/cari-acceptance-006/control.ps1 -Product -Action smoke
./tools/cari-acceptance-006/control.ps1 -Product -Action publish
./tools/cari-acceptance-006/control.ps1 -Product -Action live-check
./tools/cari-acceptance-006/control.ps1 -Product -Action snapshot
# Claude runs the existing L consumer against the owner's hash-checked three files.
./tools/cari-acceptance-006/control.ps1 -Product -Action live-check
./tools/cari-acceptance-006/control.ps1 -Product -Action verify -ExpectedProducts 1 -ExpectedMutations 2
./tools/cari-acceptance-006/control.ps1 -Product -Action stop
./tools/cari-acceptance-006/control.ps1 -Product -Action release-check
./tools/cari-acceptance-006/control.ps1 -Product -Action teardown
```

Smoke verifies real global assignment capabilities, list/catalog/missing detail and
company-only denied reads/create with no durable effects. GET never creates Product.
The live browser performs actual create/edit and replay. `verify` checks aggregate
Product/registration/audit/terminal effects; it does not replace Product owner's
typed row assertions or Claude's browser verdict. Extra attempts require explicit
expectations and distinct browser-run binding; one normal L journey is 1 Product and
2 mutation effects. Before/after snapshots are observations, not concurrent barriers.
At source preparation all live ENV, L, effects and owned cleanup remain `NOT_RUN`.

This continues the existing #228 C006 host and lifecycle. It is not a second lifecycle,
a normal-provider change, production activation, or completed Cari acceptance.

## Source and ownership

- Imported source: d46f7f819456fc578a25cdcabf5c98ad6f634456, exactly 17 backend/test/lifecycle/CI paths.
- published-source-manifest.json records ORIGINAL raw blobs and original working copies,
  not the subsequently adapted sources. Old WIP, DPAPI and evidence are not transferred.
- Backend dependency: 880cae838e5d2f01a988f1e4a89afbe67858f48c. Closed P2 is unchanged.
- Approved frontend source: c3cb45e1f85a7079c742bc0abbe62ca3aa805dfe, owned by Claude/#233.
  Coordinator accepted its seven CI checks and independent exact-head source reviews:
  https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5641353556
  https://github.com/sinangym/GoymenERP/pull/233#issuecomment-5641378360
  Source acceptance is not live browser/ERP acceptance. The publisher's explicit refusal
  of obsolete frontend 8f8e241db68b8f071e52d37a33c2176980eed1e8 remains unchanged.
- OpenAPI SHA256: 062c06175916295bc97a438adea6fd01cdb608413c58501aa62208f87038a829.
- Migration source-manifest SHA256: 1ec8222d998cb59891f437fc1ce4f62f954203508f3b2cffd4a00d97b1933e44.
- Frontend diff SHA256: a184b46b34b2208dde74c366abe105b54c9d01515aa1930f0a597cb597fd6e53 (1077156 raw bytes).
  Method: git diff --binary --full-index --no-color --no-ext-diff --no-textconv
  060da63f2644cb271d78d9eab8b7a6985d544ad1 c3cb45e1f85a7079c742bc0abbe62ca3aa805dfe
  -- src/frontend/goymen-erp-web tests/e2e.
- Runtime source-binding.json binds the clean acceptance commit, compile-time SourceRevisionId,
  output DLL disk hashes, fixture source hash and exact run/resource identities.
  Disk hashes are not memory-image attestation or independent review.

## Exact local envelope

Worktree: C:\src\GoymenERP-cari-acceptance-envelope-20260911
Frontend worktree (read only): C:\src\GoymenERP-claude
Run: c006-fc721a1b9752
Instance: 7ec92416-5a5d-42ad-9823-3574f27592a8
Database: GoymenCariC006_fc721a1b9752
SQL: 127.0.0.1,25433
API: http://127.0.0.1:25080
Test-only browser origins: http://127.0.0.1:25173 and http://localhost:25173
Evidence: artifacts/cari-acceptance-006/c006-fc721a1b9752
Browser synthetic name prefix: CARI-JOURNEY-fc721a1b9752

Exactly list/detail/create/link open in Testing; existing permissions and CompanyId remain.
company-a-viewer has no Cari write permission; its existing Cari read denial is not bypassed.
Normal provider, C01 golden and same-handle/PID/start-time lifecycle guards are unchanged.

## Commands

Use the exact backend worktree and owning Windows user. Use the built-in application approval
when needed, not security changes or safe.directory exceptions.

1. Commit the candidate, then execute the existing verification entry point:
   ./tools/ci/cari-c02-link.ps1 -Mode BackendVerify -OutputParent C:\src\GoymenERP-c006-evidence
   This builds with exact SourceRevisionId and passes every discovered SDK row with zero skips.
   Frontend/helper/browser execution is NOT_RUN; existing Prepare/Verify retain full checks.

2. Bind clean committed source and the approved frontend pins:
   dotnet tools/GoymenERP.CariAcceptance006/bin/Release/net10.0-windows/GoymenERP.CariAcceptance006.dll bind

3. Create only new disposable SQL resources:
   ./tools/cari-acceptance-006/setup.ps1

4. Start and smoke:
   ./tools/cari-acceptance-006/control.ps1 -Action start
   ./tools/cari-acceptance-006/control.ps1 -Action smoke
   Expected: roles0 create/list/detail/candidate/link across A/B; unchanged A; GET and retry
   without additional effects; company/viewer denial; contract negotiation and test-only CORS.

5. Publish fresh evidence:
   python tools/cari-acceptance-006/publish.py --tests-summary <this-head-summary.json> --frontend-review <coordinator-review-receipt.json>
   Review receipt: head, status="approved", reviewUrls with two independent #233 exact-head records.
   Source/build/run/process/listener/container/SQL and fresh acknowledged GETs are rechecked.
   Error acknowledgment is not success or readiness.

6. Claude runs its existing frontend/browser harness using environment.json. Run only L02
   first in a separate serial invocation (workers1/retries0), bracketed by actual source-check
   and typed snapshots. Compare the whole invocation's six Effects counters and Rows for zero
   change; browser timestamps are not retroactive snapshots. Run L01 separately afterwards.
   Its intermediate postcommit-to-retry SQL intervals remain NOT_MEASURED; no checkpoint
   helper is added. Actual same-key HTTP replay and the global journey delta are separate evidence.
   After the one L01 journey:
   ./tools/cari-acceptance-006/control.ps1 -Action verify -ExpectedNewParties 1
   Expected delta: one Party, two roles0 accounts, one create/link audit and execution each.
   Do not substitute C01 fixed counters.

7. Clean up only this owned run:
   ./tools/cari-acceptance-006/control.ps1 -Action stop
   ./tools/cari-acceptance-006/control.ps1 -Action release-check
   ./tools/cari-acceptance-006/control.ps1 -Action teardown

## CRO-AC-009 and evidence status

CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests.RolelessInactiveAccount_StaleVersionAndPermissionGatesRemainEnforced
has AcceptanceId=CRO-AC-009 and reuses the existing disposable real-SQL factory.
It proves roles0 -> inactive/version2; stale1 durable409 without business/audit change;
same-key409 replay; viewer/foreign-company403 without current-version disclosure;
authorized expectedVersion2 reactivation with roles0/version3.

Use GOYMEN_ERP_TEST_SQLSERVER_CONNECTION only for an approved disposable SQL test server.
Credentials never belong in a command line, committed file or uploaded artifact.

At preparation, new SDK/SQL/live/browser checks and final-head CI/reviews are NOT_RUN.
Actual results are recorded separately after execution; this document claims no PASS.
No ERP acceptance, merge authorization, mixed-node proof or production activation is implied.

## Main alignment and consumed run

Main 78494ed9bc60692fac6c186aa52b0049bbf4c306 is the squash merge of the locked
API dependency 880cae838e5d2f01a988f1e4a89afbe67858f48c. Their source trees match.
The normal main merge preserves C006 additions; it does not change BackendDependency.
AcceptanceHead and CompiledHead must name the same final C006 commit, not the API pin.
The frontend diff base remains 060da63f2644cb271d78d9eab8b7a6985d544ad1, using the
committed frontend+harness diff recipe above, independently of the PR main base.

CODEX-BACKEND is the single writer of the external contract, at:
C:\src\GoymenERP-cari-acceptance-envelope-20260911\artifacts\cari-acceptance-006\inputs-c006-fc721a1b9752\contract.json
Only real accepted sources and matching local-build evidence can populate it.

Previous run c006-303760997c6f is consumed and torn down. Its ENV_READY, inputs,
source/build/SQL receipts and L01 FAIL evidence remain historical and must not be reused.
Its strict global ExpectedNewParties=1 passed; that did not turn the browser timeout
into acceptance. New scoped L02 is required because the shared barrier changed:
existing fixture/smoke -> typed L02-before -> Claude L02 -> typed after/zero -> one L01
-> strict ExpectedNewParties=1 -> owned cleanup. No extra seed, API probe, intermediate
SQL hook or parallel lifecycle is introduced; selection coverage remains separately scoped.

The subsequent c006-ac5fc7069528 / backend6543792 / frontenda585 run is also consumed.
Its L02 test body did not load; separate browser warm-up is not test-body acceptance
or a measured API request count. The full attempted interval's typed SQL projection
was unchanged, and owned cleanup was verified. Its artifacts remain historical.
This next run binds the separately reviewed c3cb45e loader correction; neither old
ENV_READY is rebound, and no new lifecycle or intermediate SQL hook is introduced.
