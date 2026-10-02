import { useEffect, useRef, useState } from 'react'
import { fetchLinePriceReferences, sendDraftCommercialRevision } from './api'
import { buildCommercialRevisionRequest, commercialRowsFromFreshPricing, type AdjustmentKind, type CommercialRevisionRow } from './commercialRevision'
import { APPROVAL_STATE, CANCELLED_STATE, SALES_PROBLEM_CODE, type DraftCommercialRevisionRequest, type DraftDetail, type DraftMutation } from './contract'
import type { LinePriceReference } from './linePricing'
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
import { SALES_PRICE_WRITE_CAPABILITY } from './capabilities'
import { LINE_PRICING_TEXT as P, ORDER_DETAIL_TEXT as T, problemCodeText } from './salesText'
import { recallCommercialRevisionAttempt, rememberCommercialRevisionAttempt } from './pendingCommercialRevisions'
import { useDraftLinePricing } from './useDraftLinePricing'
import { hasCapability, type ShellState } from '../shell/shellState'
import styles from './SalesPages.module.css'

type RevisionAttempt = OrderAttemptState<DraftCommercialRevisionRequest, DraftMutation>

const scopeOf = (draftId: string) => `sales.draft.commercial-revision:${draftId}`

/** A line's list-price candidates as read from the Product owner (through Sales), per line. */
type ReferenceChoices =
  | { readonly phase: 'loading' }
  | { readonly phase: 'ready'; readonly items: readonly LinePriceReference[]; readonly currencyCode: string }
  | { readonly phase: 'failed' }

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
  const pricing = useDraftLinePricing(shell, detail, companyId)
  const pricingData = pricing.data?.type === 'ok-single' ? pricing.data.data : null
  const freshRows = commercialRowsFromFreshPricing(detail, pricingData)
  const [initial] = useState(() => recallCommercialRevisionAttempt(orderAttemptBinding(shell, scopeOf(detail.draft.id))))
  const [rows, setRows] = useState<readonly CommercialRevisionRow[]>(() => initial?.rows ?? freshRows ?? [])
  const [attempt, setAttempt] = useState<RevisionAttempt>(() => initial?.attempt ?? { phase: 'idle' })
  const [localError, setLocalError] = useState<string | null>(null)
  const [choices, setChoices] = useState<Readonly<Record<string, ReferenceChoices>>>({})
  const attemptRef = useRef(attempt)

  useEffect(() => {
    // The rows follow the server (a re-read detail, or the pricing of this version arriving) unless an attempt is live.
    if (freshRows !== null && attemptRef.current.phase !== 'submitting' && !isOutcomeStillUnknown(attemptRef.current)) {
      setRows(freshRows)
    }
  }, [detail, pricingData])

  if (!hasCapability(shell, SALES_PRICE_WRITE_CAPABILITY)) return null

  const text = T.priceRevision
  if (detail.lines.some((line) => line.priceDisclosure === 'Withheld')) {
    return <p className={styles.muted} data-testid="sales-price-revision-withheld">{text.withheld}</p>
  }

  // DEC-SAL-005: a cancelled order keeps its Approved facts but is terminal — no revision re-opens it.
  const stateAllowsRevision = detail.draft.state !== CANCELLED_STATE && (
    detail.draft.approvalState === APPROVAL_STATE.preparing ||
    detail.draft.approvalState === APPROVAL_STATE.rejected ||
    detail.draft.approvalState === APPROVAL_STATE.approved)

  function setAttemptState(next: RevisionAttempt) {
    attemptRef.current = next
    setAttempt(next)
  }

  async function send(
    snapshot: OrderAttemptSnapshot<DraftCommercialRevisionRequest>,
    submittedRows: readonly CommercialRevisionRow[],
  ) {
    const previous = attemptRef.current
    const inFlight = beginSend<DraftCommercialRevisionRequest, DraftMutation>(snapshot)
    setAttemptState(inFlight)
    rememberCommercialRevisionAttempt(snapshot.binding, inFlight, submittedRows)
    const outcome = await sendDraftCommercialRevision(snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      draftId: detail.draft.id,
    })
    const next = applyOutcome(inFlight, snapshot, orderAttemptBinding(shell, scopeOf(detail.draft.id)), outcome, previous)
    setAttemptState(next)
    rememberCommercialRevisionAttempt(snapshot.binding, next, submittedRows)
    if (next.phase === 'completed') {
      onRecorded()
    } else if (next.phase === 'failed' && next.failure === 'conflict') {
      onRecorded()
    }
  }

  function start() {
    setLocalError(null)
    if (freshRows === null) return
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
    if (decision.type === 'send') void send(decision.snapshot, rows)
  }

  function retry() {
    const snapshot = liveSnapshot(attemptRef.current)
    if (snapshot !== null && attemptRef.current.phase !== 'submitting') void send(snapshot, rows)
  }

  function leave() {
    const next = abandon(attemptRef.current)
    setAttemptState(next)
    if (next.phase !== 'idle') rememberCommercialRevisionAttempt(next.snapshot.binding, next, rows)
  }

  function change(lineId: string, patch: Partial<CommercialRevisionRow>) {
    setRows((current) => current.map((row) => (row.lineId === lineId ? { ...row, ...patch } : row)))
  }

  async function loadReferences(lineId: string) {
    setChoices((current) => ({ ...current, [lineId]: { phase: 'loading' } }))
    const outcome = await fetchLinePriceReferences(detail.draft.id, lineId, companyId)
    setChoices((current) => ({
      ...current,
      [lineId]: outcome.type === 'ok-single'
        ? { phase: 'ready', items: outcome.data.items, currencyCode: outcome.data.currencyCode }
        : { phase: 'failed' },
    }))
  }

  /** The chosen list price becomes the line's reference AND its default price (and price unit) until the person types another. */
  function chooseReference(lineId: string, reference: LinePriceReference) {
    change(lineId, { reference, unitPrice: reference.unitPrice, priceUnitId: reference.priceUnitId })
    setChoices((current) => {
      const next = { ...current }
      delete next[lineId]
      return next
    })
  }

  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  const notice = revisionNotice(attempt)
  const lineErrors = attempt.phase === 'failed' ? (attempt.problem?.errors ?? []).flatMap((error) => {
    const message = P.fieldError[error.code]
    const index = /^lines\[(\d+)\]/.exec(error.field)
    if (message === undefined) return []
    return [index === null ? message : P.lineError(rows[Number(index[1])]?.lineNo ?? Number(index[1]) + 1, message)]
  }) : []

  return (
    <div className={styles.actions} data-testid="sales-price-revision-form">
      <h3>{text.heading}</h3>
      <p className={styles.muted}>{detail.draft.approvalState === APPROVAL_STATE.approved ? text.approvedWarning : text.description}</p>
      {!stateAllowsRevision ? <p className={styles.muted}>{text.stateUnavailable}</p> : null}
      {freshRows === null ? <p className={styles.notice} data-testid="sales-price-pricing-unavailable">{text.pricingUnavailable}</p> : null}
      {localError === null ? null : <p role="alert" className={styles.notice}>{localError}</p>}
      {notice === null ? null : <p role={attempt.phase === 'failed' ? 'alert' : 'status'} className={styles.notice}>{notice}</p>}
      {lineErrors.map((message) => <p key={message} className={styles.notice} data-testid="sales-price-line-error">{message}</p>)}
      {rows.map((row) => {
        const unitOptions = [...new Set([row.priceUnitId, row.orderUnitId, row.reference?.priceUnitId ?? ''].filter((id) => id !== ''))]
        const pricePresent = row.unitPrice.trim() !== ''
        const choice = choices[row.lineId]
        return (
          <fieldset key={row.lineId} className={styles.line} disabled={frozen || !stateAllowsRevision} data-testid={`sales-price-line-${row.lineNo}`}>
            <legend>{row.lineNo}. {row.productName}</legend>
            <p data-testid={`sales-price-reference-${row.lineNo}`}>
              {P.reference}: {row.reference === null ? P.noReference
                : P.referenceText(row.reference.priceListName, row.reference.versionNo, row.reference.unitPrice, row.reference.currencyCode)
                  + (row.reference.floorUnitPrice === null ? '' : ` (${P.floorText(row.reference.floorUnitPrice)})`)
                  + ` (${P.tierText(row.reference.lowerQuantity, row.reference.upperQuantity)})`}
            </p>
            <div className={styles.actions}>
              <button type="button" className={styles.secondaryButton} onClick={() => void loadReferences(row.lineId)}
                data-testid={`sales-price-fetch-${row.lineNo}`}>
                {choice?.phase === 'loading' ? P.fetchingReferences : P.fetchReferences}
              </button>
              {row.reference === null ? null : (
                <button type="button" className={styles.linkButton} onClick={() => change(row.lineId, { reference: null })}>{P.removeReference}</button>
              )}
            </div>
            {choice?.phase === 'failed' ? <p className={styles.notice}>{P.referencesUnavailable}</p> : null}
            {choice?.phase === 'ready' && choice.items.length === 0 ? <p className={styles.muted}>{P.noReferences}</p> : null}
            {choice?.phase === 'ready' && choice.items.length > 0 ? (
              <ul data-testid={`sales-price-choices-${row.lineNo}`}>
                {choice.items.map((item) => (
                  <li key={`${item.priceListId}:${item.versionNo}:${item.priceUnitId}`}>
                    {P.referenceText(item.priceListName, item.versionNo, item.unitPrice, item.currencyCode)}
                    {item.floorUnitPrice === null ? '' : ` (${P.floorText(item.floorUnitPrice)})`}
                    {` (${P.tierText(item.lowerQuantity, item.upperQuantity)})`}{' '}
                    <button type="button" className={styles.secondaryButton} onClick={() => chooseReference(row.lineId, item)}>{P.useReference}</button>
                  </li>
                ))}
              </ul>
            ) : null}
            <label htmlFor={`sales-price-${row.lineId}`}>{text.unitPrice}</label>
            <input
              id={`sales-price-${row.lineId}`}
              inputMode="decimal"
              value={row.unitPrice}
              onChange={(event) => change(row.lineId, { unitPrice: event.target.value })}
            />
            {row.reference === null ? null : <p className={styles.muted}>{P.overwriteHint}</p>}
            <label htmlFor={`sales-price-unit-${row.lineId}`}>{text.priceUnit}</label>
            <select
              id={`sales-price-unit-${row.lineId}`}
              value={row.priceUnitId}
              disabled={frozen || !stateAllowsRevision || !pricePresent}
              onChange={(event) => change(row.lineId, { priceUnitId: event.target.value })}
            >
              {unitOptions.map((unitId) => <option key={unitId} value={unitId}>{unitId === row.orderUnitId ? text.orderUnit : text.currentPriceUnit} ({unitId})</option>)}
            </select>
            <label htmlFor={`sales-discount-kind-${row.lineId}`}>{P.discountKind}</label>
            <select id={`sales-discount-kind-${row.lineId}`} value={row.discountKind}
              onChange={(event) => change(row.lineId, { discountKind: event.target.value as AdjustmentKind })}>
              <option value="percent">{P.kindPercent}</option>
              <option value="amount">{P.kindAmount}</option>
            </select>
            <label htmlFor={`sales-discount-${row.lineId}`}>{row.discountKind === 'percent' ? text.discount : P.discount}</label>
            <input
              id={`sales-discount-${row.lineId}`}
              inputMode="decimal"
              value={row.discountPercent}
              onChange={(event) => change(row.lineId, { discountPercent: event.target.value })}
            />
            <label htmlFor={`sales-surcharge-kind-${row.lineId}`}>{P.surchargeKind}</label>
            <select id={`sales-surcharge-kind-${row.lineId}`} value={row.surchargeKind}
              onChange={(event) => change(row.lineId, { surchargeKind: event.target.value as AdjustmentKind })}>
              <option value="percent">{P.kindPercent}</option>
              <option value="amount">{P.kindAmount}</option>
            </select>
            <label htmlFor={`sales-surcharge-${row.lineId}`}>{P.surchargeValue}</label>
            <input
              id={`sales-surcharge-${row.lineId}`}
              inputMode="decimal"
              value={row.surchargeValue}
              onChange={(event) => change(row.lineId, { surchargeValue: event.target.value })}
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
      <button type="button" className={styles.primaryButton} disabled={frozen || !stateAllowsRevision || freshRows === null} onClick={start}>
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
