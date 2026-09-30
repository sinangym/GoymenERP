import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { fetchStockPositions } from './api'
import type { InventoryStockPositions } from './contract'
import { createRequestFence } from '../purchasing/requestFence'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellState } from '../shell/shellContext'
import { CAPABILITY } from '../shell/capabilities'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { productDetailPath } from '../product/capabilities'
import { locationDetailPath } from '../organization/capabilities'
import { inventoryLabelDetailPath } from './labelDetailRoute'
import { PurchaseSupplySection } from './PurchaseSupplySection'
import { StockRowActionMenu } from './StockRowActionMenu'
import {
  normalizeStockSearch,
  permittedStockRowActions,
  STOCK_SEARCH_PARAM,
  stockRowActionsFor,
  stockRowActionState,
  type StockRowAction,
} from './stockRowActions'
import styles from './inventory.module.css'

type State =
  | { readonly type: 'loading' }
  | { readonly type: 'ready'; readonly data: InventoryStockPositions }
  | { readonly type: 'failed'; readonly message: string }

function ownership(kind: string, id: string, companyId: string): string {
  if (kind === 'Company' && id === companyId) return 'Şirket malı'
  if (kind === 'Counterparty') return 'Harici taraf malı'
  return 'Sahiplik türü doğrulanamadı'
}

function custody(kind: string, id: string, companyId: string): string {
  if (kind === 'Company' && id === companyId) return 'Şirket gözetiminde'
  if (kind === 'Counterparty') return 'Harici taraf gözetiminde'
  return 'Gözetim türü doğrulanamadı'
}

export function InventoryStockPositionPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <InventoryStockPositionView
    key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`}
    companyId={shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null}
    canOpenProduct={hasCapability(shell, CAPABILITY.productItemsDetail)}
    canOpenLocation={hasCapability(shell, CAPABILITY.organizationLocationsDetail)}
    rowActions={permittedStockRowActions((capability) => hasCapability(shell, capability))}
    canReadPurchaseSupply={hasCapability(shell, CAPABILITY.logisticsOperationsPurchase)}
    canOpenPurchaseOrder={hasCapability(shell, CAPABILITY.purchasingOrdersDetail)}
  />
}

function InventoryStockPositionView({
  companyId,
  canOpenProduct,
  canOpenLocation,
  rowActions,
  canReadPurchaseSupply,
  canOpenPurchaseOrder,
}: {
  readonly companyId: string | null
  readonly canOpenProduct: boolean
  readonly canOpenLocation: boolean
  readonly rowActions: readonly StockRowAction[]
  readonly canReadPurchaseSupply: boolean
  readonly canOpenPurchaseOrder: boolean
}) {
  const boundary = useAuthBoundary()
  // `useAuthBoundary` answers a new object on every render. Keyed on it, the initial-read effect below re-ran after
  // EVERY render: it re-read the unfiltered list in a loop, and its cleanup aborted the label-code search just
  // submitted, so a search never answered (R19 r14 run 36317283526). The read is keyed on the company alone; the
  // boundary of the latest committed render is read from a ref when a request starts and when it answers.
  const boundaryRef = useRef(boundary)
  useEffect(() => {
    boundaryRef.current = boundary
  }, [boundary])
  // AT-063: the label-code filter lives in the URL (`?etiket=`), so a row action's back link — or the browser's back
  // button — returns to the same filtered list. The URL is the one source the read follows.
  const [searchParams, setSearchParams] = useSearchParams()
  const appliedQ = normalizeStockSearch(searchParams.get(STOCK_SEARCH_PARAM))
  const [q, setQ] = useState(appliedQ)
  const [openMenu, setOpenMenu] = useState<string | null>(null)
  const [state, setState] = useState<State>({ type: 'loading' })
  const fence = useRef(createRequestFence())

  const load = useCallback(async (search: string) => {
    if (companyId === null) return
    const token = fence.current.capture()
    const security = boundaryRef.current.capture()
    const outcome = await fetchStockPositions(companyId, search, token.signal)
    if (!fence.current.isCurrent(token)) return
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      boundaryRef.current.report(outcome.status, security)
    }
    setState(outcome.type === 'ok-single'
      ? { type: 'ready', data: outcome.data }
      : { type: 'failed', message: outcome.type === 'http'
        ? `Stok pozisyonları okunamadı (${outcome.status}).`
        : 'Stok pozisyonu kaynağına ulaşılamadı veya yanıt doğrulanamadı.' })
  }, [companyId])

  useEffect(() => {
    const currentFence = fence.current
    if (companyId !== null) void load(appliedQ)
    return () => currentFence.invalidate()
  }, [companyId, load, appliedQ])

  if (companyId === null) {
    return <main className={styles.page}><p role="alert">Yetkili tek şirket bağlamı seçilmeden stok pozisyonları görüntülenemez.</p></main>
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    const next = normalizeStockSearch(q)
    setState({ type: 'loading' })
    setOpenMenu(null)
    if (next === appliedQ) {
      // The same filter again: the URL does not change, so read it once more here.
      void load(next)
      return
    }
    // A new filter: the URL changes and the read effect follows it (exactly one read).
    setSearchParams(next === '' ? {} : { [STOCK_SEARCH_PARAM]: next }, { replace: true })
  }

  return <main className={styles.page}>
    <header className={styles.header}><div><p className={styles.eyebrow}>ST-01</p><h1>Stok / Malzeme Pozisyonu</h1></div></header>
    <p>Fiziksel, bloklu, aktif tahsisli ve serbest kalan kilogramları aynı kaynak etiketinden izleyin.</p>
    <form className={styles.searchRow} onSubmit={submit} role="search">
      <label htmlFor="stock-position-search">Etiket kodu</label>
      <input id="stock-position-search" value={q} onChange={(event) => setQ(event.target.value)} />
      <button type="submit">Ara</button>
    </form>
    {state.type === 'loading' ? <p role="status">Stok pozisyonları yükleniyor…</p> : null}
    {state.type === 'failed' ? <p role="alert" className={styles.error}>{state.message}</p> : null}
    {state.type === 'ready' && state.data.items.length === 0 ? <p>Bu şirkette eşleşen fiziksel stok yok.</p> : null}
    {state.type === 'ready' && state.data.items.length > 0 ? <>
      {state.data.searchWindowFull ? <p role="status">Arama penceresi doldu; etiket kodunu daraltın.</p> : null}
      <div className={styles.tableWrap}><table aria-busy="false"><thead><tr>
        <th>Etiket / lot</th><th>Kaynak / durum</th><th>Ürün / lokasyon</th><th>Sahiplik / gözetim</th><th>Fiziksel kg</th><th>Bloklu kg</th><th>Aktif tahsis kg</th><th>Serbest kg</th><th>İşlemler</th>
      </tr></thead><tbody>{state.data.items.map((item) => <tr key={item.labelId} data-label-id={item.labelId}>
        <td><strong><Link to={inventoryLabelDetailPath(item.labelId)} state={stockRowActionState(appliedQ)}>{item.labelCode}</Link></strong><br />{item.lot.heatNumber ?? item.heatNumber ?? 'Döküm bilinmiyor'} · {item.lot.coilNumber ?? item.coilNumber ?? 'Rulo bilinmiyor'}<br />{item.documentStatus}</td>
        <td>Fiziksel Stok</td>
        <td>
          {canOpenProduct ? <Link to={productDetailPath(item.productId)}>Ürün kartı</Link> : <span>Ürün kartı</span>}
          <br />
          {canOpenLocation ? <Link to={locationDetailPath(item.locationId)}>Lokasyon kartı</Link> : <span>Lokasyon kartı</span>}
        </td>
        <td>{ownership(item.ownerKind, item.ownerId, companyId)}<br />{custody(item.custodianKind, item.custodianId, companyId)}</td>
        <td>{item.physicalKg}</td><td>{item.blockedPhysicalKg}</td><td>{item.activeAllocationKg}</td><td>{item.availableKg}</td>
        <td><StockRowActionMenu
          row={item}
          actions={stockRowActionsFor(rowActions, item)}
          search={appliedQ}
          open={openMenu === item.labelId}
          onToggle={(open) => setOpenMenu(open ? item.labelId : null)}
        /></td>
      </tr>)}</tbody></table></div>
    </> : null}
    {canReadPurchaseSupply
      ? <PurchaseSupplySection companyId={companyId} canOpenOrder={canOpenPurchaseOrder} canOpenProduct={canOpenProduct} />
      : null}
  </main>
}
