import { useCallback, useEffect, useRef, useState } from 'react'
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
import { createRequestFence } from './requestFence'
import styles from './purchasing.module.css'

const STATUS: Record<PurchaseOrderStatus, string> = { Draft: 'Taslak', Approved: 'Onaylandı' }

/** The table's cells do not wrap; a supplier's legal name must, so it is never cut or pushed off the page. */
const SUPPLIER_CELL = { whiteSpace: 'normal', overflowWrap: 'anywhere', minWidth: '14rem' } as const
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

  const header = <header className={styles.header}><div><p className={styles.eyebrow}>P-03</p><h1>Satın alma siparişleri</h1></div><div className={styles.headerActions}>{canCreate ? <Link className={styles.primaryLink} to={PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderCreate]}>Yeni</Link> : null}<label>Durum <select value={status ?? ''} onChange={(event) => { pageFence.current.invalidate(); loading.current = false; setStatus(event.target.value === '' ? null : event.target.value as PurchaseOrderStatus); setMore([]); setCursor(undefined); setContinuation('idle') }}><option value="">Tümü</option><option value="Draft">Taslak</option><option value="Approved">Onaylandı</option></select></label></div></header>
  if (companyId === null) return <main className={styles.page}>{header}<StateBlock state={{ kind: 'no-authorized-company' }} subject="satın alma siparişleri" /></main>
  if (page === null) {
    const state = first.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(first.data)
    return <main className={styles.page}>{header}<StateBlock state={state as RemoteViewState<unknown>} subject="satın alma siparişleri" onRetry={() => void first.refetch()} /></main>
  }
  const rows = [...page.items, ...more]
  return <main className={styles.page} data-testid="purchase-order-list">{header}<p className={styles.note}>Bu görünüm fiyat veya stok göstermez; yalnız satın alma taahhüdünün sunucuda kayıtlı durumunu gösterir.</p>{rows.length === 0 ? <StateBlock state={{ kind: status === null ? 'empty-no-data' : 'empty-filtered' }} subject="satın alma siparişleri" /> : <div className={styles.tableWrap}><table><caption>Şirketin satın alma siparişleri</caption><thead><tr><th>Sipariş</th><th>Durum</th><th>{T.supplierColumn}</th><th>İş tarihi</th><th>Satır</th><th>Güncellendi</th></tr></thead><tbody>{rows.map((row) => <tr key={row.id}><th className={styles.mono}>{canOpen ? <Link to={purchaseOrderDetailPath(row.id)}>…{row.id.slice(-8)}</Link> : `…${row.id.slice(-8)}`}</th><td><span className={row.status === 'Approved' ? styles.approved : styles.draft}>{STATUS[row.status]}</span></td><td style={SUPPLIER_CELL} data-testid={`purchase-order-supplier-${row.id}`}><PurchaseOrderSupplierIdentity order={row} canOpenParty={canOpenParty} /></td><td>{row.effectiveOn}</td><td>{row.lineCount}</td><td>{new Date(row.updatedAtUtc).toLocaleString('tr-TR')}</td></tr>)}</tbody></table></div>}{nextCursor !== null ? <div>{continuation === 'loading' ? <p role="status">Sonraki sayfa yükleniyor…</p> : continuation === 'idle' ? <button type="button" className={styles.secondary} onClick={() => void loadMore()}>Daha fazla göster</button> : <><StateBlock state={continuation} subject="sonraki sipariş sayfası" /><button type="button" className={styles.secondary} onClick={() => void loadMore()}>Aynı sayfayı yeniden dene</button></>}</div> : null}</main>
}
