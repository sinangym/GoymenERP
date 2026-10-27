/**
 * One keyed command attempt of the S-01 screens, on the Sales attempt machine (./orderAttempt.ts) — not a second one:
 * one Idempotency-Key per submitted snapshot; an unknown outcome (no answer, 5xx, a 2xx that is not the pinned success,
 * a body the decoder refused) keeps the key, freezes the inputs and offers only "retry the same snapshot" or "leave it";
 * a changed payload while unknown is blocked; an outcome lands only on the attempt and binding that sent it.
 */

import { useEffect, useRef, useState } from 'react'
import {
  abandon,
  applyOutcome,
  beginSend,
  decideSubmit,
  generateOrderAttemptKey,
  isOutcomeStillUnknown,
  liveSnapshot,
  orderAttemptBinding,
  type OrderAttemptSnapshot,
  type OrderAttemptState,
  type OrderCommandOutcome,
} from './orderAttempt'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { POOL_TEXT, poolProblemText } from './poolText'
import { recallPoolAttempt, rememberPoolAttempt } from './pendingPoolAttempts'

export type PoolSend<P, R> = (snapshot: OrderAttemptSnapshot<P>) => Promise<OrderCommandOutcome<R>>

export interface PoolAttempt<P, R> {
  readonly state: OrderAttemptState<P, R>
  /** True while the inputs must not change: in flight, or an outcome that may already be recorded. */
  readonly frozen: boolean
  /** True when the last submit of a CHANGED payload was blocked by an unresolved earlier one. */
  readonly blockedChange: boolean
  /** Sends `payload` (reusing the live key for the same payload); false when nothing was sent. */
  readonly submit: (path: string, payload: P) => boolean
  readonly retry: () => void
  readonly leave: () => void
  /** Back to idle after a definite answer (e.g. a closed form); an unresolved snapshot is never forgotten. */
  readonly reset: () => void
}

/**
 * `persist` (the creates): the attempt and the form values it was sent from survive the screen
 * (./pendingPoolAttempts.ts) — written before the send awaits, recalled under the same binding on the next visit, so an
 * unknown outcome is never resent under a new key. The commands on a stored record need none: their expected version
 * makes a second send a refusal, never a second record.
 */
export function usePoolAttempt<P, R>(
  scope: string,
  send: PoolSend<P, R>,
  onSettled?: (next: OrderAttemptState<P, R>) => void,
  persist?: { readonly values: unknown },
): PoolAttempt<P, R> {
  const shell = useShellState()
  const persisted = persist !== undefined
  const [state, setState] = useState<OrderAttemptState<P, R>>(
    () => (persisted ? recallPoolAttempt<P, R, unknown>(orderAttemptBinding(shell, scope))?.attempt : undefined) ?? { phase: 'idle' },
  )
  const [blockedChange, setBlockedChange] = useState(false)
  const ref = useRef(state)
  // Mirrors for the async continuation, written in effects (render purity).
  const valuesRef = useRef<unknown>(persist?.values)
  const mountedRef = useRef(false)
  useEffect(() => {
    valuesRef.current = persist?.values
  })
  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])
  // A recalled success has now been shown on this screen: it is settled.
  useEffect(() => {
    if (persisted && state.phase === 'completed') rememberPoolAttempt(state.snapshot.binding, state, valuesRef.current, true)
  }, [persisted, state])

  const set = (next: OrderAttemptState<P, R>) => {
    ref.current = next
    // Synchronously, before any await: a screen torn down mid-send leaves its key behind. A success that lands on a
    // mounted screen is shown (its notice, or the record it opens); one that lands after the screen went is kept.
    if (persisted && next.phase !== 'idle') rememberPoolAttempt(next.snapshot.binding, next, valuesRef.current, mountedRef.current)
    setState(next)
  }

  async function run(snapshot: OrderAttemptSnapshot<P>) {
    const previous = ref.current
    const inFlight = beginSend<P, R>(snapshot)
    set(inFlight)
    const outcome = await send(snapshot)
    const next = applyOutcome(inFlight, snapshot, orderAttemptBinding(shell, scope), outcome, previous)
    set(next)
    // A late answer for a screen that is gone changes nothing on screen (no navigation, no field errors); the held
    // attempt above is what the next visit resumes.
    if (mountedRef.current) onSettled?.(next)
  }

  return {
    state,
    frozen: state.phase === 'submitting' || isOutcomeStillUnknown(state),
    blockedChange,
    submit: (path, payload) => {
      const binding = orderAttemptBinding(shell, scope)
      const companyId = shell.activeContext?.companyId ?? null
      if (binding === null || companyId === null) return false
      const decision = decideSubmit(ref.current, { path, binding, companyId, payload }, generateOrderAttemptKey)
      setBlockedChange(decision.type === 'blocked' && decision.reason === 'unresolved-other-payload')
      if (decision.type !== 'send') return false
      void run(decision.snapshot)
      return true
    },
    retry: () => {
      const live = liveSnapshot(ref.current)
      if (live === null || ref.current.phase === 'submitting') return
      setBlockedChange(false)
      void run(live)
    },
    leave: () => set(abandon(ref.current)),
    reset: () => {
      if (ref.current.phase === 'submitting' || liveSnapshot(ref.current) !== null) return
      set({ phase: 'idle' })
    },
  }
}

export interface PoolNotice {
  readonly message: string
  /** `alert` for what needs attention now (a refusal, an unknown outcome); `status` otherwise. */
  readonly role: 'alert' | 'status'
  readonly offerRetry: boolean
}

/**
 * What the screen says about an attempt. "Not done" is said only when it is known; while the outcome is unknown — and
 * after a refusal that only answered a retry — the text says it MAY be recorded. `onRecord`: a command on a stored record
 * (its 409 means the record moved, and the record is re-read); otherwise a create.
 */
export function poolNotice<P, R>(
  state: OrderAttemptState<P, R>,
  options: { readonly noun: string; readonly onRecord: boolean; readonly blockedChange?: boolean; readonly completed?: (result: R) => string },
): PoolNotice | null {
  const text = POOL_TEXT.attempt
  switch (state.phase) {
    case 'idle':
      return null
    case 'submitting':
      return { message: text.sending, role: 'status', offerRetry: false }
    case 'unknown':
      return { message: options.blockedChange ? text.unknownChangedBlocked : text.unknown(options.noun), role: 'alert', offerRetry: true }
    case 'abandoned':
      return { message: text.abandoned, role: 'status', offerRetry: false }
    case 'completed': {
      const replayed = (state.result as { readonly isReplay?: unknown }).isReplay === true
      const message = options.completed?.(state.result) ?? text.completed
      return { message: replayed ? `${message} ${text.replayed}` : message, role: 'status', offerRetry: false }
    }
    case 'failed': {
      if (state.unresolvedSince !== undefined) {
        return { message: options.blockedChange ? text.unknownChangedBlocked : text.refusedAfterUnknown, role: 'alert', offerRetry: true }
      }
      const base =
        state.failure === 'validation'
          ? text.validation
          : state.failure === 'unauthenticated'
            ? text.unauthenticated
            : state.failure === 'forbidden'
              ? text.forbidden
              : state.failure === 'not-found'
                ? text.notFound
                : state.failure === 'conflict'
                  ? options.onRecord ? text.conflict : text.createConflict
                  : text.unexpected
      const reason = poolProblemText(state.problem?.code ?? null, options.onRecord)
      return { message: reason === null ? base : `${base} ${reason}`, role: 'alert', offerRetry: false }
    }
    default:
      return null
  }
}

/** LF-14: while a form holds work (or an outcome that may be recorded), a company switch ASKS instead of dropping it. */
export function usePoolDirty(scope: string, dirty: boolean): void {
  const dispatch = useShellDispatch()
  useEffect(() => {
    dispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope })
  }, [dirty, dispatch, scope])
  useEffect(() => () => dispatch({ type: 'DIRTY_CLEAR', scope }), [dispatch, scope])
}
