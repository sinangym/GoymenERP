/**
 * API-03 / UI-15 (SAT-D11, delegation No 15): the order totals card on S-04 (Sipariş 360), as the server answers them
 * (`GET …/drafts/{id}/totals`, under the draft read AND the price read). Read only, mounted after the parent detail was read in
 * the same company, read afresh on every open (`gcTime` 0) and keyed by the draft version, so a recorded revision reads the
 * new totals.
 *
 * The screen never adds, multiplies or rounds an amount (AGENTS.md: "Fiyat/formül/toleransı ekran içinde yeniden yazma"):
 * every number is the server calculator's text. A value the server could not compute is shown as a word with its reason —
 * an unpriced line, a line priced in another unit, an incomplete document, a VAT master that does not exist yet — never as
 * zero. A caller who may not see prices (any line `Withheld`) is not asked: the card says why nothing is shown. An order
 * without lines is not asked either — there is nothing to total, and no line says whether prices are disclosed — and the card
 * says so neutrally (never "forbidden"). A failed, forbidden or foreign answer shows no amount at all.
 */
import { useContext, useId, useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchDraftTotals } from './api'
import { SALES_CAPABILITY } from './capabilities'
import { decodeProblem, type DraftDetail } from './contract'
import { cardOutcome, NO_LINES, TOTALS_STATE, type LineTotal, type OrderTotals, type TaxState } from './orderTotals'
import { SalesCurrencySourceContext } from './referenceSource'
import { ORDER_TOTALS_TEXT as T } from './salesText'
import { orderUnitCodes } from './unitNames'
import { UnitName } from './UnitName'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StatusIndicator, type StatusTone } from '../ui/StatusIndicator'
import pages from './SalesPages.module.css'

/** The container: gates the read on the bound company, the S-04 detail capability and the disclosed prices, then reads once per open. */
export function SalesOrderTotalsCard({ detail }: { readonly detail: DraftDetail }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  // API-03 publishes no capability of its own: the route is authorised by the draft read (the S-04 detail capability) and the
  // price read, which the detail already measured for this caller — a withheld line means it is not held, so nothing is asked.
  // The API still measures both afresh on every call.
  const canRead = namespace !== null && companyId !== null && companyId === detail.draft.companyId
    && hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const hasLines = detail.lines.length > 0
  const pricesDisclosed = hasLines && detail.lines.every((line) => line.priceDisclosure === 'Visible')
  const boundary = useAuthBoundary()
  // The currencies reference is the create form's read (sales.drafts.create): without it the currency's identity is shown.
  const useCurrencies = useContext(SalesCurrencySourceContext)
  const currencies = useCurrencies(
    canRead && companyId !== null && hasCapability(shell, SALES_CAPABILITY.draftsCreate) ? companyId : '', detail.draft.businessDate)
  const currencyCode = currencies.status === 'ready'
    ? (currencies.data.find((candidate) => candidate.currencyId === detail.draft.currencyId)?.code ?? null)
    : null
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-totals', detail.draft.id, detail.draft.version],
    enabled: canRead && pricesDisclosed,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftTotals(detail, companyId as string, signal)
      // A 403 here is the price read lost since the detail was read: the boundary hears it like every read; the card says why.
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })
  if (!canRead) return null
  return (
    <OrderTotalsCard
      detail={detail}
      currency={currencyCode ?? detail.draft.currencyId}
      outcome={cardOutcome(detail, query.data)}
      onRetry={() => void query.refetch()}
    />
  )
}

/** The card for one read outcome (undefined while loading). Exported so every outcome can be rendered and checked on its own. */
export function OrderTotalsCard({
  detail,
  currency,
  outcome,
  onRetry,
}: {
  readonly detail: DraftDetail
  /** The draft currency's code when readable to this caller, else its identity. */
  readonly currency: string
  readonly outcome: ViewOutcome<OrderTotals> | typeof NO_LINES | undefined
  readonly onRetry: () => void
}) {
  const headingId = useId()
  let body
  if (outcome?.type === 'no-lines') {
    body = <p className={pages.muted} role="status" data-testid="sales-totals-no-lines">{T.noLines}</p>
  } else if (outcome?.type === 'ok-single') {
    body = <TotalsBody detail={detail} currency={currency} totals={outcome.data} />
  } else if (outcome?.type === 'http' && outcome.status === 403) {
    body = <p className={pages.muted} role="status" data-testid="sales-totals-forbidden">{T.forbidden}</p>
  } else {
    const problem = outcome?.type === 'http' ? decodeProblem(outcome.body, outcome.status) : null
    body = (
      <>
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject={T.subject}
          onRetry={onRetry}
        />
        {outcome === undefined ? null : (
          <p className={pages.muted} data-testid="sales-totals-unreadable">
            {outcome.type === 'http' && outcome.status === 503 ? T.serviceUnavailable : T.unreadable}
          </p>
        )}
        {problem?.correlationId == null ? null : (
          <p className={pages.muted} data-testid="sales-totals-support-code">{T.supportCode(problem.correlationId)}</p>
        )}
      </>
    )
  }
  return (
    <section className={pages.section} aria-labelledby={headingId} aria-busy={outcome === undefined} data-testid="sales-totals">
      <h2 id={headingId}>{T.heading}</h2>
      <p className={pages.muted}>{T.note}</p>
      {body}
    </section>
  )
}

/* ── Presentation of decoded totals ───────────────────────────────────────── */

const LINE_TONE: Record<LineTotal['state'], StatusTone> = {
  Computed: 'success', Unpriced: 'warning', PriceUnitNotOrderUnit: 'warning', AmountOutOfRange: 'warning',
}
const TAX_TONE: Record<TaxState, StatusTone> = { Computed: 'success', NoSource: 'neutral', NotComputed: 'neutral' }

function TotalsBody({ detail, currency, totals }: { readonly detail: DraftDetail; readonly currency: string; readonly totals: OrderTotals }) {
  const linesHeadingId = useId()
  const unitCodes = useMemo(() => orderUnitCodes(detail), [detail])
  const products = useMemo(
    () => new Map(detail.lines.map((line) => [line.lineId.toLowerCase(), `${line.productCode} — ${line.productName}`])),
    [detail],
  )
  const document = totals.totals
  const taxed = document.tax !== null
  const notes: { readonly key: string; readonly text: string }[] = []
  if (document.state === TOTALS_STATE.incomplete && totals.lines.length === 0) {
    notes.push({ key: 'no-lines', text: T.noLines })
  } else if (document.state === TOTALS_STATE.incomplete) {
    notes.push({ key: 'incomplete', text: T.incomplete(document.unpricedLineCount, document.priceUnitNotOrderUnitLineCount,
      document.amountOutOfRangeLineCount) })
  } else if (document.state === TOTALS_STATE.amountOutOfRange) {
    notes.push({ key: 'document-out-of-range', text: T.documentOutOfRange })
  }
  if (totals.taxState === TOTALS_STATE.noSource) notes.push({ key: 'tax-no-source', text: T.taxNoSource })
  else if (document.state === TOTALS_STATE.computed && !taxed) notes.push({ key: 'tax-partial', text: T.taxPartial })
  return (
    <>
      <dl className={pages.facts} data-testid="sales-totals-document">
        <div>
          <dt>{T.revisionLabel}</dt>
          <dd data-testid="sales-totals-revision">{T.revision(totals.revision, totals.draftVersion)}</dd>
        </div>
        <div>
          <dt>{T.currency}</dt>
          <dd data-testid="sales-totals-currency">{currency}</dd>
        </div>
        <Amount label={T.net} value={document.netAmount} testId="sales-totals-net" />
        <Amount label={T.vat} value={document.tax?.vatAmount ?? null} testId="sales-totals-vat" />
        <Amount label={T.withheld} value={document.tax?.withheldVatAmount ?? null} testId="sales-totals-withheld" />
        <Amount label={T.gross} value={document.tax?.grossAmount ?? null} testId="sales-totals-gross" />
        <Amount label={T.payable} value={document.tax?.payableAmount ?? null} testId="sales-totals-payable" />
      </dl>
      <p className={pages.muted}>{T.currencyNote}</p>
      {notes.map((note) => (
        <p key={note.key} className={pages.muted} data-testid={`sales-totals-note-${note.key}`}>{note.text}</p>
      ))}
      <h3 id={linesHeadingId} className={pages.caption}>{T.linesCaption}</h3>
      <div className={pages.tableWrap}>
        <table className={pages.table} aria-labelledby={linesHeadingId} data-testid="sales-totals-lines">
          <thead><tr>
            <th scope="col">{T.column.lineNo}</th>
            <th scope="col">{T.column.product}</th>
            <th scope="col">{T.column.quantity}</th>
            <th scope="col">{T.column.netUnitPrice}</th>
            <th scope="col">{T.column.netAmount}</th>
            <th scope="col">{T.column.vatPercent}</th>
            <th scope="col">{T.column.vat}</th>
            <th scope="col">{T.column.withheld}</th>
            <th scope="col">{T.column.payable}</th>
            <th scope="col">{T.column.state}</th>
          </tr></thead>
          <tbody>{totals.lines.map((line) => (
            <LineRow key={line.lineId} line={line} product={products.get(line.lineId.toLowerCase())} unitCodes={unitCodes} />
          ))}</tbody>
        </table>
      </div>
    </>
  )
}

/** One document amount; a value the server did not compute is the word "Yok", never zero. */
function Amount({ label, value, testId }: { readonly label: string; readonly value: string | null; readonly testId: string }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd className={pages.numericValue} data-testid={testId} data-computed={value !== null}>{value ?? T.notAvailable}</dd>
    </div>
  )
}

function LineRow({
  line,
  product,
  unitCodes,
}: {
  readonly line: LineTotal
  readonly product: string | undefined
  readonly unitCodes: ReadonlyMap<string, string>
}) {
  const id = `sales-totals-line-${line.lineNo}`
  const none = T.notAvailable
  return (
    <tr data-testid={id} data-state={line.state}>
      <th scope="row">{line.lineNo}</th>
      <td>{product}</td>
      <td className={pages.numericValue} data-testid={`${id}-quantity`}>
        {line.quantity} <UnitName id={line.orderUnitId} unitCodes={unitCodes} />
      </td>
      <td className={pages.numericValue} data-testid={`${id}-net-unit-price`}>{line.netUnitPrice ?? none}</td>
      <td className={pages.numericValue} data-testid={`${id}-net`}>{line.netAmount ?? none}</td>
      <td className={pages.numericValue} data-testid={`${id}-vat-percent`}>{line.vatPercent === null ? none : T.percent(line.vatPercent)}</td>
      <td className={pages.numericValue} data-testid={`${id}-vat`}>{line.tax?.vatAmount ?? none}</td>
      <td className={pages.numericValue} data-testid={`${id}-withheld`}>{line.tax?.withheldVatAmount ?? none}</td>
      <td className={pages.numericValue} data-testid={`${id}-payable`}>{line.tax?.payableAmount ?? none}</td>
      <td data-testid={`${id}-state`}>
        <StatusIndicator tone={LINE_TONE[line.state]}>{T.lineState[line.state]}</StatusIndicator>
        {' '}
        <StatusIndicator tone={TAX_TONE[line.taxState]}>{`${T.vat}: ${T.taxState[line.taxState]}`}</StatusIndicator>
      </td>
    </tr>
  )
}
