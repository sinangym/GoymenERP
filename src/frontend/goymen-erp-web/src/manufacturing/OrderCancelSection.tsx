/**
 * F-009 D6 (URT-D33): the order cancel INSIDE the order screen, drawn only for the `manufacturing.orders.cancel`
 * control. The endpoint is URT-S4's and not merged: no provider publishes the control yet, so nothing is drawn in a
 * running shell until that contract is confirmed (the transport's route and body are a draft, see ./commandApi.ts).
 *
 *  - The server decides every precondition (net output, net issue and WIP zero, no locked mass); this screen checks
 *    none of them and states them only as help.
 *  - One attempt is frozen once — the order version this screen read, the reason and the key — and a re-send of an
 *    UNKNOWN outcome sends that same snapshot. A new attempt is a new key.
 *  - After an accepted (or replayed) cancel, or a version conflict, the order is re-read; nothing is predicted.
 */

import { useState } from 'react'
import { cancelOrder, type FactFailure, type FactUnknown } from './commandApi'
import { newCommandId } from './commandId'
import type { OrderCancelSnapshot, ProductionOrderView } from './contract'
import { ORDER_CANCEL_TEXT, cancelFailureText, unknownText } from './manufacturingText'
import { Button } from '../ui/Button'
import styles from './OrderDetailPage.module.css'

type CancelAttempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'confirming' }
  | { readonly phase: 'sending'; readonly snapshot: OrderCancelSnapshot }
  | { readonly phase: 'applied'; readonly replayed: boolean }
  | { readonly phase: 'refused'; readonly failure: FactFailure }
  | { readonly phase: 'unknown'; readonly snapshot: OrderCancelSnapshot; readonly unknown: FactUnknown }

export function OrderCancelSection({
  order,
  companyId,
  onRefresh,
}: {
  readonly order: ProductionOrderView
  readonly companyId: string
  readonly onRefresh: () => Promise<void>
}) {
  const [attempt, setAttempt] = useState<CancelAttempt>({ phase: 'idle' })
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)

  const send = async (snapshot: OrderCancelSnapshot) => {
    setAttempt({ phase: 'sending', snapshot })
    const outcome = await cancelOrder(companyId, order.orderId, snapshot)
    if (outcome.type === 'applied') {
      setAttempt({ phase: 'applied', replayed: outcome.replayed })
      setReason('')
      await onRefresh()
      return
    }
    if (outcome.type === 'failed') {
      setAttempt({ phase: 'refused', failure: outcome.failure })
      if (outcome.failure.kind === 'version-conflict' || outcome.failure.kind === 'concurrent-change') {
        await onRefresh()
      }
      return
    }
    setAttempt({ phase: 'unknown', snapshot, unknown: outcome.unknown })
  }

  const confirm = () => {
    const trimmed = reason.trim()
    if (trimmed.length === 0) {
      setReasonError(ORDER_CANCEL_TEXT.reasonRequired)
      return
    }
    setReasonError(null)
    void send({ idempotencyKey: newCommandId(), expectedVersion: order.version, reason: trimmed })
  }

  return (
    <section aria-labelledby="mfg-cancel-heading" className={styles.section} data-testid="order-cancel">
      <h2 id="mfg-cancel-heading">{ORDER_CANCEL_TEXT.heading}</h2>
      <p className={styles.help}>{ORDER_CANCEL_TEXT.help}</p>
      {order.status !== 'Open' && attempt.phase !== 'unknown' ? (
        <p className={styles.help} data-testid="order-cancel-not-open">{ORDER_CANCEL_TEXT.notOpen}</p>
      ) : attempt.phase === 'confirming' ? (
        <div className={styles.section}>
          <div className={styles.form}>
            <div className={styles.control}>
              <label htmlFor="mfg-cancel-reason">{ORDER_CANCEL_TEXT.reason}</label>
              <textarea
                id="mfg-cancel-reason"
                value={reason}
                maxLength={256}
                rows={3}
                onChange={(event) => {
                  setReason(event.target.value)
                  setReasonError(null)
                }}
              />
            </div>
          </div>
          {reasonError === null ? null : <p role="alert">{reasonError}</p>}
          <p role="alert">{ORDER_CANCEL_TEXT.confirmQuestion}</p>
          <div className={styles.actions}>
            <Button type="button" onClick={confirm}>{ORDER_CANCEL_TEXT.confirm}</Button>
            <Button type="button" onClick={() => setAttempt({ phase: 'idle' })}>{ORDER_CANCEL_TEXT.cancel}</Button>
          </div>
        </div>
      ) : attempt.phase === 'unknown' ? (
        <div className={styles.actions}>
          <Button type="button" onClick={() => void send(attempt.snapshot)}>{ORDER_CANCEL_TEXT.resend}</Button>
        </div>
      ) : (
        <div className={styles.actions}>
          <Button
            type="button"
            onClick={() => setAttempt({ phase: 'confirming' })}
            disabled={attempt.phase === 'sending'}
            aria-busy={attempt.phase === 'sending'}
          >
            {attempt.phase === 'sending' ? ORDER_CANCEL_TEXT.sending : ORDER_CANCEL_TEXT.action}
          </Button>
        </div>
      )}
      {attempt.phase === 'applied' ? (
        <div className={styles.noticeOk} role="status" data-outcome={attempt.replayed ? 'replayed' : 'applied'}>
          <p className={styles.noticeTitle}>
            {attempt.replayed ? ORDER_CANCEL_TEXT.replayedTitle : ORDER_CANCEL_TEXT.appliedTitle}
          </p>
          <p>{attempt.replayed ? ORDER_CANCEL_TEXT.replayedDetail : ORDER_CANCEL_TEXT.appliedDetail}</p>
        </div>
      ) : null}
      {attempt.phase === 'refused' ? (
        <div className={styles.noticeError} role="alert" data-outcome="refused">
          <p className={styles.noticeTitle}>{ORDER_CANCEL_TEXT.refusedTitle}</p>
          <p data-testid="order-cancel-refusal">{cancelFailureText(attempt.failure)}</p>
        </div>
      ) : null}
      {attempt.phase === 'unknown' ? (
        <div className={styles.noticeWarn} role="alert" data-outcome="unknown">
          <p className={styles.noticeTitle}>{ORDER_CANCEL_TEXT.unknownTitle}</p>
          <p>{unknownText(attempt.unknown)}</p>
          <p>{ORDER_CANCEL_TEXT.unknownHelp}</p>
        </div>
      ) : null}
    </section>
  )
}
