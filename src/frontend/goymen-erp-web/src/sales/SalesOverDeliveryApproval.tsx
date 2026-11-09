/**
 * SAT-D30 / SAT-D168 / SAT-D213 / SAT-D216 — "Fazla teslimi onayla" on S-04, inside the status card's realization detail, behind
 * the `sales.orders.over-delivery.approve` control the shell publishes only to an actor holding that name beside the read.
 *
 *  - Only the lines the SERVER answers as `AwaitingApproval` are offered; the screen judges no band, tolerance or excess — the
 *    excess shown is the server's deviation (SAT-D15/D30).
 *  - The command names the shipped total the user sees (SAT-D213): when the Logistics total moved, the server refuses and the
 *    status is read again.
 *  - SAT-D216 is the server's rule: a shipper approving their own over-delivery, or an owner that does not name the shipper, is
 *    refused and said in Turkish; nothing is assumed here.
 *  - One idempotent attempt per line snapshot (the force-close's rules): an unknown outcome keeps its key, a retry resends the
 *    same snapshot, a recorded approval re-reads the detail (and with it the status).
 */
import { useEffect, useId, useRef, useState } from 'react'
import { sendDraftOverDeliveryApproval } from './api'
import { SALES_PROBLEM_CODE, type DraftDetail, type DraftMutation, type DraftOverDeliveryApprovalRequest } from './contract'
import {
  abandon,
  applyOutcome,
  beginSend,
  decideSubmit,
  generateOrderAttemptKey,
  isOutcomeStillUnknown,
  liveSnapshot,
  orderAttemptBinding,
  type OrderAttemptSnapshot,
  type OrderAttemptState,
} from './orderAttempt'
import { SALES_OVER_DELIVERY_APPROVE_CAPABILITY } from './capabilities'
import { STATUS_AXIS_STATE, type OrderStatus } from './orderStatus'
import { OVER_DELIVERY_TEXT as T, problemCodeText } from './salesText'
import { hasCapability, type ShellState } from '../shell/shellState'
import { Button } from '../ui/Button'
import styles from './SalesPages.module.css'

type OverDeliveryAttempt = OrderAttemptState<DraftOverDeliveryApprovalRequest, DraftMutation>

const scopeOf = (draftId: string) => `sales.draft.over-delivery-approval:${draftId}`

export function SalesOverDeliveryApproval({
  shell,
  detail,
  status,
  companyId,
  onRecorded,
}: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly status: OrderStatus
  readonly companyId: string
  readonly onRecorded: () => void
}) {
  const headingId = useId()
  const [attempt, setAttempt] = useState<OverDeliveryAttempt>({ phase: 'idle' })
  const attemptRef = useRef(attempt)

  useEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  const awaiting = status.realization.lines.filter(
    (line) => line.state === STATUS_AXIS_STATE.awaitingApproval && line.shipped !== null,
  )
  if (!hasCapability(shell, SALES_OVER_DELIVERY_APPROVE_CAPABILITY) || companyId !== detail.draft.companyId) return null
  if (awaiting.length === 0 && attempt.phase === 'idle') return null

  async function send(snapshot: OrderAttemptSnapshot<DraftOverDeliveryApprovalRequest>) {
    const previous = attemptRef.current
    const inFlight = beginSend<DraftOverDeliveryApprovalRequest, DraftMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    const outcome = await sendDraftOverDeliveryApproval(snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      draftId: detail.draft.id,
    })
    const next = applyOutcome(inFlight, snapshot, orderAttemptBinding(shell, scopeOf(detail.draft.id)), outcome, previous)
    attemptRef.current = next
    setAttempt(next)
    // A recorded approval, or a conflict (the order, the total or the shipper moved): the detail and the status are read again.
    if (next.phase === 'completed' || (next.phase === 'failed' && next.failure === 'conflict')) onRecorded()
  }

  function approve(lineId: string, shipped: string) {
    const binding = orderAttemptBinding(shell, scopeOf(detail.draft.id))
    if (binding === null) return
    const payload: DraftOverDeliveryApprovalRequest = { expectedVersion: detail.draft.version, lineId, shipped }
    const decision = decideSubmit(
      attemptRef.current,
      { path: `${detail.draft.id}/over-delivery-approval/${lineId}`, binding, companyId, payload },
      generateOrderAttemptKey,
    )
    if (decision.type === 'send') void send(decision.snapshot)
  }

  function retry() {
    const snapshot = liveSnapshot(attemptRef.current)
    if (snapshot !== null && attemptRef.current.phase !== 'submitting') void send(snapshot)
  }

  function leave() {
    const next = abandon(attemptRef.current)
    attemptRef.current = next
    setAttempt(next)
  }

  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  const notice = overDeliveryNotice(attempt)
  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid="sales-over-delivery">
      <h3 id={headingId}>{T.heading}</h3>
      <p className={styles.muted}>{T.description}</p>
      {notice === null ? null : (
        <p role={attempt.phase === 'failed' ? 'alert' : 'status'} className={styles.notice} data-testid="sales-over-delivery-notice">
          {notice}
        </p>
      )}
      {isOutcomeStillUnknown(attempt) && attempt.phase !== 'submitting' ? (
        <div className={styles.actions}>
          <Button type="button" variant="secondary" onClick={retry}>{T.retry}</Button>
          <Button type="button" variant="ghost" className={styles.linkButton} onClick={leave}>{T.abandon}</Button>
        </div>
      ) : null}
      {awaiting.length === 0 ? (
        <p className={styles.muted} data-testid="sales-over-delivery-empty">{T.empty}</p>
      ) : (
        <div className={styles.tableWrap}>
          <table className={styles.table} data-testid="sales-over-delivery-lines">
            <caption className={styles.caption}>{T.caption}</caption>
            <thead><tr>
              <th scope="col">{T.lineNo}</th><th scope="col">{T.ordered}</th><th scope="col">{T.shipped}</th>
              <th scope="col">{T.excess}</th><th scope="col">{T.action}</th>
            </tr></thead>
            <tbody>{awaiting.map((line) => (
              <tr key={line.lineId} data-testid={`sales-over-delivery-line-${line.lineNo}`}>
                <th scope="row">{line.lineNo}</th>
                <td className={styles.numericValue}>{line.ordered}</td>
                <td className={styles.numericValue}>{line.shipped}</td>
                <td className={styles.numericValue}>{line.deviation}</td>
                <td>
                  <Button
                    type="button"
                    variant="primary"
                    disabled={frozen}
                    aria-label={T.approveLabel(line.lineNo)}
                    onClick={() => approve(line.lineId, line.shipped as string)}
                    data-testid={`sales-over-delivery-approve-${line.lineNo}`}
                  >
                    {T.approve}
                  </Button>
                </td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      )}
    </section>
  )
}

function overDeliveryNotice(attempt: OverDeliveryAttempt): string | null {
  switch (attempt.phase) {
    case 'idle': return null
    case 'submitting': return T.submitting
    case 'unknown':
    case 'abandoned': return T.unknown
    case 'completed': return attempt.result.isReplay ? T.replayed : T.completed
    case 'failed': {
      const code = attempt.problem?.code ?? null
      const codeText = code === SALES_PROBLEM_CODE.versionConflict ? T.versionConflict : problemCodeText(code)
      const base = attempt.failure === 'unauthenticated' ? T.unauthenticated
        : attempt.failure === 'forbidden' ? T.forbidden
          : attempt.failure === 'not-found' ? T.notFound
            : attempt.failure === 'conflict' ? T.conflict
              : attempt.failure === 'validation' ? T.validation
                : T.unexpected
      return codeText === null ? base : `${base} ${codeText}`
    }
    default: return null
  }
}
