import { useState } from 'react'
import type { DraftDetail, DraftPriceFloorExceptionApprovalRequest } from './contract'
import { sendDraftPriceFloorExceptionApproval } from './api'
import { SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY } from './capabilities'
import { LINE_PRICING_TEXT as T } from './salesText'
import { useDraftLinePricing } from './useDraftLinePricing'
import { hasCapability, type ShellState } from '../shell/shellState'
import styles from './SalesPages.module.css'

type Attempt = {
  readonly key: string
  readonly payload: DraftPriceFloorExceptionApprovalRequest
  readonly phase: 'submitting' | 'unknown' | 'failed'
  readonly message: string
}

/**
 * The narrow checker control. It never computes a floor or a net: only server-reported pending snapshots are submitted,
 * every one exactly once, with the checker-entered reason. An uncertain response retains the same key and body for retry.
 */
export function SalesDraftPriceFloorApproval({ shell, detail, companyId, onRecorded }: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly companyId: string
  readonly onRecorded: () => void
}) {
  const query = useDraftLinePricing(shell, detail, companyId)
  const [reasons, setReasons] = useState<Record<string, string>>({})
  const [attempt, setAttempt] = useState<Attempt | null>(null)
  const pricing = query.data?.type === 'ok-single' ? query.data.data : null
  const pending = pricing?.lines.filter((line) => line.floorException !== null && line.floorException.approvedBy === null) ?? []

  if (!hasCapability(shell, SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY) || pending.length === 0 || pricing === null) return null
  const frozen = attempt !== null

  async function send(key: string, payload: DraftPriceFloorExceptionApprovalRequest) {
    setAttempt({ key, payload, phase: 'submitting', message: T.floorApproval.submitting })
    const outcome = await sendDraftPriceFloorExceptionApproval(payload, {
      idempotencyKey: key, companyId, draftId: detail.draft.id,
    })
    if (outcome.type === 'succeeded') {
      setAttempt(null)
      setReasons({})
      onRecorded()
      return
    }
    if (outcome.type === 'network-error' || outcome.type === 'offline' || outcome.type === 'malformed') {
      setAttempt({ key, payload, phase: 'unknown', message: T.floorApproval.unknown })
      return
    }
    setAttempt({ key, payload, phase: 'failed', message: T.floorApproval.failed })
  }

  function submit() {
    const lines = pending.map((line) => ({ lineId: line.lineId, reason: (reasons[line.lineId] ?? '').trim() }))
    if (lines.some((line) => line.reason === '')) return
    const payload = { expectedVersion: pricing.version, lines }
    void send(crypto.randomUUID(), payload)
  }

  return (
    <section className={styles.actions} aria-label={T.floorApproval.heading} data-testid="sales-price-floor-approval">
      <h3>{T.floorApproval.heading}</h3>
      <p className={styles.muted}>{T.floorApproval.note}</p>
      {pending.map((line) => (
        <label key={line.lineId} htmlFor={`sales-floor-reason-${line.lineId}`}>
          {T.floorApproval.reason(line.lineNo)}
          <textarea id={`sales-floor-reason-${line.lineId}`} value={reasons[line.lineId] ?? ''} disabled={frozen}
            onChange={(event) => setReasons((current) => ({ ...current, [line.lineId]: event.target.value }))} />
        </label>
      ))}
      {attempt === null ? null : <p role={attempt.phase === 'failed' ? 'alert' : 'status'}>{attempt.message}</p>}
      <button type="button" disabled={frozen || pending.some((line) => (reasons[line.lineId] ?? '').trim() === '')} onClick={submit}>
        {T.floorApproval.approve}
      </button>
      {attempt?.phase === 'unknown' ? (
        <button type="button" className={styles.secondaryButton} onClick={() => void send(attempt.key, attempt.payload)}>
          {T.floorApproval.retry}
        </button>
      ) : null}
      {attempt?.phase === 'failed' ? (
        <button type="button" className={styles.secondaryButton} onClick={() => setAttempt(null)}>{T.floorApproval.correct}</button>
      ) : null}
    </section>
  )
}
