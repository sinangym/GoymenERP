import { useCallback, useEffect, useId, useRef, useState } from 'react'
import type { PurchaseRequestSummary } from './contract'
import { decidePurchaseRequest, type RequestDecisionOutcome } from './requestDecisionApi'
import {
  pendingPurchaseRequestWithdrawalKey,
  recallPendingPurchaseRequestWithdrawal,
  releasePendingPurchaseRequestWithdrawal,
  reservePendingPurchaseRequestWithdrawal,
  type PendingPurchaseRequestWithdrawal,
} from './pendingRequestStore'
import { REQUEST_WITHDRAWAL_PROBLEM_TEXT, REQUEST_WITHDRAWAL_TEXT as T } from './purchasingText'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './purchasing.module.css'

const MAXIMUM_REASON_LENGTH = 4000

type Attempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'withdrawing'; readonly snapshot: PendingPurchaseRequestWithdrawal }
  | { readonly phase: 'unknown'; readonly snapshot: PendingPurchaseRequestWithdrawal; readonly outcome: Extract<RequestDecisionOutcome, { type: 'unknown' }> }
  | { readonly phase: 'applied'; readonly replayed: boolean }
  | { readonly phase: 'failed'; readonly status: number; readonly code: string | null }

export interface PurchaseRequestWithdrawalPanelProps {
  readonly sessionEpoch: number
  readonly fenceKey: string
  readonly actorId: string
  readonly companyId: string
  readonly request: PurchaseRequestSummary
  /** Re-reads the request after a definite answer (applied or 409), as the submit and decision controls do. */
  readonly onSettled: () => Promise<unknown>
}

/**
 * A1-1b (plan API-01b; ALS-D46): the submitter withdraws their own Submitted request with a required reason. Shown only
 * behind the `purchasing.requests.withdraw` child capability and only to the recorded submitter (the caller measures
 * both); the backend re-measures the name, the version, "only the submitter" and a bound approved order on every call.
 * This panel only keeps an unresolved snapshot (same key, version and reason) for an explicit resend and re-reads the
 * record after a definite answer.
 */
export function PurchaseRequestWithdrawalPanel({ sessionEpoch, fenceKey, actorId, companyId, request, onSettled }: PurchaseRequestWithdrawalPanelProps) {
  const boundary = useAuthBoundary()
  const reasonId = useId()
  const storeKey = pendingPurchaseRequestWithdrawalKey(sessionEpoch, actorId, companyId, request.id)
  const [attempt, setAttempt] = useState<Attempt>(() => {
    const recalled = recallPendingPurchaseRequestWithdrawal(storeKey)
    return recalled === null ? { phase: 'idle' } : { phase: 'unknown', snapshot: recalled, outcome: { type: 'unknown', reason: 'network-error' } }
  })
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const inFlight = useRef(false)
  const responseGeneration = useRef(0)
  useEffect(() => { inFlight.current = false; return () => { responseGeneration.current += 1 } }, [fenceKey])

  const send = useCallback(async (retry?: PendingPurchaseRequestWithdrawal) => {
    if (inFlight.current) return
    let snapshot: PendingPurchaseRequestWithdrawal
    if (retry !== undefined) {
      snapshot = retry
    } else {
      if (reason.trim().length === 0) { setReasonError(T.reasonRequired); return }
      if (reason.length > MAXIMUM_REASON_LENGTH) { setReasonError(T.reasonTooLong); return }
      setReasonError(null)
      snapshot = {
        actorId, companyId, requestId: request.id, expectedVersion: request.version,
        idempotencyKey: globalThis.crypto.randomUUID(), reason,
      }
    }
    if (snapshot.actorId !== actorId || snapshot.companyId !== companyId || snapshot.requestId !== request.id) return
    inFlight.current = true
    const generation = ++responseGeneration.current
    reservePendingPurchaseRequestWithdrawal(storeKey, snapshot)
    setAttempt({ phase: 'withdrawing', snapshot })
    const security = boundary.capture()
    const result = await decidePurchaseRequest(companyId, snapshot.requestId, 'withdrawal', snapshot.expectedVersion, snapshot.idempotencyKey,
      snapshot.reason)
    if (responseGeneration.current !== generation) return
    inFlight.current = false
    if (result.type === 'failed' && (result.status === 401 || result.status === 403)) boundary.report(result.status, security)
    if (result.type === 'unknown') { setAttempt({ phase: 'unknown', snapshot, outcome: result }); return }
    releasePendingPurchaseRequestWithdrawal(storeKey, snapshot.idempotencyKey)
    setAttempt(result.type === 'applied'
      ? { phase: 'applied', replayed: result.replayed }
      : { phase: 'failed', status: result.status, code: result.code })
    if (result.type === 'applied') setReason('')
    if (result.type === 'applied' || result.status === 409) await onSettled()
  }, [actorId, boundary, companyId, onSettled, reason, request.id, request.version, storeKey])

  const unresolved = attempt.phase === 'unknown' || attempt.phase === 'withdrawing'
  const notice = attempt.phase === 'applied'
    ? <p role="status" className={styles.success}>{attempt.replayed ? T.replayed : T.withdrawn}</p>
    : attempt.phase === 'failed'
      ? <p role="alert" className={styles.error}>{T.failed} ({attempt.code ?? attempt.status}). {attempt.code !== null && REQUEST_WITHDRAWAL_PROBLEM_TEXT[attempt.code] !== undefined ? `${REQUEST_WITHDRAWAL_PROBLEM_TEXT[attempt.code]} ` : ''}{attempt.status === 409 ? T.reread : ''}</p>
      : null
  if (request.status !== 'Submitted' && !unresolved) return notice

  const pending = unresolved ? attempt.snapshot : null
  const busy = attempt.phase === 'withdrawing'
  return <>
    {notice}
    <section className={styles.decisionCard} aria-busy={busy}>
      <h2>{T.heading}</h2>
      <p>{T.note}</p>
      {attempt.phase === 'unknown' ? <p role="alert" className={styles.warning}>{T.unknown}</p> : null}
      {pending !== null ? (
        <Button type="button" variant="danger" disabled={busy} onClick={() => void send(pending)}>
          {busy ? T.withdrawing : T.retry}
        </Button>
      ) : <div className={styles.decisionReason}>
        <label htmlFor={reasonId}>{T.reasonLabel}</label>
        <textarea id={reasonId} value={reason} rows={3}
          aria-invalid={reasonError !== null} aria-describedby={`${reasonId}-hint`}
          onChange={(event) => { setReason(event.target.value); setReasonError(null) }} />
        <p id={`${reasonId}-hint`} className={styles.hint}>{T.reasonHint}</p>
        {reasonError !== null ? <p role="alert" className={styles.error}>{reasonError}</p> : null}
        <Button type="button" variant="danger" onClick={() => void send()}>{T.withdraw}</Button>
      </div>}
    </section>
  </>
}
