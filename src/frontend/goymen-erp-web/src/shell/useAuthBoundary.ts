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
import { useShellDispatch, useShellState } from './shellContext'
import { contextKey, contextKeyString } from './shellState'
import { captureSecuritySnapshot, type SecuritySnapshot } from './authBoundary'

export interface AuthBoundary {
  /** Snapshot the security bindings for a read that is about to start (SAB-001). */
  readonly capture: () => SecuritySnapshot
  /** Report the status a read came back with. The reducer decides what it means. */
  readonly report: (status: number, snapshot: SecuritySnapshot) => void
}

export function useAuthBoundary(): AuthBoundary {
  const state = useShellState()
  const dispatch = useShellDispatch()

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
      dispatch({ type: 'BUSINESS_AUTH_OUTCOME', status, snapshot })
    },
    [dispatch],
  )

  return { capture, report }
}
