# Organization location real-browser journeys (ORG-01 / ORG-02)

Status: **SOURCE_PREPARED — never executed.** No environment has been started, no browser
has run, and no count in this package has been observed. Preparing source is not a PASS.

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

## Prerequisites that do not exist yet

This package cannot run today, and says so rather than degrading:

1. the server must publish the `organization.*` capabilities (#246 provider);
2. the shell must mount the routes — `shell/capabilities.ts` declares the two ids
   (#247), which is the frontend saying it has an implementation; the router/nav layer
   is the shared owner's follow-up;
3. the Organization environment must be stood up and declared ENV_READY with the tuple
   above, per `tests/e2e-fixtures/organization-locations/README.md`;
4. the `o-02` no-sites case additionally needs a **locations-only actor**, which the
   fixture package marks `NOT_PREPARED` (PR #244 `a2a3cfc8`). Until one is bound, that
   case **skips** and `artifacts/o-02-scope-and-permissions/coverage.json` records that it
   was not measured. It is never reported as covered by this seed or by the SQL test's
   permission reduction.

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
