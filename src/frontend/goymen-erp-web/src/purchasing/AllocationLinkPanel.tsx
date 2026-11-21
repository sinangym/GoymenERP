import { useCallback, useEffect, useId, useRef, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { PurchaseOrderLine, PurchaseOrderSummary } from './contract'
import type { PurchaseOrderFutureAllocationLink } from './allocationLinkContract'
import {
  cancelPurchaseOrderWithAllocationRelease,
  fetchPurchaseOrderFutureAllocations,
  releasePurchaseOrderFutureAllocation,
  type AllocationLinkOutcome,
  type SeenAllocation,
} from './allocationLinkApi'
import { ALLOCATION_LINK_PROBLEM_TEXT, ALLOCATION_LINK_TEXT as T } from './allocationLinkText'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './purchasing.module.css'

/** The producer's reason bound (PurchaseOrder.MaximumReasonLength); the server re-measures it on every call. */
const MAXIMUM_REASON_LENGTH = 4000

type Snapshot =
  | { readonly kind: 'drop'; readonly orderVersion: string; readonly allocation: SeenAllocation; readonly idempotencyKey: string; readonly reason: string }
  | { readonly kind: 'cancel'; readonly orderVersion: string; readonly seen: readonly SeenAllocation[]; readonly idempotencyKey: string; readonly reason: string }

type Attempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'sending'; readonly snapshot: Snapshot }
  | { readonly phase: 'unknown'; readonly snapshot: Snapshot }
  | { readonly phase: 'applied'; readonly kind: Snapshot['kind']; readonly replayed: boolean }
  | { readonly phase: 'failed'; readonly status: number; readonly code: string | null }

export interface AllocationLinkPanelProps {
  /** The query namespace and route entry this panel was opened in; a change drops any late answer. */
  readonly fenceKey: string
  readonly actorId: string
  readonly companyId: string
  readonly order: PurchaseOrderSummary
  readonly lines: readonly PurchaseOrderLine[]
  /** Whether the caller measured the `purchasing.orders.cancel` capability; the drop-and-cancel control needs it as well. */
  readonly canCancel: boolean
  /** Re-reads the order after a definite answer (applied or 409). */
  readonly onSettled: () => Promise<unknown>
}

function newKey(): string {
  return globalThis.crypto.randomUUID()
}

function reasonProblem(reason: string): string | null {
  if (reason.trim().length === 0) return T.reasonRequired
  return reason.length > MAXIMUM_REASON_LENGTH ? T.reasonTooLong : null
}

/**
 * F-017 §3.5 F17-4 (ALS-D83/ALS-D85): the active future allocations Inventory holds on an APPROVED order's general lines. One
 * link can be dropped with a required reason (the order quantities and version stay); when the order has links its remainder
 * is cancelled only together with exactly the links listed here, which the actor confirms. The panel sends the versions it
 * read, keeps an unresolved attempt (same key and body) for an explicit resend, re-reads after a definite answer and reports
 * 401/403 to the auth boundary. The backend re-measures the permission, the set, every version and the state; nothing here is
 * a rule. A 403 on the read hides the panel (the release permission is not granted).
 */
export function AllocationLinkPanel({ fenceKey, actorId, companyId, order, lines, canCancel, onSettled }: AllocationLinkPanelProps) {
  const boundary = useAuthBoundary()
  const headingId = useId()
  const dropReasonId = useId()
  const cancelReasonId = useId()
  const confirmId = useId()
  const [attempt, setAttempt] = useState<Attempt>({ phase: 'idle' })
  const [dropTarget, setDropTarget] = useState<string | null>(null)
  const [dropReason, setDropReason] = useState('')
  const [cancelReason, setCancelReason] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const inFlight = useRef(false)
  const responseGeneration = useRef(0)
  useEffect(() => { inFlight.current = false; return () => { responseGeneration.current += 1 } }, [fenceKey])

  const unresolved = attempt.phase === 'unknown' || attempt.phase === 'sending'
  const visible = order.status === 'Approved' || unresolved
  const query = useQuery({
    queryKey: [fenceKey, 'purchase-order-future-allocations', actorId, companyId, order.id, order.version],
    enabled: visible,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await fetchPurchaseOrderFutureAllocations(companyId, order.id, signal)
      // 403 here means the release permission is not granted (no shell capability names it yet): the panel hides, the
      // session is not questioned. 401 still goes to the boundary.
      if (result.type === 'http' && result.status === 401) boundary.report(result.status, snapshot)
      return result
    },
  })

  const send = useCallback(async (snapshot: Snapshot) => {
    if (inFlight.current) return
    inFlight.current = true
    const generation = ++responseGeneration.current
    setAttempt({ phase: 'sending', snapshot })
    const security = boundary.capture()
    const result: AllocationLinkOutcome<unknown> = snapshot.kind === 'drop'
      ? await releasePurchaseOrderFutureAllocation(companyId, order.id, snapshot.orderVersion, snapshot.allocation, snapshot.idempotencyKey, snapshot.reason)
      : await cancelPurchaseOrderWithAllocationRelease(companyId, order.id, snapshot.orderVersion, snapshot.seen, snapshot.idempotencyKey, snapshot.reason)
    if (responseGeneration.current !== generation) return
    inFlight.current = false
    if (result.type === 'failed' && (result.status === 401 || result.status === 403)) boundary.report(result.status, security)
    if (result.type === 'unknown') { setAttempt({ phase: 'unknown', snapshot }); return }
    setAttempt(result.type === 'applied'
      ? { phase: 'applied', kind: snapshot.kind, replayed: result.replayed }
      : { phase: 'failed', status: result.status, code: result.code })
    if (result.type === 'applied') {
      setDropTarget(null); setDropReason(''); setCancelReason(''); setConfirmed(false)
    }
    if (result.type === 'applied' || result.status === 409 || result.status === 404) {
      await onSettled()
      await query.refetch()
    }
  }, [boundary, companyId, onSettled, order.id, query])

  const data = query.data
  const view = data?.type === 'ok-single' ? data.data : null
  const stale = view !== null && view.orderVersion !== order.version

  const startDrop = (link: PurchaseOrderFutureAllocationLink) => {
    if (view === null || stale) return
    const problem = reasonProblem(dropReason)
    if (problem !== null) { setFormError(problem); return }
    setFormError(null)
    void send({ kind: 'drop', orderVersion: view.orderVersion, idempotencyKey: newKey(), reason: dropReason,
      allocation: { allocationId: link.allocationId, expectedAllocationVersion: link.version } })
  }

  const startCancel = () => {
    if (view === null || stale || view.links.length === 0) return
    if (!confirmed) { setFormError(T.confirmRequired); return }
    const problem = reasonProblem(cancelReason)
    if (problem !== null) { setFormError(problem); return }
    setFormError(null)
    void send({ kind: 'cancel', orderVersion: view.orderVersion, idempotencyKey: newKey(), reason: cancelReason,
      seen: view.links.map((link) => ({ allocationId: link.allocationId, expectedAllocationVersion: link.version })) })
  }

  const notice = attempt.phase === 'applied'
    ? <p role="status" className={styles.success}>{attempt.kind === 'drop'
      ? (attempt.replayed ? T.droppedReplay : T.dropped)
      : (attempt.replayed ? T.cancelledReplay : T.cancelled)}</p>
    : attempt.phase === 'failed'
      ? <p role="alert" className={styles.error}>{T.failed} ({attempt.code ?? attempt.status}). {attempt.code !== null && ALLOCATION_LINK_PROBLEM_TEXT[attempt.code] !== undefined ? `${ALLOCATION_LINK_PROBLEM_TEXT[attempt.code]} ` : ''}{attempt.status === 409 ? T.reread : ''}</p>
      : null
  if (!visible) return notice
  if (data?.type === 'http' && data.status === 403) return notice

  const busy = attempt.phase === 'sending'
  const pending = unresolved ? attempt.snapshot : null
  const lineNo = (lineId: string) => lines.find((line) => line.lineId === lineId)?.lineNo ?? '—'

  let body: ReactNode
  if (pending !== null) {
    body = <>
      {attempt.phase === 'unknown' ? <p role="alert" className={styles.warning}>{T.unknown}</p> : null}
      <Button type="button" variant="danger" disabled={busy} onClick={() => void send(pending)}>
        {busy ? (pending.kind === 'drop' ? T.dropping : T.cancelling) : T.resend}
      </Button>
      {attempt.phase === 'unknown'
        ? <Button type="button" variant="secondary" onClick={() => { setAttempt({ phase: 'idle' }); void query.refetch() }}>{T.discard}</Button>
        : null}
    </>
  } else if (query.isPending || data === undefined) {
    body = <p role="status">{T.loading}</p>
  } else if (view === null) {
    const message = data.type === 'offline' ? T.offline
      : data.type === 'network-error' ? T.networkError
        : data.type === 'malformed' ? T.malformed
          : data.type === 'http' && data.status === 404 ? T.notFound
            : T.unavailable
    body = <>
      <p role="alert" className={styles.error}>{message}</p>
      <Button type="button" variant="secondary" onClick={() => void query.refetch()}>{T.retry}</Button>
    </>
  } else if (stale) {
    body = <>
      <p role="status">{T.stale}</p>
      <Button type="button" variant="secondary" onClick={() => void onSettled().then(() => query.refetch())}>{T.retry}</Button>
    </>
  } else if (view.links.length === 0) {
    body = <p role="status" data-testid="purchase-order-future-allocations-empty">{T.empty}</p>
  } else {
    const target = view.links.find((link) => link.allocationId === dropTarget) ?? null
    body = <>
      <div className={styles.tableWrap}>
        <table aria-label={T.caption}>
          <thead><tr>
            <th scope="col">{T.lineNo}</th>
            <th scope="col">{T.salesOrder}</th>
            <th scope="col">{T.allocated}</th>
            <th scope="col">{T.remaining}</th>
            <th scope="col">{T.createdAt}</th>
            <th scope="col">{T.actions}</th>
          </tr></thead>
          <tbody>{view.links.map((link) => <tr key={link.allocationId} data-testid={`purchase-order-future-allocation-${link.allocationId}`}>
            <td>{lineNo(link.lineId)}</td>
            <td>{link.salesOrderId}</td>
            <td>{link.allocatedQuantity}</td>
            <td>{link.remainingQuantity}</td>
            <td>{link.createdAtUtc}</td>
            <td><Button type="button" variant="secondary" onClick={() => { setDropTarget(link.allocationId); setFormError(null) }}>{T.drop}</Button></td>
          </tr>)}</tbody>
        </table>
      </div>
      {target !== null ? <div className={styles.decisionReason} data-testid="purchase-order-future-allocation-drop">
        <h3>{T.dropHeading}</h3>
        <label htmlFor={dropReasonId}>{T.reasonLabel}</label>
        <textarea id={dropReasonId} value={dropReason} rows={3} aria-describedby={`${dropReasonId}-hint`}
          aria-invalid={formError !== null} onChange={(event) => { setDropReason(event.target.value); setFormError(null) }} />
        <p id={`${dropReasonId}-hint`} className={styles.hint}>{T.reasonHint}</p>
        <Button type="button" variant="danger" onClick={() => startDrop(target)}>{T.dropConfirm}</Button>
        <Button type="button" variant="secondary" onClick={() => { setDropTarget(null); setFormError(null) }}>{T.dropCancel}</Button>
      </div> : null}
      {canCancel && target === null ? <div className={styles.decisionReason} data-testid="purchase-order-cancel-with-allocations">
        <h3>{T.cancelHeading}</h3>
        <p>{T.cancelNote}</p>
        <label htmlFor={confirmId}>
          <input id={confirmId} type="checkbox" checked={confirmed} onChange={(event) => { setConfirmed(event.target.checked); setFormError(null) }} />
          {' '}{T.cancelConfirmLabel}
        </label>
        <label htmlFor={cancelReasonId}>{T.cancelReasonLabel}</label>
        <textarea id={cancelReasonId} value={cancelReason} rows={3} aria-describedby={`${cancelReasonId}-hint`}
          aria-invalid={formError !== null} onChange={(event) => { setCancelReason(event.target.value); setFormError(null) }} />
        <p id={`${cancelReasonId}-hint`} className={styles.hint}>{T.reasonHint}</p>
        <Button type="button" variant="danger" onClick={startCancel}>{T.cancel}</Button>
      </div> : null}
      {formError !== null ? <p role="alert" className={styles.error}>{formError}</p> : null}
    </>
  }

  return <>
    {notice}
    <section className={styles.decisionCard} aria-labelledby={headingId} aria-busy={busy} data-testid="purchase-order-future-allocations">
      <h2 id={headingId}>{T.heading}</h2>
      <p>{T.note}</p>
      {body}
    </section>
  </>
}
