/**
 * The period command of one screen: the attempt reducer, the memory-only survival slot and the
 * single send path, shared by the period list (approve / close) and the proposal form.
 *
 * Rules (./periodAttempt.ts): one frozen snapshot at a time; an unknown outcome is kept and can only
 * be re-sent unchanged; a settled snapshot releases exactly its own slot; after a settled answer the
 * caller RE-READS the list — nothing is predicted from a command body (a replay is history).
 */

import { useCallback, useEffect, useReducer } from 'react'
import { sendPeriodCommand } from './commandApi'
import type { PeriodCommandSnapshot } from './contract'
import {
  initialPeriodAttempt,
  isUnresolved,
  periodAttemptReducer,
  restoredPeriodAttempt,
  type PeriodAttempt,
} from './periodAttempt'
import {
  recallPendingPeriodCommand,
  releasePendingPeriodCommand,
  reservePendingPeriodCommand,
} from './pendingPeriodCommands'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellDispatch } from '../shell/shellContext'

function restored(storeKey: string | null): PeriodAttempt {
  const pending = recallPendingPeriodCommand(storeKey)
  return pending === null ? initialPeriodAttempt : restoredPeriodAttempt(pending.snapshot, pending.resends)
}

export interface PeriodCommand {
  readonly attempt: PeriodAttempt
  readonly unresolved: boolean
  readonly submit: (snapshot: PeriodCommandSnapshot) => void
  readonly resend: () => void
  readonly reset: () => void
}

export function usePeriodCommand(
  storeKey: string | null,
  companyId: string,
  dirtyScope: string,
  /** Called after every settled answer (applied or a definite refusal): the caller re-reads. */
  onSettled: () => Promise<void> | void,
): PeriodCommand {
  const boundary = useAuthBoundary()
  const shellDispatch = useShellDispatch()
  const [attempt, dispatch] = useReducer(periodAttemptReducer, storeKey, restored)
  const unresolved = isUnresolved(attempt)

  // A company switch with an unsettled command warns first.
  useEffect(() => {
    shellDispatch({ type: unresolved ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: dirtyScope })
  }, [dirtyScope, shellDispatch, unresolved])
  useEffect(() => () => shellDispatch({ type: 'DIRTY_CLEAR', scope: dirtyScope }), [dirtyScope, shellDispatch])

  // Settled snapshots release exactly their own slot; an unknown one keeps it.
  useEffect(() => {
    if (attempt.phase === 'applied' || attempt.phase === 'failed') {
      releasePendingPeriodCommand(storeKey, attempt.snapshot.idempotencyKey)
    } else if (attempt.phase === 'unknown') {
      reservePendingPeriodCommand(storeKey, { snapshot: attempt.snapshot, resends: attempt.resends })
    }
  }, [attempt, storeKey])

  const send = useCallback(
    async (snapshot: PeriodCommandSnapshot, resends: number) => {
      // RESERVE FIRST, synchronously, before the request can be in flight.
      reservePendingPeriodCommand(storeKey, { snapshot, resends })
      const security = boundary.capture()
      const outcome = await sendPeriodCommand(companyId, snapshot)
      if (outcome.type === 'failed') {
        if (outcome.failure.kind === 'unauthenticated') boundary.report(401, security)
        if (outcome.failure.kind === 'forbidden') boundary.report(403, security)
      }
      dispatch({ type: 'SETTLE', outcome, idempotencyKey: snapshot.idempotencyKey })
      if (outcome.type !== 'unknown') await onSettled()
    },
    [boundary, companyId, onSettled, storeKey],
  )

  const submit = useCallback(
    (snapshot: PeriodCommandSnapshot) => {
      if (unresolved) return
      dispatch({ type: 'SUBMIT', snapshot })
      void send(snapshot, 0)
    },
    [send, unresolved],
  )

  const resend = useCallback(() => {
    if (attempt.phase !== 'unknown') return
    const { snapshot, resends } = attempt
    dispatch({ type: 'RESEND' })
    void send(snapshot, resends + 1)
  }, [attempt, send])

  const reset = useCallback(() => dispatch({ type: 'RESET' }), [])

  return { attempt, unresolved, submit, resend, reset }
}

/** A fresh v4 id for a key or a period id, minted once per snapshot. */
export function newId(): string {
  const webCrypto = globalThis.crypto
  if (typeof webCrypto?.randomUUID === 'function') return webCrypto.randomUUID()
  // Fallback only where randomUUID is missing; still a v4-shaped id the server accepts.
  const hex = Array.from({ length: 32 }, () => Math.floor(Math.random() * 16).toString(16))
  hex[12] = '4'
  hex[16] = ((parseInt(hex[16], 16) & 0x3) | 0x8).toString(16)
  const s = hex.join('')
  return `${s.slice(0, 8)}-${s.slice(8, 12)}-${s.slice(12, 16)}-${s.slice(16, 20)}-${s.slice(20)}`
}
