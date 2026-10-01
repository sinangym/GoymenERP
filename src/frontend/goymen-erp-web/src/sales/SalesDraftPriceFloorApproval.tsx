import { useRef, useState } from 'react'
import type { DraftDetail, DraftMutation, DraftPriceFloorExceptionApprovalRequest } from './contract'
import { sendDraftPriceFloorExceptionApproval } from './api'
import { SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY } from './capabilities'
import { LINE_PRICING_TEXT as T } from './salesText'
import { useDraftLinePricing } from './useDraftLinePricing'
import {
  abandon, applyOutcome, beginSend, decideSubmit, generateOrderAttemptKey, isOutcomeStillUnknown, liveSnapshot,
  orderAttemptBinding, type OrderAttemptSnapshot,
} from './orderAttempt'
import { recallPriceFloorApproval, rememberPriceFloorApproval, type PriceFloorApprovalAttempt } from './pendingPriceFloorApprovals'
import { hasCapability, type ShellState } from '../shell/shellState'
import styles from './SalesPages.module.css'

const scopeOf = (draftId: string) => `sales.draft.price-floor-exception-approval:${draftId}`

/** The narrow checker control. It sends only server-reported pending snapshots and preserves an uncertain keyed attempt. */
export function SalesDraftPriceFloorApproval({ shell, detail, companyId, onRecorded }: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly companyId: string
  readonly onRecorded: () => void
}) {
  const query = useDraftLinePricing(shell, detail, companyId)
  const [initial] = useState(() => recallPriceFloorApproval(orderAttemptBinding(shell, scopeOf(detail.draft.id))))
  const [attempt, setAttempt] = useState<PriceFloorApprovalAttempt>(() => initial ?? { phase: 'idle' })
  const [reasons, setReasons] = useState<Record<string, string>>(() => Object.fromEntries(
    initial === null || initial.phase === 'idle' ? [] : initial.snapshot.payload.lines.map((line) => [line.lineId, line.reason]),
  ))
  const attemptRef = useRef(attempt)
  const pricing = query.data?.type === 'ok-single' ? query.data.data : null
  const pending = pricing?.lines.filter((line) => line.floorException !== null && line.floorException.approvedBy === null) ?? []
  const unresolvedSnapshot = liveSnapshot(attempt)
  const approvalRows = unresolvedSnapshot === null
    ? pending.map((line) => ({ lineId: line.lineId, lineNo: line.lineNo }))
    : unresolvedSnapshot.payload.lines.map((line) => ({
        lineId: line.lineId,
        lineNo: pricing?.lines.find((candidate) => candidate.lineId === line.lineId)?.lineNo ?? 0,
      }))

  function setAttemptState(next: PriceFloorApprovalAttempt) {
    attemptRef.current = next
    setAttempt(next)
  }

  async function send(snapshot: OrderAttemptSnapshot<DraftPriceFloorExceptionApprovalRequest>) {
    const previous = attemptRef.current
    const inFlight = beginSend<DraftPriceFloorExceptionApprovalRequest, DraftMutation>(snapshot)
    setAttemptState(inFlight)
    rememberPriceFloorApproval(snapshot.binding, inFlight)
    const outcome = await sendDraftPriceFloorExceptionApproval(snapshot.payload, {
      idempotencyKey: snapshot.key, companyId: snapshot.companyId, draftId: detail.draft.id,
    })
    const next = applyOutcome(inFlight, snapshot, orderAttemptBinding(shell, scopeOf(detail.draft.id)), outcome, previous)
    setAttemptState(next)
    rememberPriceFloorApproval(snapshot.binding, next)
    if (next.phase === 'completed' || (next.phase === 'failed' && next.failure === 'conflict')) onRecorded()
  }

  if (!hasCapability(shell, SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY) || pricing === null
    || (approvalRows.length === 0 && attempt.phase === 'idle')) return null
  const frozen = attempt.phase !== 'idle'

  function start() {
    const lines = pending.map((line) => ({ lineId: line.lineId, reason: (reasons[line.lineId] ?? '').trim() }))
    if (lines.some((line) => line.reason === '')) return
    const binding = orderAttemptBinding(shell, scopeOf(detail.draft.id))
    if (binding === null) return
    const decision = decideSubmit(attemptRef.current, {
      path: `${detail.draft.id}/below-floor-exception-approval`, binding, companyId,
      payload: { expectedVersion: pricing.version, lines },
    }, generateOrderAttemptKey)
    if (decision.type === 'send') void send(decision.snapshot)
  }

  function retry() {
    const snapshot = liveSnapshot(attemptRef.current)
    if (snapshot !== null && attemptRef.current.phase !== 'submitting') void send(snapshot)
  }

  function leave() {
    const next = abandon(attemptRef.current)
    setAttemptState(next)
    if (next.phase !== 'idle') rememberPriceFloorApproval(next.snapshot.binding, next)
  }

  const notice = attempt.phase === 'submitting' ? T.floorApproval.submitting
    : isOutcomeStillUnknown(attempt) ? T.floorApproval.unknown
      : attempt.phase === 'failed' ? T.floorApproval.failed : null

  return (
    <section className={styles.actions} aria-label={T.floorApproval.heading} data-testid="sales-price-floor-approval">
      <h3>{T.floorApproval.heading}</h3>
      <p className={styles.muted}>{T.floorApproval.note}</p>
      {approvalRows.map((line) => (
        <label key={line.lineId} htmlFor={`sales-floor-reason-${line.lineId}`}>
          {T.floorApproval.reason(line.lineNo)}
          <textarea id={`sales-floor-reason-${line.lineId}`} value={reasons[line.lineId] ?? ''} disabled={frozen} maxLength={4000}
            onChange={(event) => setReasons((current) => ({ ...current, [line.lineId]: event.target.value }))} />
        </label>
      ))}
      {notice === null ? null : <p role={attempt.phase === 'failed' ? 'alert' : 'status'}>{notice}</p>}
      {attempt.phase === 'idle' ? (
        <button type="button" disabled={pending.some((line) => (reasons[line.lineId] ?? '').trim() === '')} onClick={start}>
          {T.floorApproval.approve}
        </button>
      ) : null}
      {isOutcomeStillUnknown(attempt) ? (
        <>
          <button type="button" className={styles.secondaryButton} onClick={retry}>{T.floorApproval.retry}</button>
          <button type="button" className={styles.secondaryButton} onClick={leave}>{T.floorApproval.abandon}</button>
        </>
      ) : null}
      {attempt.phase === 'failed' && !isOutcomeStillUnknown(attempt) ? (
        <button type="button" className={styles.secondaryButton} onClick={() => setAttemptState({ phase: 'idle' })}>
          {T.floorApproval.correct}
        </button>
      ) : null}
    </section>
  )
}
