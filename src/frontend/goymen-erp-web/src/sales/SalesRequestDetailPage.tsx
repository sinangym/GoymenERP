/**
 * S-01 — a sales request as `GET /api/v1/sales/requests/{id}` describes it (API-20), with its decision (API-21; delegation
 * No 160, 161).
 *
 * Only what the detail carries: the state, the concurrency version and the decision facts (who, when, the rejection's
 * reason) as stored. "Onayla" is offered from `Onay bekliyor`; "Reddet" from `Onay bekliyor` or `Onaylı` (the two source
 * states of the decision) and needs its reason — the server refuses a blank one as well. No approver count, threshold or
 * maker≠checker is chosen here: one decision is sent and the server judges it (403 / 409 shown as it answers). After a
 * recorded decision, or a 409 (the request moved or no longer allows it), the request is re-read; a typed reason is kept.
 */

import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { fetchRequestDetail, sendRequestDecision } from './poolApi'
import {
  REQUEST_STATE,
  type SalesRequestDecisionBody,
  type SalesRequestDecisionCommand,
  type SalesRequestDetail,
  type SalesRequestMutation,
} from './poolContract'
import { POOL_SUBJECT_TEXT, POOL_TEXT, REQUEST_STATE_TEXT } from './poolText'
import { SALES_POOL_ROUTE, quoteCreatePathFor, useSalesPoolCapability } from './poolCapabilities'
import { poolNotice, usePoolAttempt } from './poolAttempt'
import { PoolAttemptNotice, PoolPartyCell } from './SalesPoolShared'
import { REQUEST_TONE } from './poolView'
import { formatBusinessDate, formatInstant } from './format'
import { classifyOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { StatusIndicator } from '../ui/StatusIndicator'
import styles from './SalesPool.module.css'

const T = POOL_TEXT.requestDetail
const C = POOL_TEXT.common

export function SalesRequestDetailPage() {
  const shell = useShellState()
  const { requestId } = useParams()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()
  const canList = useSalesPoolCapability('requestsList')

  const queryKey = [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-request', requestId ?? '']
  const query = useQuery({
    queryKey,
    enabled: namespace !== null && companyId !== null && (requestId ?? '').length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchRequestDetail(requestId as string, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const backLink = canList ? (
    <Link className={styles.link} to={SALES_POOL_ROUTE.requests}>
      ← {T.back}
    </Link>
  ) : null

  const outcome = query.data
  if (outcome === undefined || outcome.type !== 'ok-single') {
    return (
      <section className={styles.page} aria-label={T.region}>
        {backLink}
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject={POOL_SUBJECT_TEXT.request}
          onRetry={() => void query.refetch()}
        />
      </section>
    )
  }
  const detail = outcome.data
  return (
    <RequestView
      key={`${namespace ?? ''}:${detail.request.id}`}
      detail={detail}
      backLink={backLink}
      onReread={() => void queryClient.invalidateQueries({ queryKey })}
    />
  )
}

function RequestView({
  detail,
  backLink,
  onReread,
}: {
  readonly detail: SalesRequestDetail
  readonly backLink: React.ReactNode
  readonly onReread: () => void
}) {
  const { request } = detail
  const canQuote = useSalesPoolCapability('quotesCreate')
  return (
    <section className={styles.page} aria-labelledby="pool-request-heading">
      {backLink}
      <header className={styles.header}>
        <div className={styles.titleRow}>
          <h1 id="pool-request-heading" className={styles.title}>
            {T.heading(request.documentNo)}
          </h1>
          <StatusIndicator tone={REQUEST_TONE[request.state]}>
            <span data-testid="pool-request-state">{REQUEST_STATE_TEXT[request.state]}</span>
          </StatusIndicator>
        </div>
      </header>
      <dl className={styles.facts}>
        <div><dt>{C.party}</dt><dd><PoolPartyCell partyId={request.partyId} /></dd></div>
        <div><dt>{C.businessDate}</dt><dd>{formatBusinessDate(request.businessDate)}</dd></div>
        <div>
          <dt>{C.requestedDeliveryDate}</dt>
          <dd>{detail.requestedDeliveryDate === null ? C.unknown : formatBusinessDate(detail.requestedDeliveryDate)}</dd>
        </div>
        <div><dt>{C.version}</dt><dd className={styles.identifier}>{request.version}</dd></div>
        <div><dt>{C.createdAt}</dt><dd>{formatInstant(request.createdAtUtc)}</dd></div>
        <div className={styles.wide}><dt>{C.note}</dt><dd>{detail.note ?? C.unknown}</dd></div>
      </dl>

      <section className={styles.section} aria-labelledby="pool-request-decision-heading">
        <h2 id="pool-request-decision-heading" className={styles.subtitle}>
          {T.decision}
        </h2>
        {detail.decision === null ? (
          <p className={styles.muted} data-testid="pool-request-decision">{T.noDecision}</p>
        ) : (
          <dl className={styles.facts} data-testid="pool-request-decision">
            <div><dt>{C.decidedBy}</dt><dd>{detail.decision.decidedBy}</dd></div>
            <div><dt>{C.decidedAt}</dt><dd>{formatInstant(detail.decision.decidedAtUtc)}</dd></div>
            {detail.decision.rejectionReason === null ? null : (
              <div className={styles.wide}><dt>{T.rejectionReason}</dt><dd>{detail.decision.rejectionReason}</dd></div>
            )}
          </dl>
        )}
        <DecisionCommands detail={detail} onReread={onReread} />
      </section>

      <RequestLines detail={detail} />

      {canQuote && request.state !== REQUEST_STATE.rejected ? (
        <div>
          <Link className={styles.primaryLink} to={quoteCreatePathFor([request.id])}>
            {T.quote}
          </Link>
        </div>
      ) : null}
    </section>
  )
}

function RequestLines({ detail }: { readonly detail: SalesRequestDetail }) {
  return (
    <section className={styles.section} aria-labelledby="pool-request-lines-heading">
      <h2 id="pool-request-lines-heading" className={styles.subtitle}>
        {C.lines}
      </h2>
      <div className={styles.scroll}>
        <table aria-labelledby="pool-request-lines-heading">
          <thead>
            <tr>
              <th scope="col">{C.lineNo}</th>
              <th scope="col">{C.product}</th>
              <th scope="col" className={styles.numeric}>{C.quantity}</th>
              <th scope="col">{C.unit}</th>
              <th scope="col">{C.lineDeliveryDate}</th>
              <th scope="col">{C.note}</th>
            </tr>
          </thead>
          <tbody>
            {detail.lines.map((line) => (
              <tr key={line.lineId} data-testid={`pool-request-line-${line.lineNo}`}>
                <td>{line.lineNo}</td>
                <td>{line.productCode} — {line.productName}</td>
                <td className={styles.numeric}>{line.quantity}</td>
                <td>{line.orderUnitCode}</td>
                <td>{line.requestedDeliveryDate === null ? C.unknown : formatBusinessDate(line.requestedDeliveryDate)}</td>
                <td>{line.note ?? C.unknown}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}

/** The attempt binding scope of a decision: one per request, so a key never crosses records. */
const decisionScope = (requestId: string) => `sales.request.decision:${requestId}`

function DecisionCommands({ detail, onReread }: { readonly detail: SalesRequestDetail; readonly onReread: () => void }) {
  const { request } = detail
  const canDecide = useSalesPoolCapability('requestsDecide')
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [command, setCommand] = useState<SalesRequestDecisionCommand>('approval')
  const attempt = usePoolAttempt<SalesRequestDecisionBody, SalesRequestMutation>(
    decisionScope(request.id),
    (snapshot) =>
      sendRequestDecision(
        snapshot.path.endsWith('/rejection') ? 'rejection' : 'approval',
        snapshot.payload,
        { idempotencyKey: snapshot.key, companyId: snapshot.companyId, requestId: request.id },
      ),
    (next) => {
      if (next.phase === 'completed' || (next.phase === 'failed' && next.failure === 'conflict')) onReread()
    },
  )

  const offerApprove = canDecide && request.state === REQUEST_STATE.submitted
  const offerReject = canDecide && (request.state === REQUEST_STATE.submitted || request.state === REQUEST_STATE.approved)
  const notice = poolNotice(attempt.state, { noun: 'karar', onRecord: true, blockedChange: attempt.blockedChange })
  if (!offerApprove && !offerReject && notice === null) return null

  function send(kind: SalesRequestDecisionCommand) {
    if (kind === 'rejection' && reason.trim() === '') {
      setReasonError(T.reasonRequired)
      document.getElementById('pool-request-reason')?.focus()
      return
    }
    setReasonError(null)
    setCommand(kind)
    // A recorded earlier decision closed its own attempt; the next decision is a new one (an unresolved one is kept).
    attempt.reset()
    const payload: SalesRequestDecisionBody =
      kind === 'rejection' ? { expectedVersion: request.version, reason: reason.trim() } : { expectedVersion: request.version }
    attempt.submit(`${request.id}/${kind}`, payload)
  }

  return (
    <div className={styles.page} role="group" aria-label={T.commands} data-testid="pool-request-commands">
      <PoolAttemptNotice attempt={attempt} notice={notice} testId={`pool-request-notice-${command}`} />
      {offerApprove ? (
        <div>
          <Button type="button" variant="primary" disabled={attempt.frozen} onClick={() => send('approval')}>
            {T.approve}
          </Button>
        </div>
      ) : null}
      {offerReject ? (
        <div className={styles.grid}>
          <div className={styles.wide}>
            <label htmlFor="pool-request-reason">{T.reasonLabel}</label>
            <textarea
              id="pool-request-reason"
              className={styles.control}
              value={reason}
              disabled={attempt.frozen}
              aria-invalid={reasonError === null ? undefined : true}
              aria-describedby={reasonError === null ? undefined : 'pool-request-reason-error'}
              onChange={(event) => setReason(event.target.value)}
            />
            {reasonError === null ? null : (
              <p id="pool-request-reason-error" className={styles.rowError}>
                {reasonError}
              </p>
            )}
          </div>
          <div>
            <Button type="button" variant="danger" disabled={attempt.frozen} onClick={() => send('rejection')}>
              {T.reject}
            </Button>
          </div>
        </div>
      ) : null}
    </div>
  )
}
