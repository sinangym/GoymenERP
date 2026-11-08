/**
 * API-36: the finalize / Finance re-send attempt (one idempotent attempt per snapshot, `orderAttempt`), the rule that offers a
 * control (server state only) and the notice text — kept apart from the component so `SalesInvoiceFinalization.tsx` exports
 * components only.
 */
import { useEffect, useRef, useState } from 'react'
import { SALES_PROBLEM_CODE } from './contract'
import { sendInvoiceFinalization } from './invoiceApi'
import {
  FINANCE_STAGE,
  INVOICE_FINALIZATION_KIND,
  financeStage,
  type InvoiceFinalizationKind,
  type InvoiceFinalizationRequest,
  type SalesInvoiceDetail,
} from './invoiceFinalizationContract'
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
import { INVOICE_FINALIZATION_TEXT as T } from './invoiceText'
import { problemCodeText } from './salesText'
import type { ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'

export type InvoiceFinalizationAttempt = OrderAttemptState<InvoiceFinalizationRequest, SalesInvoiceDetail>

const scopeOf = (invoiceId: string) => `sales.invoice.finalization:${invoiceId}`
const pathOf = (kind: InvoiceFinalizationKind, invoiceId: string) => `${invoiceId}/${kind}`
const kindOf = (snapshot: OrderAttemptSnapshot<InvoiceFinalizationRequest>): InvoiceFinalizationKind =>
  snapshot.path.endsWith(`/${INVOICE_FINALIZATION_KIND.resend}`) ? INVOICE_FINALIZATION_KIND.resend : INVOICE_FINALIZATION_KIND.finalize

/**
 * The command the SERVER's state allows, or null: finalize an approved invoice that has no finalization yet; re-send a
 * finalized invoice Finance has not accepted. The screen does not judge the match, the difference or the plan — the server does.
 */
export function offeredInvoiceCommand(detail: SalesInvoiceDetail): InvoiceFinalizationKind | null {
  if (detail.state === 'Approved' && detail.finalization === null) return INVOICE_FINALIZATION_KIND.finalize
  if (detail.state === 'Finalized' && detail.finalization !== null && financeStage(detail.finalization) !== FINANCE_STAGE.accepted) {
    return INVOICE_FINALIZATION_KIND.resend
  }
  return null
}

export interface InvoiceFinalizationController {
  readonly attempt: InvoiceFinalizationAttempt
  readonly submit: (kind: InvoiceFinalizationKind, detail: SalesInvoiceDetail) => void
  readonly retry: () => void
  readonly leave: () => void
}

export function useInvoiceFinalization({
  shell,
  invoiceId,
  companyId,
  onRecorded,
}: {
  readonly shell: ShellState
  readonly invoiceId: string
  readonly companyId: string | null
  readonly onRecorded: () => void
}): InvoiceFinalizationController {
  const boundary = useAuthBoundary()
  const [attempt, setAttempt] = useState<InvoiceFinalizationAttempt>({ phase: 'idle' })
  const attemptRef = useRef(attempt)
  // The shell as it is NOW: an answer that lands after the actor, session or company changed is dropped by `applyOutcome`.
  const shellRef = useRef(shell)

  useEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  useEffect(() => {
    shellRef.current = shell
  }, [shell])

  function commit(next: InvoiceFinalizationAttempt) {
    attemptRef.current = next
    setAttempt(next)
  }

  async function send(snapshot: OrderAttemptSnapshot<InvoiceFinalizationRequest>) {
    const previous = attemptRef.current
    const inFlight = beginSend<InvoiceFinalizationRequest, SalesInvoiceDetail>(snapshot)
    commit(inFlight)
    const security = boundary.capture()
    const outcome = await sendInvoiceFinalization(kindOf(snapshot), snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      invoiceId,
    })
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    // `applyOutcome` drops an answer for another attempt or another actor/session/company binding.
    const next = applyOutcome(inFlight, snapshot, orderAttemptBinding(shellRef.current, scopeOf(invoiceId)), outcome, previous)
    if (next === inFlight) return
    commit(next)
    // A recorded command, or a conflict (the version or the state moved): read the invoice again.
    if (next.phase === 'completed' || (next.phase === 'failed' && next.failure === 'conflict')) onRecorded()
  }

  function submit(kind: InvoiceFinalizationKind, detail: SalesInvoiceDetail) {
    if (companyId === null || detail.id.toLowerCase() !== invoiceId.toLowerCase()) return
    const binding = orderAttemptBinding(shell, scopeOf(invoiceId))
    if (binding === null) return
    let current = attemptRef.current
    if (current.phase === 'submitting' || isOutcomeStillUnknown(current)) return
    // A finished attempt (recorded or definitely refused) is closed before the next command starts.
    if (current.phase === 'completed' || current.phase === 'failed') {
      current = { phase: 'idle' }
      commit(current)
    }
    const decision = decideSubmit(
      current,
      { path: pathOf(kind, invoiceId), binding, companyId, payload: { expectedVersion: detail.version } },
      generateOrderAttemptKey,
    )
    if (decision.type === 'send') void send(decision.snapshot)
  }

  function retry() {
    const current = attemptRef.current
    const snapshot = liveSnapshot(current)
    if (snapshot !== null && current.phase !== 'submitting') void send(snapshot)
  }

  function leave() {
    commit(abandon(attemptRef.current))
  }

  return { attempt, submit, retry, leave }
}

const UNKNOWN_TEXT = {
  offline: T.unknownOffline,
  'network-error': T.unknownNetwork,
  'service-unavailable': T.unknownServiceUnavailable,
  'malformed-response': T.unknownMalformed,
} as const

export function invoiceFinalizationNotice(attempt: InvoiceFinalizationAttempt): string | null {
  switch (attempt.phase) {
    case 'idle': return null
    case 'submitting':
      return kindOf(attempt.snapshot) === INVOICE_FINALIZATION_KIND.resend ? T.submittingResend : T.submittingFinalize
    case 'unknown': return UNKNOWN_TEXT[attempt.reason]
    case 'abandoned': return T.abandoned
    case 'completed':
      if (attempt.result.isReplay) return T.replayed
      return kindOf(attempt.snapshot) === INVOICE_FINALIZATION_KIND.resend ? T.completedResend : T.completedFinalize
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
