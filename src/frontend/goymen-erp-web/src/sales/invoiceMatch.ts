/**
 * UI-64b / API-35: the official e-invoice match attempt (one idempotent attempt per snapshot, `orderAttempt`), the rule that
 * offers the form (server state only), the client SHAPE checks the contract states (number length, ETTN form, reason present when
 * approving) and the notice text — kept apart from the component so `SalesInvoiceMatchPanel.tsx` exports components only.
 *
 * The screen never judges the match: whether the numbers, the customer and the currency agree, how large the difference is and
 * whether it is within the tolerance (SAT-D89 / SAT-D229) are the server's answers. The difference-approval fields are offered
 * only after the server answered `difference_approval_required` for exactly this number and ETTN, and only to a caller the
 * shell lets approve (SAT-D106 / SAT-D204); the self-approval rule stays the server's.
 */
import { useEffect, useRef, useState } from 'react'
import { LIMITS } from './contract'
import { sendInvoiceMatch, type InvoiceMatchRequest } from './invoiceMatchApi'
import type { SalesInvoiceDetail } from './invoiceFinalizationContract'
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
import { INVOICE_FINALIZATION_TEXT as F } from './invoiceText'
import {
  INVOICE_MATCH_DIFFERENCE_FIELD_TEXT,
  INVOICE_MATCH_FIELD_TEXT,
  INVOICE_MATCH_PROBLEM_CODE as CODE,
  INVOICE_MATCH_PROBLEM_TEXT,
  INVOICE_MATCH_TEXT as T,
} from './invoiceMatchText'
import { problemCodeText } from './salesText'
import type { ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'

export type InvoiceMatchAttempt = OrderAttemptState<InvoiceMatchRequest, SalesInvoiceDetail>

/** `SalesInvoice.MaximumOfficialNoLength` (OpenAPI `MatchSalesInvoiceRequest`): a contract shape, not a policy. */
export const OFFICIAL_INVOICE_NO_MAX_LENGTH = 64
export const DIFFERENCE_REASON_MAX_LENGTH = LIMITS.note
/** The "D" GUID form the server parses (`Guid.TryParseExact(…, "D")`); the empty GUID is refused too. */
const ETTN_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

const scopeOf = (invoiceId: string) => `sales.invoice.match:${invoiceId}`
const pathOf = (invoiceId: string) => `${invoiceId}/external-match`

/** Refusals that are about the entered data: the form stays as typed and the invoice is not read again. */
const DATA_REFUSALS: ReadonlySet<string> = new Set([
  CODE.officialDocumentNotFound,
  CODE.officialMatchMismatch,
  CODE.differenceApprovalRequired,
])

/** The form is offered only for what the SERVER's state allows: an approved invoice with no match yet. */
export function offerMatch(detail: SalesInvoiceDetail): boolean {
  return detail.state === 'Approved' && detail.match === null
}

export interface InvoiceMatchForm {
  readonly officialInvoiceNo: string
  readonly ettn: string
  readonly approveDifference: boolean
  readonly differenceReason: string
}

export interface InvoiceMatchFormErrors {
  readonly officialInvoiceNo?: string
  readonly ettn?: string
  readonly differenceReason?: string
}

/** The number and ETTN as they will be sent: surrounding blanks dropped, the ETTN lower-cased (a GUID has no case). */
export function normalizedIdentity(form: Pick<InvoiceMatchForm, 'officialInvoiceNo' | 'ettn'>) {
  return { officialInvoiceNo: form.officialInvoiceNo.trim(), ettn: form.ettn.trim().toLowerCase() }
}

/** Contract-shape checks only. Returns the request body (without the version) or the field errors. */
export function validateMatchForm(
  form: InvoiceMatchForm,
  approvalOffered: boolean,
): { readonly ok: true; readonly body: Omit<InvoiceMatchRequest, 'expectedVersion'> } | { readonly ok: false; readonly errors: InvoiceMatchFormErrors } {
  const { officialInvoiceNo, ettn } = normalizedIdentity(form)
  const approving = approvalOffered && form.approveDifference
  const reason = form.differenceReason.trim()
  const errors: { officialInvoiceNo?: string; ettn?: string; differenceReason?: string } = {}
  if (officialInvoiceNo.length === 0 || officialInvoiceNo.length > OFFICIAL_INVOICE_NO_MAX_LENGTH) {
    errors.officialInvoiceNo = T.invalidOfficialNo(OFFICIAL_INVOICE_NO_MAX_LENGTH)
  }
  if (!ETTN_PATTERN.test(ettn) || ettn === EMPTY_GUID) errors.ettn = T.invalidEttn
  if (approving && (reason.length === 0 || reason.length > DIFFERENCE_REASON_MAX_LENGTH)) {
    errors.differenceReason = T.invalidReason(DIFFERENCE_REASON_MAX_LENGTH)
  }
  if (Object.keys(errors).length > 0) return { ok: false, errors }
  return {
    ok: true,
    body: { officialInvoiceNo, ettn, approveDifference: approving, differenceReason: approving ? reason : null },
  }
}

export interface InvoiceMatchController {
  readonly attempt: InvoiceMatchAttempt
  /** The number and ETTN the server last asked a difference approval for, or null. */
  readonly approvalAskedFor: { readonly officialInvoiceNo: string; readonly ettn: string } | null
  readonly submit: (detail: SalesInvoiceDetail, body: Omit<InvoiceMatchRequest, 'expectedVersion'>) => void
  readonly retry: () => void
  readonly leave: () => void
}

export function useInvoiceMatch({
  shell,
  invoiceId,
  companyId,
  onRecorded,
}: {
  readonly shell: ShellState
  readonly invoiceId: string
  readonly companyId: string | null
  readonly onRecorded: () => void
}): InvoiceMatchController {
  const boundary = useAuthBoundary()
  const [attempt, setAttempt] = useState<InvoiceMatchAttempt>({ phase: 'idle' })
  const [approvalAskedFor, setApprovalAskedFor] = useState<InvoiceMatchController['approvalAskedFor']>(null)
  const attemptRef = useRef(attempt)
  // The shell as it is NOW: an answer that lands after the actor, session or company changed is dropped by `applyOutcome`.
  const shellRef = useRef(shell)

  useEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  useEffect(() => {
    shellRef.current = shell
  }, [shell])

  function commit(next: InvoiceMatchAttempt) {
    attemptRef.current = next
    setAttempt(next)
  }

  async function send(snapshot: OrderAttemptSnapshot<InvoiceMatchRequest>) {
    const previous = attemptRef.current
    const inFlight = beginSend<InvoiceMatchRequest, SalesInvoiceDetail>(snapshot)
    commit(inFlight)
    const security = boundary.capture()
    const outcome = await sendInvoiceMatch(snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      invoiceId,
    })
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    const next = applyOutcome(inFlight, snapshot, orderAttemptBinding(shellRef.current, scopeOf(invoiceId)), outcome, previous)
    if (next === inFlight) return
    commit(next)
    if (next.phase === 'failed' && next.problem?.code === CODE.differenceApprovalRequired) {
      setApprovalAskedFor({ officialInvoiceNo: snapshot.payload.officialInvoiceNo, ettn: snapshot.payload.ettn })
    }
    // A recorded match, or a conflict that is not about the typed data (the version or the state moved): read again.
    if (
      next.phase === 'completed' ||
      (next.phase === 'failed' && next.failure === 'conflict' && !DATA_REFUSALS.has(next.problem?.code ?? ''))
    ) {
      onRecorded()
    }
  }

  function submit(detail: SalesInvoiceDetail, body: Omit<InvoiceMatchRequest, 'expectedVersion'>) {
    if (companyId === null || detail.id.toLowerCase() !== invoiceId.toLowerCase() || !offerMatch(detail)) return
    const binding = orderAttemptBinding(shell, scopeOf(invoiceId))
    if (binding === null) return
    let current = attemptRef.current
    if (current.phase === 'submitting' || isOutcomeStillUnknown(current)) return
    // A finished attempt (recorded or definitely refused) is closed before the next one starts.
    if (current.phase === 'completed' || current.phase === 'failed') {
      current = { phase: 'idle' }
      commit(current)
    }
    const decision = decideSubmit(
      current,
      { path: pathOf(invoiceId), binding, companyId, payload: { expectedVersion: detail.version, ...body } },
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

  return { attempt, approvalAskedFor, submit, retry, leave }
}

const UNKNOWN_TEXT = {
  offline: F.unknownOffline,
  'network-error': F.unknownNetwork,
  'service-unavailable': T.unknownServiceUnavailable,
  'malformed-response': F.unknownMalformed,
} as const

function codeText(code: string | null): string | null {
  if (code === null) return null
  return Object.hasOwn(INVOICE_MATCH_PROBLEM_TEXT, code) ? INVOICE_MATCH_PROBLEM_TEXT[code] : problemCodeText(code)
}

export function invoiceMatchNotice(attempt: InvoiceMatchAttempt): string | null {
  switch (attempt.phase) {
    case 'idle': return null
    case 'submitting': return T.submitting
    case 'unknown': return UNKNOWN_TEXT[attempt.reason]
    case 'abandoned': return F.abandoned
    case 'completed': return attempt.result.isReplay ? T.replayed : T.completed
    case 'failed': {
      const code = attempt.problem?.code ?? null
      const base = attempt.failure === 'unauthenticated' ? F.unauthenticated
        : attempt.failure === 'forbidden' ? F.forbidden
          : attempt.failure === 'not-found' ? F.notFound
            : attempt.failure === 'conflict' ? (DATA_REFUSALS.has(code ?? '') ? T.refused : T.conflict)
              : attempt.failure === 'validation' ? F.validation
                : F.unexpected
      const parts: string[] = [base]
      const refinement = codeText(code)
      if (refinement !== null) parts.push(refinement)
      if (code === CODE.officialMatchMismatch) {
        const fields = (attempt.problem?.errors ?? [])
          .map((error) => (Object.hasOwn(INVOICE_MATCH_FIELD_TEXT, error.field) ? INVOICE_MATCH_FIELD_TEXT[error.field] : null))
          .filter((label): label is string => label !== null)
        if (fields.length > 0) parts.push(`${T.mismatchFields}: ${fields.join(', ')}.`)
      }
      if (code === CODE.differenceApprovalRequired) {
        const fields = (attempt.problem?.errors ?? [])
          .map((error) => (Object.hasOwn(INVOICE_MATCH_DIFFERENCE_FIELD_TEXT, error.field) ? INVOICE_MATCH_DIFFERENCE_FIELD_TEXT[error.field] : null))
          .filter((label): label is string => label !== null)
        if (fields.length > 0) parts.push(`${T.exceededFields}: ${fields.join(', ')}.`)
      }
      // A refusal that answered a retry of an unknown outcome says nothing about the first send.
      if (attempt.unresolvedSince !== undefined) parts.push(F.unknown)
      return parts.join(' ')
    }
    default: return null
  }
}
