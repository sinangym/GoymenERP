/**
 * WP4 (SAT-D475 / SAT-D476) — the Sales approval request panels:
 *  - `SalesQuoteConversionRequests` on S-01's quote detail (shown and read only while the quote is expired and unapproved): the conversion requests of the quote, "Dönüşüm onayı talep et"
 *    (quote write; offered only while the server says the quote is expired, its revision unapproved and no request of the
 *    revision is pending) and "Reddet" on a pending request (expired-conversion approval). The approval itself stays the
 *    quote's own "Süresi geçmiş teklifi onayla" command.
 *  - `SalesOverDeliveryRequests` on S-04's status card: the over-delivery requests the Logistics shipment opened, with
 *    "Onayla" / "Reddet" on a pending one behind the over-delivery approval control (the server refuses the shipper); shown and
 *    read only for an actor holding that control — everyone else sees the requests in the Work inbox when they may decide.
 * Each command is one keyed attempt (./poolAttempt.ts): an unknown outcome keeps its key and resends the same snapshot; a
 * recorded command or a 409 re-reads the list. Nothing is judged here: statuses and quantities are the server's.
 */

import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  APPROVAL_REQUEST_STATUS,
  decideOverDeliveryRequest,
  fetchOverDeliveryRequests,
  fetchQuoteConversionRequests,
  overDeliveryDecisionPath,
  quoteConversionRejectionPath,
  quoteConversionRequestsPath,
  rejectQuoteConversionRequest,
  requestQuoteConversion,
  type ApprovalDecisionBody,
  type ApprovalRequest,
  type ApprovalRequestMutation,
  type QuoteConversionRequestBody,
} from './approvalRequests'
import { APPROVAL_REQUEST_STATUS_TEXT, APPROVAL_REQUEST_TEXT as T, approvalRequestProblemText } from './approvalRequestText'
import { SALES_OVER_DELIVERY_APPROVE_CAPABILITY } from './capabilities'
import { formatInstant } from './format'
import { usePoolAttempt, type PoolAttempt } from './poolAttempt'
import { useSalesPoolCapability } from './poolCapabilities'
import { QUOTE_STATE, type SalesQuoteDetail } from './poolContract'
import type { DraftDetail } from './contract'
import { LIMITS } from './contract'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import type { ViewOutcome } from '../shell/uiState'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import styles from './SalesPool.module.css'

type ListOutcome = ViewOutcome<readonly ApprovalRequest[]>

function useRequestList(kind: 'quote' | 'over-delivery', subjectId: string, companyId: string,
  fetcher: (id: string, company: string, signal?: AbortSignal) => Promise<ListOutcome>, needed: boolean) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const boundary = useAuthBoundary()
  return useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-approval-requests', kind, subjectId],
    // Read only where the panel can matter (an expired quote; an actor who may decide over-deliveries): no extra call otherwise.
    enabled: namespace !== null && needed,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetcher(subjectId, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
}

function ListState({ outcome, onRetry }: { readonly outcome: ListOutcome | undefined; readonly onRetry: () => void }) {
  if (outcome === undefined) return <p className={styles.muted} role="status">{T.loading}</p>
  if (outcome.type === 'http' && outcome.status === 403) return <p className={styles.muted} role="status">{T.forbidden}</p>
  return (
    <p className={styles.notice} role="alert">
      {T.unavailable}{' '}
      <Button type="button" variant="secondary" onClick={onRetry}>{T.retry}</Button>
    </p>
  )
}

function AttemptNotice<P>({ attempt, completed, testId }: {
  readonly attempt: PoolAttempt<P, ApprovalRequestMutation>
  readonly completed: string
  readonly testId: string
}) {
  const state = attempt.state
  if (state.phase === 'idle') return null
  if (state.phase === 'submitting') return <p className={styles.muted} role="status" data-testid={testId}>{T.sending}</p>
  if (state.phase === 'completed') return <p className={styles.notice} role="status" data-testid={testId}>{completed}</p>
  if (state.phase === 'failed') {
    return <p className={styles.notice} role="alert" data-testid={testId}>{approvalRequestProblemText(state.problem?.code)}</p>
  }
  return (
    <p className={styles.notice} role="alert" data-testid={testId}>
      {T.unknownOutcome}{' '}
      <Button type="button" variant="secondary" onClick={attempt.retry}>{T.retrySame}</Button>
    </p>
  )
}

function RequestFacts({ item }: { readonly item: ApprovalRequest }) {
  return (
    <dl className={styles.facts}>
      <dt>{T.status}</dt>
      <dd data-testid={`approval-request-status-${item.requestNo}`}>{APPROVAL_REQUEST_STATUS_TEXT[item.status]}</dd>
      <dt>{T.requestedBy}</dt>
      <dd>{item.requestedBy}</dd>
      <dt>{T.requestedAt}</dt>
      <dd>{formatInstant(item.requestedAtUtc)}</dd>
      {item.decidedBy === null ? null : (
        <>
          <dt>{T.decidedBy}</dt>
          <dd>{item.decidedBy}</dd>
        </>
      )}
      {item.decisionReason === null ? null : (
        <>
          <dt>{T.decisionReason}</dt>
          <dd>{item.decisionReason}</dd>
        </>
      )}
    </dl>
  )
}

/** A rejection with its reason (required; bounded), one keyed attempt. */
function RejectForm({ id, attempt, path, expectedVersion }: {
  readonly id: string
  readonly attempt: PoolAttempt<ApprovalDecisionBody, ApprovalRequestMutation>
  readonly path: string
  readonly expectedVersion: string
}) {
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const disabled = attempt.frozen || attempt.state.phase === 'completed'
  return (
    <form
      noValidate
      onSubmit={(event) => {
        event.preventDefault()
        const text = reason.trim()
        if (text === '') {
          setError(approvalRequestProblemText('sales_approval_request_reason_required'))
          return
        }
        if (reason.length > LIMITS.note) {
          setError(approvalRequestProblemText('sales_approval_request_text_too_long'))
          return
        }
        setError(null)
        attempt.submit(path, { expectedVersion, reason: text })
      }}
    >
      <Field id={id} label={T.reasonLabel} error={error ?? undefined}>
        {(control) => (
          <textarea {...control} className={styles.control} value={reason} disabled={disabled} onChange={(event) => setReason(event.target.value)} />
        )}
      </Field>
      <Button type="submit" variant="danger" disabled={disabled}>{T.reject}</Button>
    </form>
  )
}

// ---- SAT-D476: the quote's conversion requests ---------------------------------------------------------------------------

export function SalesQuoteConversionRequests({ detail, companyId, onRecorded }: {
  readonly detail: SalesQuoteDetail
  readonly companyId: string
  readonly onRecorded: () => void
}) {
  const quote = detail.quote
  const canRequestName = useSalesPoolCapability('quotesWrite')
  const canDecide = useSalesPoolCapability('quotesApproveExpired')
  const expired = quote.state === QUOTE_STATE.open && detail.expiry.isExpired && !detail.expiry.isApproved
  const query = useRequestList('quote', quote.id, companyId, fetchQuoteConversionRequests, expired)
  const reread = () => {
    onRecorded()
    void query.refetch()
  }
  const outcome = query.data
  const items = outcome?.type === 'ok-single' ? outcome.data : null
  if (!expired) return null
  const pendingOfRevision = items?.some((item) => item.status === APPROVAL_REQUEST_STATUS.pending && item.subjectRevision === quote.revision) ?? true
  return (
    <section className={styles.section} aria-labelledby="quote-conversion-requests-heading" data-testid="quote-conversion-requests">
      <h2 id="quote-conversion-requests-heading" className={styles.subtitle}>{T.quote.heading}</h2>
      <p className={styles.muted}>{T.quote.intro}</p>
      {items === null ? <ListState outcome={outcome} onRetry={() => void query.refetch()} /> : null}
      {expired && canRequestName && items !== null && !pendingOfRevision
        ? <QuoteRequestForm detail={detail} onRecorded={reread} />
        : null}
      {items !== null && items.length === 0 ? <p className={styles.muted}>{T.quote.empty}</p> : null}
      {items !== null && items.length > 0 ? (
        <ul className={styles.requestList}>
          {items.map((item) => (
            <li key={item.requestNo} data-testid={`quote-conversion-request-${item.requestNo}`}>
              <strong>{T.quote.revision(item.subjectRevision)}</strong>
              {item.note === null ? null : <p>{item.note}</p>}
              <RequestFacts item={item} />
              {canDecide && item.status === APPROVAL_REQUEST_STATUS.pending
                ? <QuoteRequestReject quoteId={quote.id} item={item} onRecorded={reread} />
                : null}
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  )
}

function QuoteRequestForm({ detail, onRecorded }: { readonly detail: SalesQuoteDetail; readonly onRecorded: () => void }) {
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const quoteId = detail.quote.id
  const attempt = usePoolAttempt<QuoteConversionRequestBody, ApprovalRequestMutation>(
    `sales.quote.conversion-request:${quoteId}`,
    (snapshot) => requestQuoteConversion(quoteId, snapshot.payload, { idempotencyKey: snapshot.key, companyId: snapshot.companyId }),
    (next) => {
      if (next.phase === 'completed' || (next.phase === 'failed' && next.failure === 'conflict')) onRecorded()
    },
  )
  const disabled = attempt.frozen || attempt.state.phase === 'completed'
  return (
    <form
      noValidate
      data-testid="quote-conversion-request-form"
      onSubmit={(event) => {
        event.preventDefault()
        if (note.length > LIMITS.note) {
          setError(approvalRequestProblemText('sales_approval_request_text_too_long'))
          return
        }
        setError(null)
        const text = note.trim()
        attempt.submit(quoteConversionRequestsPath(quoteId), { expectedVersion: detail.quote.version, note: text === '' ? null : text })
      }}
    >
      <Field id="quote-conversion-request-note" label={T.quote.noteLabel} error={error ?? undefined}>
        {(control) => (
          <textarea {...control} className={styles.control} value={note} disabled={disabled} onChange={(event) => setNote(event.target.value)} />
        )}
      </Field>
      <AttemptNotice attempt={attempt} completed={T.quote.requested} testId="quote-conversion-request-notice" />
      <Button type="submit" variant="primary" disabled={disabled}>{T.quote.request}</Button>
    </form>
  )
}

function QuoteRequestReject({ quoteId, item, onRecorded }: {
  readonly quoteId: string
  readonly item: ApprovalRequest
  readonly onRecorded: () => void
}) {
  const attempt = usePoolAttempt<ApprovalDecisionBody, ApprovalRequestMutation>(
    `sales.quote.conversion-request-rejection:${quoteId}:${item.requestNo}`,
    (snapshot) => rejectQuoteConversionRequest(quoteId, item.requestNo, snapshot.payload,
      { idempotencyKey: snapshot.key, companyId: snapshot.companyId }),
    (next) => {
      if (next.phase === 'completed' || (next.phase === 'failed' && next.failure === 'conflict')) onRecorded()
    },
  )
  return (
    <>
      <RejectForm id={`quote-conversion-request-reason-${item.requestNo}`} attempt={attempt}
        path={quoteConversionRejectionPath(quoteId, item.requestNo)} expectedVersion={item.version} />
      <AttemptNotice attempt={attempt} completed={T.quote.rejected} testId={`quote-conversion-request-reject-notice-${item.requestNo}`} />
    </>
  )
}

// ---- SAT-D475: the order's over-delivery requests -------------------------------------------------------------------------

export function SalesOverDeliveryRequests({ detail, companyId, onRecorded }: {
  readonly detail: DraftDetail
  readonly companyId: string
  readonly onRecorded: () => void
}) {
  const shell = useShellState()
  const draftId = detail.draft.id
  const canDecide = hasCapability(shell, SALES_OVER_DELIVERY_APPROVE_CAPABILITY)
  const query = useRequestList('over-delivery', draftId, companyId, fetchOverDeliveryRequests, canDecide)
  const reread = () => {
    onRecorded()
    void query.refetch()
  }
  const outcome = query.data
  const items = outcome?.type === 'ok-single' ? outcome.data : null
  if (!canDecide || companyId !== detail.draft.companyId || (items !== null && items.length === 0)) return null
  if (items === null && outcome === undefined) return null
  return (
    <section className={styles.section} aria-labelledby="over-delivery-requests-heading" data-testid="over-delivery-requests">
      <h3 id="over-delivery-requests-heading" className={styles.subtitle}>{T.overDelivery.heading}</h3>
      <p className={styles.muted}>{T.overDelivery.intro}</p>
      {items === null ? <ListState outcome={outcome} onRetry={() => void query.refetch()} /> : (
        <ul className={styles.requestList}>
          {items.map((item) => (
            <li key={item.requestNo} data-testid={`over-delivery-request-${item.requestNo}`}>
              <strong>{T.overDelivery.line(item.requestNo)}</strong>
              <dl className={styles.facts}>
                <dt>{T.overDelivery.ordered}</dt>
                <dd>{item.ordered}</dd>
                <dt>{T.overDelivery.attempted}</dt>
                <dd>{item.quantity}</dd>
                <dt>{T.overDelivery.requestedTotal}</dt>
                <dd>{item.requestedTotal}</dd>
              </dl>
              <RequestFacts item={item} />
              {canDecide && item.status === APPROVAL_REQUEST_STATUS.pending
                ? <OverDeliveryDecision draftId={draftId} item={item} onRecorded={reread} />
                : null}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function OverDeliveryDecision({ draftId, item, onRecorded }: {
  readonly draftId: string
  readonly item: ApprovalRequest
  readonly onRecorded: () => void
}) {
  const settle = (next: { readonly phase: string; readonly failure?: string }) => {
    if (next.phase === 'completed' || (next.phase === 'failed' && next.failure === 'conflict')) onRecorded()
  }
  const approval = usePoolAttempt<ApprovalDecisionBody, ApprovalRequestMutation>(
    `sales.draft.over-delivery-request-approval:${draftId}:${item.requestNo}`,
    (snapshot) => decideOverDeliveryRequest(draftId, item.requestNo, 'approval', snapshot.payload,
      { idempotencyKey: snapshot.key, companyId: snapshot.companyId }),
    settle,
  )
  const rejection = usePoolAttempt<ApprovalDecisionBody, ApprovalRequestMutation>(
    `sales.draft.over-delivery-request-rejection:${draftId}:${item.requestNo}`,
    (snapshot) => decideOverDeliveryRequest(draftId, item.requestNo, 'rejection', snapshot.payload,
      { idempotencyKey: snapshot.key, companyId: snapshot.companyId }),
    settle,
  )
  const busy = approval.frozen || rejection.frozen || approval.state.phase === 'completed' || rejection.state.phase === 'completed'
  return (
    <div className={styles.actions}>
      <Button type="button" variant="primary" disabled={busy}
        onClick={() => approval.submit(overDeliveryDecisionPath(draftId, item.requestNo, 'approval'), { expectedVersion: item.version })}>
        {T.overDelivery.approve}
      </Button>
      <AttemptNotice attempt={approval} completed={T.overDelivery.approved} testId={`over-delivery-request-approve-notice-${item.requestNo}`} />
      <RejectForm id={`over-delivery-request-reason-${item.requestNo}`} attempt={rejection}
        path={overDeliveryDecisionPath(draftId, item.requestNo, 'rejection')} expectedVersion={item.version} />
      <AttemptNotice attempt={rejection} completed={T.overDelivery.rejected} testId={`over-delivery-request-reject-notice-${item.requestNo}`} />
    </div>
  )
}
