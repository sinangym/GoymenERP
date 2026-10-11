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
 */

import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
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
} as const

const HEADING_ID = 'purchase-order-list-heading'

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
  const [status, setStatus] = useState<PurchaseOrderStatus | null>(null)
  const [more, setMore] = useState<readonly PurchaseOrderSummary[]>([])
  const [cursor, setCursor] = useState<string | null | undefined>(undefined)
  const [continuation, setContinuation] = useState<'idle' | 'loading' | RemoteViewState<unknown>>('idle')
  const loading = useRef(false)
  const pageFence = useRef(createRequestFence())
  useEffect(() => {
    const fence = pageFence.current
    return () => fence.invalidate()
  }, [namespace, shell.routeEntryGeneration])
  const first = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'purchasing-orders', status ?? 'all'],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPurchaseOrders(companyId ?? '', { status }, signal)
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
    const outcome = await fetchPurchaseOrders(companyId, { cursor: nextCursor, status }, token.signal)
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
  }, [boundary, companyId, nextCursor, page, status])

  /** The filter is the server's (`status`); changing it drops the pages already read and restarts the list. */
  function chooseStatus(value: string) {
    pageFence.current.invalidate()
    loading.current = false
    setStatus(value === '' ? null : value as PurchaseOrderStatus)
    setMore([])
    setCursor(undefined)
    setContinuation('idle')
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
            <select {...control} className={styles.select} value={status ?? ''} onChange={(event) => chooseStatus(event.target.value)}>
              <option value="">{LIST_TEXT.filterAll}</option>
              <option value="Draft">{STATUS.Draft}</option>
              <option value="Approved">{STATUS.Approved}</option>
            </select>
          )}
        </Field>
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
  const supplierCell = (row: PurchaseOrderSummary) => (
    <span className={styles.supplier} data-testid={`purchase-order-supplier-${row.id}`}>
      <PurchaseOrderSupplierIdentity order={row} canOpenParty={canOpenParty} />
    </span>
  )

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
        <StateBlock state={{ kind: status === null ? 'empty-no-data' : 'empty-filtered' }} subject={LIST_TEXT.subject} />
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
