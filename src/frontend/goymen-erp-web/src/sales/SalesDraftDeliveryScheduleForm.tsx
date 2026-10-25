/**
 * DEC-SAL-003 (KILITLI): "termin güncellemesi planlama/sevkiyat rolündedir ve yeniden onay gerektirmez, ancak auditlidir."
 * The S-04 control that moves the requested delivery dates of the header, every line and every existing slice. Dates
 * only: slice quantities are shown, never edited; no slice is added or removed. The approval state does not change.
 *
 * Shown only when the shell publishes the `sales.orders.delivery-schedule.write` control; the server measures the grant
 * afresh on every send. One idempotent attempt per snapshot (the commercial revision's rules): an unknown outcome keeps
 * its key and its values, a retry resends the same snapshot, and a recorded change re-reads the detail.
 */
import { useEffect, useRef, useState } from 'react'
import { sendDraftDeliverySchedule } from './api'
import {
  buildDeliveryScheduleRequest,
  deliveryScheduleRowsFromDetail,
  type DeliveryScheduleRows,
} from './deliverySchedule'
import { APPROVAL_STATE, CANCELLED_STATE, SALES_PROBLEM_CODE, type DraftDeliveryScheduleRequest, type DraftDetail, type DraftMutation } from './contract'
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
import { SALES_DELIVERY_SCHEDULE_WRITE_CAPABILITY } from './capabilities'
import { ORDER_DETAIL_TEXT, problemCodeText } from './salesText'
import { recallDeliveryScheduleAttempt, rememberDeliveryScheduleAttempt } from './pendingDeliverySchedules'
import { hasCapability, type ShellState } from '../shell/shellState'
import styles from './SalesPages.module.css'
import { Button } from '../ui/Button'

type ScheduleAttempt = OrderAttemptState<DraftDeliveryScheduleRequest, DraftMutation>

const T = ORDER_DETAIL_TEXT.deliverySchedule
const scopeOf = (draftId: string) => `sales.draft.delivery-schedule:${draftId}`

export function SalesDraftDeliveryScheduleForm({
  shell,
  detail,
  companyId,
  onRecorded,
}: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly companyId: string
  readonly onRecorded: () => void
}) {
  const [initial] = useState(() => recallDeliveryScheduleAttempt(orderAttemptBinding(shell, scopeOf(detail.draft.id))))
  const [rows, setRows] = useState<DeliveryScheduleRows>(() => initial?.rows ?? deliveryScheduleRowsFromDetail(detail))
  const [attempt, setAttempt] = useState<ScheduleAttempt>(() => initial?.attempt ?? { phase: 'idle' })
  const [localError, setLocalError] = useState<string | null>(null)
  const attemptRef = useRef(attempt)

  useEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  useEffect(() => {
    if (attemptRef.current.phase !== 'submitting' && !isOutcomeStillUnknown(attemptRef.current)) {
      setRows(deliveryScheduleRowsFromDetail(detail))
    }
  }, [detail])

  if (!hasCapability(shell, SALES_DELIVERY_SCHEDULE_WRITE_CAPABILITY)) return null

  // Withdrawn (D2) and Cancelled (DEC-SAL-005) are terminal: the dates of a closed order do not move.
  const withdrawn = detail.draft.approvalState === APPROVAL_STATE.withdrawn || detail.draft.state === CANCELLED_STATE

  async function send(snapshot: OrderAttemptSnapshot<DraftDeliveryScheduleRequest>, submittedRows: DeliveryScheduleRows) {
    const previous = attemptRef.current
    const inFlight = beginSend<DraftDeliveryScheduleRequest, DraftMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    rememberDeliveryScheduleAttempt(snapshot.binding, inFlight, submittedRows)
    const outcome = await sendDraftDeliverySchedule(snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      draftId: detail.draft.id,
    })
    const next = applyOutcome(inFlight, snapshot, orderAttemptBinding(shell, scopeOf(detail.draft.id)), outcome, previous)
    attemptRef.current = next
    setAttempt(next)
    rememberDeliveryScheduleAttempt(snapshot.binding, next, submittedRows)
    if (next.phase === 'completed') {
      setRows(deliveryScheduleRowsFromDetail(next.result.detail))
      onRecorded()
    } else if (next.phase === 'failed' && next.failure === 'conflict') {
      onRecorded()
    }
  }

  function start() {
    setLocalError(null)
    const built = buildDeliveryScheduleRequest(detail.draft.version, rows, deliveryScheduleRowsFromDetail(detail))
    if (!built.ok) {
      setLocalError(built.reason === 'invalid-header-date' ? T.invalidHeaderDate
        : built.reason === 'no-change' ? T.noChange
          : built.reason === 'invalid-line-date' ? T.invalidLineDate(built.lineNo)
            : T.sliceDateRequired(built.lineNo))
      return
    }
    const binding = orderAttemptBinding(shell, scopeOf(detail.draft.id))
    if (binding === null) return
    const decision = decideSubmit(
      attemptRef.current,
      { path: `${detail.draft.id}/delivery-schedule`, binding, companyId, payload: built.payload },
      generateOrderAttemptKey,
    )
    if (decision.type === 'send') void send(decision.snapshot, rows)
  }

  function retry() {
    const snapshot = liveSnapshot(attemptRef.current)
    if (snapshot !== null && attemptRef.current.phase !== 'submitting') void send(snapshot, rows)
  }

  function leave() {
    const next = abandon(attemptRef.current)
    attemptRef.current = next
    setAttempt(next)
    if (next.phase !== 'idle') rememberDeliveryScheduleAttempt(next.snapshot.binding, next, rows)
  }

  function changeLine(lineId: string, date: string) {
    setRows((current) => ({ ...current, lines: current.lines.map((line) => (line.lineId === lineId ? { ...line, date } : line)) }))
  }

  function changeSlice(lineId: string, sliceId: string, date: string) {
    setRows((current) => ({
      ...current,
      lines: current.lines.map((line) => (line.lineId !== lineId ? line : {
        ...line,
        slices: line.slices.map((slice) => (slice.sliceId === sliceId ? { ...slice, date } : slice)),
      })),
    }))
  }

  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  const disabled = frozen || withdrawn
  const notice = scheduleNotice(attempt)

  return (
    <div className={styles.actions} data-testid="sales-delivery-schedule-form">
      <h3>{T.heading}</h3>
      <p className={styles.muted}>{T.description}</p>
      {withdrawn ? <p className={styles.muted}>{T.stateUnavailable}</p> : null}
      {localError === null ? null : <p role="alert" className={styles.notice}>{localError}</p>}
      {notice === null ? null : <p role={attempt.phase === 'failed' ? 'alert' : 'status'} className={styles.notice}>{notice}</p>}
      <fieldset className={styles.line} disabled={disabled}>
        <legend>{T.headerLegend}</legend>
        <label htmlFor="sales-schedule-header-date">{T.headerDate}</label>
        <input
          id="sales-schedule-header-date"
          type="date"
          value={rows.headerDate}
          onChange={(event) => setRows((current) => ({ ...current, headerDate: event.target.value }))}
        />
      </fieldset>
      {rows.lines.map((line) => (
        <fieldset key={line.lineId} className={styles.line} disabled={disabled} data-testid={`sales-schedule-line-${line.lineNo}`}>
          <legend>{line.lineNo}. {line.productName}</legend>
          <label htmlFor={`sales-schedule-line-date-${line.lineId}`}>{T.lineDate}</label>
          <input
            id={`sales-schedule-line-date-${line.lineId}`}
            type="date"
            value={line.date}
            onChange={(event) => changeLine(line.lineId, event.target.value)}
          />
          {line.slices.map((slice, index) => (
            <div key={slice.sliceId}>
              <label htmlFor={`sales-schedule-slice-${slice.sliceId}`}>{T.sliceDate(index + 1, slice.quantity)}</label>
              <input
                id={`sales-schedule-slice-${slice.sliceId}`}
                type="date"
                required
                value={slice.date}
                onChange={(event) => changeSlice(line.lineId, slice.sliceId, event.target.value)}
              />
            </div>
          ))}
        </fieldset>
      ))}
      {isOutcomeStillUnknown(attempt) && attempt.phase !== 'submitting' ? (
        <>
          <Button type="button" variant="secondary" onClick={retry}>{T.retry}</Button>
          <Button type="button" variant="ghost" className={styles.linkButton} onClick={leave}>{T.abandon}</Button>
        </>
      ) : null}
      <Button type="button" variant="primary" disabled={disabled} onClick={start}>
        {T.submit}
      </Button>
    </div>
  )
}

function scheduleNotice(attempt: ScheduleAttempt): string | null {
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
