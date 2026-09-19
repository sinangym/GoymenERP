/**
 * The lifecycle of one fact command on the order screen.
 *
 *   idle ─SUBMIT─▶ submitting ─SETTLE─▶ applied | failed | unknown
 *   unknown ─RESEND─▶ submitting (same snapshot: same fact id, same key)
 *
 * THE RULE: only the server settles a snapshot. An UNKNOWN outcome keeps the snapshot,
 * and the only way forward is re-sending it unchanged; a fresh submit is refused while
 * one is unresolved, because a new fact id + key after a fact that may already be
 * recorded is a second fact.
 *
 * WHICH ANSWERS SETTLE A RE-SENT SNAPSHOT. The server answers a known key from its record
 * (ADR-011), but it measures the caller's authority FIRST (§5, MFG-API README). So a
 * 401/403 on a re-send says nothing about the earlier request — it may still be
 * recorded — and the snapshot stays unknown. Every other answer to a re-send is about
 * this key: a replay is the recorded answer, and a fresh refusal means no record exists.
 */

import type { FactFailure, FactOutcome, FactUnknown } from './commandApi'
import type { FactSnapshot } from './contract'

export type FactAttempt =
  | { readonly phase: 'idle' }
  | {
      readonly phase: 'submitting'
      readonly snapshot: FactSnapshot
      readonly resends: number
      /** Set on a re-send: the unknown this re-send is trying to resolve. */
      readonly unresolvedSince: FactUnknown | null
    }
  | {
      readonly phase: 'applied'
      readonly snapshot: FactSnapshot
      readonly replayed: boolean
    }
  | {
      readonly phase: 'failed'
      readonly snapshot: FactSnapshot
      readonly failure: FactFailure
      readonly replayed: boolean
    }
  | {
      readonly phase: 'unknown'
      readonly snapshot: FactSnapshot
      readonly unknown: FactUnknown
      readonly resends: number
      /** A re-send that was turned away at the authority gate, shown beside the unknown. */
      readonly lastRefusal: FactFailure | null
    }

export type FactAttemptEvent =
  | { readonly type: 'SUBMIT'; readonly snapshot: FactSnapshot }
  | { readonly type: 'RESEND' }
  | { readonly type: 'SETTLE'; readonly outcome: FactOutcome; readonly idempotencyKey: string }
  | { readonly type: 'RESET' }

export const initialFactAttempt: FactAttempt = { phase: 'idle' }

export function isUnresolved(attempt: FactAttempt): boolean {
  return attempt.phase === 'submitting' || attempt.phase === 'unknown'
}

/** An unresolved attempt restored after a remount: its outcome is unknown until re-sent. */
export function restoredFactAttempt(snapshot: FactSnapshot, resends: number): FactAttempt {
  return { phase: 'unknown', snapshot, unknown: { kind: 'aborted' }, resends, lastRefusal: null }
}

export function factAttemptReducer(state: FactAttempt, event: FactAttemptEvent): FactAttempt {
  switch (event.type) {
    case 'SUBMIT':
      // Frozen: nothing new while a snapshot is unresolved.
      if (isUnresolved(state)) return state
      return { phase: 'submitting', snapshot: event.snapshot, resends: 0, unresolvedSince: null }
    case 'RESEND':
      if (state.phase !== 'unknown') return state
      return {
        phase: 'submitting',
        snapshot: state.snapshot,
        resends: state.resends + 1,
        unresolvedSince: state.unknown,
      }
    case 'RESET':
      // An unresolved snapshot is never discarded by the screen.
      return isUnresolved(state) ? state : initialFactAttempt
    case 'SETTLE': {
      if (state.phase !== 'submitting') return state
      // A late answer for another snapshot settles nothing.
      if (state.snapshot.idempotencyKey !== event.idempotencyKey) return state
      const { outcome } = event
      if (outcome.type === 'applied') {
        return { phase: 'applied', snapshot: state.snapshot, replayed: outcome.replayed }
      }
      if (outcome.type === 'unknown') {
        return {
          phase: 'unknown',
          snapshot: state.snapshot,
          unknown: outcome.unknown,
          resends: state.resends,
          lastRefusal: null,
        }
      }
      const gate =
        outcome.failure.kind === 'unauthenticated' || outcome.failure.kind === 'forbidden'
      if (state.unresolvedSince !== null && gate) {
        return {
          phase: 'unknown',
          snapshot: state.snapshot,
          unknown: state.unresolvedSince,
          resends: state.resends,
          lastRefusal: outcome.failure,
        }
      }
      return {
        phase: 'failed',
        snapshot: state.snapshot,
        failure: outcome.failure,
        replayed: outcome.replayed,
      }
    }
  }
}
