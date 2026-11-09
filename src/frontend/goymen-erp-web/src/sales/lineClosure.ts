/**
 * SAT-D17: the line-closure attempt (one idempotent attempt per line snapshot, `orderAttempt`), the closable-line rule (server
 * facts only) and the notice text — kept apart from the component so `SalesLineClosure.tsx` exports components only.
 */
import { useEffect, useRef, useState } from 'react'
import { sendDraftLineClosure } from './api'
import { SALES_PROBLEM_CODE, type DraftDetail, type DraftLineClosureRequest, type DraftMutation } from './contract'
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
} from './orderAttempt'
import { STATUS_AXIS_STATE, type LineRealization } from './orderStatus'
import { LINE_CLOSURE_TEXT as T, problemCodeText } from './salesText'
import type { ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'

export type LineClosureAttempt = OrderAttemptState<DraftLineClosureRequest, DraftMutation>

const scopeOf = (draftId: string) => `sales.draft.line-closure:${draftId}`

/** A line the server answers as closable short: under-delivered, shipped, not closed yet, measured (never NotStarted). */
export function isLineClosable(line: LineRealization): boolean {
  return line.direction === 'Under' && line.shipped !== null && line.deviation !== null && line.closedRemainder === null
    && (line.state === STATUS_AXIS_STATE.partial || line.state === STATUS_AXIS_STATE.completed)
}

export interface LineClosureController {
  readonly attempt: LineClosureAttempt
  /** The line whose confirmation is open, or null. */
  readonly selectedLineId: string | null
  readonly open: (lineId: string) => void
  readonly cancel: () => void
  readonly confirm: (line: LineRealization) => void
  readonly retry: () => void
  readonly leave: () => void
}

export function useLineClosure({
  shell,
  detail,
  companyId,
  onRecorded,
}: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly companyId: string | null
  readonly onRecorded: () => void
}): LineClosureController {
  const boundary = useAuthBoundary()
  const [attempt, setAttempt] = useState<LineClosureAttempt>({ phase: 'idle' })
  const [selectedLineId, setSelectedLineId] = useState<string | null>(null)
  const attemptRef = useRef(attempt)

  useEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  function commit(next: LineClosureAttempt) {
    attemptRef.current = next
    setAttempt(next)
  }

  async function send(snapshot: OrderAttemptSnapshot<DraftLineClosureRequest>, draftId: string) {
    const previous = attemptRef.current
    const inFlight = beginSend<DraftLineClosureRequest, DraftMutation>(snapshot)
    commit(inFlight)
    const security = boundary.capture()
    const outcome = await sendDraftLineClosure(snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      draftId,
    })
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    // `applyOutcome` drops an answer for another attempt or another actor/session/company binding.
    const next = applyOutcome(inFlight, snapshot, orderAttemptBinding(shell, scopeOf(draftId)), outcome, previous)
    if (next === inFlight) return
    commit(next)
    if (next.phase === 'completed') setSelectedLineId(null)
    // A recorded closure, or a conflict (the order, the shipped total, the shipper or the commitments moved): read again.
    if (next.phase === 'completed' || (next.phase === 'failed' && next.failure === 'conflict')) onRecorded()
  }

  function open(lineId: string) {
    const current = attemptRef.current
    if (current.phase === 'submitting' || isOutcomeStillUnknown(current)) return
    // A finished attempt (recorded or definitely refused) is closed before another line is offered.
    if (current.phase === 'completed' || current.phase === 'failed') commit({ phase: 'idle' })
    setSelectedLineId(lineId)
  }

  function cancel() {
    if (attemptRef.current.phase === 'submitting') return
    setSelectedLineId(null)
  }

  function confirm(line: LineRealization) {
    if (companyId === null || line.shipped === null) return
    const binding = orderAttemptBinding(shell, scopeOf(detail.draft.id))
    if (binding === null) return
    const payload: DraftLineClosureRequest = { expectedVersion: detail.draft.version, lineId: line.lineId, shipped: line.shipped }
    const decision = decideSubmit(
      attemptRef.current,
      { path: `${detail.draft.id}/line-closure/${line.lineId}`, binding, companyId, payload },
      generateOrderAttemptKey,
    )
    if (decision.type === 'send') void send(decision.snapshot, detail.draft.id)
  }

  function retry() {
    const current = attemptRef.current
    const snapshot = liveSnapshot(current)
    if (snapshot !== null && current.phase !== 'submitting') void send(snapshot, detail.draft.id)
  }

  function leave() {
    commit(abandon(attemptRef.current))
    setSelectedLineId(null)
  }

  return { attempt, selectedLineId, open, cancel, confirm, retry, leave }
}

const UNKNOWN_TEXT = {
  offline: T.unknownOffline,
  'network-error': T.unknownNetwork,
  'service-unavailable': T.unknownServiceUnavailable,
  'malformed-response': T.unknownMalformed,
} as const

export function lineClosureNotice(attempt: LineClosureAttempt): string | null {
  switch (attempt.phase) {
    case 'idle': return null
    case 'submitting': return T.submitting
    case 'unknown': return UNKNOWN_TEXT[attempt.reason]
    case 'abandoned': return T.abandoned
    case 'completed': return attempt.result.isReplay ? T.replayed : T.completed
    case 'failed': {
      const code = attempt.problem?.code ?? null
      const codeText = code === SALES_PROBLEM_CODE.versionConflict ? T.versionConflict
        : code === SALES_PROBLEM_CODE.stateConflict ? T.stateConflict
          : problemCodeText(code)
      const base = attempt.failure === 'unauthenticated' ? T.unauthenticated
        : attempt.failure === 'forbidden' ? T.forbidden
          : attempt.failure === 'not-found' ? T.notFound
            : attempt.failure === 'conflict' ? T.conflict
              : attempt.failure === 'validation' ? T.validation
                : T.unexpected
      const text = codeText === null ? base : `${base} ${codeText}`
      // A refusal that answered a retry of an unknown outcome says nothing about the first send.
      return attempt.unresolvedSince === undefined ? text : `${text} ${T.unknown}`
    }
    default: return null
  }
}
