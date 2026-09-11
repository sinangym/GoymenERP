# Shell — SHELL-PILOT-01B

Issue #129 (`SHELL-PILOT-01B`). Design: `docs/delivery/CRITICAL_ANALYSIS/shell-companycontext-fence/claude.md`.
Stack: ADR-013 (`react-router` + `@tanstack/react-query` + CSS Modules + reducer).

## Files

| File | Purpose |
|---|---|
| `shellState.ts` | CompanyContext reducer — two-phase transition, `requestFenceGeneration` (single authoritative counter + transition attempt id), `committedContextRevision`, `contextKey` tuple, `capabilityFailClosed`. |
| `contextFence.ts` | `isResponseStale` + `FenceView` — pure staleness primitive for the generated-client transport layer (Issue #122). Not wired yet; the switch fence is enforced by the reducer's `forGeneration` check. |
| `uiState.ts` | `RemoteViewState` §12 taxonomy + `classifyOutcome`. |
| `api.ts` | **Hand-written** thin transport (HANDOFF: replaced by generated client, Issue #122). Mirrors merged backend PR #132. |
| `shellContext.ts` | Reducer contexts + `useShellState` / `useShellDispatch`. |
| `ShellProvider.tsx` | Reducer + `QueryClient`. |
| `useShellOrchestrator.ts` | Bootstrap + company-switch effects. Call once (in `AppShell`). `useCompanyDirectory` is a safe shared read. |
| `AppShell.tsx` / `AppShell.module.css` | Dark-navy nav (capability-gated, empty), toolbar + company selector, `<Outlet/>`. |
| `ShellHome.tsx` | Index route — active company + honest "no module yet". |
| `StateBlock.tsx` / `.module.css` | Renders a `RemoteViewState` with distinct text per §12 state. |
| `router.tsx` | `shellRoutes` (config, for tests) + `shellRouter` (browser). |
| `SwitchConfirmDialog.tsx` / `.module.css` | Dirty-scope confirmation (`confirming-switch`): Kal / Değişiklikleri bırak, focus return. The bound context stays mounted underneath. |

## Bootstrap / switch flow

```
mount → identity + directory (useQuery)
      → pick company (localStorage preference) → GET /shell/capabilities
      → SESSION_READY({ userId, companyId, capabilityGeneration | null })

company select → REQUEST_SWITCH → phase 'switching' (fence++, activeContext null)
              → GET /shell/capabilities(target), attempt = requestFenceGeneration
              → REAUTH_OK{forGeneration} | REAUTH_FAIL{forGeneration}
              → reducer no-ops if forGeneration != current (stale attempt)
```

## Not here yet

Generated client; browser E2E for the C-02/CARI-LINK write paths; responsive `1600px`
drawer. (Route guards, deep-link non-disclosure and the dirty-form confirm UI landed
with C-01/C-03 and C-02/CARI-LINK respectively.)
