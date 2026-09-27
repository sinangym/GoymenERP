import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
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
  />
}

function InventoryStockPositionView({
  companyId,
  canOpenProduct,
  canOpenLocation,
}: {
  readonly companyId: string | null
  readonly canOpenProduct: boolean
  readonly canOpenLocation: boolean
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
  const [q, setQ] = useState('')
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
    if (companyId !== null) void load('')
    return () => currentFence.invalidate()
  }, [companyId, load])

  if (companyId === null) {
    return <main className={styles.page}><p role="alert">Yetkili tek şirket bağlamı seçilmeden stok pozisyonları görüntülenemez.</p></main>
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    setState({ type: 'loading' })
    void load(q)
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
        <th>Etiket / lot</th><th>Ürün / lokasyon</th><th>Sahiplik / gözetim</th><th>Fiziksel kg</th><th>Bloklu kg</th><th>Aktif tahsis kg</th><th>Serbest kg</th>
      </tr></thead><tbody>{state.data.items.map((item) => <tr key={item.labelId} data-label-id={item.labelId}>
        <td><strong><Link to={inventoryLabelDetailPath(item.labelId)}>{item.labelCode}</Link></strong><br />{item.lot.heatNumber ?? item.heatNumber ?? 'Döküm bilinmiyor'} · {item.lot.coilNumber ?? item.coilNumber ?? 'Rulo bilinmiyor'}<br />{item.documentStatus}</td>
        <td>
          {canOpenProduct ? <Link to={productDetailPath(item.productId)}>Ürün kartı</Link> : <span>Ürün kartı</span>}
          <br />
          {canOpenLocation ? <Link to={locationDetailPath(item.locationId)}>Lokasyon kartı</Link> : <span>Lokasyon kartı</span>}
        </td>
        <td>{ownership(item.ownerKind, item.ownerId, companyId)}<br />{custody(item.custodianKind, item.custodianId, companyId)}</td>
        <td>{item.physicalKg}</td><td>{item.blockedPhysicalKg}</td><td>{item.activeAllocationKg}</td><td>{item.availableKg}</td>
      </tr>)}</tbody></table></div>
    </> : null}
  </main>
}
