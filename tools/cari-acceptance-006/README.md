# C006 disposable Cari acceptance environment

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
