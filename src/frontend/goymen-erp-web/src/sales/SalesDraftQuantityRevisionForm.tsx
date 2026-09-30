import { useEffect, useId, useRef, useState } from 'react'
import { sendDraftQuantityRevision } from './api'
import { SALES_QUANTITY_WRITE_CAPABILITY } from './capabilities'
import { APPROVAL_STATE, CANCELLED_STATE, REV_0, SALES_PROBLEM_CODE, type DraftDetail, type DraftMutation, type DraftQuantityRevisionRequest } from './contract'
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
import { recallQuantityRevisionAttempt, rememberQuantityRevisionAttempt } from './pendingQuantityRevisions'
import { buildQuantityRevisionRequest, quantityRowsFromDetail, type QuantityRevisionRow } from './quantityRevision'
import { ORDER_DETAIL_TEXT as T, problemCodeText } from './salesText'
import { hasCapability, type ShellState } from '../shell/shellState'
import styles from './SalesPages.module.css'

type RevisionAttempt = OrderAttemptState<DraftQuantityRevisionRequest, DraftMutation>

const scopeOf = (draftId: string) => `sales.draft.quantity-revision:${draftId}`

/**
 * DEC-SAL-002 / AT-023 — "Revizyon oluştur" on S-04 behind the `sales.orders.quantity.write` control: every line's new
 * ordered quantity and a required reason, one keyed attempt (the commercial revision's attempt rules: an unknown outcome
 * keeps the key and retries the same payload). An approved order is archived by the server and re-enters approval; the
 * screen says so and re-reads the detail after a recorded revision. The server decides every rule.
 */
export function SalesDraftQuantityRevisionForm({
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
  const [initial] = useState(() => recallQuantityRevisionAttempt(orderAttemptBinding(shell, scopeOf(detail.draft.id))))
  const [open, setOpen] = useState(initial !== null)
  const [rows, setRows] = useState<readonly QuantityRevisionRow[]>(() => initial?.rows ?? quantityRowsFromDetail(detail))
  const [reason, setReason] = useState(initial?.reason ?? '')
  const [attempt, setAttempt] = useState<RevisionAttempt>(() => initial?.attempt ?? { phase: 'idle' })
  const [localError, setLocalError] = useState<string | null>(null)
  const attemptRef = useRef(attempt)
  attemptRef.current = attempt
  const reasonId = useId()
  const headingId = useId()

  useEffect(() => {
    if (attemptRef.current.phase !== 'submitting' && !isOutcomeStillUnknown(attemptRef.current)) {
      setRows(quantityRowsFromDetail(detail))
    }
  }, [detail])

  if (!hasCapability(shell, SALES_QUANTITY_WRITE_CAPABILITY)) return null

  const text = T.quantityRevision
  // DEC-SAL-005: a cancelled order is terminal; D2: withdrawn is terminal; a pending decision is made first.
  const stateAllowsRevision = detail.draft.state !== CANCELLED_STATE && (
    detail.draft.approvalState === APPROVAL_STATE.preparing ||
    detail.draft.approvalState === APPROVAL_STATE.rejected ||
    detail.draft.approvalState === APPROVAL_STATE.approved)

  async function send(snapshot: OrderAttemptSnapshot<DraftQuantityRevisionRequest>, submittedRows: readonly QuantityRevisionRow[], submittedReason: string) {
    const previous = attemptRef.current
    const inFlight = beginSend<DraftQuantityRevisionRequest, DraftMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    rememberQuantityRevisionAttempt(snapshot.binding, inFlight, submittedRows, submittedReason)
    const outcome = await sendDraftQuantityRevision(snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      draftId: detail.draft.id,
    })
    const next = applyOutcome(inFlight, snapshot, orderAttemptBinding(shell, scopeOf(detail.draft.id)), outcome, previous)
    attemptRef.current = next
    setAttempt(next)
    rememberQuantityRevisionAttempt(snapshot.binding, next, submittedRows, submittedReason)
    if (next.phase === 'completed') {
      setRows(quantityRowsFromDetail(next.result.detail))
      setReason('')
      onRecorded()
    } else if (next.phase === 'failed' && next.failure === 'conflict') {
      onRecorded()
    }
  }

  function start() {
    setLocalError(null)
    const built = buildQuantityRevisionRequest(detail.draft.version, reason, rows)
    if (!built.ok) {
      setLocalError(text.localError(built.lineNo, built.reason))
      return
    }
    const binding = orderAttemptBinding(shell, scopeOf(detail.draft.id))
    if (binding === null) return
    const decision = decideSubmit(
      attemptRef.current,
      { path: `${detail.draft.id}/quantity-revision`, binding, companyId, payload: built.payload },
      generateOrderAttemptKey,
    )
    if (decision.type === 'send') void send(decision.snapshot, rows, reason)
  }

  function retry() {
    const snapshot = liveSnapshot(attemptRef.current)
    if (snapshot !== null && attemptRef.current.phase !== 'submitting') void send(snapshot, rows, reason)
  }

  function leave() {
    const next = abandon(attemptRef.current)
    attemptRef.current = next
    setAttempt(next)
    if (next.phase !== 'idle') rememberQuantityRevisionAttempt(next.snapshot.binding, next, rows, reason)
  }

  function change(lineId: string, quantity: string) {
    setRows((current) => current.map((row) => (row.lineId === lineId ? { ...row, quantity } : row)))
  }

  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  const notice = revisionNotice(attempt)

  if (!open) {
    return (
      <div className={styles.actions} data-testid="sales-quantity-revision">
        {notice === null ? null : <p role="status" className={styles.notice}>{notice}</p>}
        <button type="button" className={styles.secondaryButton} disabled={!stateAllowsRevision} onClick={() => setOpen(true)}>
          {text.open}
        </button>
        {!stateAllowsRevision ? <p className={styles.muted}>{text.stateUnavailable}</p> : null}
      </div>
    )
  }

  return (
    <section className={styles.actions} aria-labelledby={headingId} data-testid="sales-quantity-revision-form">
      <h3 id={headingId}>{text.heading}</h3>
      <p className={styles.muted}>{text.description}</p>
      {detail.draft.approvalState === APPROVAL_STATE.approved ? <p className={styles.muted}>{text.approvedWarning}</p> : null}
      <p className={styles.muted}>{text.linkedWarning}</p>
      {!stateAllowsRevision ? <p className={styles.muted}>{text.stateUnavailable}</p> : null}
      {localError === null ? null : <p role="alert" className={styles.notice}>{localError}</p>}
      {notice === null ? null : <p role={attempt.phase === 'failed' ? 'alert' : 'status'} className={styles.notice}>{notice}</p>}
      {rows.map((row) => (
        <fieldset key={row.lineId} className={styles.line} disabled={frozen || !stateAllowsRevision}>
          <legend>{row.lineNo}. {row.productName}</legend>
          <label htmlFor={`sales-quantity-${row.lineId}`}>{text.quantity(row.orderUnitId)}</label>
          <input
            id={`sales-quantity-${row.lineId}`}
            inputMode="decimal"
            value={row.quantity}
            aria-describedby={`sales-quantity-current-${row.lineId}`}
            onChange={(event) => change(row.lineId, event.target.value)}
          />
          <span id={`sales-quantity-current-${row.lineId}`} className={styles.muted}>{text.current(row.current)}</span>
        </fieldset>
      ))}
      <label htmlFor={reasonId}>{text.reason}</label>
      <textarea
        id={reasonId}
        value={reason}
        maxLength={4000}
        required
        aria-describedby={`${reasonId}-hint`}
        disabled={frozen || !stateAllowsRevision}
        onChange={(event) => setReason(event.target.value)}
      />
      <span id={`${reasonId}-hint`} className={styles.muted}>{text.reasonHint}</span>
      {isOutcomeStillUnknown(attempt) && attempt.phase !== 'submitting' ? (
        <>
          <button type="button" className={styles.secondaryButton} onClick={retry}>{text.retry}</button>
          <button type="button" className={styles.linkButton} onClick={leave}>{text.abandon}</button>
        </>
      ) : null}
      <button type="button" className={styles.primaryButton} disabled={frozen || !stateAllowsRevision} onClick={start}>
        {text.submit}
      </button>
      <button type="button" className={styles.linkButton} disabled={frozen} onClick={() => setOpen(false)}>{text.close}</button>
    </section>
  )
}

function revisionNotice(attempt: RevisionAttempt): string | null {
  const text = T.quantityRevision
  switch (attempt.phase) {
    case 'idle': return null
    case 'submitting': return text.submitting
    case 'unknown':
    case 'abandoned': return attempt.reason === 'service-unavailable' ? `${text.unavailable} ${text.unknown}` : text.unknown
    case 'completed':
      // A revision above Rev.0 back in preparation must be sent for approval again (the existing submit/approve flow).
      return attempt.result.isReplay ? text.replayed
        : attempt.result.detail.draft.approvalState === APPROVAL_STATE.preparing && attempt.result.detail.draft.revision !== REV_0
          ? text.completed : text.completedUnapproved
    case 'failed': {
      const code = attempt.problem?.code ?? null
      const blocked = code === SALES_PROBLEM_CODE.revisionBlocked
        ? (attempt.problem?.errors ?? []).map((error) => text.blockedSource(error.field)).join(' ')
        : ''
      const codeText = code === SALES_PROBLEM_CODE.versionConflict ? text.versionConflict : problemCodeText(code)
      const base = attempt.failure === 'unauthenticated' ? text.unauthenticated
        : attempt.failure === 'forbidden' ? text.forbidden
          : attempt.failure === 'not-found' ? text.notFound
            : attempt.failure === 'conflict' ? text.conflict
              : attempt.failure === 'validation' ? text.validation
                : text.unexpected
      return [base, codeText, blocked].filter((part) => part !== null && part !== '').join(' ')
    }
    default: return null
  }
}
