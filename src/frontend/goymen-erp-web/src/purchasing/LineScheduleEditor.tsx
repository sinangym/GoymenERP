import { useCallback, useEffect, useId, useRef, useState } from 'react'
import type { PurchaseOrderLine, PurchaseOrderSummary } from './contract'
import { setPurchaseOrderLineSchedule, type LineScheduleFieldProblem, type LineScheduleOutcome } from './lineScheduleApi'
import { LINE_SCHEDULE_PROBLEM_TEXT, LINE_SCHEDULE_TEXT as T } from './lineScheduleText'
import {
  pendingLineScheduleKey,
  recallPendingLineSchedule,
  releasePendingLineSchedule,
  reservePendingLineSchedule,
  type PendingLineSchedule,
  type PendingLineScheduleSlice,
} from './pendingLineScheduleStore'
import { isDateOnly, parseQuantityInput } from '../sales/wirePrimitives'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './PurchaseOrderDetailPage.module.css'

type Attempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'sending'; readonly snapshot: PendingLineSchedule }
  | { readonly phase: 'unknown'; readonly snapshot: PendingLineSchedule; readonly outcome: Extract<LineScheduleOutcome, { type: 'unknown' }> }
  | { readonly phase: 'applied'; readonly replayed: boolean }
  | {
    readonly phase: 'failed'
    readonly status: number
    readonly code: string | null
    readonly problems: readonly LineScheduleFieldProblem[]
  }

/** One editable row: the raw text the user typed, sent only after a local shape check. */
interface Row {
  readonly sliceId: string
  readonly quantity: string
  readonly deliveryDate: string
  /**
   * The server's own quantity text this row was filled with, if any: left untouched it is sent back as received (the typed-input
   * separator rule would misread a server value such as `2.125` as ambiguous).
   */
  readonly serverQuantity: string | null
}

export interface LineScheduleEditorProps {
  readonly sessionEpoch: number
  /** The query namespace and route entry this control was opened in; a change drops any late answer. */
  readonly fenceKey: string
  readonly actorId: string
  readonly companyId: string
  readonly order: PurchaseOrderSummary
  readonly line: PurchaseOrderLine
  /** Re-reads the order after a definite answer (applied or 409), as the other order controls do. */
  readonly onSettled: () => Promise<unknown>
}

function newId(): string {
  return globalThis.crypto.randomUUID()
}

/**
 * The rows the form starts from: the server's current slices as received (each keeps its id, so a received slice stays the same
 * slice — ALS-D81), or one row with the line's ordered quantity and no date when nothing is recorded yet.
 */
function rowsOf(line: PurchaseOrderLine): Row[] {
  const current = line.deliverySchedule ?? []
  return current.length > 0
    ? current.map((slice) => ({
      sliceId: slice.sliceId, quantity: slice.quantity, deliveryDate: slice.deliveryDate, serverQuantity: slice.quantity,
    }))
    : [{ sliceId: newId(), quantity: line.ordered, deliveryDate: '', serverQuantity: line.ordered }]
}

function rowsOfSnapshot(snapshot: PendingLineSchedule): Row[] {
  return snapshot.slices.map((slice) => ({
    sliceId: slice.sliceId, quantity: slice.quantity, deliveryDate: slice.deliveryDate, serverQuantity: slice.quantity,
  }))
}

/** Shape only: a readable positive decimal and a calendar date per row. The sum and the locks are the server's. */
function checkRows(rows: readonly Row[]): { readonly slices: PendingLineScheduleSlice[] } | { readonly errors: string[] } {
  const errors: string[] = []
  const slices: PendingLineScheduleSlice[] = []
  rows.forEach((row, index) => {
    const position = index + 1
    const quantity = row.serverQuantity !== null && row.quantity === row.serverQuantity
      ? { ok: true as const, value: row.serverQuantity }
      : parseQuantityInput(row.quantity)
    if (!quantity.ok) {
      errors.push(quantity.error === 'required'
        ? T.quantityRequired(position)
        : quantity.error === 'not-positive' ? T.quantityNotPositive(position) : T.quantityInvalid(position))
    }
    if (!isDateOnly(row.deliveryDate)) errors.push(T.dateRequired(position))
    if (quantity.ok && isDateOnly(row.deliveryDate)) {
      slices.push({ sliceId: row.sliceId, quantity: quantity.value, deliveryDate: row.deliveryDate })
    }
  })
  return errors.length > 0 ? { errors } : { slices }
}

function problemText(code: string): string {
  return LINE_SCHEDULE_PROBLEM_TEXT[code] ?? code
}

/**
 * F-027 (UI-07, EK16 §2a; ALS-D94, ALS-D81): the termin change of one general order line behind the
 * `purchasing.orders.line-schedule.write` child capability (the caller measures it). On an approved order the change needs no
 * re-approval; every save is a new audited revision at the next order version. The control sends the slices as entered with the
 * version the page was read at, keeps an unresolved attempt (same key, version and slices) for an explicit resend with the form
 * frozen, re-reads the order after a definite answer (applied or 409) and reports 401/403 to the auth boundary. A technical line or
 * a cancelled order shows no form (the server would refuse it). The backend re-measures the name, the version, the state, the total
 * and the received-portion lock on every call; nothing here is a rule.
 */
export function LineScheduleEditor({ sessionEpoch, fenceKey, actorId, companyId, order, line, onSettled }: LineScheduleEditorProps) {
  const boundary = useAuthBoundary()
  const headingId = useId()
  const fieldId = useId()
  const storeKey = pendingLineScheduleKey(sessionEpoch, actorId, companyId, order.id, line.lineId)
  const [attempt, setAttempt] = useState<Attempt>(() => {
    const recalled = recallPendingLineSchedule(storeKey)
    return recalled === null ? { phase: 'idle' } : { phase: 'unknown', snapshot: recalled, outcome: { type: 'unknown', reason: 'network-error' } }
  })
  const [rows, setRows] = useState<Row[]>(() => {
    const recalled = recallPendingLineSchedule(storeKey)
    return recalled === null ? rowsOf(line) : rowsOfSnapshot(recalled)
  })
  const [shapeErrors, setShapeErrors] = useState<readonly string[]>([])
  const [syncedVersion, setSyncedVersion] = useState(order.version)
  const inFlight = useRef(false)
  const responseGeneration = useRef(0)
  useEffect(() => { inFlight.current = false; return () => { responseGeneration.current += 1 } }, [fenceKey])

  const unresolved = attempt.phase === 'unknown' || attempt.phase === 'sending'
  // A newer order read replaces the rows with the server's slices, unless an unresolved attempt still owns them.
  if (order.version !== syncedVersion && !unresolved) {
    setSyncedVersion(order.version)
    setRows(rowsOf(line))
    setShapeErrors([])
  }

  const send = useCallback(async (retry?: PendingLineSchedule) => {
    if (inFlight.current) return
    let snapshot: PendingLineSchedule
    if (retry !== undefined) {
      snapshot = retry
    } else {
      const checked = checkRows(rows)
      if ('errors' in checked) { setShapeErrors(checked.errors); return }
      setShapeErrors([])
      snapshot = {
        actorId, companyId, orderId: order.id, lineId: line.lineId, expectedVersion: order.version, idempotencyKey: newId(),
        slices: checked.slices,
      }
    }
    if (snapshot.actorId !== actorId || snapshot.companyId !== companyId || snapshot.orderId !== order.id || snapshot.lineId !== line.lineId) return
    inFlight.current = true
    const generation = ++responseGeneration.current
    reservePendingLineSchedule(storeKey, snapshot)
    setAttempt({ phase: 'sending', snapshot })
    const security = boundary.capture()
    const result = await setPurchaseOrderLineSchedule(
      companyId, snapshot.orderId, snapshot.lineId, snapshot.expectedVersion, snapshot.idempotencyKey, snapshot.slices)
    if (responseGeneration.current !== generation) return
    inFlight.current = false
    if (result.type === 'failed' && (result.status === 401 || result.status === 403)) boundary.report(result.status, security)
    if (result.type === 'unknown') { setAttempt({ phase: 'unknown', snapshot, outcome: result }); return }
    releasePendingLineSchedule(storeKey, snapshot.idempotencyKey)
    setAttempt(result.type === 'applied'
      ? { phase: 'applied', replayed: result.replayed }
      : { phase: 'failed', status: result.status, code: result.code, problems: result.problems })
    if (result.type === 'applied' || result.status === 409) await onSettled()
  }, [actorId, boundary, companyId, line.lineId, onSettled, order.id, order.version, rows, storeKey])

  const abandon = useCallback(() => {
    if (attempt.phase !== 'unknown') return
    releasePendingLineSchedule(storeKey, attempt.snapshot.idempotencyKey)
    setAttempt({ phase: 'idle' })
    setSyncedVersion(order.version)
    setRows(rowsOf(line))
  }, [attempt, line, order.version, storeKey])

  const notice = attempt.phase === 'applied'
    ? <p role="status" className={styles.success}>{attempt.replayed ? T.replayed : T.applied}</p>
    : attempt.phase === 'failed'
      ? <div role="alert" className={styles.error}>
        <p>{T.failed} ({attempt.code ?? attempt.status}). {attempt.code !== null && LINE_SCHEDULE_PROBLEM_TEXT[attempt.code] !== undefined
          ? `${LINE_SCHEDULE_PROBLEM_TEXT[attempt.code]} ` : ''}{attempt.status === 409 ? T.reread : ''}</p>
        {attempt.problems.length > 0
          ? <ul>{attempt.problems.map((problem, index) => <li key={`${problem.field}-${problem.code}-${index}`}>
            {problemText(problem.code)} ({problem.field})
          </li>)}</ul>
          : null}
      </div>
      : null
  const editable = line.kind === 'General' && order.status !== 'Cancelled'
  if (!editable && !unresolved) return notice

  const pending = unresolved ? attempt.snapshot : null
  const busy = attempt.phase === 'sending'
  const frozen = pending !== null
  const update = (index: number, change: Partial<Pick<Row, 'quantity' | 'deliveryDate'>>) =>
    setRows((current) => current.map((row, position) => position === index ? { ...row, ...change } : row))
  return <section aria-labelledby={headingId} aria-busy={busy} data-testid={`purchase-order-line-schedule-editor-${line.lineNo}`}>
    <h4 id={headingId}>{T.editHeading(line.lineNo)}</h4>
    <p>{T.editNote}</p>
    {notice}
    {attempt.phase === 'unknown' ? <p role="alert" className={styles.warning}>{T.unknown}</p> : null}
    <fieldset disabled={frozen} aria-labelledby={headingId}>
      {rows.map((row, index) => {
        const position = index + 1
        return <fieldset key={row.sliceId} data-testid={`purchase-order-line-schedule-row-${line.lineNo}-${position}`}>
          <legend>{T.editLegend(position)}</legend>
          <label htmlFor={`${fieldId}-q-${row.sliceId}`}>{T.editQuantity(position)}</label>
          <input id={`${fieldId}-q-${row.sliceId}`} type="text" inputMode="decimal" value={row.quantity}
            onChange={(event) => update(index, { quantity: event.target.value })} />
          <label htmlFor={`${fieldId}-d-${row.sliceId}`}>{T.editDeliveryDate(position)}</label>
          <input id={`${fieldId}-d-${row.sliceId}`} type="date" value={row.deliveryDate}
            onChange={(event) => update(index, { deliveryDate: event.target.value })} />
          {rows.length > 1
            ? <Button type="button" variant="ghost" onClick={() => setRows((current) => current.filter((_, at) => at !== index))}>
              {T.removeSlice(position)}
            </Button>
            : null}
        </fieldset>
      })}
      <Button type="button" variant="secondary"
        onClick={() => setRows((current) => [...current, { sliceId: newId(), quantity: '', deliveryDate: '', serverQuantity: null }])}>
        {T.addSlice}
      </Button>
    </fieldset>
    {shapeErrors.length > 0
      ? <ul role="alert" className={styles.error}>{shapeErrors.map((error) => <li key={error}>{error}</li>)}</ul>
      : null}
    {pending !== null
      ? <>
        <Button type="button" variant="primary" disabled={busy} onClick={() => void send(pending)}>{busy ? T.saving : T.retry}</Button>
        {attempt.phase === 'unknown' ? <Button type="button" variant="ghost" onClick={abandon}>{T.abandon}</Button> : null}
      </>
      : <Button type="button" variant="primary" onClick={() => void send()}>{T.save}</Button>}
  </section>
}
