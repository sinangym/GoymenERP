# U-01/U-02 local acceptance environment contract

Status: source preparation only. No accepted schema, SQL environment, actual fixture,
grantee assignment or `ENV_READY` publication is created by this document. All live
values below must come from the admitted source and its actual isolated run.

This is the single input contract for PR #245's Product fixture and PR #238's L
consumer. It consumes the existing C006 `SourceSnapshot` / capture-then-verify / owned
host-resource pattern and C010 browser run ownership. It does not change those Cari
files or introduce a second general environment system. The C006 constants and
already completed C006 evidence must never be used as Product values.

## Files and writers

The Product acceptance root is exactly:

`C:\Users\goyme\.codex\worktrees\dd1f\GoymenERP-codex\artifacts\product-u01-u02\acceptance\<runId>`

`runId` is `product-u0102-` followed by 12 lowercase hexadecimal characters. This is
a resource identity, distinct from the C010 browser execution run ID. The publisher
allocates it once, with a non-nil UUID `instanceId`; no pre-existing directory or
resource is adopted. These are technical fixture identifiers, not business data.

| File / source | Writer and purpose |
| --- | --- |
| `accepted-contract.json` | Product fixture owner assembles the exact candidate with Shared Backend's source/schema/runtime inputs and Claude's frontend pins. Existing Orchestrator records its SHA-256 in the #245 handoff before live execution. It is immutable for that run. |
| `source-binding.json` | Actual Shared Backend environment publisher captures and verifies current source/build plus actual Product resource/host ownership against the accepted contract, using the C006 pattern. This is observed evidence, not manually filled readiness. |
| `environment-ready.json` | Same publisher, only after the source → schema → SQL gates and its immediately preceding ownership/source verification succeed. It references the exact two files above by hash. |
| Product fixture / assertions | CODEX-BACKEND-URUN owns Product code-group/data setup and Product SQL assertions after schema admission. No shared Identity/audit/terminal mapping, migration snapshot or runtime registration is edited by this owner. |
| `tests/e2e/product-u01-u02/**` | Existing Product Claude owns the manifest validator and L consumer, retaining C010 run ownership and C009 evidence writers. |
| API, Identity, audit, terminal, schema, host composition | Existing Shared Backend is the single source writer and actual runtime publisher. Existing Orchestrator owns admission and merge. |

Each owner writes only in its assigned worktree. When the actual publisher runs in
the Shared Backend worktree, it hands off its verified file bytes; the Product owner
places unchanged, hash-checked copies at the paths above. This does not authorize
either owner to write in the other's worktree or alter observed publisher evidence.

The frontend receives the absolute `environment-ready.json` path through
`URUN_ENVIRONMENT_MANIFEST` and the Orchestrator-recorded **accepted contract** hash
through `URUN_ACCEPTED_CONTRACT_SHA256`. The latter must not be read from an arbitrary
manifest and then trusted as its own approval. No readiness example file is supplied.

## Accepted contract: exact expected inputs

JSON uses lower camel case. Every field below is required, with no ambient fallback.
Hashes are lowercase SHA-256 (64 hex); Git commits/trees are lowercase 40-hex IDs.
All paths are absolute and must match the accepted path, not merely have a suffix.

| Field | Required value / type |
| --- | --- |
| `contractId`, `taskId`, `environmentName` | `PRODUCT-U0102-L-v1`, `U-01-U-02`, `Testing` |
| `runId`, `instanceId` | Resource run ID above; non-nil UUID |
| `acceptanceRoot` | Exact root above with this run ID |
| `apiBaseUrl`, `sqlEndpoint`, `uiOrigin` | Exactly `http://127.0.0.1:26080`, `127.0.0.1,26433`, `http://127.0.0.1:26173`; no userinfo/path/query/fragment, other host/port or override |
| `databaseName` | Exactly `GoymenERP_Product_U0102_<12-hex-run-suffix>` |
| `composeProject`, `containerName`, `volumeName`, `networkName` | Respectively this `runId`, `<runId>-sql`, `<runId>-data`, `<runId>-network` |
| `actor`, `actorId`, `unauthorizedActor`, `unauthorizedActorId` | Actual isolated fixture development-user names and distinct non-nil Identity UUIDs supplied by Shared Backend; first has explicit current global Product authority, second is company-only without it. No production/person assignment is implied. |
| `companyId`, `codeGroupId`, `browserPrefix` | Actual shell-context company UUID, real fixture code-group UUID, exactly `PRODUCT-U0102-<12-hex-run-suffix>` |
| `source` | Exact expected source object below |
| `erpAccepted`, `productionActivated` | Both the JSON boolean `false` |

`source` follows C006's source/build/fixture binding, with explicit Product and shared
dependency commits. These values are supplied after admission, never guessed:

```text
worktree: absolute admitted backend/acceptance worktree
acceptanceHead: final acceptance source Git commit
compiledHead: same commit as acceptanceHead, embedded in the running build
backendDependency: accepted shared runtime/Identity/audit/terminal Git commit
productDependency: accepted Product Git commit
backendTree: actual src/backend Git tree
openApiSha256: exact admitted OpenAPI file bytes
migrationSourcesSha256: exact admitted migration source inventory hash (C006 algorithm)
frontendWorktree: absolute frontend checkout used by the browser server
frontendHead: actual approved frontend Git commit
frontendDiffBase: approved base commit for the frontend diff
frontendDiffSha256: exact binary/full-index diff (C006 algorithm, frontend + tests/e2e)
fixtureSha256: C006 sorted path=SHA256 inventory algorithm over the exact fixturePaths
fixturePaths: non-empty sorted repository-relative fixture source/run-settings paths
sourcesClean: true, observed using the C006 tracked/untracked/build-input checks
diskArtifacts: non-empty sorted map of GoymenERP*.dll basenames to actual disk SHA256
```

All fixture paths must stay under the admitted worktree and be the exact reviewed
list. Fixture inputs include the Product code-group/run settings and actual Shared
Backend test Identity declarations. `codeGroupId` must refer to that fixture's
canonical valid-prefix group. Optional unavailable catalogs remain absent.

## Observed source binding and ready publication

`source-binding.json` has required fields `contractId`, `runId`, `instanceId`,
`acceptedContractSha256`, `source` (the same shape, captured from actual source/build),
`ownership` and `verifiedAtUtc`. The publisher compares all source fields and the
canonical ordinal artifact map with the accepted contract as C006 does. A clean flag
or syntactically valid hash alone is insufficient.

`ownership` records the actual `databaseName`, `sqlEndpoint`, `apiBaseUrl`,
`composeProject`, `containerId`, `containerName`, `volumeName`, `networkId`,
`networkName`, `resourceRunId`, `resourceInstanceId`, `hostPid` and `hostStartTimeUtc`.
The actual resource labels must carry this run/instance, the expected isolated DB
must be observed on SQL26433, and the live API26080 listener must belong to that
recorded host PID/start time. The publisher verifies these OS/container/DB facts;
the browser consumer does not open raw SQL or accept a self-declared ownership flag.
No connection string, password, token or cookie belongs in these files.

`environment-ready.json` repeats these exact accepted contract fields for the
existing L consumer: `contractId`, `taskId`, `environmentName`, `runId`, `instanceId`,
`apiBaseUrl`, `actor`, `unauthorizedActor`, `companyId`, `codeGroupId`, `browserPrefix`,
`erpAccepted`, `productionActivated`. It also requires:

```text
readyForBrowser: true (only the actual publisher may emit it after verification)
acceptedContractSha256: hash of the exact accepted-contract.json bytes
sourceBindingSha256: hash of the exact source-binding.json bytes
verifiedAtUtc: same UTC timestamp as the immediately preceding publisher verification
```

The companion paths are fixed siblings, never manifest-supplied arbitrary paths. The
ready publication is immutable; changed source, build, fixture, host or resource
identity requires a new accepted run. The existing publisher verification must run
again immediately before and after a live browser execution, comparing its observed
source/ownership with the immutable bound snapshot. A stale file is not a live host
verification. Until that publisher operation exists and succeeds, L stays NOT_RUN.

## Consumer validation before any side effect

1. Read the manifest and both fixed sibling files once as bytes. Validate exact
   types, required fields, hashes, accepted contract hash from the handoff, all
   repeated values, reserved endpoints, run/root/DB/resource names and the full
   expected-versus-observed source object. Require both final-status flags to be
   exactly `false`. Invalid, missing or mismatched input fails before C010 run/cache
   claim, Vite, browser or any request. A `readyForBrowser` boolean alone never passes.
2. Freeze that validated snapshot. Pass its manifest hash and accepted/source-binding
   hashes to Vite/workers; if any process rereads a file, compare exact bytes with
   those hashes before use. Vite's proxy target and development user come from that
   snapshot only. Ambient target/user overrides must not redirect it.
3. Claim the normal fresh C010 **browser** run. Record both that browser run ID and
   the environment's resource `runId`/`instanceId` plus all three file hashes in
   browser evidence. Distinct browser and resource run IDs are allowed and explicitly
   linked; neither silently substitutes for the other.
4. Select the exact `codeGroupId` from the live authorized catalog, rather than an
   arbitrary first group. Preserve the global Product contract: shell company context
   is not a Product owner or implicit grant. The same-origin Vite `/api` proxy needs
   no new shared CORS change for this journey.

Preparation tests may use obviously synthetic files with a separate temporary
directory to prove rejection/matching logic. They must never publish them as the
real run's ENV_READY, execute Product requests or count them as SQL/API acceptance.
