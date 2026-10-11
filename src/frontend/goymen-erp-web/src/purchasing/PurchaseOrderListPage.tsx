/**
 * P-03 — purchase orders of the committed company, read from `GET /api/v1/purchasing/orders?status&cursor`.
 *
 * The columns are what the read carries and nothing more: no amount, no VAT, no currency, no total and no derived
 * status. The supplier is the record-bound display of the read DTO (#661). Continuation follows the contract:
 * `nextCursor = null` is the end, there is no total count, and a failed next page keeps the rows already shown and
 * offers an explicit retry of the SAME cursor — never an automatic resend.
 *
 * PURCHASING-DS-LIST-01 (GOY-DS-001): the same facts on the design system — the shared DataTable on a wide screen,
 * one labelled card per order on a phone (the shell's compact width), the status filter in a Field, the continuation
 * on Button and the status on StatusIndicator. A long supplier name wraps; nothing is cut.
 *
 * PURCHASING-SUPPLIER-FILTER-UI-01: a row's own supplier can be made the list's filter. It is the server's
 * `supplierPartyId` query (#671), taken only from that row's record-bound identity — no picker, no lookup, no
 * client-side filtering. The active filter stays on screen with its own remove control; applying, changing or
 * removing it drops the pages already read and starts the cursor over. A read the server refuses is shown as
 * refused: the list never quietly falls back to the unfiltered one.
 *
 * The status and the supplier filter live in the address (`?status=…&supplierPartyId=…`), so a link opens the
 * filtered list and the browser's Back/Forward bring a previous filter back. The address carries only the party
 * id: the supplier's name is the record-bound identity of an order of the answer (or of the row the filter was
 * taken from) — never looked up and never made up; with neither, the filter says its name is not available.
 * Filters set in one company/actor context are dropped from the address when the context changes.
 */

import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useLocation, useSearchParams } from 'react-router'
import { fetchPurchaseOrders } from './api'
import { PURCHASING_CAPABILITY, PURCHASING_ROUTE, purchaseOrderDetailPath } from './capabilities'
import { supplierDisplayOf, type PurchaseOrderStatus, type PurchaseOrderSummary } from './contract'
import { PURCHASE_ORDER_DETAIL_TEXT as T } from './purchaseOrderDetailText'
import { CAPABILITY, CAPABILITY_ROUTE } from '../shell/capabilities'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { DataTable, type DataTableColumn } from '../ui/DataTable'
import { Field } from '../ui/Field'
import { StatusIndicator, type StatusTone } from '../ui/StatusIndicator'
import { createRequestFence } from './requestFence'
import styles from './PurchaseOrderListPage.module.css'

const STATUS: Record<PurchaseOrderStatus, string> = { Draft: 'Taslak', Approved: 'Onaylandı' }

/** The dot's colour only — the status text carries the meaning. */
const STATUS_TONE: Record<PurchaseOrderStatus, StatusTone> = { Draft: 'neutral', Approved: 'success' }

const LIST_TEXT = {
  heading: 'Satın alma siparişleri',
  subject: 'satın alma siparişleri',
  tableLabel: 'Şirketin satın alma siparişleri',
  create: 'Yeni',
  filter: 'Durum',
  filterAll: 'Tümü',
  scope: 'Bu görünüm fiyat veya stok göstermez; yalnız satın alma taahhüdünün sunucuda kayıtlı durumunu gösterir.',
  order: 'Sipariş',
  status: 'Durum',
  businessDate: 'İş tarihi',
  lineCount: 'Satır',
  updatedAt: 'Güncellendi',
  loadMore: 'Daha fazla göster',
  loadingMore: 'Sonraki sayfa yükleniyor…',
  retrySamePage: 'Aynı sayfayı yeniden dene',
  nextPageSubject: 'sonraki sipariş sayfası',
  filterBySupplier: 'Filtrele',
  supplierFilter: 'Tedarikçi filtresi',
  clearSupplierFilter: 'Tedarikçi filtresini kaldır',
  supplierFilterUnnamed: 'Tedarikçi adı bu listeden okunamadı',
} as const

const HEADING_ID = 'purchase-order-list-heading'
const SUPPLIER_FILTER_VALUE_ID = 'purchase-order-supplier-filter-value'

const STATUS_PARAM = 'status'
const SUPPLIER_PARAM = 'supplierPartyId'
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const NIL_GUID = '00000000-0000-0000-0000-000000000000'

/** Only the server's two states are a status filter; anything else in the address is no filter. */
function statusOf(params: URLSearchParams): PurchaseOrderStatus | null {
  const value = params.get(STATUS_PARAM)
  return value === 'Draft' || value === 'Approved' ? value : null
}

/** Only a real, non-empty party id is a supplier filter; it is compared in the server's lower-case form. */
function supplierPartyIdOf(params: URLSearchParams): string | null {
  const value = params.get(SUPPLIER_PARAM)?.toLowerCase() ?? null
  return value !== null && GUID.test(value) && value !== NIL_GUID ? value : null
}

/**
 * What a history entry remembers beside its address: the context its filters were set in, and — when the filter
 * was taken from a row — that row's record-bound supplier identity. An entry opened from a bare link has none.
 */
interface FilterEntryState {
  readonly namespace: string
  readonly supplierPartyId: string | null
  readonly supplierLabel: string | null
}

function filterEntryStateOf(state: unknown): FilterEntryState | null {
  const entry = typeof state === 'object' && state !== null ? (state as { readonly purchaseOrderFilter?: unknown }).purchaseOrderFilter : null
  if (typeof entry !== 'object' || entry === null) return null
  const { namespace, supplierPartyId, supplierLabel } = entry as Record<string, unknown>
  return typeof namespace === 'string' && (supplierPartyId === null || typeof supplierPartyId === 'string') &&
    (supplierLabel === null || typeof supplierLabel === 'string')
    ? { namespace, supplierPartyId, supplierLabel }
    : null
}


/** The shell's phone width (`AppShell` COMPACT_VIEWPORT): rows become cards instead of a squeezed table. */
const CARD_VIEWPORT = '(max-width: 767px)'

function subscribeCards(onChange: () => void): () => void {
  if (typeof window.matchMedia !== 'function') return () => {}
  const query = window.matchMedia(CARD_VIEWPORT)
  query.addEventListener('change', onChange)
  return () => query.removeEventListener('change', onChange)
}

function readCards(): boolean {
  return typeof window.matchMedia === 'function' && window.matchMedia(CARD_VIEWPORT).matches
}

/** Presentation only: the wire's `yyyy-MM-dd` business date in Turkish order. Anything else is shown as received. */
function formatBusinessDate(value: string): string {
  const parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  return parts === null ? value : `${parts[3]}.${parts[2]}.${parts[1]}`
}

/** Presentation only: the wire's instant in the reader's local time. An unreadable value is shown as received. */
function formatInstant(value: string): string {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}

const SUPPLIER_LINE = { display: 'block' } as const

/**
 * The supplier bound to this order by the read DTO (#661): code and legal name, the trade name beneath when it adds
 * something. It links to the Cari card only with that card's own capability; otherwise it is plain text. No lookup
 * is made and no id is shown in place of a name — an order without a trustworthy identity says so.
 */
export function PurchaseOrderSupplierIdentity({ order, canOpenParty }: { readonly order: PurchaseOrderSummary; readonly canOpenParty: boolean }) {
  const supplier = supplierDisplayOf(order)
  if (supplier === null) return <span data-supplier-display="unavailable">{T.supplierUnavailable}</span>
  const identity = `${supplier.code} — ${supplier.legalName}`
  const tradeName = supplier.tradeName !== null && supplier.tradeName.trim() !== '' && supplier.tradeName !== supplier.legalName
    ? supplier.tradeName
    : null
  return <span data-supplier-display="record">
    <span style={SUPPLIER_LINE}>{canOpenParty
      ? <Link to={CAPABILITY_ROUTE[CAPABILITY.cariPartiesDetail].replace(':partyId', encodeURIComponent(supplier.partyId))}>{identity}</Link>
      : identity}</span>
    {tradeName === null ? null : <span style={SUPPLIER_LINE}>{T.supplierTradeName}: {tradeName}</span>}
  </span>
}

export function PurchaseOrderListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <PurchaseOrderList key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function PurchaseOrderList({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const canOpen = hasCapability(shell, PURCHASING_CAPABILITY.orderDetail as never)
  const canCreate = hasCapability(shell, PURCHASING_CAPABILITY.orderCreate as never)
  const canOpenParty = hasCapability(shell, CAPABILITY.cariPartiesDetail)
  /** Cards or table is a layout choice only: the same rows, the same order, no new read when it flips. */
  const cards = useSyncExternalStore(subscribeCards, readCards, () => false)
  const location = useLocation()
  const [searchParams, setSearchParams] = useSearchParams()
  const entry = filterEntryStateOf(location.state)
  /** Filters that were set in another company/actor context are not this context's: they are not read, and are removed. */
  const foreignFilters = namespace !== null && entry !== null && entry.namespace !== namespace
  const status = foreignFilters ? null : statusOf(searchParams)
  /** The server's supplier filter, or none: the one value both the first page and every next page send. */
  const supplierPartyId = foreignFilters ? null : supplierPartyIdOf(searchParams)
  useEffect(() => {
    if (namespace === null) return
    if (foreignFilters) {
      const cleared = new URLSearchParams(searchParams)
      cleared.delete(STATUS_PARAM)
      cleared.delete(SUPPLIER_PARAM)
      setSearchParams(cleared, { replace: true, state: null })
    } else if (entry === null && (status !== null || supplierPartyId !== null)) {
      // A bare link's filters are bound to the context they were first read in, so a later context change drops them.
      setSearchParams(searchParams, { replace: true, state: { purchaseOrderFilter: { namespace, supplierPartyId, supplierLabel: null } } })
    }
  }, [entry, foreignFilters, namespace, searchParams, setSearchParams, status, supplierPartyId])
  const [more, setMore] = useState<readonly PurchaseOrderSummary[]>([])
  const [cursor, setCursor] = useState<string | null | undefined>(undefined)
  const [continuation, setContinuation] = useState<'idle' | 'loading' | RemoteViewState<unknown>>('idle')
  const loading = useRef(false)
  const pageFence = useRef(createRequestFence())
  const statusControl = useRef<HTMLSelectElement>(null)
  const clearSupplierControl = useRef<HTMLButtonElement>(null)
  /** Where focus goes once the supplier filter has changed: the control it was on is gone by then. */
  const focusAfterSupplierChange = useRef<'clear' | 'status' | null>(null)
  useEffect(() => {
    if (focusAfterSupplierChange.current === 'clear') clearSupplierControl.current?.focus()
    else if (focusAfterSupplierChange.current === 'status') statusControl.current?.focus()
    focusAfterSupplierChange.current = null
  }, [supplierPartyId])
  // A filter that changed without this page's own controls (Back/Forward, a link) restarts the list the same way:
  // the pages already read and the cursor belong to the filter they were read with.
  const filterKey = `${status ?? ''}|${supplierPartyId ?? ''}`
  const [listFilterKey, setListFilterKey] = useState(filterKey)
  if (listFilterKey !== filterKey) {
    setListFilterKey(filterKey)
    setMore([])
    setCursor(undefined)
    setContinuation('idle')
  }
  useEffect(() => {
    const fence = pageFence.current
    return () => {
      fence.invalidate()
      loading.current = false
    }
  }, [namespace, shell.routeEntryGeneration, filterKey])
  const first = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'purchasing-orders', status ?? 'all', supplierPartyId ?? 'all-suppliers'],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPurchaseOrders(companyId ?? '', { status, supplierPartyId }, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const page = first.data?.type === 'ok-single' ? first.data.data : null
  const nextCursor = cursor === undefined ? page?.nextCursor ?? null : cursor
  const loadMore = useCallback(async () => {
    if (loading.current || companyId === null || nextCursor === null) return
    loading.current = true
    setContinuation('loading')
    const token = pageFence.current.capture()
    const snapshot = boundary.capture()
    const outcome = await fetchPurchaseOrders(companyId, { cursor: nextCursor, status, supplierPartyId }, token.signal)
    if (!pageFence.current.isCurrent(token)) return
    loading.current = false
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, snapshot)
    if (outcome.type === 'ok-single') {
      setMore((current) => {
        const seen = new Set([...(page?.items ?? []), ...current].map((item) => item.id))
        return [...current, ...outcome.data.items.filter((item) => !seen.has(item.id))]
      })
      setCursor(outcome.data.nextCursor)
      setContinuation('idle')
    } else setContinuation(classifyOutcome(outcome) as RemoteViewState<unknown>)
  }, [boundary, companyId, nextCursor, page, status, supplierPartyId])

  /** Any filter change drops the pages already read, abandons a next page in flight and starts the cursor over. */
  function restartList() {
    pageFence.current.invalidate()
    loading.current = false
    setMore([])
    setCursor(undefined)
    setContinuation('idle')
  }

  // The filtered answer's own orders name the supplier: every one of them is that supplier's (./api refuses the
  // page otherwise). Before an answer, or when it is empty, the row the filter was taken from still does.
  const answered = page !== null && page.items.length > 0 ? supplierDisplayOf(page.items[0]) : null
  const supplierLabel = supplierPartyId === null
    ? null
    : answered !== null && answered.partyId === supplierPartyId
      ? `${answered.code} — ${answered.legalName}`
      : entry !== null && entry.supplierPartyId === supplierPartyId ? entry.supplierLabel : null

  /** A filter change is a new history entry: the address says what is filtered, and Back returns to what was. */
  function showFilters(nextStatus: PurchaseOrderStatus | null, nextSupplierPartyId: string | null, nextSupplierLabel: string | null) {
    restartList()
    const next = new URLSearchParams(searchParams)
    if (nextStatus === null) next.delete(STATUS_PARAM)
    else next.set(STATUS_PARAM, nextStatus)
    if (nextSupplierPartyId === null) next.delete(SUPPLIER_PARAM)
    else next.set(SUPPLIER_PARAM, nextSupplierPartyId)
    setSearchParams(next, {
      state: namespace === null ? null : { purchaseOrderFilter: { namespace, supplierPartyId: nextSupplierPartyId, supplierLabel: nextSupplierLabel } },
    })
  }

  /** The filter is the server's (`status`); changing it restarts the list and keeps the supplier filter. */
  function chooseStatus(value: string) {
    showFilters(value === 'Draft' || value === 'Approved' ? value : null, supplierPartyId, supplierLabel)
  }

  /** The row's own record-bound supplier becomes the server filter; a row without one offers no filter. */
  function filterBySupplierOf(row: PurchaseOrderSummary) {
    const display = supplierDisplayOf(row)
    if (display === null) return
    focusAfterSupplierChange.current = 'clear'
    showFilters(status, display.partyId, `${display.code} — ${display.legalName}`)
  }

  function clearSupplierFilter() {
    focusAfterSupplierChange.current = 'status'
    showFilters(status, null, null)
  }

  const header = (
    <header className={styles.header}>
      <div className={styles.titleRow}>
        <h1 id={HEADING_ID} className={styles.title}>{LIST_TEXT.heading}</h1>
        {/* A link styled as the primary action: it navigates, so it stays a link (no button inside it). */}
        {canCreate
          ? <Link className={styles.createLink} to={PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderCreate]}>{LIST_TEXT.create}</Link>
          : null}
      </div>
      <div className={styles.toolbar}>
        <Field id="purchase-order-status-filter" label={LIST_TEXT.filter} className={styles.filter}>
          {(control) => (
            <select {...control} ref={statusControl} className={styles.select} value={status ?? ''} onChange={(event) => chooseStatus(event.target.value)}>
              <option value="">{LIST_TEXT.filterAll}</option>
              <option value="Draft">{STATUS.Draft}</option>
              <option value="Approved">{STATUS.Approved}</option>
            </select>
          )}
        </Field>
        {/* The active supplier filter stays on screen, on every width, with its own remove control. */}
        {supplierPartyId !== null ? (
          <div className={styles.activeFilter} role="group" aria-label={LIST_TEXT.supplierFilter} data-testid="purchase-order-supplier-filter">
            <span className={styles.activeFilterLabel}>{LIST_TEXT.supplierFilter}</span>
            <span
              className={styles.activeFilterValue}
              id={SUPPLIER_FILTER_VALUE_ID}
              data-supplier-name={supplierLabel === null ? 'unavailable' : 'record'}
            >
              {supplierLabel ?? LIST_TEXT.supplierFilterUnnamed}
            </span>
            <Button
              ref={clearSupplierControl}
              type="button"
              size="sm"
              variant="secondary"
              aria-describedby={SUPPLIER_FILTER_VALUE_ID}
              onClick={clearSupplierFilter}
            >
              {LIST_TEXT.clearSupplierFilter}
            </Button>
          </div>
        ) : null}
      </div>
    </header>
  )

  if (companyId === null) {
    return (
      <section className={styles.page} aria-labelledby={HEADING_ID}>
        {header}
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={LIST_TEXT.subject} />
      </section>
    )
  }
  if (page === null) {
    const state = first.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(first.data)
    return (
      <section className={styles.page} aria-labelledby={HEADING_ID}>
        {header}
        <StateBlock state={state as RemoteViewState<unknown>} subject={LIST_TEXT.subject} onRetry={() => void first.refetch()} />
      </section>
    )
  }

  const rows = [...page.items, ...more]

  // The cells the table and the cards share, so both say exactly the same thing.
  const orderCell = (row: PurchaseOrderSummary) => canOpen
    ? <Link className={styles.orderLink} to={purchaseOrderDetailPath(row.id)}>…{row.id.slice(-8)}</Link>
    : <span className={styles.orderId}>…{row.id.slice(-8)}</span>
  const statusCell = (row: PurchaseOrderSummary) => <StatusIndicator tone={STATUS_TONE[row.status]}>{STATUS[row.status]}</StatusIndicator>
  // The filter action sits beside the identity, outside it: the identity element holds the supplier and nothing else.
  // While a supplier filter is active every row is that supplier's, so the action is not repeated on each of them.
  const supplierCell = (row: PurchaseOrderSummary) => {
    const display = supplierPartyId === null ? supplierDisplayOf(row) : null
    return (
      <span className={styles.supplierCell}>
        <span className={styles.supplier} data-testid={`purchase-order-supplier-${row.id}`}>
          <PurchaseOrderSupplierIdentity order={row} canOpenParty={canOpenParty} />
        </span>
        {display === null ? null : (
          <Button
            type="button"
            size="sm"
            variant="secondary"
            className={styles.filterBySupplier}
            data-testid={`purchase-order-filter-supplier-${row.id}`}
            onClick={() => filterBySupplierOf(row)}
          >
            {LIST_TEXT.filterBySupplier}
            <span className={styles.srOnly}>: {display.code} — {display.legalName}</span>
          </Button>
        )}
      </span>
    )
  }

  const columns: readonly DataTableColumn<PurchaseOrderSummary>[] = [
    { key: 'order', header: LIST_TEXT.order, cell: orderCell, pin: 'start', className: styles.nowrap },
    { key: 'status', header: LIST_TEXT.status, cell: statusCell },
    { key: 'supplier', header: T.supplierColumn, cell: supplierCell, className: styles.supplierColumn },
    { key: 'effectiveOn', header: LIST_TEXT.businessDate, cell: (row) => formatBusinessDate(row.effectiveOn), className: styles.nowrap },
    { key: 'lineCount', header: LIST_TEXT.lineCount, cell: (row) => row.lineCount, align: 'end' },
    { key: 'updatedAt', header: LIST_TEXT.updatedAt, cell: (row) => formatInstant(row.updatedAtUtc), className: styles.nowrap },
  ]

  return (
    <section className={styles.page} aria-labelledby={HEADING_ID} data-testid="purchase-order-list">
      {header}
      <p className={styles.scope}>{LIST_TEXT.scope}</p>
      {rows.length === 0 ? (
        <StateBlock state={{ kind: status === null && supplierPartyId === null ? 'empty-no-data' : 'empty-filtered' }} subject={LIST_TEXT.subject} />
      ) : cards ? (
        /* Phone: one card per order, every fact labelled, in the table's order. */
        <ul className={styles.cards} aria-label={LIST_TEXT.tableLabel} data-testid="purchase-order-cards">
          {rows.map((row) => (
            <li key={row.id} className={styles.card} data-order-id={row.id}>
              <div className={styles.cardHead}>
                <span className={styles.cardOrder}><span className={styles.cardOrderLabel}>{LIST_TEXT.order}</span> {orderCell(row)}</span>
                {statusCell(row)}
              </div>
              <dl className={styles.cardFacts}>
                <div className={styles.wide}><dt>{T.supplierColumn}</dt><dd>{supplierCell(row)}</dd></div>
                <div><dt>{LIST_TEXT.businessDate}</dt><dd>{formatBusinessDate(row.effectiveOn)}</dd></div>
                <div><dt>{LIST_TEXT.lineCount}</dt><dd>{row.lineCount}</dd></div>
                <div className={styles.wide}><dt>{LIST_TEXT.updatedAt}</dt><dd>{formatInstant(row.updatedAtUtc)}</dd></div>
              </dl>
            </li>
          ))}
        </ul>
      ) : (
        <DataTable
          label={LIST_TEXT.tableLabel}
          columns={columns}
          rows={rows}
          rowKey={(row) => row.id}
          rowAttributes={(row) => ({ 'data-order-id': row.id })}
        />
      )}
      {nextCursor !== null ? (
        <div className={styles.continuation}>
          {typeof continuation === 'object' ? (
            <div className={styles.inlineError}>
              <StateBlock state={continuation} subject={LIST_TEXT.nextPageSubject} />
              <Button type="button" variant="secondary" onClick={() => void loadMore()}>{LIST_TEXT.retrySamePage}</Button>
            </div>
          ) : (
            <Button
              type="button"
              variant="secondary"
              onClick={() => void loadMore()}
              disabled={continuation === 'loading'}
              aria-busy={continuation === 'loading'}
            >
              {continuation === 'loading' ? LIST_TEXT.loadingMore : LIST_TEXT.loadMore}
            </Button>
          )}
        </div>
      ) : null}
    </section>
  )
}
