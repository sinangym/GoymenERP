/**
 * React binding for the central business-read security boundary (Issue #165).
 *
 * WHAT THIS HOOK IS NOT: the decision. It only captures the snapshot and forwards
 * the status. Whether an outcome is current, and whether its effect has already
 * been applied, is decided by the reducer.
 *
 * The first version got this wrong, and ChatGPT's #169 review was right about
 * both halves. The hook held `stateRef`/`actorRef` and an `applied` Set per
 * instance, so:
 *
 *  - once the consumer unmounted, its refs stopped tracking the provider and its
 *    in-flight promise judged a departed actor's late response against frozen
 *    state — which is the exact mistake the boundary exists to prevent;
 *  - two readers meant two Sets, so the same 401 could be applied twice.
 *
 * Both disappear when there is exactly one decider, and there is exactly one
 * reducer. See `BUSINESS_AUTH_OUTCOME` in ./shellState.ts.
 */

import { useCallback } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useShellDispatch, useShellState } from './shellContext'
import { contextKey, contextKeyString } from './shellState'
import { captureSecuritySnapshot, type SecuritySnapshot } from './authBoundary'
import type { IdentitySummary } from './api'
import type { ViewOutcome } from './uiState'

export interface AuthBoundary {
  /** Snapshot the security bindings for a read that is about to start (SAB-001). */
  readonly capture: () => SecuritySnapshot
  /** Report the status a read came back with. The reducer decides what it means. */
  readonly report: (status: number, snapshot: SecuritySnapshot) => void
}

export function useAuthBoundary(): AuthBoundary {
  const state = useShellState()
  const dispatch = useShellDispatch()
  const queryClient = useQueryClient()

  const capture = useCallback((): SecuritySnapshot => {
    const key = contextKey(state)
    // The actor is read from the COMMITTED context, not from a caller-supplied
    // value: it is the identity the read is actually authorized under.
    return captureSecuritySnapshot(
      state,
      state.activeContext?.userId ?? null,
      key === null ? null : contextKeyString(key),
    )
  }, [state])

  const report = useCallback(
    (status: number, snapshot: SecuritySnapshot) => {
      if (status !== 401 && status !== 403) return
      dispatch({
        type: 'BUSINESS_AUTH_OUTCOME',
        status,
        snapshot,
        // Read at REPORT time, straight from the query cache.
        //
        // The first version carried this into the reducer with an
        // IDENTITY_OBSERVED effect, which left a window: identity renders as B,
        // the effect has not run, and the reducer still believes A is
        // authenticated — so A's late 401 could terminate B (ChatGPT/Codex #169
        // P1-1). An effect is one tick too late for a security decision.
        //
        // The cache has no such lag. `report` is called from a queryFn, which is
        // after render, so whatever identity has resolved is already here.
        authenticatedActorId: readAuthenticatedActor(queryClient),
      })
    },
    [dispatch, queryClient],
  )

  return { capture, report }
}

/**
 * The actor the server currently authenticates, from the live identity query.
 *
 * Returns null when identity is unresolved or did not come back as a valid
 * envelope — and the reducer treats null as "unknown", which does not by itself
 * make an outcome stale. Refusing every outcome while identity is loading would
 * turn a slow identity call into a silent security bypass.
 */
function readAuthenticatedActor(queryClient: ReturnType<typeof useQueryClient>): string | null {
  const cached = queryClient.getQueryData<ViewOutcome<IdentitySummary>>(['shell', 'identity'])
  return cached?.type === 'ok-single' ? cached.data.userId : null
}
