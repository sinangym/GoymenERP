/**
 * UI-80 "Satış raporları" (plan §3.4 API-41 / API-42, Sales shell 1.11, `sales.reports.read`): three read-only report tabs —
 * Açık siparişler (KPI No 124: approved − shipped per customer, product and unit), Kayıp fırsatlar (SAT-D25: the lost quotes with
 * their list reason and their net amount in the quote's own currency) and Fiyat sapması (No 25: "kaynak yok" until the invoice
 * owner exists).
 *
 * The screen computes nothing: no sum, conversion, rounding or regrouping — every quantity and amount is the server's text, and
 * different units or currencies are never added. The filters (date range, one customer) are the SERVER's: changing them restarts
 * the list; paging follows the server cursor. Each tab has its loading, empty, forbidden, unavailable, malformed and "kaynak yok"
 * states; the tabs follow the WAI-ARIA pattern (roving tabindex, ←/→/Home/End), the selected tab is kept in the address.
 */

import { useId, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { SALES_ROUTE } from './capabilities'
import { DispatchCustomerCell, DispatchOrderCell } from './SalesDispatchShared'
import { PoolContinuation } from './SalesPoolShared'
import { usePoolPages } from './usePoolPages'
import {
  NO_REPORT_FILTER,
  REPORT_PARAM,
  fetchLostOpportunityReport,
  fetchOpenOrderReport,
  fetchPriceVarianceReport,
  parseReportFilterKey,
  reportFilterKey,
  type ReportFilter,
} from './reportApi'
import type { LostOpportunity, OpenOrderGroup, OpenOrderLine, PriceVariance } from './reportContract'
import { AMOUNT_STATE_TEXT, REPORT_TABS, REPORT_TEXT, UNMEASURED_REASON_TEXT, type ReportTab } from './reportText'
import { formatBusinessDate, formatInstant } from './format'
import { isDateOnly, isReferenceId } from './wirePrimitives'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { DataTable, type DataTableColumn } from '../ui/DataTable'
import { Field } from '../ui/Field'
import { StatusIndicator } from '../ui/StatusIndicator'
import styles from './SalesPool.module.css'
import tabStyles from './SalesDraftDetailPage.module.css'

const T = REPORT_TEXT

export function SalesReportsPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <SalesReports key={namespace ?? 'no-context'} namespace={namespace} />
}

const isTab = (value: string | null): value is ReportTab => value !== null && (REPORT_TABS as readonly string[]).includes(value)

/** The filter the address names; a malformed member is never sent (it is dropped, not guessed). */
function filterFrom(params: URLSearchParams): ReportFilter {
  const from = params.get(REPORT_PARAM.from)
  const to = params.get(REPORT_PARAM.to)
  const account = params.get(REPORT_PARAM.account)
  const filter = {
    from: from !== null && isDateOnly(from) ? from : null,
    to: to !== null && isDateOnly(to) ? to : null,
    companyAccountId: account !== null && isReferenceId(account) ? account : null,
  }
  return filter.from !== null && filter.to !== null && filter.from > filter.to ? { ...filter, to: null } : filter
}

function SalesReports({ namespace }: { readonly namespace: string | null }) {
  const [params, setParams] = useSearchParams()
  const requested = params.get(REPORT_PARAM.tab)
  const active: ReportTab = isTab(requested) ? requested : 'open-orders'
  const filter = filterFrom(params)
  const baseId = useId()
  const tabRefs = useRef<Partial<Record<ReportTab, HTMLButtonElement | null>>>({})

  function update(next: { readonly tab?: ReportTab; readonly filter?: ReportFilter }) {
    const tab = next.tab ?? active
    const nextFilter = next.filter ?? filter
    const search = new URLSearchParams()
    search.set(REPORT_PARAM.tab, tab)
    if (nextFilter.from !== null) search.set(REPORT_PARAM.from, nextFilter.from)
    if (nextFilter.to !== null) search.set(REPORT_PARAM.to, nextFilter.to)
    if (nextFilter.companyAccountId !== null) search.set(REPORT_PARAM.account, nextFilter.companyAccountId)
    setParams(search)
  }

  function move(event: KeyboardEvent<HTMLButtonElement>, current: ReportTab) {
    const index = REPORT_TABS.indexOf(current)
    let next: ReportTab | null = null
    if (event.key === 'ArrowRight') next = REPORT_TABS[(index + 1) % REPORT_TABS.length]
    else if (event.key === 'ArrowLeft') next = REPORT_TABS[(index - 1 + REPORT_TABS.length) % REPORT_TABS.length]
    else if (event.key === 'Home') next = REPORT_TABS[0]
    else if (event.key === 'End') next = REPORT_TABS[REPORT_TABS.length - 1]
    if (next === null) return
    event.preventDefault()
    update({ tab: next })
    tabRefs.current[next]?.focus()
  }

  const onlyCustomer = (companyAccountId: string) => update({ filter: { ...filter, companyAccountId } })

  return (
    <section className={styles.page} aria-labelledby="sales-reports-heading">
      <header className={styles.header}>
        <div className={styles.titleRow}>
          <h1 id="sales-reports-heading" className={styles.title}>
            {T.heading}
          </h1>
        </div>
        <p className={styles.muted}>{T.intro}</p>
      </header>
      <div className={tabStyles.tabs}>
        <div role="tablist" aria-label={T.tabsLabel} className={tabStyles.tabList}>
          {REPORT_TABS.map((tab) => {
            const selected = tab === active
            return (
              <button
                key={tab}
                ref={(element) => {
                  tabRefs.current[tab] = element
                }}
                type="button"
                role="tab"
                id={`${baseId}-tab-${tab}`}
                aria-selected={selected}
                // Only the selected tab's panel is rendered: the others point at nothing.
                aria-controls={selected ? `${baseId}-panel-${tab}` : undefined}
                tabIndex={selected ? 0 : -1}
                className={tabStyles.tab}
                data-testid={`sales-report-tab-${tab}`}
                onClick={() => update({ tab })}
                onKeyDown={(event) => move(event, tab)}
              >
                {T.tab[tab]}
              </button>
            )
          })}
        </div>
        <div
          role="tabpanel"
          id={`${baseId}-panel-${active}`}
          aria-labelledby={`${baseId}-tab-${active}`}
          tabIndex={0}
          className={tabStyles.tabPanel}
          data-testid={`sales-report-panel-${active}`}
        >
          <FilterForm key={`${active}|${reportFilterKey(filter)}`} filter={filter} onApply={(next) => update({ filter: next })} />
          {active === 'open-orders' ? (
            <OpenOrders namespace={namespace} filter={filter} onlyCustomer={onlyCustomer} />
          ) : active === 'lost' ? (
            <LostOpportunities namespace={namespace} filter={filter} onlyCustomer={onlyCustomer} />
          ) : (
            <PriceVarianceReportPanel namespace={namespace} filter={filter} />
          )}
        </div>
      </div>
    </section>
  )
}

/** The date range form; the customer narrowing comes from a row ("Yalnız bu müşteri") and is cleared here. */
function FilterForm({ filter, onApply }: { readonly filter: ReportFilter; readonly onApply: (filter: ReportFilter) => void }) {
  const [from, setFrom] = useState(filter.from ?? '')
  const [to, setTo] = useState(filter.to ?? '')
  const invalid = from !== '' && to !== '' && from > to
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (invalid) return
    onApply({ from: from === '' ? null : from, to: to === '' ? null : to, companyAccountId: filter.companyAccountId })
  }
  return (
    <form className={styles.toolbar} onSubmit={submit} aria-label={T.filter.legend} noValidate>
      <Field id="sales-report-from" label={T.filter.from} className={styles.filter}>
        {(control) => <input {...control} type="date" className={styles.control} value={from} onChange={(event) => setFrom(event.target.value)} />}
      </Field>
      <Field id="sales-report-to" label={T.filter.to} className={styles.filter} error={invalid ? T.filter.rangeInvalid : undefined}>
        {(control) => <input {...control} type="date" className={styles.control} value={to} onChange={(event) => setTo(event.target.value)} />}
      </Field>
      <div className={styles.actions}>
        <Button type="submit" variant="secondary" disabled={invalid}>
          {T.filter.apply}
        </Button>
        <Button type="button" variant="secondary" onClick={() => onApply(NO_REPORT_FILTER)}>
          {T.filter.clear}
        </Button>
      </div>
      {filter.companyAccountId !== null ? (
        <p className={styles.muted}>
          {T.filter.filteredTo}{' '}
          <Link className={styles.link} to={`${SALES_ROUTE.reports}?${narrowedOff(filter)}`}>
            {T.filter.allCustomers}
          </Link>
        </p>
      ) : null}
    </form>
  )
}

function narrowedOff(filter: ReportFilter): string {
  const search = new URLSearchParams()
  if (filter.from !== null) search.set(REPORT_PARAM.from, filter.from)
  if (filter.to !== null) search.set(REPORT_PARAM.to, filter.to)
  return search.toString()
}

const isFiltered = (filter: ReportFilter) => filter.from !== null || filter.to !== null || filter.companyAccountId !== null

function Quantity({ value, unit }: { readonly value: string; readonly unit: string }) {
  return (
    <span className={styles.numeric}>
      {value} {unit}
    </span>
  )
}

/* ── Açık siparişler ───────────────────────────────────────────────────────── */

function OpenOrders({
  namespace,
  filter,
  onlyCustomer,
}: {
  readonly namespace: string | null
  readonly filter: ReportFilter
  readonly onlyCustomer: (companyAccountId: string) => void
}) {
  const O = T.openOrders
  const list = usePoolPages(namespace, 'sales-report-open-orders', fetchOpenOrderReport, reportFilterKey(filter))
  if (list.rows === null) {
    return <StateBlock state={list.firstState ?? { kind: 'initial-loading' }} subject={O.subject} onRetry={list.retryFirst} />
  }
  const intro = <p className={styles.muted}>{O.intro}</p>
  if (list.rows.length === 0 && list.nextCursor !== null) {
    // The server's bounded scan ended this page before it found an open group: continue on request, never "Kayıt yok".
    return (
      <>
        {intro}
        <p className={styles.notice} role="status" data-testid="sales-report-open-orders-scan-continues">
          {O.scanContinues}
        </p>
        <PoolContinuation list={list} />
      </>
    )
  }
  if (list.rows.length === 0) {
    return (
      <>
        {intro}
        <StateBlock state={{ kind: isFiltered(filter) ? 'empty-filtered' : 'empty-no-data' }} subject={O.subject} />
      </>
    )
  }

  const sum = (group: OpenOrderGroup, value: string | null) =>
    value === null ? <span className={styles.muted}>{T.common.none}</span> : <Quantity value={value} unit={group.unitCode} />
  const columns: readonly DataTableColumn<OpenOrderGroup>[] = [
    {
      key: 'customer',
      header: T.common.customer,
      pin: 'start',
      cell: (group) => (
        <>
          <DispatchCustomerCell customer={group.customer} />
          {filter.companyAccountId === null ? (
            <>
              {' '}
              <button type="button" className={styles.link} onClick={() => onlyCustomer(group.customer.companyAccountId)}>
                {T.filter.onlyCustomer}
              </button>
            </>
          ) : null}
        </>
      ),
    },
    { key: 'product', header: T.common.product, cell: (group) => `${group.productCode} ${group.productName}` },
    { key: 'unit', header: O.unit, cell: (group) => group.unitCode },
    { key: 'orders', header: O.orderCount, align: 'end', cell: (group) => <span className={styles.numeric}>{group.orderCount}</span> },
    { key: 'approved', header: O.approved, align: 'end', cell: (group) => sum(group, group.approvedQuantity) },
    { key: 'shipped', header: O.shipped, align: 'end', cell: (group) => sum(group, group.shippedQuantity) },
    {
      key: 'remaining',
      header: O.remaining,
      align: 'end',
      cell: (group) => (
        <>
          {sum(group, group.remainingQuantity)}
          {group.state === 'TooManyOrders' ? (
            <StatusIndicator tone="warning">
              <span data-testid={`sales-report-group-too-many-${group.id}`}>{O.tooManyOrders(group.scopeOrderCount)}</span>
            </StatusIndicator>
          ) : group.complete ? null : (
            <StatusIndicator tone="warning">
              <span data-testid={`sales-report-group-incomplete-${group.id}`}>{O.incomplete}</span>
            </StatusIndicator>
          )}
        </>
      ),
    },
    // SAT-D137 (SINAN_KARARLARI_2026-10-31_SATIS_EK19.md): kept apart from the open tonnage, never added to it.
    { key: 'awaiting', header: O.awaitingAcceptance, align: 'end', cell: (group) => sum(group, group.awaitingAcceptanceQuantity) },
  ]

  return (
    <>
      {intro}
      <DataTable label={O.caption} columns={columns} rows={list.rows} rowKey={(group) => group.id} rowAttributes={(group) => ({ 'data-group-id': group.id })} />
      {list.rows.filter((group) => group.state === 'Measured').map((group) => (
        <OpenOrderDetail key={group.id} group={group} />
      ))}
      <PoolContinuation list={list} />
    </>
  )
}

function OpenOrderDetail({ group }: { readonly group: OpenOrderGroup }) {
  const O = T.openOrders
  const product = `${group.productCode} ${group.productName}`
  const columns: readonly DataTableColumn<OpenOrderLine>[] = [
    {
      key: 'order',
      header: O.order,
      pin: 'start',
      cell: (line) => (
        <>
          <DispatchOrderCell orderId={line.orderId} documentNo={line.documentNo} lineNo={line.lineNo} />
          {line.headerRevisionPending ? (
            <StatusIndicator tone="warning">
              <span data-testid={`sales-report-line-header-pending-${line.lineId}`}>{O.headerRevisionPending}</span>
            </StatusIndicator>
          ) : null}
        </>
      ),
    },
    { key: 'orderDate', header: O.orderDate, cell: (line) => formatBusinessDate(line.businessDate) },
    {
      key: 'delivery',
      header: O.delivery,
      cell: (line) => (line.requestedDeliveryDate === null ? T.common.none : formatBusinessDate(line.requestedDeliveryDate)),
    },
    {
      key: 'basis',
      header: O.basisRevision,
      cell: (line) => line.basisRevision ?? <span className={styles.muted}>{O.noAcceptedRevision}</span>,
    },
    { key: 'approved', header: O.approved, align: 'end', cell: (line) => <Quantity value={line.approvedQuantity} unit={group.unitCode} /> },
    { key: 'shipped', header: O.shipped, align: 'end', cell: (line) => <Quantity value={line.shippedQuantity} unit={group.unitCode} /> },
    {
      key: 'remaining',
      header: O.remaining,
      align: 'end',
      cell: (line) => (
        <>
          <Quantity value={line.remainingQuantity} unit={group.unitCode} />
          {line.completedWithinTolerance ? (
            <StatusIndicator tone="success">
              <span data-testid={`sales-report-line-tolerance-${line.lineId}`}>{O.completedWithinTolerance}</span>
            </StatusIndicator>
          ) : null}
        </>
      ),
    },
    {
      key: 'awaiting',
      header: O.awaitingAcceptance,
      align: 'end',
      cell: (line) => (
        <>
          <Quantity value={line.awaitingAcceptanceQuantity} unit={group.unitCode} />
          {line.awaitingAcceptanceRevision === null ? null : (
            <span className={styles.muted} data-testid={`sales-report-line-awaiting-${line.lineId}`}>
              {' '}
              {O.awaitingRevision(line.awaitingAcceptanceRevision)}
            </span>
          )}
        </>
      ),
    },
  ]
  return (
    <details className={styles.section} data-testid={`sales-report-group-detail-${group.id}`}>
      <summary>
        {O.detail}: {group.customer.legalName} — {product} ({group.unitCode})
      </summary>
      {group.lines.length > 0 ? (
        <DataTable label={O.detailCaption(product, group.customer.legalName)} columns={columns} rows={group.lines} rowKey={(line) => line.lineId} />
      ) : null}
      {group.unmeasured.length > 0 ? (
        <div className={styles.notice} role="note">
          <p>
            <strong>{O.unmeasured}</strong>
          </p>
          <ul aria-label={O.unmeasured}>
            {group.unmeasured.map((line) => (
              <li key={line.lineId}>
                {line.documentNo} / {line.lineNo} — {UNMEASURED_REASON_TEXT[line.reason]}
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </details>
  )
}

/* ── Kayıp fırsatlar ───────────────────────────────────────────────────────── */

function LostOpportunities({
  namespace,
  filter,
  onlyCustomer,
}: {
  readonly namespace: string | null
  readonly filter: ReportFilter
  readonly onlyCustomer: (companyAccountId: string) => void
}) {
  const L = T.lost
  const list = usePoolPages(namespace, 'sales-report-lost', fetchLostOpportunityReport, reportFilterKey(filter))
  if (list.rows === null) {
    return <StateBlock state={list.firstState ?? { kind: 'initial-loading' }} subject={L.subject} onRetry={list.retryFirst} />
  }
  const intro = <p className={styles.muted}>{L.intro}</p>
  if (list.rows.length === 0) {
    return (
      <>
        {intro}
        <StateBlock state={{ kind: isFiltered(filter) ? 'empty-filtered' : 'empty-no-data' }} subject={L.subject} />
      </>
    )
  }
  // Each row carries its own page's disclosure: a later page read under another grant shows its own state.
  const anyWithheld = list.rows.some((item) => item.amountWithheld)
  const columns: DataTableColumn<LostOpportunity>[] = [
    { key: 'quote', header: L.quote, pin: 'start', cell: (item) => `${item.documentNo} (${item.revision})` },
    { key: 'quoteDate', header: L.quoteDate, cell: (item) => formatBusinessDate(item.businessDate) },
    {
      key: 'customer',
      header: T.common.customer,
      cell: (item) => (
        <>
          <DispatchCustomerCell customer={item.customer} />
          {filter.companyAccountId === null ? (
            <>
              {' '}
              <button type="button" className={styles.link} onClick={() => onlyCustomer(item.customer.companyAccountId)}>
                {T.filter.onlyCustomer}
              </button>
            </>
          ) : null}
        </>
      ),
    },
    {
      key: 'lostOn',
      header: L.lostOn,
      cell: (item) => <span title={formatInstant(item.lostAtUtc)}>{formatBusinessDate(item.lostOn)}</span>,
    },
    {
      key: 'reason',
      header: L.reason,
      cell: (item) => (item.reason === null ? <span className={styles.muted}>{L.legacyReason}</span> : `${item.reason.label} (${item.reason.code})`),
    },
    { key: 'explanation', header: L.explanation, cell: (item) => item.explanation ?? T.common.none },
    { key: 'lines', header: L.lineCount, align: 'end', cell: (item) => <span className={styles.numeric}>{item.lineCount}</span> },
  ]
  columns.push(
    {
      key: 'amount',
      header: L.netAmount,
      align: 'end',
      cell: (item) =>
        item.amountWithheld ? (
          <span className={styles.muted} data-testid={`sales-report-lost-amount-withheld-${item.id}`}>{L.withheldCell}</span>
        ) : item.amount?.netAmount != null ? (
          <span className={styles.numeric}>
            {item.amount.netAmount} <span className={styles.identifier}>{item.currencyId}</span>
          </span>
        ) : (
          T.common.none
        ),
    },
    {
      key: 'amountState',
      header: L.amountState,
      cell: (item) => (item.amount === null ? T.common.none : AMOUNT_STATE_TEXT[item.amount.state]),
    },
  )
  return (
    <>
      {intro}
      {anyWithheld ? (
        <p className={styles.notice} role="note" data-testid="sales-report-lost-withheld">
          {L.withheld}
        </p>
      ) : null}
      <DataTable label={L.caption} columns={columns} rows={list.rows} rowKey={(item) => item.id} rowAttributes={(item) => ({ 'data-quote-id': item.id })} />
      <PoolContinuation list={list} />
    </>
  )
}

/* ── Fiyat sapması ─────────────────────────────────────────────────────────── */

function PriceVarianceReportPanel({ namespace, filter }: { readonly namespace: string | null; readonly filter: ReportFilter }) {
  const P = T.priceVariance
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const filterKey = reportFilterKey(filter)
  const read = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-report-price-variance', filterKey],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPriceVarianceReport(companyId as string, signal, parseReportFilterKey(filterKey))
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const intro = <p className={styles.muted}>{P.intro}</p>
  const data = read.data?.type === 'ok-single' ? read.data.data : null
  if (data === null) {
    const state: RemoteViewState<unknown> =
      read.data === undefined ? { kind: 'initial-loading' } : (classifyOutcome(read.data) as RemoteViewState<unknown>)
    return (
      <>
        {intro}
        <StateBlock state={state} subject={P.subject} onRetry={() => void read.refetch()} />
      </>
    )
  }
  if (data.state === 'NoSource') {
    return (
      <>
        {intro}
        <p className={styles.notice} role="status" data-testid="sales-report-price-variance-no-source">
          {P.noSource}
        </p>
      </>
    )
  }
  if (data.items.length === 0) {
    return (
      <>
        {intro}
        <p className={styles.muted}>{P.empty}</p>
      </>
    )
  }
  const columns: readonly DataTableColumn<PriceVariance>[] = [
    { key: 'invoice', header: P.invoice, pin: 'start', cell: (row) => row.invoiceNo },
    { key: 'invoiceDate', header: P.invoiceDate, cell: (row) => formatBusinessDate(row.invoiceDate) },
    { key: 'customer', header: T.common.customer, cell: (row) => <DispatchCustomerCell customer={row.customer} /> },
    {
      key: 'order',
      header: P.order,
      cell: (row) => <DispatchOrderCell orderId={row.orderId} documentNo={row.orderDocumentNo} lineNo={row.orderLineNo} />,
    },
    { key: 'product', header: T.common.product, cell: (row) => `${row.productCode} ${row.productName}` },
    { key: 'quantity', header: P.quantity, align: 'end', cell: (row) => <Quantity value={row.invoicedQuantity} unit={row.unitCode} /> },
    { key: 'orderPrice', header: P.orderPrice, align: 'end', cell: (row) => <span className={styles.numeric}>{row.orderNetUnitPrice}</span> },
    { key: 'invoicePrice', header: P.invoicePrice, align: 'end', cell: (row) => <span className={styles.numeric}>{row.invoiceNetUnitPrice}</span> },
    { key: 'difference', header: P.difference, align: 'end', cell: (row) => <span className={styles.numeric}>{row.unitPriceDifference}</span> },
    {
      key: 'differenceAmount',
      header: P.differenceAmount,
      align: 'end',
      cell: (row) => (
        <span className={styles.numeric}>
          {row.differenceAmount} <span className={styles.identifier}>{row.currencyId}</span>
        </span>
      ),
    },
    { key: 'direction', header: P.direction, cell: (row) => (row.direction === 'Below' ? P.below : P.above) },
  ]
  return (
    <>
      {intro}
      <DataTable label={P.caption} columns={columns} rows={data.items} rowKey={(row) => `${row.invoiceId}|${row.orderLineId}`} />
    </>
  )
}
