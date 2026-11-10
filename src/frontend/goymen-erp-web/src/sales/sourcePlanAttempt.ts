/**
 * API-11 (SAT-D252..SAT-D255): the source-plan save attempt (one idempotent attempt per plan snapshot, `orderAttempt`) and its
 * notice text — kept apart from the component so `SalesDraftSourcePlan.tsx` exports components only.
 *
 * An unknown outcome keeps its key and a retry resends the same snapshot. The attempt lives in the panel, which the detail page
 * keys WITHOUT the draft version, so a recorded plan (a version step) keeps the notice. Leaving the tab drops the attempt; a
 * later save then names the version it read, and a plan the lost answer did record makes that save a 409 version conflict —
 * never a second plan on the old version.
 */
import { useEffect, useRef, useState } from 'react'
import { sendDraftSourcePlan } from './api'
import { SALES_PROBLEM_CODE, type DraftMutation } from './contract'
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
import { SOURCE_PLAN_TEXT as T, problemCodeText } from './salesText'
import type { SourcePlanRequest } from './sourcePlan'
import type { ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'

export type SourcePlanAttempt = OrderAttemptState<SourcePlanRequest, DraftMutation>

const scopeOf = (draftId: string) => `sales.draft.source-plan:${draftId}`

export interface SourcePlanController {
  readonly attempt: SourcePlanAttempt
  /** Sends `payload` (a new key, or the unknown attempt's key when it is the same snapshot); false when nothing was sent. */
  readonly save: (payload: SourcePlanRequest) => boolean
  readonly retry: () => void
  readonly leave: () => void
}

export function useSourcePlanAttempt({
  shell,
  draftId,
  companyId,
  onRecorded,
}: {
  readonly shell: ShellState
  readonly draftId: string
  readonly companyId: string
  /** A recorded plan (`recorded` true) or a conflict (false): the detail and the plan are read again. */
  readonly onRecorded: (recorded: boolean) => void
}): SourcePlanController {
  const boundary = useAuthBoundary()
  const [attempt, setAttempt] = useState<SourcePlanAttempt>({ phase: 'idle' })
  const attemptRef = useRef(attempt)

  useEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  function commit(next: SourcePlanAttempt) {
    attemptRef.current = next
    setAttempt(next)
  }

  async function send(snapshot: OrderAttemptSnapshot<SourcePlanRequest>) {
    const previous = attemptRef.current
    const inFlight = beginSend<SourcePlanRequest, DraftMutation>(snapshot)
    commit(inFlight)
    const security = boundary.capture()
    const outcome = await sendDraftSourcePlan(snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      draftId,
    })
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    // `applyOutcome` drops an answer for another attempt or another actor/session/company binding.
    const next = applyOutcome(inFlight, snapshot, orderAttemptBinding(shell, scopeOf(draftId)), outcome, previous)
    if (next === inFlight) return
    commit(next)
    if (next.phase === 'completed' || (next.phase === 'failed' && next.failure === 'conflict')) onRecorded(next.phase === 'completed')
  }

  function save(payload: SourcePlanRequest): boolean {
    const binding = orderAttemptBinding(shell, scopeOf(draftId))
    if (binding === null) return false
    let current = attemptRef.current
    // A finished attempt (recorded or definitely refused) is closed before another plan is sent.
    if (current.phase === 'completed' || current.phase === 'failed') {
      current = { phase: 'idle' }
      commit(current)
    }
    const decision = decideSubmit(current, { path: `${draftId}/source-plan`, binding, companyId, payload }, generateOrderAttemptKey)
    if (decision.type !== 'send') return false
    void send(decision.snapshot)
    return true
  }

  function retry() {
    const current = attemptRef.current
    const snapshot = liveSnapshot(current)
    if (snapshot !== null && current.phase !== 'submitting') void send(snapshot)
  }

  function leave() {
    commit(abandon(attemptRef.current))
  }

  return { attempt, save, retry, leave }
}

export const sourcePlanFrozen = (attempt: SourcePlanAttempt): boolean =>
  attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)

const UNKNOWN_TEXT = {
  offline: T.unknownOffline,
  'network-error': T.unknownNetwork,
  'service-unavailable': T.unknownServiceUnavailable,
  'malformed-response': T.unknownMalformed,
} as const

export function sourcePlanNotice(attempt: SourcePlanAttempt): string | null {
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
