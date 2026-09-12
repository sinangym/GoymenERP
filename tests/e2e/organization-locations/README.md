# Organization location real-browser journeys (ORG-01 / ORG-02)

Status: **MEASURED (normal profile) — run `20260912o39r2`, 7 passed / 1 skipped.**
A real Chromium ran the seven authorized cases against the real API on `127.0.0.1:27080`
and its own SQL database, twice consecutively green on the committed tree.

The eighth case — "without the optional sites capability the screen issues ZERO /sites
requests" — is **MEASURED SEPARATELY**, in run `20260912o39lo1` on its own reduced-
permission seed and database, with a bound `locationsOnlyActor` (Node v24.21.0). It
passed: the entitled list rendered, the entitled card opened with no reasoned-change
control and zero commands sent, and `/api/v1/organization/sites` was never requested at
all — not issued and discarded. `o-02-scope-and-permissions/coverage.json` records which
of the two situations produced the evidence, so a skipped run can never read as a pass.

Database rows, audit contents and cleanup are the environment owner's observation, not
this package's.

Owner: `CLAUDE-FRONTEND-ORGANIZASYON`, sole writer of `tests/e2e/organization-locations/**`.
The environment, the SQL side and cleanup belong to the Organization backend owner.

## What this measures, and why a browser is needed for it

The screens already have 177 component/DOM cases in `src/organization/**`. Those prove
what the client does with an answer. They cannot prove what the server answers, and four
things in this slice only exist at that boundary:

| Case | What only the real stack shows |
|---|---|
| `o-01` list → card → reasoned change → current view | a 204 carries no body and no version, so the card must come from a GET the app issued **after** the command settled — the ordering is asserted from the wire, not assumed |
| `o-01` reactivate | `expectedVersion` equals the version actually **read**; `version + 1` is never predicted (a same-state command leaves the version alone) |
| `o-02` scope / permissions | the server refusing, not merely the screen not rendering — "did not display it" and "cannot do it" are different guarantees |
| `o-03` conflict / unknown / replay | a real optimistic-concurrency 409, and a **server-side** replay of a real key. A synthesised 204 with a replay header proves our parser, not the contract |

## Nothing is mocked

There is no `route.fulfill` anywhere in this package. Two interceptions exist and both
are deliberate and narrow:

- `DeliveryWithholder` re-issues one **real** command with `route.fetch`, reads the
  server's **real** answer, and then aborts the browser's delivery of it. That is how a
  genuinely unknown outcome is produced on demand: the command really is committed and
  the client really does not learn the result. Injecting a 503 instead would exercise our
  error branch while avoiding the exact situation the idempotency key exists for.
- `o-02` rewrites **one** request header — the Development identity — to run the same real
  application as another real seeded actor. Every status and body remains the server's.

## It refuses to run without the tuple

`src/manifest.ts` throws unless `ORG_ENVIRONMENT_MANIFEST` points at a file that declares
`envReady: true` and names every bound commit (backend, frontend, capability provider,
shell mount, fixture, journey), both worktrees, the owned container, database and run id.
Commits must be full 40 characters; an abbreviation does not identify a tree.

There is no fallback port and no ambient default. A run that cannot say which code it
served is not evidence about that code — and a run that silently reached another lane's
API would be measuring, and disturbing, someone else's environment.

Ports are pinned: **27173 UI · 27080 API · 27433 SQL**. Cari (25xxx) and Ürün (26xxx)
belong to other owners and are never touched. `strictPort` covers occupation; an
`ORG_UI_PORT` that disagrees is refused here rather than becoming a confusing failure.

## Prerequisites

State as of this commit, verified against `main` rather than asserted:

1. **Done.** The shell mounts the routes and the nav entry (#248, `fc14554`), and
   `shell/capabilities.ts` declares the two route ids (#247).
2. **Outstanding, but NOT a merge.** The run needs an API assembly that publishes the
   `organization.*` capabilities. That is the environment owner's published build
   (#246 `4f9dab4`), bound in the manifest as `capabilityProviderCommit` — **not** a
   merge to `main`. Not finding the provider in the frontend's `main` tree says nothing
   about the separate binary this run is served by; the isolated acceptance binds
   API `4f9dab4` + this journey/frontend + the shell mount + the fixture/schema as one
   tuple. Until such an assembly is bound and running, these routes mount nothing —
   `CapabilityRoute` closes them onto the neutral not-found block, by design.
3. **Outstanding.** The Organization environment must be stood up and declared ENV_READY
   with the tuple above, per `tests/e2e-fixtures/organization-locations/README.md`;
4. the `o-02` no-sites case additionally needs a **locations-only actor bound in the
   manifest**. The environment owner has now PREPARED that payload, but it is not loaded
   (`runtimeLoaded=false`) and it runs against its own reduced-permission seed in a fresh
   owned database — the normal viewer/site assertions belong to the normal seed run.
   Prepared input is not an observation: until a manifest binds the actor, that case
   **skips** and `artifacts/o-02-scope-and-permissions/coverage.json` records that it was
   not measured. It is never marked observed from a source hash, and never reported as
   covered by the normal seed or by the SQL test's permission reduction.

## Order of operations

Source review and CI on this package → the environment owner observes the actual
ENV/capability state → **this suite runs the browser** → the environment owner takes the
SQL result and does cleanup → the coordinator decides the #246 merge. The provider merge
is the LAST step, not a precondition for the browser run.

## Who owns port 27173 during a run

This suite starts Vite itself through Playwright's `webServer` with
`reuseExistingServer: false`, so it must be the only thing on 27173 when it starts. If
the environment owner has a temporary Vite smoke process on that port, it has to be
released first — otherwise `strictPort` refuses and the run stops before any case.

That order is deliberate: the suite serving its own bound frontend is what makes the
evidence say which frontend commit was measured.

## Running it, once the environment exists

From `tests/e2e` (where `@playwright/test` is already a dependency — this package adds no
script, no CI job and no shared config):

```powershell
$env:ORG_ENVIRONMENT_MANIFEST = '<absolute path to the ENV_READY manifest>'
npx playwright test --config organization-locations/playwright.config.ts
```

Evidence lands in `organization-locations/artifacts/` (gitignored): `run.json` with the
browser build and the bound tuple, per-case network JSON, screenshots, JUnit and the
Playwright JSON report.

## Redaction

Only a URL's pathname and its query **keys** are ever recorded; no query value can reach
the evidence through that path. `authorization` and `cookie` headers are never read. The
recorded request headers are the company id, the idempotency key, the development actor
and the correlation id — the attempt's identity, which is the thing being measured.

`webServer.env` carries only the four variables the Vite profile needs, because the JSON
reporter serializes that object into `results.json`; spreading `process.env` there is how
a report comes to contain a whole environment.

## What this package does not claim

- No database assertion. PR #241 owns the real SQL lifecycle evidence and the environment
  owner takes the before/after snapshot; this suite reads the API through a browser.
- No audit-content assertion, and nothing about companies, stock, ownership or capacity.
- No identity-provider revocation: the Development bootstrap host authenticates by
  header, so a real revocation cannot be produced here and is not measured.
- A green run here is browser evidence for these four cases only — not scenario parity
  with the Business package, and not an ENV_READY claim of its own.
