/**
 * UI-62 / UI-64 (API-32 / API-33): the invoice draft create attempt on S-08 and the draft's submit / approve / reject / cancel
 * attempt, both on the Sales attempt machine (./orderAttempt.ts) — one Idempotency-Key per submitted snapshot, an unknown
 * outcome keeps the key and offers only "retry the same" or "leave it". The rule that offers a command reads the server's state
 * only; who may approve (never the preparer, SAT-D88 / SAT-D204) is the server's answer, never the screen's guess. Kept apart
 * from the components so they export components only.
 */
import { useEffect, useRef, useState } from 'react'
import { SALES_PROBLEM_CODE, type SalesProblem } from './contract'
import {
  INVOICE_DRAFT_COMMAND,
  createSalesInvoice,
  sendInvoiceDraftCommand,
  type CreateSalesInvoicePayload,
  type InvoiceDraftCommand,
  type InvoiceDraftCommandPayload,
} from './invoiceDraftApi'
import type { InvoiceCandidateShare } from './dispatchContract'
import type { SalesInvoiceDetail } from './invoiceFinalizationContract'
import { INVOICE_DRAFT_ACTIONS_TEXT as A, INVOICE_DRAFT_CREATE_TEXT as CT, invoiceNotReadyReasonText } from './invoiceDraftText'
import { INVOICE_FINALIZATION_TEXT as T } from './invoiceText'
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
import { usePoolAttempt, type PoolAttempt } from './poolAttempt'
import { problemCodeText } from './salesText'
import type { ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'

/** API-32 409: the shares cannot be invoiced together now (not recorded under the key; the same key asks again). */
export const INVOICE_NOT_READY_CODE = 'invoice_not_ready'
/** API-33 409: the preparer or the submitter tried to approve or reject (recorded under the key). */
export const INVOICE_SELF_APPROVAL_CODE = 'sales_invoice_self_approval'

/* ── UI-64: the draft's state commands ─────────────────────────────────────── */

export type InvoiceDraftAttempt = OrderAttemptState<InvoiceDraftCommandPayload, SalesInvoiceDetail>

const commandScopeOf = (invoiceId: string) => `sales.invoice.draft:${invoiceId}`
const commandPathOf = (kind: InvoiceDraftCommand, invoiceId: string) => `${invoiceId}/${kind}`
const COMMANDS: readonly InvoiceDraftCommand[] = [
  INVOICE_DRAFT_COMMAND.submit,
  INVOICE_DRAFT_COMMAND.approve,
  INVOICE_DRAFT_COMMAND.reject,
  INVOICE_DRAFT_COMMAND.cancel,
]
export const draftCommandOf = (snapshot: OrderAttemptSnapshot<InvoiceDraftCommandPayload>): InvoiceDraftCommand =>
  COMMANDS.find((kind) => snapshot.path.endsWith(`/${kind}`)) ?? INVOICE_DRAFT_COMMAND.submit

/**
 * The commands the SERVER's state allows: a draft is submitted or cancelled; a draft pending approval is approved, rejected
 * or cancelled. An approved invoice's cancellation (SAT-D96) is not this slice; nothing else is offered.
 */
export function offeredDraftCommands(detail: SalesInvoiceDetail): readonly InvoiceDraftCommand[] {
  if (detail.state === 'Draft') return [INVOICE_DRAFT_COMMAND.submit, INVOICE_DRAFT_COMMAND.cancel]
  if (detail.state === 'PendingApproval') {
    return [INVOICE_DRAFT_COMMAND.approve, INVOICE_DRAFT_COMMAND.reject, INVOICE_DRAFT_COMMAND.cancel]
  }
  return []
}

export interface InvoiceDraftCommandController {
  readonly attempt: InvoiceDraftAttempt
  readonly submit: (kind: InvoiceDraftCommand, detail: SalesInvoiceDetail, reason: string | null) => void
  readonly retry: () => void
  readonly leave: () => void
}

export function useInvoiceDraftCommand({
  shell,
  invoiceId,
  companyId,
  onRecorded,
}: {
  readonly shell: ShellState
  readonly invoiceId: string
  readonly companyId: string | null
  readonly onRecorded: () => void
}): InvoiceDraftCommandController {
  const boundary = useAuthBoundary()
  const [attempt, setAttempt] = useState<InvoiceDraftAttempt>({ phase: 'idle' })
  const attemptRef = useRef(attempt)
  // The shell as it is NOW: an answer that lands after the actor, session or company changed is dropped by `applyOutcome`.
  const shellRef = useRef(shell)

  useEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  useEffect(() => {
    shellRef.current = shell
  }, [shell])

  function commit(next: InvoiceDraftAttempt) {
    attemptRef.current = next
    setAttempt(next)
  }

  async function send(snapshot: OrderAttemptSnapshot<InvoiceDraftCommandPayload>) {
    const previous = attemptRef.current
    const inFlight = beginSend<InvoiceDraftCommandPayload, SalesInvoiceDetail>(snapshot)
    commit(inFlight)
    const security = boundary.capture()
    const outcome = await sendInvoiceDraftCommand(draftCommandOf(snapshot), snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      invoiceId,
    })
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    const next = applyOutcome(inFlight, snapshot, orderAttemptBinding(shellRef.current, commandScopeOf(invoiceId)), outcome, previous)
    if (next === inFlight) return
    commit(next)
    // A recorded command, or a conflict (the version, the state or the approver moved): read the invoice again.
    if (next.phase === 'completed' || (next.phase === 'failed' && next.failure === 'conflict')) onRecorded()
  }

  function submit(kind: InvoiceDraftCommand, detail: SalesInvoiceDetail, reason: string | null) {
    if (companyId === null || detail.id.toLowerCase() !== invoiceId.toLowerCase()) return
    const binding = orderAttemptBinding(shell, commandScopeOf(invoiceId))
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
      { path: commandPathOf(kind, invoiceId), binding, companyId, payload: { expectedVersion: detail.version, reason } },
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

function failureBase(failure: string): string {
  switch (failure) {
    case 'unauthenticated': return T.unauthenticated
    case 'forbidden': return T.forbidden
    case 'not-found': return T.notFound
    case 'conflict': return T.conflict
    case 'validation': return T.validation
    default: return T.unexpected
  }
}

function commandCodeText(code: string | null): string | null {
  switch (code) {
    case INVOICE_SELF_APPROVAL_CODE: return A.selfApproval
    case SALES_PROBLEM_CODE.concurrentChange: return A.concurrentChange
    case SALES_PROBLEM_CODE.sameKeyPayloadMismatch: return A.sameKeyPayloadMismatch
    case SALES_PROBLEM_CODE.approvalReasonTooLong: return A.reasonTooLongServer
    case SALES_PROBLEM_CODE.versionConflict: return T.versionConflict
    case SALES_PROBLEM_CODE.stateConflict: return T.stateConflict
    default: return problemCodeText(code)
  }
}

export function invoiceDraftCommandNotice(attempt: InvoiceDraftAttempt): string | null {
  switch (attempt.phase) {
    case 'idle': return null
    case 'submitting': return A.submitting[draftCommandOf(attempt.snapshot)]
    case 'unknown': return UNKNOWN_TEXT[attempt.reason]
    case 'abandoned': return T.abandoned
    case 'completed':
      return attempt.result.isReplay ? A.replayed : A.completed[draftCommandOf(attempt.snapshot)]
    case 'failed': {
      const codeText = commandCodeText(attempt.problem?.code ?? null)
      const base = failureBase(attempt.failure)
      const text = codeText === null ? base : `${base} ${codeText}`
      // A refusal that answered a retry of an unknown outcome says nothing about the first send.
      return attempt.unresolvedSince === undefined ? text : `${text} ${T.unknown}`
    }
    default: return null
  }
}

/* ── UI-62: the draft create on S-08 ───────────────────────────────────────── */

export type InvoiceDraftCreateAttempt = PoolAttempt<CreateSalesInvoicePayload, SalesInvoiceDetail>

export const invoiceDraftCreateScope = (groupIdentity: string) => `sales.invoice.create:${groupIdentity}`

/** How S-08 names a share: the dispatch note's system number and line, as the candidate list shows it. */
export const invoiceShareLabel = (share: InvoiceCandidateShare) => `${share.dispatchSystemNumber} / ${share.dispatchLineNo}`

/**
 * The create attempt of one candidate group. The attempt and the selection it was sent from outlive the screen
 * (./pendingPoolAttempts.ts), so an unknown outcome is never resent under a new key. 401/403 go to the auth boundary.
 */
export function useInvoiceDraftCreate(
  groupIdentity: string,
  selected: readonly string[],
  onSettled: (next: OrderAttemptState<CreateSalesInvoicePayload, SalesInvoiceDetail>) => void,
): InvoiceDraftCreateAttempt {
  const boundary = useAuthBoundary()
  return usePoolAttempt<CreateSalesInvoicePayload, SalesInvoiceDetail>(
    invoiceDraftCreateScope(groupIdentity),
    async (snapshot) => {
      const security = boundary.capture()
      const outcome = await createSalesInvoice(snapshot.payload, { idempotencyKey: snapshot.key, companyId: snapshot.companyId })
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
      return outcome
    },
    onSettled,
    { values: selected },
  )
}

export interface InvoiceDraftCreateNotice {
  readonly message: string
  readonly role: 'alert' | 'status'
  /** API-32 409 `invoice_not_ready`: the server's reasons, one per share and code, verbatim and in the server's order. */
  readonly reasons: readonly string[]
}

const SHARE_FIELD = /^shares\[(\d+)\]/

/** The server's `invoice_not_ready` field errors as "share: reason"; the share is named by the index of the sent body. */
export function invoiceNotReadyItems(
  problem: SalesProblem | null,
  sentShipmentIds: readonly string[],
  shareLabel: (shipmentId: string) => string | null,
): readonly string[] {
  if (problem === null) return []
  return problem.errors.map((error) => {
    const match = SHARE_FIELD.exec(error.field)
    const shipmentId = match === null ? undefined : sentShipmentIds[Number(match[1])]
    const label = shipmentId === undefined ? null : shareLabel(shipmentId)
    return CT.notReadyItem(label ?? CT.unknownShare, invoiceNotReadyReasonText(error.code))
  })
}

export function invoiceDraftCreateNotice(
  state: OrderAttemptState<CreateSalesInvoicePayload, SalesInvoiceDetail>,
  blockedChange: boolean,
  shareLabel: (shipmentId: string) => string | null,
): InvoiceDraftCreateNotice | null {
  switch (state.phase) {
    case 'idle': return null
    case 'submitting': return { message: CT.submitting, role: 'status', reasons: [] }
    case 'unknown': {
      const base = UNKNOWN_TEXT[state.reason]
      return { message: blockedChange ? `${base} ${CT.editAfterUnknown}` : base, role: 'alert', reasons: [] }
    }
    case 'abandoned': return { message: T.abandoned, role: 'status', reasons: [] }
    case 'completed':
      return {
        message: state.result.isReplay ? CT.replayed(state.result.documentNo) : CT.completed(state.result.documentNo),
        role: 'status',
        reasons: [],
      }
    case 'failed': {
      const code = state.problem?.code ?? null
      let text: string
      let reasons: readonly string[] = []
      if (state.failure === 'conflict' && code === INVOICE_NOT_READY_CODE) {
        text = CT.notReady
        reasons = invoiceNotReadyItems(state.problem, state.snapshot.payload.shipmentIds, shareLabel)
      } else if (state.failure === 'conflict') {
        const codeText = code === SALES_PROBLEM_CODE.sameKeyPayloadMismatch ? A.sameKeyPayloadMismatch : null
        text = codeText === null ? CT.createConflict : `${CT.createConflict} ${codeText}`
      } else {
        const codeText = problemCodeText(code)
        const base = failureBase(state.failure)
        text = codeText === null ? base : `${base} ${codeText}`
      }
      if (state.unresolvedSince !== undefined) text = `${text} ${T.unknown}`
      if (blockedChange) text = `${text} ${CT.editAfterUnknown}`
      return { message: text, role: 'alert', reasons }
    }
    default: return null
  }
}
