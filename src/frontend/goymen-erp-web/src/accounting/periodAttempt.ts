/**
 * The lifecycle of one period command (propose / approve / close).
 *
 *   idle ─SUBMIT─▶ submitting ─SETTLE─▶ applied | failed | unknown
 *   unknown ─RESEND─▶ submitting (same snapshot: same key, same period id, same version)
 *
 * THE RULE: only the server settles a snapshot. An UNKNOWN outcome keeps the snapshot, and the
 * only way forward is re-sending it unchanged; a fresh submit is refused while one is unresolved,
 * because a new key after a proposal that may already be stored would be a second attempt.
 *
 * WHICH ANSWERS SETTLE A RE-SENT SNAPSHOT. The server answers a known key from its record
 * (ADR-011), but it measures the caller's authority FIRST (§5). So a 401/403 on a re-send says
 * nothing about the earlier request and the snapshot stays unknown. Every other answer to a
 * re-send is about this key: a replay is the recorded answer, a fresh refusal means no record.
 * (The Manufacturing fact attempt's rules, ./../manufacturing/factAttempt.ts.)
 */

import type { PeriodCommandOutcome, PeriodFailure, PeriodUnknown } from './commandApi'
import type { PeriodCommandSnapshot } from './contract'

export type PeriodAttempt =
  | { readonly phase: 'idle' }
  | {
      readonly phase: 'submitting'
      readonly snapshot: PeriodCommandSnapshot
      readonly resends: number
      readonly unresolvedSince: PeriodUnknown | null
    }
  | { readonly phase: 'applied'; readonly snapshot: PeriodCommandSnapshot; readonly replayed: boolean }
  | {
      readonly phase: 'failed'
      readonly snapshot: PeriodCommandSnapshot
      readonly failure: PeriodFailure
      readonly replayed: boolean
    }
  | {
      readonly phase: 'unknown'
      readonly snapshot: PeriodCommandSnapshot
      readonly unknown: PeriodUnknown
      readonly resends: number
      /** A re-send turned away at the authority gate, shown beside the unknown. */
      readonly lastRefusal: PeriodFailure | null
    }

export type PeriodAttemptEvent =
  | { readonly type: 'SUBMIT'; readonly snapshot: PeriodCommandSnapshot }
  | { readonly type: 'RESEND' }
  | { readonly type: 'SETTLE'; readonly outcome: PeriodCommandOutcome; readonly idempotencyKey: string }
  | { readonly type: 'RESET' }

export const initialPeriodAttempt: PeriodAttempt = { phase: 'idle' }

export function isUnresolved(attempt: PeriodAttempt): boolean {
  return attempt.phase === 'submitting' || attempt.phase === 'unknown'
}

/** An unresolved attempt restored after a remount: its outcome is unknown until re-sent. */
export function restoredPeriodAttempt(snapshot: PeriodCommandSnapshot, resends: number): PeriodAttempt {
  return { phase: 'unknown', snapshot, unknown: { kind: 'aborted' }, resends, lastRefusal: null }
}

export function periodAttemptReducer(state: PeriodAttempt, event: PeriodAttemptEvent): PeriodAttempt {
  switch (event.type) {
    case 'SUBMIT':
      if (isUnresolved(state)) return state
      return { phase: 'submitting', snapshot: event.snapshot, resends: 0, unresolvedSince: null }
    case 'RESEND':
      if (state.phase !== 'unknown') return state
      return { phase: 'submitting', snapshot: state.snapshot, resends: state.resends + 1, unresolvedSince: state.unknown }
    case 'RESET':
      // An unresolved snapshot is never discarded by the screen.
      return isUnresolved(state) ? state : initialPeriodAttempt
    case 'SETTLE': {
      if (state.phase !== 'submitting') return state
      // A late answer for another snapshot settles nothing.
      if (state.snapshot.idempotencyKey !== event.idempotencyKey) return state
      const { outcome } = event
      if (outcome.type === 'applied') return { phase: 'applied', snapshot: state.snapshot, replayed: outcome.replayed }
      if (outcome.type === 'unknown') {
        return { phase: 'unknown', snapshot: state.snapshot, unknown: outcome.unknown, resends: state.resends, lastRefusal: null }
      }
      const gate = outcome.failure.kind === 'unauthenticated' || outcome.failure.kind === 'forbidden'
      if (state.unresolvedSince !== null && gate) {
        return {
          phase: 'unknown',
          snapshot: state.snapshot,
          unknown: state.unresolvedSince,
          resends: state.resends,
          lastRefusal: outcome.failure,
        }
      }
      return { phase: 'failed', snapshot: state.snapshot, failure: outcome.failure, replayed: outcome.replayed }
    }
  }
}
