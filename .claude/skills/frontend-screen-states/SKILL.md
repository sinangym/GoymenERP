---
name: frontend-screen-states
description: Use when building or reviewing a React screen, API client, contract decoder or Vitest test in src/frontend/goymen-erp-web (any lane module folder). Covers the strict decoder pattern (exact keys, fail-closed null), the RemoteViewState/StateBlock states (loading, empty-no-data, empty-filtered, unauthenticated, forbidden, not-found, conflict, offline, network-error, service-unavailable/503, malformed, stale), auth boundary reporting, command outcomes with idempotency replay headers and pending keys, Turkish text files, and the exact npm commands CI runs.
---

# Frontend screen states, decoders and tests

AGENTS.md PR gate: "Error/empty/loading/conflict UI durumları"; ban on re-implementing price/formula/tolerance in
the screen. Canonical module: `src/frontend/goymen-erp-web/src/purchasing/` (list `PurchaseRequestListPage.tsx`,
detail `PurchaseRequestDetailPage.tsx`, decoder `contract.ts`, command client `requestSubmissionApi.ts`, pending key
store `pendingRequestStore.ts`, texts `purchasingText.ts`, tests `PurchaseRequestSource.test.tsx`). Shared shell
pieces: `src/shell/uiState.ts` (`RemoteViewState`, `ViewOutcome`, `classifyOutcome`), `src/shell/StateBlock.tsx`,
`src/shell/useAuthBoundary.ts`, `src/shell/contextFence.ts`, design system `src/ui/Button.tsx`, `DataTable.tsx`,
`Field.tsx`, `StatusIndicator.tsx`, `ConflictDiff.tsx`.

## 1. Contract decoder (`<m>/contract.ts` or `<feature>Contract.ts`)

- Every response is `unknown` until decoded; a decoder returns the typed value or `null` — never a partial object,
  never a default. `null` ⇒ `{ type: 'malformed' }` ⇒ "Beklenmeyen yanıt".
- Objects: `record(value)` (non-array object) + `exact(value, FIELDS)` (key set equals the OpenAPI schema exactly;
  extra or missing key ⇒ null). Field lists are `as const` arrays next to the type.
- Scalars: `guid` (lowercase-insensitive UUID, not `00000000-…`), `canonicalVersion` (`^[1-9]\d*$`, ≤ int64 via
  `BigInt`), `dateOnly` (real calendar day), `instant` (ISO with offset, ≤ 64 chars), decimals as canonical
  **strings** (never `Number` for money/quantity), enums by explicit union check, nullable explicitly `=== null`.
- Cross-check identity after decode: response `companyId` equals the active company, `id` equals the requested id,
  expected status after a command — else malformed.
- Keep it in sync with `OpenApiContractGenerationTests.cs` / `contracts/openapi/goymen-erp-v1.json`
  (`openapi-regenerate`); add a decoder test (`contract.test.ts`, `requestContract.test.ts`) for missing, extra,
  mistyped and null fields.

## 2. Read → state

- `fetch…` returns `ViewOutcome<T>`: `offline` (navigator.onLine false), `network-error` (fetch threw),
  `http` with status/body, `malformed`, `ok`/`ok-single`.
- Before the read `const snapshot = boundary.capture()`; on 401/403 `boundary.report(status, snapshot)` (session
  and authority changes are owned by the shell reducer, not the screen).
- Query key includes the context namespace and `shell.routeEntryGeneration`; the component is keyed on them so a
  company switch remounts and stale answers are dropped (`stale-rejected`).
- Render `<StateBlock state={…} subject={T.subject} onRetry={…} />` for every non-ready state. Distinct states
  (`ERP-000K_SHELL_CONTRACT §12`): `initial-loading`, `no-authorized-company`, `empty-no-data`, `empty-filtered`
  (filter active), `unauthenticated` (401), `forbidden` (403, non-disclosing), `not-found` (404, same shape for a
  foreign record), `conflict` (409, with safe diff via `ConflictDiff`), `offline`, `network-error`,
  `service-unavailable` (≥ 500, retry), `malformed-response`, `stale-rejected`. Retry only for offline,
  network-error, service-unavailable, malformed.
- Paging: opaque `nextCursor`; a separate continuation state with its own `StateBlock` and "retry same page";
  de-duplicate by id; abort the previous page with a request fence (`purchasing/requestFence.ts`).

## 3. Commands

- Outcome union: `applied` (decoded response, `replayed`), `failed` (status 400/401/403/404/409/415/422 + stable
  `code`), `unknown` (`offline` | `network-error` | `service-unavailable` | `undecodable` | `unexpected-status`).
- Send `Idempotency-Key`; on 200 require replay headers to agree with the body: `isReplay` ⇔
  `Idempotency-Replayed: true` and `Idempotency-Original-Correlation-Id === originalCorrelationId`.
- An `unknown` outcome keeps the **same** key for the retry: persist it in a pending store keyed
  `goymen.<module>.<action>.v1:<sessionEpoch>:<actorId>:<companyId>[:<id>]` (`pendingRequestStore.ts`); drop it on
  `applied`/`failed`. Never mint a new key for a retry of an unknown outcome.
- Show the server's decision; never compute totals, limits, tolerances or risk in the screen ("sunucu vermiyor").

## 4. Texts and UI

- All user text in `<m>/<m>Text.ts` (Turkish), imported as `T`; no literal Turkish in TSX (TEK-03 #879).
- Buttons are `ui/Button` (TEK-02 #878); tables have `<caption>`; status as text + style, not colour only.
- Capability gates via `hasCapability(shell, <M>_CAPABILITY.x)`; never role names.

## 5. Tests (Vitest + Testing Library)

- Unit/DOM tests sit next to the file: `X.test.tsx`. Mock shell hooks
  (`vi.mock('../shell/shellContext', …)`, `shellState`, `useAuthBoundary`) and the lane's api module, or use the lane
  harness `<m>/testHarness.tsx` (`installFetch`, `json`, `problem`, `renderAt`, `requestsFor`).
- `new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })`, clear clients and
  `cleanup()` in `afterEach`.
- One test per state: loading, empty vs filtered-empty, 401 reported, 403 text, 404, 409 conflict, 503 retry,
  malformed body, stale context, replay headers mismatch, same key on retry. Data marked `SENTETIK`.
- Route/capability changes: `src/shell/<m>MountDrift.test.ts`, `capabilities.test.ts`.

## 6. Commands (what CI's `frontend` job runs, in `src/frontend/goymen-erp-web`)

```bash
cd src/frontend/goymen-erp-web
npm ci
npm audit --audit-level=high
npm run lint                                   # oxlint
npm test -- --maxWorkers=1 --no-file-parallelism   # vitest run
npm run build                                  # tsc -b && vite build
npx vitest run src/purchasing/PurchaseRequestSource.test.tsx   # one file while iterating
```

## Pitfalls

- `Number(...)` on a decimal string loses precision; keep strings and let the backend format.
- A 2xx with a body for another company or id is not success — treat as malformed.
- Do not collapse 403 and 404 into one message, or empty and filtered-empty.
- Do not add a dependency to `package.json` without an explicit reason (audit runs at high).
