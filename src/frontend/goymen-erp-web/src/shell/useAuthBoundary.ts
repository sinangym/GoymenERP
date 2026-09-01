/**
 * React binding for the central business-read security boundary (Issue #165).
 *
 * Every authenticated business read calls `capture()` before it starts and
 * `report()` with the status it got. The hook owns the effect; the feature
 * component owns nothing security-relevant beyond passing the snapshot through.
 */

import { useCallback, useEffect, useRef } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useShellDispatch, useShellState } from './shellContext'
import { contextKey, contextKeyString } from './shellState'
import {
  boundaryEffectKey,
  captureSecuritySnapshot,
  decideBoundaryAction,
  type BoundaryAction,
  type SecuritySnapshot,
} from './authBoundary'

export interface AuthBoundary {
  /** Snapshot the security bindings for a read that is about to start. */
  readonly capture: () => SecuritySnapshot
  /** Report the status a read came back with. Returns what the boundary did. */
  readonly report: (status: number, snapshot: SecuritySnapshot) => BoundaryAction
}

export function useAuthBoundary(actorId: string | null): AuthBoundary {
  const state = useShellState()
  const dispatch = useShellDispatch()
  const queryClient = useQueryClient()

  /**
   * Security outcomes already applied. SAB-010: the same 401 seen by two
   * observers must reset once, not twice — a second reset advances the fence
   * past what the facts justify and triggers a re-render storm.
   *
   * A ref, not state: this must not itself cause a render, and it must be
   * consulted synchronously inside the same event that reports the outcome.
   */
  const applied = useRef<Set<string>>(new Set())

  /**
   * `report` must compare against the state as it is WHEN THE RESPONSE LANDS, not
   * as it was when the request was made — that comparison is the whole point. So
   * it reads through refs rather than closing over a render value.
   *
   * Synced in an effect, not during render: writing a ref while rendering is a
   * tearing hazard, and it is unnecessary here because a response can only
   * arrive after the render that started it has committed.
   */
  const stateRef = useRef(state)
  const actorRef = useRef(actorId)
  useEffect(() => {
    stateRef.current = state
    actorRef.current = actorId
  }, [state, actorId])

  const capture = useCallback((): SecuritySnapshot => {
    const key = contextKey(stateRef.current)
    return captureSecuritySnapshot(
      stateRef.current,
      actorRef.current,
      key === null ? null : contextKeyString(key),
    )
  }, [])

  const report = useCallback(
    (status: number, snapshot: SecuritySnapshot): BoundaryAction => {
      const action = decideBoundaryAction(status, snapshot, stateRef.current, actorRef.current)
      const effectKey = boundaryEffectKey(action, snapshot)
      if (effectKey === null) return action
      if (applied.current.has(effectKey)) return action
      applied.current.add(effectKey)

      if (action.kind === 'session-reset') {
        // SAB-002/003: end the session centrally, TERMINALLY, and drop every cached read
        // taken under it — feature, identity, directory and capability alike.
        // `clear()` also removes the queries, so mounted observers have nothing
        // to serve and no data to resurrect on focus or reconnect (SAB-007).
        dispatch({ type: 'SESSION_INVALIDATED' })
        queryClient.clear()
        return action
      }

      // SAB-004/005: a 403 does not have to end the session, but the capability
      // we read under is no longer trustworthy. CAPABILITY_REVOKED nulls the
      // generation and the granted list and advances the fence, so
      // `capabilityFailClosed` closes every gated mount until a FRESH snapshot
      // is fetched — the feature cannot come back on stale authority.
      dispatch({ type: 'CAPABILITY_REVOKED' })
      // Remove, not invalidate: invalidate would refetch under the very
      // authority the server just rejected.
      queryClient.removeQueries({
        predicate: (query) =>
          snapshot.namespace !== null && query.queryKey[0] === snapshot.namespace,
      })
      return action
    },
    [dispatch, queryClient],
  )

  return { capture, report }
}
