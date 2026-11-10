/**
 * API-07 (EK21 SAT-D153…D162; Sinan 2026-11-10): the single-shipment risk exceptions of an order on S-04 (Sipariş 360), as the
 * server answers them (`GET …/drafts/{id}/risk-exceptions`, under the draft read), with the request and the decision actions.
 *
 * The list is shown to a caller holding the S-04 detail in the order's company. The request form appears only with
 * `sales.orders.risk-exception.request`, the approve / reject buttons only on a pending exception and only with
 * `sales.orders.risk-exception.approve`; the server measures both afresh and decides (no self-decision, SAT-D153; a decision at
 * the exception's version). The requester's own pending request carries a "Geri çek" button under the request capability
 * (EK56 SAT-D286); whether the caller is the requester is the server's decision, the button only follows the shell's user id.
 * Validity is the server's `isValidToday` for its Istanbul business date (SAT-D160), never computed here. A command whose outcome
 * is unknown (offline, network error, malformed answer, or a 502/503/504 gateway answer) keeps its key in memory and is retried
 * with the same key, never a new request. No amount is shown (SAT-D162).
 */
import { useId, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchDraftRiskExceptions, sendRiskExceptionDecision, sendRiskExceptionRequest, type RiskExceptionMutation } from './api'
import {
  SALES_CAPABILITY,
  SALES_RISK_EXCEPTION_APPROVE_CAPABILITY,
  SALES_RISK_EXCEPTION_REQUEST_CAPABILITY,
} from './capabilities'
import { decodeProblem, type DraftDetail } from './contract'
import { formatInstant } from './format'
import { generateOrderAttemptKey, type OrderCommandOutcome } from './orderAttempt'
import {
  RISK_EXCEPTION_REASON_MAX,
  RISK_EXCEPTION_STATE,
  type RiskException,
  type RiskExceptionDecision,
  type RiskExceptionDecisionRequest,
  type RiskExceptionRequest,
} from './riskException'
import { RISK_EXCEPTION_TEXT as T } from './riskText'
import { isReferenceId } from './wirePrimitives'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import pages from './SalesPages.module.css'

/** One command in flight or of unknown outcome: retried only with the same key and the same body. */
export type PendingRiskExceptionAction =
  | { readonly kind: 'request'; readonly key: string; readonly payload: RiskExceptionRequest }
  | {
      readonly kind: RiskExceptionDecision
      readonly key: string
      readonly exceptionNo: number
      readonly payload: RiskExceptionDecisionRequest
    }

/** The container: gates the list on the bound company and the S-04 detail capability, and each action on its own capability. */
export function SalesOrderRiskExceptions({ detail }: { readonly detail: DraftDetail }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const canRead = namespace !== null && companyId !== null && companyId === detail.draft.companyId
    && hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const canRequest = canRead && hasCapability(shell, SALES_RISK_EXCEPTION_REQUEST_CAPABILITY)
  const canDecide = canRead && hasCapability(shell, SALES_RISK_EXCEPTION_APPROVE_CAPABILITY)
  const actorId = shell.activeContext?.userId ?? null
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-risk-exceptions', detail.draft.id, detail.draft.version],
    enabled: canRead,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftRiskExceptions(detail, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })

  async function send(action: PendingRiskExceptionAction): Promise<OrderCommandOutcome<RiskExceptionMutation>> {
    const snapshot = boundary.capture()
    const context = { idempotencyKey: action.key, companyId: companyId as string, draftId: detail.draft.id }
    const outcome = action.kind === 'request'
      ? await sendRiskExceptionRequest(action.payload, context)
      : await sendRiskExceptionDecision(action.exceptionNo, action.kind, action.payload, context)
    if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
    return outcome
  }

  if (!canRead) return null
  return (
    <OrderRiskExceptions
      outcome={query.data}
      canRequest={canRequest}
      canDecide={canDecide}
      actorId={actorId}
      send={send}
      onRecorded={() => void query.refetch()}
    />
  )
}

/** The panel for one read outcome (undefined while loading). Exported so every outcome and action can be checked on its own. */
export function OrderRiskExceptions({
  outcome, canRequest, canDecide, actorId = null, send, onRecorded,
}: {
  readonly outcome: ViewOutcome<readonly RiskException[]> | undefined
  readonly canRequest: boolean
  readonly canDecide: boolean
  /** The shell's user id: the withdraw button is offered only on this user's own pending request (SAT-D286). */
  readonly actorId?: string | null
  readonly send: (action: PendingRiskExceptionAction) => Promise<OrderCommandOutcome<RiskExceptionMutation>>
  readonly onRecorded: () => void
}) {
  const headingId = useId()
  const [shipment, setShipment] = useState('')
  const [reason, setReason] = useState('')
  const [rejectionReasons, setRejectionReasons] = useState<Readonly<Record<number, string>>>({})
  const [pending, setPending] = useState<PendingRiskExceptionAction | null>(null)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<{ readonly text: string; readonly supportCode: string | null } | null>(null)
  const locked = busy || pending !== null

  /** The request and the withdrawal run under the request capability; a decision under the approve capability. */
  const allowed = (kind: PendingRiskExceptionAction['kind']) => kind === 'request' || kind === 'withdrawal' ? canRequest : canDecide

  async function execute(action: PendingRiskExceptionAction) {
    if (!allowed(action.kind)) {
      setNotice({ text: T.noAuthority, supportCode: null })
      return
    }
    setBusy(true)
    setNotice({ text: T.submitting, supportCode: null })
    const result = await send(action)
    setBusy(false)
    if (result.type === 'succeeded') {
      setPending(null)
      const done = action.kind === 'request' ? T.requested
        : action.kind === 'approval' ? T.approved
          : action.kind === 'withdrawal' ? T.withdrawn : T.rejected
      setNotice({ text: result.data.isReplay ? done + T.replayed : done, supportCode: null })
      if (action.kind === 'request') {
        setShipment('')
        setReason('')
      } else if (action.kind !== 'withdrawal') {
        setRejectionReasons((current) => ({ ...current, [action.exceptionNo]: '' }))
      }
      onRecorded()
      return
    }
    if (result.type === 'offline' || result.type === 'network-error' || result.type === 'aborted' || result.type === 'malformed') {
      // The server may have recorded it: keep the key so the retry replays instead of opening a second request.
      setPending(action)
      setNotice({ text: T.unknown, supportCode: null })
      return
    }
    if (GATEWAY_STATUSES.includes(result.status)) {
      // A gateway or unavailable answer does not say whether the command was recorded: keep the key for a same-key retry.
      setPending(action)
      setNotice({ text: T.retryPending, supportCode: result.problem?.correlationId ?? null })
      return
    }
    setPending(null)
    setNotice(failureNotice(result))
    if (result.status === 409) onRecorded()
  }

  function reserve(action: PendingRiskExceptionAction) {
    setPending(action)
    void execute(action)
  }

  function request() {
    const id = shipment.trim()
    if (!isReferenceId(id)) {
      setNotice({ text: T.invalidShipment, supportCode: null })
      return
    }
    if (reason.trim() === '') {
      setNotice({ text: T.invalidReason, supportCode: null })
      return
    }
    // The bound is the server's; a longer reason is refused here too, never cut.
    if (reason.trim().length > RISK_EXCEPTION_REASON_MAX) {
      setNotice({ text: T.reasonTooLong(RISK_EXCEPTION_REASON_MAX), supportCode: null })
      return
    }
    reserve({ kind: 'request', key: generateOrderAttemptKey(), payload: { loadingInstructionId: id, reason: reason.trim() } })
  }

  function decide(item: RiskException, decision: RiskExceptionDecision) {
    const rejection = (rejectionReasons[item.exceptionNo] ?? '').trim()
    if (decision === 'rejection' && rejection === '') {
      setNotice({ text: T.invalidRejection, supportCode: null })
      return
    }
    if (rejection.length > RISK_EXCEPTION_REASON_MAX) {
      setNotice({ text: T.reasonTooLong(RISK_EXCEPTION_REASON_MAX), supportCode: null })
      return
    }
    reserve({
      kind: decision, key: generateOrderAttemptKey(), exceptionNo: item.exceptionNo,
      payload: { expectedVersion: item.version, reason: decision === 'rejection' ? rejection : null },
    })
  }

  /** SAT-D286: the requester's withdrawal of their own pending request at its version (no reason). */
  function withdraw(item: RiskException) {
    reserve({
      kind: 'withdrawal', key: generateOrderAttemptKey(), exceptionNo: item.exceptionNo,
      payload: { expectedVersion: item.version, reason: null },
    })
  }

  let body
  if (outcome?.type === 'ok-single') {
    const items = outcome.data
    body = (
      <>
        {notice === null ? null : (
          <p role="status" className={pages.notice} data-testid="sales-risk-exception-notice">
            {notice.text}
            {notice.supportCode === null ? null : <> · {T.supportCode(notice.supportCode)}</>}
          </p>
        )}
        {pending !== null && !busy ? (
          <Button type="button" variant="secondary" disabled={!allowed(pending.kind)}
            onClick={() => void execute(pending)} data-testid="sales-risk-exception-retry">{T.retry}</Button>
        ) : null}
        {canRequest ? (
          <div className={pages.actions} data-testid="sales-risk-exception-request">
            <h3>{T.requestHeading}</h3>
            <label htmlFor="sales-risk-exception-shipment">{T.shipment}</label>
            <input id="sales-risk-exception-shipment" value={shipment} disabled={locked} autoComplete="off" spellCheck={false}
              aria-describedby="sales-risk-exception-shipment-hint" onChange={(event) => setShipment(event.target.value)} />
            <p id="sales-risk-exception-shipment-hint" className={pages.muted}>{T.shipmentHint}</p>
            <label htmlFor="sales-risk-exception-reason">{T.reason}</label>
            <textarea id="sales-risk-exception-reason" value={reason} disabled={locked} onChange={(event) => setReason(event.target.value)} />
            <Button type="button" variant="primary" disabled={locked} onClick={request}>{T.request}</Button>
          </div>
        ) : null}
        {items.length === 0 ? <p data-testid="sales-risk-exception-empty">{T.empty}</p> : items.map((item) => (
          <article key={item.exceptionNo} className={pages.line} data-testid={`sales-risk-exception-${item.exceptionNo}`}>
            <h3>
              {T.exceptionHeading(item.exceptionNo)} · <span data-testid={`sales-risk-exception-${item.exceptionNo}-state`}>
                {T.state[item.state]}</span>
            </h3>
            <p>{T.shipmentFact(item.loadingInstructionId)}</p>
            <p className={pages.muted}>{item.reason} · {T.requestedBy(item.requestedBy, formatInstant(item.requestedAtUtc))}</p>
            {item.decidedBy === null || item.decidedAtUtc === null ? null : (
              <p className={pages.muted} data-testid={`sales-risk-exception-${item.exceptionNo}-decided`}>
                {T.decidedBy(item.decidedBy, formatInstant(item.decidedAtUtc))}
                {item.rejectionReason === null ? null : ` · ${item.rejectionReason}`}
              </p>
            )}
            {item.state === RISK_EXCEPTION_STATE.approved && item.validOn !== null ? (
              <p data-testid={`sales-risk-exception-${item.exceptionNo}-validity`}>
                {item.isValidToday ? T.validToday(item.validOn) : T.lapsed(item.validOn)}
              </p>
            ) : null}
            {item.state === RISK_EXCEPTION_STATE.requested && canDecide ? (
              <div className={pages.actions} data-testid={`sales-risk-exception-${item.exceptionNo}-decision`}>
                <label htmlFor={`sales-risk-exception-reject-${item.exceptionNo}`}>{T.rejectionReason}</label>
                <input id={`sales-risk-exception-reject-${item.exceptionNo}`} value={rejectionReasons[item.exceptionNo] ?? ''}
                  disabled={locked}
                  onChange={(event) => setRejectionReasons((current) => ({ ...current, [item.exceptionNo]: event.target.value }))} />
                <Button type="button" variant="primary" disabled={locked} onClick={() => decide(item, 'approval')}>{T.approve}</Button>
                <Button type="button" variant="secondary" disabled={locked} onClick={() => decide(item, 'rejection')}>{T.reject}</Button>
              </div>
            ) : null}
            {item.state === RISK_EXCEPTION_STATE.requested && canRequest && actorId !== null && item.requestedBy === actorId ? (
              <div className={pages.actions} data-testid={`sales-risk-exception-${item.exceptionNo}-withdrawal`}>
                <Button type="button" variant="secondary" disabled={locked} onClick={() => withdraw(item)}>{T.withdraw}</Button>
              </div>
            ) : null}
          </article>
        ))}
      </>
    )
  } else if (outcome?.type === 'http' && outcome.status === 403) {
    body = <p className={pages.muted} role="status" data-testid="sales-risk-exception-forbidden">{T.forbidden}</p>
  } else {
    const problem = outcome?.type === 'http' ? decodeProblem(outcome.body, outcome.status) : null
    body = (
      <>
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject={T.subject}
          onRetry={onRecorded}
        />
        {outcome === undefined ? null : (
          <p className={pages.muted} data-testid="sales-risk-exception-unreadable">
            {outcome.type === 'http' && outcome.status === 503 ? T.serviceUnavailable : T.unreadable}
          </p>
        )}
        {problem?.correlationId == null ? null : (
          <p className={pages.muted} data-testid="sales-risk-exception-support-code">{T.supportCode(problem.correlationId)}</p>
        )}
      </>
    )
  }
  return (
    <section className={pages.section} aria-labelledby={headingId} aria-busy={outcome === undefined} data-testid="sales-risk-exceptions">
      <h2 id={headingId}>{T.heading}</h2>
      <p className={pages.muted}>{T.note}</p>
      {body}
    </section>
  )
}

/** Answers that do not say whether the command was recorded (a gateway or an unavailable server): the key is kept. */
const GATEWAY_STATUSES: readonly number[] = [502, 503, 504]

/** A refused command in words: the problem code the server named when known, else the status. */
function failureNotice(outcome: Extract<OrderCommandOutcome<RiskExceptionMutation>, { type: 'http' }>) {
  const supportCode = outcome.problem?.correlationId ?? null
  const code = outcome.problem?.code ?? null
  if (outcome.status === 401) return { text: T.unauthenticated, supportCode }
  if (outcome.status === 403) return { text: T.noAuthority, supportCode }
  if (outcome.status === 404) return { text: T.notFound, supportCode }
  if (code !== null && T.problem[code] !== undefined) return { text: T.problem[code], supportCode }
  if (outcome.status === 409) return { text: T.conflict, supportCode }
  if (outcome.status >= 500) return { text: T.serviceUnavailable, supportCode }
  return { text: code === null ? T.failed : T.failedWithCode(code), supportCode }
}
