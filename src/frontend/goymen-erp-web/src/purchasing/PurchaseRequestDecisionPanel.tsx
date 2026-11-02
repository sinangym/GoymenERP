import { useCallback, useEffect, useId, useRef, useState } from 'react'
import type { PurchaseRequestSummary } from './contract'
import { decidePurchaseRequest, type RequestDecisionKind, type RequestDecisionOutcome } from './requestDecisionApi'
import {
  pendingPurchaseRequestDecisionKey,
  recallPendingPurchaseRequestDecision,
  releasePendingPurchaseRequestDecision,
  reservePendingPurchaseRequestDecision,
  type PendingPurchaseRequestDecision,
} from './pendingRequestStore'
import { REQUEST_DECISION_PROBLEM_TEXT, REQUEST_DECISION_TEXT as T } from './purchasingText'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './purchasing.module.css'

const MAXIMUM_REASON_LENGTH = 4000

type Attempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'deciding'; readonly snapshot: PendingPurchaseRequestDecision }
  | { readonly phase: 'unknown'; readonly snapshot: PendingPurchaseRequestDecision; readonly outcome: Extract<RequestDecisionOutcome, { type: 'unknown' }> }
  | { readonly phase: 'applied'; readonly kind: RequestDecisionKind; readonly replayed: boolean }
  | { readonly phase: 'failed'; readonly status: number; readonly code: string | null }

export interface PurchaseRequestDecisionPanelProps {
  readonly sessionEpoch: number
  readonly fenceKey: string
  readonly actorId: string
  readonly companyId: string
  readonly request: PurchaseRequestSummary
  /** Re-reads the request after a definite answer (applied or 409), as the submit control does. */
  readonly onSettled: () => Promise<unknown>
}

/**
 * A1-1 (plan API-01; ALS-D2, No.161 §1): the purchase manager's decision on a request, shown only behind the
 * `purchasing.requests.approve` child capability (the caller measures it). Approve from Submitted; reject from
 * Submitted or Approved with a required reason. The backend re-measures the name, the version, maker ≠ checker and the
 * bound approved order on every call; this panel only keeps an unresolved snapshot (same key, version and reason) for
 * an explicit resend and re-reads the record after a definite answer.
 */
export function PurchaseRequestDecisionPanel({ sessionEpoch, fenceKey, actorId, companyId, request, onSettled }: PurchaseRequestDecisionPanelProps) {
  const boundary = useAuthBoundary()
  const reasonId = useId()
  const storeKey = pendingPurchaseRequestDecisionKey(sessionEpoch, actorId, companyId, request.id)
  const [attempt, setAttempt] = useState<Attempt>(() => {
    const recalled = recallPendingPurchaseRequestDecision(storeKey)
    return recalled === null ? { phase: 'idle' } : { phase: 'unknown', snapshot: recalled, outcome: { type: 'unknown', reason: 'network-error' } }
  })
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const inFlight = useRef(false)
  const responseGeneration = useRef(0)
  useEffect(() => { inFlight.current = false; return () => { responseGeneration.current += 1 } }, [fenceKey])

  const send = useCallback(async (kind: RequestDecisionKind, retry?: PendingPurchaseRequestDecision) => {
    if (inFlight.current) return
    let snapshot: PendingPurchaseRequestDecision
    if (retry !== undefined) {
      snapshot = retry
    } else {
      const text = kind === 'rejection' ? reason : null
      if (text !== null && text.trim().length === 0) { setReasonError(T.reasonRequired); return }
      if (text !== null && text.length > MAXIMUM_REASON_LENGTH) { setReasonError(T.reasonTooLong); return }
      setReasonError(null)
      snapshot = {
        actorId, companyId, requestId: request.id, kind, expectedVersion: request.version,
        idempotencyKey: globalThis.crypto.randomUUID(), reason: text,
      }
    }
    if (snapshot.actorId !== actorId || snapshot.companyId !== companyId || snapshot.requestId !== request.id) return
    inFlight.current = true
    const generation = ++responseGeneration.current
    reservePendingPurchaseRequestDecision(storeKey, snapshot)
    setAttempt({ phase: 'deciding', snapshot })
    const security = boundary.capture()
    const result = await decidePurchaseRequest(companyId, snapshot.requestId, snapshot.kind, snapshot.expectedVersion, snapshot.idempotencyKey,
      snapshot.reason ?? undefined)
    if (responseGeneration.current !== generation) return
    inFlight.current = false
    if (result.type === 'failed' && (result.status === 401 || result.status === 403)) boundary.report(result.status, security)
    if (result.type === 'unknown') { setAttempt({ phase: 'unknown', snapshot, outcome: result }); return }
    releasePendingPurchaseRequestDecision(storeKey, snapshot.idempotencyKey)
    setAttempt(result.type === 'applied'
      ? { phase: 'applied', kind: snapshot.kind, replayed: result.replayed }
      : { phase: 'failed', status: result.status, code: result.code })
    if (result.type === 'applied') setReason('')
    if (result.type === 'applied' || result.status === 409) await onSettled()
  }, [actorId, boundary, companyId, onSettled, reason, request.id, request.version, storeKey])

  const unresolved = attempt.phase === 'unknown' || attempt.phase === 'deciding'
  const notice = attempt.phase === 'applied'
    ? <p role="status" className={styles.success}>{attempt.replayed ? T.replayed : attempt.kind === 'approval' ? T.approved : T.rejected}</p>
    : attempt.phase === 'failed'
      ? <p role="alert" className={styles.error}>{T.failed} ({attempt.code ?? attempt.status}). {attempt.code !== null && REQUEST_DECISION_PROBLEM_TEXT[attempt.code] !== undefined ? `${REQUEST_DECISION_PROBLEM_TEXT[attempt.code]} ` : ''}{attempt.status === 409 ? T.reread : ''}</p>
      : null
  const canApprove = request.status === 'Submitted'
  const canReject = request.status === 'Submitted' || request.status === 'Approved'
  if (!canApprove && !canReject && !unresolved) return notice

  const pending = attempt.phase === 'unknown' || attempt.phase === 'deciding' ? attempt.snapshot : null
  const busy = attempt.phase === 'deciding'
  return <>
    {notice}
    <section className={styles.decisionCard} aria-busy={busy}>
      <h2>{T.heading}</h2>
      <p>{T.note}</p>
      {attempt.phase === 'unknown' ? <p role="alert" className={styles.warning}>{T.unknown}</p> : null}
      {pending !== null ? (
        <Button type="button" variant="primary" disabled={busy} onClick={() => void send(pending.kind, pending)}>
          {busy ? (pending.kind === 'approval' ? T.approving : T.rejecting) : pending.kind === 'approval' ? T.retryApprove : T.retryReject}
        </Button>
      ) : <>
        {canApprove ? <Button type="button" variant="primary" onClick={() => void send('approval')}>{T.approve}</Button> : null}
        {canReject ? <div className={styles.decisionReason}>
          <label htmlFor={reasonId}>{T.reasonLabel}</label>
          <textarea id={reasonId} value={reason} rows={3}
            aria-invalid={reasonError !== null} aria-describedby={`${reasonId}-hint`}
            onChange={(event) => { setReason(event.target.value); setReasonError(null) }} />
          <p id={`${reasonId}-hint`} className={styles.hint}>{T.reasonHint}</p>
          {reasonError !== null ? <p role="alert" className={styles.error}>{reasonError}</p> : null}
          <Button type="button" variant="danger" onClick={() => void send('rejection')}>{T.reject}</Button>
        </div> : null}
      </>}
    </section>
  </>
}
