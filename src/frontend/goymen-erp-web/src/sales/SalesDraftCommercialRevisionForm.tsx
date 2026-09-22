import { useEffect, useRef, useState } from 'react'
import { sendDraftCommercialRevision } from './api'
import { buildCommercialRevisionRequest, commercialRowsFromDetail, type CommercialRevisionRow } from './commercialRevision'
import { APPROVAL_STATE, SALES_PROBLEM_CODE, type DraftCommercialRevisionRequest, type DraftDetail, type DraftMutation } from './contract'
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
import { SALES_CAPABILITY } from './capabilities'
import { ORDER_DETAIL_TEXT as T, problemCodeText } from './salesText'
import { hasCapability, type ShellState } from '../shell/shellState'
import styles from './SalesPages.module.css'

type RevisionAttempt = OrderAttemptState<DraftCommercialRevisionRequest, DraftMutation>

const scopeOf = (draftId: string) => `sales.draft.commercial-revision:${draftId}`

export function SalesDraftCommercialRevisionForm({
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
  const [rows, setRows] = useState<readonly CommercialRevisionRow[]>(() => commercialRowsFromDetail(detail))
  const [attempt, setAttempt] = useState<RevisionAttempt>({ phase: 'idle' })
  const [localError, setLocalError] = useState<string | null>(null)
  const attemptRef = useRef(attempt)
  attemptRef.current = attempt

  useEffect(() => {
    if (attemptRef.current.phase !== 'submitting' && !isOutcomeStillUnknown(attemptRef.current)) {
      setRows(commercialRowsFromDetail(detail))
    }
  }, [detail])

  if (!hasCapability(shell, SALES_CAPABILITY.ordersPriceWrite)) return null

  const text = T.priceRevision
  if (detail.lines.some((line) => line.priceDisclosure === 'Withheld')) {
    return <p className={styles.muted} data-testid="sales-price-revision-withheld">{text.withheld}</p>
  }

  const stateAllowsRevision =
    detail.draft.approvalState === APPROVAL_STATE.preparing ||
    detail.draft.approvalState === APPROVAL_STATE.rejected ||
    detail.draft.approvalState === APPROVAL_STATE.approved

  async function send(snapshot: OrderAttemptSnapshot<DraftCommercialRevisionRequest>) {
    const previous = attemptRef.current
    const inFlight = beginSend<DraftCommercialRevisionRequest, DraftMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    const outcome = await sendDraftCommercialRevision(snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      draftId: detail.draft.id,
    })
    const next = applyOutcome(inFlight, snapshot, orderAttemptBinding(shell, scopeOf(detail.draft.id)), outcome, previous)
    attemptRef.current = next
    setAttempt(next)
    if (next.phase === 'completed') {
      setRows(commercialRowsFromDetail(next.result.detail))
      onRecorded()
    } else if (next.phase === 'failed' && next.failure === 'conflict') {
      onRecorded()
    }
  }

  function start() {
    setLocalError(null)
    const built = buildCommercialRevisionRequest(detail.draft.version, rows)
    if (!built.ok) {
      setLocalError(text.localError(built.lineNo, built.reason))
      return
    }
    const binding = orderAttemptBinding(shell, scopeOf(detail.draft.id))
    if (binding === null) return
    const decision = decideSubmit(
      attemptRef.current,
      { path: `${detail.draft.id}/commercial-revision`, binding, companyId, payload: built.payload },
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

  function change(lineId: string, patch: Partial<CommercialRevisionRow>) {
    setRows((current) => current.map((row) => (row.lineId === lineId ? { ...row, ...patch } : row)))
  }

  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  const notice = revisionNotice(attempt)

  return (
    <div className={styles.actions} data-testid="sales-price-revision-form">
      <h3>{text.heading}</h3>
      <p className={styles.muted}>{detail.draft.approvalState === APPROVAL_STATE.approved ? text.approvedWarning : text.description}</p>
      {!stateAllowsRevision ? <p className={styles.muted}>{text.stateUnavailable}</p> : null}
      {localError === null ? null : <p role="alert" className={styles.notice}>{localError}</p>}
      {notice === null ? null : <p role={attempt.phase === 'failed' ? 'alert' : 'status'} className={styles.notice}>{notice}</p>}
      {rows.map((row) => {
        const unitOptions = [...new Set([row.priceUnitId, row.orderUnitId].filter((id) => id !== ''))]
        const pricePresent = row.unitPrice.trim() !== ''
        return (
          <fieldset key={row.lineId} className={styles.line} disabled={frozen || !stateAllowsRevision}>
            <legend>{row.lineNo}. {row.productName}</legend>
            <label htmlFor={`sales-price-${row.lineId}`}>{text.unitPrice}</label>
            <input
              id={`sales-price-${row.lineId}`}
              inputMode="decimal"
              value={row.unitPrice}
              onChange={(event) => change(row.lineId, { unitPrice: event.target.value })}
            />
            <label htmlFor={`sales-price-unit-${row.lineId}`}>{text.priceUnit}</label>
            <select
              id={`sales-price-unit-${row.lineId}`}
              value={row.priceUnitId}
              disabled={frozen || !stateAllowsRevision || !pricePresent}
              onChange={(event) => change(row.lineId, { priceUnitId: event.target.value })}
            >
              {unitOptions.map((unitId) => <option key={unitId} value={unitId}>{unitId === row.orderUnitId ? text.orderUnit : text.currentPriceUnit} ({unitId})</option>)}
            </select>
            <label htmlFor={`sales-discount-${row.lineId}`}>{text.discount}</label>
            <input
              id={`sales-discount-${row.lineId}`}
              inputMode="decimal"
              value={row.discountPercent}
              onChange={(event) => change(row.lineId, { discountPercent: event.target.value })}
            />
          </fieldset>
        )
      })}
      {isOutcomeStillUnknown(attempt) && attempt.phase !== 'submitting' ? (
        <>
          <button type="button" className={styles.secondaryButton} onClick={retry}>{text.retry}</button>
          <button type="button" className={styles.linkButton} onClick={leave}>{text.abandon}</button>
        </>
      ) : null}
      <button type="button" className={styles.primaryButton} disabled={frozen || !stateAllowsRevision} onClick={start}>
        {text.submit}
      </button>
    </div>
  )
}

function revisionNotice(attempt: RevisionAttempt): string | null {
  const text = T.priceRevision
  switch (attempt.phase) {
    case 'idle': return null
    case 'submitting': return text.submitting
    case 'unknown':
    case 'abandoned': return text.unknown
    case 'completed': return attempt.result.isReplay ? text.replayed : text.completed
    case 'failed': {
      const code = attempt.problem?.code ?? null
      const codeText = code === SALES_PROBLEM_CODE.versionConflict ? text.versionConflict : problemCodeText(code)
      const base = attempt.failure === 'unauthenticated' ? text.unauthenticated
        : attempt.failure === 'forbidden' ? text.forbidden
          : attempt.failure === 'not-found' ? text.notFound
            : attempt.failure === 'conflict' ? text.conflict
              : attempt.failure === 'validation' ? text.validation
                : text.unexpected
      return codeText === null ? base : `${base} ${codeText}`
    }
    default: return null
  }
}
