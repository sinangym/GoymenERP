# Shell — SHELL-PILOT-01B fence core

Issue #129 (`SHELL-PILOT-01B`). Parent #118. Design source:
`docs/delivery/CRITICAL_ANALYSIS/shell-companycontext-fence/claude.md`.

## What is here (this PR)

The **package-independent** core of the CompanyContext fence, as an isolated,
fully unit-tested module. No React, router, fetch or new npm package.

| File | Purpose |
|---|---|
| `contextFence.ts` | `RequestFence` primitive — INV-FENCE (§A.2). Advances at transition **start**. |
| `shellState.ts` | `shellReducer` two-phase transition state machine — INV-PENDING / INV-REAUTH / INV-NEUTRAL (§A.3); `contextKey` INV-NS (§A.4); `capabilityFailClosed`. |
| `fixtures.ts` | **TEMPORARY** contract shapes for parallel development. Deleted when the generated client lands (see deletion contract in the file). |
| `*.test.ts` | Reducer + fence unit tests, including out-of-order response rejection and failed-switch non-disclosure. |

## What is NOT here yet

- React binding (`useReducer` provider), layout, router, route outlet — needs the
  Stage 0 package decision (PR #118 → `ADR-frontend-stack`).
- Generated TypeScript client consumption — needs SHELL-PILOT-01A (#128 / PR #132)
  and the OpenAPI contract-drift gate (Issue #122).
- Company-directory fetch, capability fetch, shared UI states, browser/a11y E2E.
- Any business feature mount. `capabilityFailClosed` returns `true` until a real
  server-backed capability generation is resolved.

## Integration path

```text
fixtures.ts (temporary shapes)
  → generated client from src/api/generated/** (Issue #122)
  → shellState.ts imports context/capability shapes from generated types
  → fixtures.ts deleted; drift check enforces the boundary
```

Final integration must consume the exact accepted backend contract. This module
claims no integration completeness.
