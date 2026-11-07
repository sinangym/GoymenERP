import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { PurchaseOrderSummary } from './contract'
import { cancelPurchaseOrderRemainder, fetchPurchaseOrderCancelImpact, type OrderCancellationOutcome } from './orderCancellationApi'
import {
  pendingOrderCancellationKey,
  recallPendingOrderCancellation,
  releasePendingOrderCancellation,
  reservePendingOrderCancellation,
  type PendingOrderCancellation,
} from './pendingCancellationStore'
import { ORDER_CANCELLATION_PROBLEM_TEXT, ORDER_CANCELLATION_TEXT as T } from './purchasingText'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './purchasing.module.css'

const MAXIMUM_REASON_LENGTH = 4000

type Attempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'cancelling'; readonly snapshot: PendingOrderCancellation }
  | { readonly phase: 'unknown'; readonly snapshot: PendingOrderCancellation; readonly outcome: Extract<OrderCancellationOutcome, { type: 'unknown' }> }
  | { readonly phase: 'applied'; readonly replayed: boolean }
  | { readonly phase: 'failed'; readonly status: number; readonly code: string | null }

export interface PurchaseOrderCancellationPanelProps {
  readonly sessionEpoch: number
  /** The query namespace and route entry this panel was opened in; a change drops any late answer. */
  readonly fenceKey: string
  readonly actorId: string
  readonly companyId: string
  readonly order: PurchaseOrderSummary
  /** Re-reads the order after a definite answer (applied or 409), as the approval control does. */
  readonly onSettled: () => Promise<unknown>
}

function newKey(): string {
  return globalThis.crypto.randomUUID()
}

/**
 * A1-3 (plan API-10/UI-05; No.161 §2): the cancellation of an APPROVED order's whole not-yet-accepted remainder, with a required
 * reason, behind the `purchasing.orders.cancel` child capability (the caller measures it). The panel first shows the server's
 * impact preview (the remainder to cancel per line, a partial acceptance that stays, or the blocker); it sends the version the
 * preview was read at, keeps an unresolved attempt (same key, version and reason) for an explicit resend, re-reads the order
 * and the preview after a definite answer (applied or 409) and reports 401/403 to the auth boundary. The backend re-measures
 * the name, the version, the state and the remainder on every call; nothing here is a rule.
 */
export function PurchaseOrderCancellationPanel({ sessionEpoch, fenceKey, actorId, companyId, order, onSettled }: PurchaseOrderCancellationPanelProps) {
  const boundary = useAuthBoundary()
  const reasonId = useId()
  const headingId = useId()
  const storeKey = pendingOrderCancellationKey(sessionEpoch, actorId, companyId, order.id)
  const [attempt, setAttempt] = useState<Attempt>(() => {
    const recalled = recallPendingOrderCancellation(storeKey)
    return recalled === null ? { phase: 'idle' } : { phase: 'unknown', snapshot: recalled, outcome: { type: 'unknown', reason: 'network-error' } }
  })
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const inFlight = useRef(false)
  const responseGeneration = useRef(0)
  useEffect(() => { inFlight.current = false; return () => { responseGeneration.current += 1 } }, [fenceKey])

  const unresolved = attempt.phase === 'unknown' || attempt.phase === 'cancelling'
  const showControl = order.status === 'Approved' || unresolved
  const impact = useQuery({
    queryKey: [fenceKey, 'purchase-order-cancel-impact', order.id, order.version],
    enabled: showControl && !unresolved,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await fetchPurchaseOrderCancelImpact(companyId, order.id, signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
      return result
    },
  })

  const send = useCallback(async (retry?: PendingOrderCancellation) => {
    if (inFlight.current) return
    let snapshot: PendingOrderCancellation
    if (retry !== undefined) {
      snapshot = retry
    } else {
      const preview = impact.data
      // The version sent is the one the preview was read at; a stale or missing preview sends nothing.
      if (preview?.type !== 'ok-single' || !preview.data.cancellable || preview.data.version !== order.version) return
      if (reason.trim().length === 0) { setReasonError(T.reasonRequired); return }
      if (reason.length > MAXIMUM_REASON_LENGTH) { setReasonError(T.reasonTooLong); return }
      setReasonError(null)
      snapshot = { actorId, companyId, orderId: order.id, expectedVersion: preview.data.version, idempotencyKey: newKey(), reason }
    }
    if (snapshot.actorId !== actorId || snapshot.companyId !== companyId || snapshot.orderId !== order.id) return
    inFlight.current = true
    const generation = ++responseGeneration.current
    reservePendingOrderCancellation(storeKey, snapshot)
    setAttempt({ phase: 'cancelling', snapshot })
    const security = boundary.capture()
    const result = await cancelPurchaseOrderRemainder(companyId, snapshot.orderId, snapshot.expectedVersion, snapshot.idempotencyKey, snapshot.reason)
    if (responseGeneration.current !== generation) return
    inFlight.current = false
    if (result.type === 'failed' && (result.status === 401 || result.status === 403)) boundary.report(result.status, security)
    if (result.type === 'unknown') { setAttempt({ phase: 'unknown', snapshot, outcome: result }); return }
    releasePendingOrderCancellation(storeKey, snapshot.idempotencyKey)
    setAttempt(result.type === 'applied'
      ? { phase: 'applied', replayed: result.replayed }
      : { phase: 'failed', status: result.status, code: result.code })
    if (result.type === 'applied') setReason('')
    if (result.type === 'applied' || result.status === 409) {
      await onSettled()
      await impact.refetch()
    }
  }, [actorId, boundary, companyId, impact, onSettled, order.id, order.version, reason, storeKey])

  const notice = attempt.phase === 'applied'
    ? <p role="status" className={styles.success}>{attempt.replayed ? T.replayed : T.cancelled}</p>
    : attempt.phase === 'failed'
      ? <p role="alert" className={styles.error}>{T.failed} ({attempt.code ?? attempt.status}). {attempt.code !== null && ORDER_CANCELLATION_PROBLEM_TEXT[attempt.code] !== undefined ? `${ORDER_CANCELLATION_PROBLEM_TEXT[attempt.code]} ` : ''}{attempt.status === 409 ? T.reread : ''}</p>
      : null
  if (!showControl) return notice

  const pending = unresolved ? attempt.snapshot : null
  const busy = attempt.phase === 'cancelling'
  const preview = impact.data?.type === 'ok-single' ? impact.data.data : null
  const stale = preview !== null && preview.version !== order.version
  return <>
    {notice}
    <section className={styles.decisionCard} aria-labelledby={headingId} aria-busy={busy} data-testid="purchase-order-cancellation">
      <h2 id={headingId}>{T.heading}</h2>
      <p>{T.note}</p>
      {attempt.phase === 'unknown' ? <p role="alert" className={styles.warning}>{T.unknown}</p> : null}
      {pending !== null ? (
        <Button type="button" variant="danger" disabled={busy} onClick={() => void send(pending)}>
          {busy ? T.cancelling : T.retry}
        </Button>
      ) : impact.isPending ? <p role="status">{T.impactLoading}</p>
        : preview === null ? <>
          <p role="alert" className={styles.error}>{T.impactUnavailable}</p>
          <Button type="button" variant="secondary" onClick={() => void impact.refetch()}>{T.impactRetry}</Button>
        </>
          : stale ? <>
            <p role="status">{T.impactStale}</p>
            <Button type="button" variant="secondary" onClick={() => void onSettled().then(() => impact.refetch())}>{T.impactRetry}</Button>
          </>
            : <>
              <h3>{T.impactHeading}</h3>
              {preview.hasAcceptance ? <p>{T.partialAcceptance}</p> : null}
              <div className={styles.tableWrap}>
                <table aria-label={T.impactCaption}>
                  <thead><tr>
                    <th scope="col">{T.lineNo}</th>
                    <th scope="col">{T.committed}</th>
                    <th scope="col">{T.accepted}</th>
                    <th scope="col">{T.toCancel}</th>
                  </tr></thead>
                  <tbody>{preview.lines.map((line) => <tr key={line.lineId} data-testid={`purchase-order-cancel-impact-${line.lineNo}`}>
                    <td>{line.lineNo}</td>
                    <td>{line.committed}</td>
                    <td>{line.accepted}</td>
                    <td>{line.toCancel}</td>
                  </tr>)}</tbody>
                </table>
              </div>
              {preview.cancellable ? <div className={styles.decisionReason}>
                <label htmlFor={reasonId}>{T.reasonLabel}</label>
                <textarea id={reasonId} value={reason} rows={3}
                  aria-invalid={reasonError !== null} aria-describedby={`${reasonId}-hint`}
                  onChange={(event) => { setReason(event.target.value); setReasonError(null) }} />
                <p id={`${reasonId}-hint`} className={styles.hint}>{T.reasonHint}</p>
                {reasonError !== null ? <p role="alert" className={styles.error}>{reasonError}</p> : null}
                <Button type="button" variant="danger" onClick={() => void send()}>{T.cancel}</Button>
              </div> : <p role="status" className={styles.warning}>
                {T.blocked}{preview.blocker !== null && ORDER_CANCELLATION_PROBLEM_TEXT[preview.blocker] !== undefined
                  ? `: ${ORDER_CANCELLATION_PROBLEM_TEXT[preview.blocker]}` : ''}
              </p>}
            </>}
    </section>
  </>
}
