# Finance → shared shell mount handoff (single package)

Owner of this file: CLAUDE-FRONTEND-FINANS (`src/finance/**`).
Recipient: the existing shared shell/router owner, via the coordinator. Nothing here is
applied to a shared file by this lane.

Status: **BLOCKED ON TWO BACKEND PUBLICATIONS — do not mount yet.** The screens exist and
are component-tested; mounting today would only render a fail-closed block.

## What exists in `src/finance/**` (source #258 @fb482de, #254 @6f0cd10)

| Export | File | Purpose |
|---|---|---|
| `FinanceSourceList({ onOpen })` | `FinanceSourceList.tsx` | Authorized source list, cursor paged |
| `FinanceSourceDetail({ sourceId })` | `FinanceSourceDetail.tsx` | Detail + Record (Draft) / Reverse (Recorded), separate commands |
| `ManualReceiptDraftForm({ party, profile, draft?, onSaved? })` | `ManualReceiptDraftForm.tsx` | FS0 bank reader + FS1 draft save |
| `FinancePortProvider({ port })` | `financePort.tsx` | Injects the transport; absent → `service-unavailable` |

Company, actor, session and fence come from the existing `useShellState`/`contextKey`;
read states use `shell/StateBlock`. No shared file is imported for writing.

## Blockers (exact)

1. **No Finance shell capability provider.** #258 publishes no capability ids or route
   declarations. The shell registry must not get invented ids. Needed from Finance Backend:
   the provider's capability ids for list / detail / draft (and whether record/reverse are
   route or control capabilities).
2. **No HTTP mapping for `FinanceReceiptPort`.** #258 is a module port contract: no routes,
   methods, status mapping, `Idempotency-Key`/replay header names or platform 403 code.
   The HTTP adapter (module-owned, `src/finance/**`) is written once published.

Also open, not mount-blocking: a server source for a NEW draft's `ProfileReference`
(no Finance read returns one), a currency display code, and a party display name for the
list; the Cari → "Tahsilat taslağı" entry that supplies `party` is a later cross-lane item.

## The mount change, once 1 and 2 land (shell owner, one serial change)

1. `src/shell/capabilities.ts` — add the Finance ids exactly as the provider publishes them,
   their routes and nav label(s).
2. `src/shell/router.tsx` — import and spread `financeRoutes` (to be added in
   `src/finance/financeRoutes.tsx` by this lane, each page wrapped in `CapabilityRoute` and
   `FinancePortProvider` with the HTTP adapter) before the catch-all.
3. `src/shell/AppShell.tsx` — one nav entry for the list capability.
4. `src/shell/financeMountDrift.test.ts` — same pattern as `organizationMountDrift.test.ts`:
   shell ids == `FINANCE_ROUTE` keys, routes/labels equal, control-only ids not routes.

Serialize with the pending Product router spread and the Sipariş lane; two lanes must not
edit `router.tsx` in parallel.

## Evidence boundary

Component/DOM tests with a recording port and real pending promises (SENTETIK/NOT_ENV).
Not API, SQL, provider/profile/period readiness, browser or ENV acceptance. UI port 29173
is a reservation only; nothing was started.
