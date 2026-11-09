import { useContext, useEffect, useId, useRef, useState } from 'react'
import { sendDraftLineRevision } from './api'
import { SALES_PRICE_WRITE_CAPABILITY, SALES_QUANTITY_WRITE_CAPABILITY } from './capabilities'
import {
  APPROVAL_STATE,
  isTerminalState,
  REV_0,
  SALES_PROBLEM_CODE,
  type DraftDetail,
  type DraftLineRevisionRequest,
  type DraftMutation,
} from './contract'
import {
  buildLineRevisionRequest,
  emptyNewLine,
  lineRevisionFromDetail,
  reinstatementWaits,
  rowOfServerField,
  type LineAction,
  type LineRevisionValues,
  type NewLineValues,
} from './lineRevision'
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
import { browserIds, type OrderDraftOptions } from './orderDraftForm'
import { recallLineRevisionAttempt, rememberLineRevisionAttempt } from './pendingLineRevisions'
import { SalesReferenceSourceContext } from './referenceSource'
import { todayText } from '../product/priceLists/contract'
import { ORDER_DETAIL_TEXT as T, problemCodeText } from './salesText'
import { hasCapability, type ShellState } from '../shell/shellState'
import styles from './SalesPages.module.css'
import { Button } from '../ui/Button'

type RevisionAttempt = OrderAttemptState<DraftLineRevisionRequest, DraftMutation>
type Products = OrderDraftOptions['products']

const scopeOf = (draftId: string) => `sales.draft.line-revision:${draftId}`

/**
 * SAT-D167 (EK24) — "Satır ekle / çıkar / ürün değiştir" on S-04 (Order 360) behind the `sales.orders.quantity.write`
 * control (SAT-D210, EK36: the people who revise quantities): for each current line keep it, remove it (it goes passive) or
 * change its product (a replacement line in the same revision), and add new lines chosen from the Product owner's list for the
 * order's business date (a fason order lists only service products). One keyed attempt with a required reason (the quantity
 * revision's attempt rules: an unknown outcome keeps the key and retries the same payload). A price is offered only with the
 * price-write control. An approved order is archived by the server and re-enters approval; the server decides every rule.
 */
export function SalesDraftLineRevisionForm({
  shell,
  detail,
  companyId,
  onRecorded,
  mint = browserIds,
}: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly companyId: string
  readonly onRecorded: () => void
  readonly mint?: () => string
}) {
  const [initial] = useState(() => recallLineRevisionAttempt(orderAttemptBinding(shell, scopeOf(detail.draft.id))))
  const [open, setOpen] = useState(initial !== null)
  const [values, setValues] = useState<LineRevisionValues>(() => initial?.values ?? lineRevisionFromDetail(detail, mint))
  const [attempt, setAttempt] = useState<RevisionAttempt>(() => initial?.attempt ?? { phase: 'idle' })
  const [localError, setLocalError] = useState<string | null>(null)
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const attemptRef = useRef(attempt)
  attemptRef.current = attempt
  const reasonId = useId()
  const headingId = useId()
  const searchId = useId()

  useEffect(() => {
    if (attemptRef.current.phase !== 'submitting' && !isOutcomeStillUnknown(attemptRef.current)) {
      setValues(lineRevisionFromDetail(detail, mint))
    }
  }, [detail, mint])

  // The Product owner's candidates for the REVISION DAY (SAT-D212, EK36: a new line's product, unit and tax are read for the
  // Europe/Istanbul business day it is added, not the order's business date; the server reads them again for its own day), read
  // only while the form is open (a fason order asks for the service products only). Nothing is offered before the owner answered.
  const subcontract = detail.draft.subcontractService === true
  const [revisionDay] = useState(() => todayText())
  const useReferences = useContext(SalesReferenceSourceContext)
  const references = useReferences(open ? companyId : '', revisionDay, '', search, subcontract ? 'Service' : undefined)
  const productRead = references.status === 'ready' ? null : (references.header?.products ?? null)
  const products: Products | null = references.status === 'ready'
    ? references.options.products
    : productRead?.status === 'ready' ? productRead.data.items : null

  if (!hasCapability(shell, SALES_QUANTITY_WRITE_CAPABILITY)) return null

  const text = T.lineRevision
  const priceWrite = hasCapability(shell, SALES_PRICE_WRITE_CAPABILITY)
  const stateAllowsRevision = !isTerminalState(detail.draft.state) && (
    detail.draft.approvalState === APPROVAL_STATE.preparing ||
    detail.draft.approvalState === APPROVAL_STATE.rejected ||
    detail.draft.approvalState === APPROVAL_STATE.approved)

  async function send(snapshot: OrderAttemptSnapshot<DraftLineRevisionRequest>, submitted: LineRevisionValues) {
    const previous = attemptRef.current
    const inFlight = beginSend<DraftLineRevisionRequest, DraftMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    rememberLineRevisionAttempt(snapshot.binding, inFlight, submitted)
    const outcome = await sendDraftLineRevision(snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      draftId: detail.draft.id,
    })
    const next = applyOutcome(inFlight, snapshot, orderAttemptBinding(shell, scopeOf(detail.draft.id)), outcome, previous)
    attemptRef.current = next
    setAttempt(next)
    rememberLineRevisionAttempt(snapshot.binding, next, submitted)
    if (next.phase === 'completed') {
      setValues(lineRevisionFromDetail(next.result.detail, mint))
      onRecorded()
    } else if (next.phase === 'failed' && next.failure === 'conflict') {
      onRecorded()
    }
  }

  function start() {
    setLocalError(null)
    const built = buildLineRevisionRequest(detail.draft.version, values, products ?? [], priceWrite, reinstatementWaits(detail))
    if (!built.ok) {
      setLocalError(text.localError(built.row, built.problem))
      return
    }
    const binding = orderAttemptBinding(shell, scopeOf(detail.draft.id))
    if (binding === null) return
    const decision = decideSubmit(
      attemptRef.current,
      { path: `${detail.draft.id}/line-revision`, binding, companyId, payload: built.payload },
      generateOrderAttemptKey,
    )
    if (decision.type === 'send') void send(decision.snapshot, values)
  }

  function retry() {
    const snapshot = liveSnapshot(attemptRef.current)
    if (snapshot !== null && attemptRef.current.phase !== 'submitting') void send(snapshot, values)
  }

  function leave() {
    const next = abandon(attemptRef.current)
    attemptRef.current = next
    setAttempt(next)
    if (next.phase !== 'idle') rememberLineRevisionAttempt(next.snapshot.binding, next, values)
  }

  const setAction = (lineId: string, action: LineAction) =>
    setValues((current) => ({ ...current, existing: current.existing.map((row) => (row.lineId === lineId ? { ...row, action } : row)) }))
  const setReplacement = (lineId: string, change: (line: NewLineValues) => NewLineValues) =>
    setValues((current) => ({
      ...current,
      existing: current.existing.map((row) => (row.lineId === lineId ? { ...row, replacement: change(row.replacement) } : row)),
    }))
  const setAdded = (lineId: string, change: (line: NewLineValues) => NewLineValues) =>
    setValues((current) => ({ ...current, added: current.added.map((row) => (row.lineId === lineId ? change(row) : row)) }))

  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  const notice = revisionNotice(attempt, values)

  if (!open) {
    return (
      <div className={styles.actions} data-testid="sales-line-revision">
        {notice === null ? null : <p role="status" className={styles.notice}>{notice}</p>}
        <Button type="button" variant="secondary" disabled={!stateAllowsRevision} onClick={() => setOpen(true)}>
          {text.open}
        </Button>
        {!stateAllowsRevision ? <p className={styles.muted}>{text.stateUnavailable}</p> : null}
      </div>
    )
  }

  const disabled = frozen || !stateAllowsRevision
  const productStatus = products !== null
    ? null
    : productRead?.status === 'unavailable' || (references.status === 'unavailable' && references.header === undefined && open)
      ? 'unavailable'
      : 'loading'

  return (
    <section className={styles.actions} aria-labelledby={headingId} data-testid="sales-line-revision-form">
      <h3 id={headingId}>{text.heading}</h3>
      <p className={styles.muted}>{text.description}</p>
      {detail.draft.approvalState === APPROVAL_STATE.approved ? <p className={styles.muted}>{text.approvedWarning}</p> : null}
      <p className={styles.muted}>{text.linkedWarning}</p>
      {reinstatementWaits(detail) ? <p className={styles.notice} role="note" data-testid="sales-line-revision-reinstatement">{text.reinstatementNote}</p> : null}
      {subcontract ? <p className={styles.muted} data-testid="sales-line-revision-subcontract">{text.subcontractNote}</p> : null}
      <p className={styles.muted} data-testid="sales-line-revision-day">{text.revisionDay(revisionDay)}</p>
      {!stateAllowsRevision ? <p className={styles.muted}>{text.stateUnavailable}</p> : null}
      {localError === null ? null : <p role="alert" className={styles.notice}>{localError}</p>}
      {notice === null ? null : <p role={attempt.phase === 'failed' ? 'alert' : 'status'} className={styles.notice}>{notice}</p>}

      <div className={styles.actions}>
        <label htmlFor={searchId}>{text.productSearch}</label>
        <input id={searchId} value={searchInput} disabled={disabled} onChange={(event) => setSearchInput(event.target.value)} />
        <Button type="button" variant="secondary" disabled={disabled} onClick={() => setSearch(searchInput)}>{text.productSearchSubmit}</Button>
        {productStatus === 'loading' ? <p className={styles.muted} role="status">{text.productsLoading}</p> : null}
        {productStatus === 'unavailable' ? (
          <p role="alert" className={styles.notice} data-testid="sales-line-revision-products-unavailable">
            {text.productsUnavailable}{' '}
            {productRead?.status === 'unavailable'
              ? <Button type="button" variant="ghost" className={styles.linkButton} onClick={productRead.retry}>{text.productsRetry}</Button>
              : null}
          </p>
        ) : null}
        {products !== null && products.length === 0 ? <p className={styles.muted}>{text.productsEmpty}</p> : null}
      </div>

      {values.existing.map((row) => (
        <fieldset key={row.lineId} className={styles.line} disabled={disabled} data-testid={`sales-line-revision-row-${row.lineNo}`}>
          <legend>{row.lineNo}. {row.label} ({row.quantity})</legend>
          <label htmlFor={`sales-line-action-${row.lineId}`}>{text.action}</label>
          <select
            id={`sales-line-action-${row.lineId}`}
            value={row.action}
            onChange={(event) => setAction(row.lineId, event.target.value as LineAction)}
          >
            <option value="keep">{text.actions.keep}</option>
            <option value="remove">{text.actions.remove}</option>
            <option value="replace">{text.actions.replace}</option>
          </select>
          {row.action === 'replace' ? (
            <NewLineFields
              idStem={`sales-line-replacement-${row.lineId}`}
              heading={text.replacementHeading}
              line={row.replacement}
              products={products}
              priceWrite={priceWrite}
              onChange={(change) => setReplacement(row.lineId, change)}
            />
          ) : null}
        </fieldset>
      ))}

      {values.added.length === 0 ? <p className={styles.muted}>{text.noAdded}</p> : null}
      {values.added.map((row, index) => (
        <fieldset key={row.lineId} className={styles.line} disabled={disabled} data-testid={`sales-line-revision-added-${index + 1}`}>
          <legend>{text.addedHeading(index + 1)}</legend>
          <NewLineFields
            idStem={`sales-line-added-${row.lineId}`}
            heading={null}
            line={row}
            products={products}
            priceWrite={priceWrite}
            onChange={(change) => setAdded(row.lineId, change)}
          />
          <Button
            type="button"
            variant="ghost"
            className={styles.linkButton}
            onClick={() => setValues((current) => ({ ...current, added: current.added.filter((candidate) => candidate.lineId !== row.lineId) }))}
          >
            {text.removeAdded}
          </Button>
        </fieldset>
      ))}
      <Button
        type="button"
        variant="secondary"
        disabled={disabled || products === null}
        onClick={() => setValues((current) => ({ ...current, added: [...current.added, emptyNewLine(mint())] }))}
      >
        {text.addLine}
      </Button>

      <label htmlFor={reasonId}>{text.reason}</label>
      <textarea
        id={reasonId}
        value={values.reason}
        maxLength={4000}
        required
        aria-describedby={`${reasonId}-hint`}
        disabled={disabled}
        onChange={(event) => {
          const reason = event.target.value
          setValues((current) => ({ ...current, reason }))
        }}
      />
      <span id={`${reasonId}-hint`} className={styles.muted}>{text.reasonHint}</span>
      {isOutcomeStillUnknown(attempt) && attempt.phase !== 'submitting' ? (
        <>
          <Button type="button" variant="secondary" onClick={retry}>{text.retry}</Button>
          <Button type="button" variant="ghost" className={styles.linkButton} onClick={leave}>{text.abandon}</Button>
        </>
      ) : null}
      <Button type="button" variant="primary" disabled={disabled} onClick={start}>
        {text.submit}
      </Button>
      <Button type="button" variant="ghost" className={styles.linkButton} disabled={frozen} onClick={() => setOpen(false)}>{text.close}</Button>
    </section>
  )
}

/** One new line's product, unit, quantity and (with the price-write control) price; the choices are the Product owner's. */
function NewLineFields({
  idStem,
  heading,
  line,
  products,
  priceWrite,
  onChange,
}: {
  readonly idStem: string
  readonly heading: string | null
  readonly line: NewLineValues
  readonly products: Products | null
  readonly priceWrite: boolean
  readonly onChange: (change: (line: NewLineValues) => NewLineValues) => void
}) {
  const text = T.lineRevision
  const product = products?.find((candidate) => candidate.productId === line.productId)
  return (
    <div className={styles.actions}>
      {heading === null ? null : <p className={styles.muted}>{heading}</p>}
      <label htmlFor={`${idStem}-product`}>{text.product}</label>
      <select
        id={`${idStem}-product`}
        value={line.productId}
        disabled={products === null}
        onChange={(event) => {
          const productId = event.target.value
          onChange((current) => ({ ...current, productId, orderUnitId: '', priceUnitId: '' }))
        }}
      >
        <option value="">{text.productChoose}</option>
        {(products ?? []).map((candidate) => (
          <option key={candidate.productId} value={candidate.productId}>{candidate.label}</option>
        ))}
      </select>
      <label htmlFor={`${idStem}-unit`}>{text.unit}</label>
      <select
        id={`${idStem}-unit`}
        value={line.orderUnitId}
        disabled={product === undefined}
        onChange={(event) => {
          const orderUnitId = event.target.value
          onChange((current) => ({ ...current, orderUnitId }))
        }}
      >
        <option value="">{text.unitChoose}</option>
        {(product?.orderUnits ?? []).map((unit) => (
          <option key={unit.unitId} value={unit.unitId}>{unit.label}</option>
        ))}
      </select>
      <label htmlFor={`${idStem}-quantity`}>{text.quantity}</label>
      <input
        id={`${idStem}-quantity`}
        inputMode="decimal"
        value={line.quantityText}
        onChange={(event) => {
          const quantityText = event.target.value
          onChange((current) => ({ ...current, quantityText }))
        }}
      />
      {priceWrite ? (
        <>
          <label htmlFor={`${idStem}-price`}>{text.unitPrice}</label>
          <input
            id={`${idStem}-price`}
            inputMode="decimal"
            value={line.unitPriceText}
            onChange={(event) => {
              const unitPriceText = event.target.value
              onChange((current) => ({ ...current, unitPriceText }))
            }}
          />
          <label htmlFor={`${idStem}-price-unit`}>{text.priceUnit}</label>
          <select
            id={`${idStem}-price-unit`}
            value={line.priceUnitId}
            disabled={product === undefined}
            onChange={(event) => {
              const priceUnitId = event.target.value
              onChange((current) => ({ ...current, priceUnitId }))
            }}
          >
            <option value="">{text.unitChoose}</option>
            {(product?.orderUnits ?? []).map((unit) => (
              <option key={unit.unitId} value={unit.unitId}>{unit.label}</option>
            ))}
          </select>
        </>
      ) : null}
    </div>
  )
}

/** The server's field errors on `added[i]…` / `removed[i]…`, each named for its row with the rule's text. */
function ruleText(attempt: Extract<RevisionAttempt, { phase: 'failed' }>, values: LineRevisionValues): string {
  const text = T.lineRevision
  return (attempt.problem?.errors ?? [])
    .map((error) => {
      const known = Object.hasOwn(text.ruleCode, error.code) ? text.ruleCode[error.code] : problemCodeText(error.code)
      return known === null ? null : text.rowError(rowOfServerField(error.field, attempt.snapshot.payload, values), known)
    })
    .filter((part): part is string => part !== null)
    .join(' ')
}

function revisionNotice(attempt: RevisionAttempt, values: LineRevisionValues): string | null {
  const text = T.lineRevision
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
      const codeText = code === SALES_PROBLEM_CODE.versionConflict ? text.versionConflict : problemCodeText(code)
      const base = attempt.failure === 'unauthenticated' ? text.unauthenticated
        : attempt.failure === 'forbidden' ? text.forbidden
          : attempt.failure === 'not-found' ? text.notFound
            : attempt.failure === 'conflict' ? text.conflict
              : attempt.failure === 'validation' ? text.validation
                : text.unexpected
      const rules = attempt.failure === 'validation' || attempt.failure === 'conflict' ? ruleText(attempt, values) : ''
      return [base, codeText, rules].filter((part) => part !== null && part !== '').join(' ')
    }
    default: return null
  }
}
