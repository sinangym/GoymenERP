import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { fetchPurchaseWork } from '../logistics/api'
import { formatBusinessDate } from '../logistics/format'
import { PURCHASE_LINE_KIND_TEXT } from '../logistics/logisticsText'
import type { PurchaseWorkRow } from '../logistics/operationsWorkContract'
import { productDetailPath } from '../product/capabilities'
import { purchaseOrderDetailPath } from '../purchasing/capabilities'
import { createRequestFence } from '../purchasing/requestFence'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './inventory.module.css'

/** Master v3 §25.5 / DEC-STK-002: the source type every row of this section carries. */
export const PURCHASE_SUPPLY_SOURCE_TEXT = 'Alış Siparişi'

type State =
  | { readonly type: 'loading' }
  | { readonly type: 'failed'; readonly message: string }
  | { readonly type: 'ready'; readonly rows: readonly PurchaseWorkRow[]; readonly nextCursor: string | null; readonly more: 'idle' | 'loading' | 'failed' }

const shortRef = (id: string) => `…${id.slice(-8)}`

/**
 * AT-064 — the open purchase-order remainder on ST-01 as FUTURE SUPPLY, in its own section beside (never inside) the
 * physical rows. It reads the Purchasing owner's remainder (committed − accepted of APPROVED lines, never recomputed
 * here) through the existing price-free, company-fenced purchase work read (`IPurchaseOpenLineReadPortV1`, L-09), so it
 * shows the same numbers as that screen. It is drawn only for an actor the server gives that read's capability; the
 * physical/blocked/allocated/free kilograms above are a separate read and this section never adds to them.
 */
export function PurchaseSupplySection({ companyId, canOpenOrder, canOpenProduct }: {
  readonly companyId: string
  readonly canOpenOrder: boolean
  readonly canOpenProduct: boolean
}) {
  const boundary = useAuthBoundary()
  const boundaryRef = useRef(boundary)
  useEffect(() => {
    boundaryRef.current = boundary
  }, [boundary])
  const fence = useRef(createRequestFence())
  const [state, setState] = useState<State>({ type: 'loading' })

  const read = useCallback(async (cursor: string | null) => {
    const token = fence.current.capture()
    const security = boundaryRef.current.capture()
    const outcome = await fetchPurchaseWork(cursor, companyId, token.signal)
    if (!fence.current.isCurrent(token)) return
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      boundaryRef.current.report(outcome.status, security)
    }
    if (outcome.type === 'ok-single') {
      setState((current) => {
        const earlier = cursor !== null && current.type === 'ready' ? current.rows : []
        const seen = new Set(earlier.map((row) => row.lineId))
        return {
          type: 'ready',
          rows: [...earlier, ...outcome.data.items.filter((row) => !seen.has(row.lineId))],
          nextCursor: outcome.data.nextCursor,
          more: 'idle',
        }
      })
      return
    }
    setState((current) => cursor !== null && current.type === 'ready'
      ? { ...current, more: 'failed' }
      : { type: 'failed', message: outcome.type === 'http'
        ? `Alış siparişi kalanları okunamadı (${outcome.status}).`
        : 'Alış siparişi kalanlarına ulaşılamadı veya yanıt doğrulanamadı.' })
  }, [companyId])

  useEffect(() => {
    const currentFence = fence.current
    void read(null)
    return () => currentFence.invalidate()
  }, [read])

  const loadMore = () => {
    if (state.type !== 'ready' || state.nextCursor === null || state.more === 'loading') return
    setState({ ...state, more: 'loading' })
    void read(state.nextCursor)
  }

  return <section aria-labelledby="purchase-supply-heading">
    <h2 id="purchase-supply-heading">Gelecek arz — Alış Siparişi</h2>
    <p>
      Onaylı alış siparişlerinin mal kabulü bekleyen kalanı (satın alma kaydının kendi kalanı). Bu miktar fiziksel,
      serbest veya sevk edilebilir stok değildir; mal kabulden önce yukarıdaki kilogramları artırmaz.
    </p>
    {state.type === 'loading' ? <p role="status">Alış siparişi kalanları yükleniyor…</p> : null}
    {state.type === 'failed' ? <p role="alert" className={styles.error}>{state.message}</p> : null}
    {state.type === 'ready' && state.rows.length === 0 ? <p>Bu şirkette mal kabulü bekleyen onaylı alış siparişi satırı yok.</p> : null}
    {state.type === 'ready' && state.rows.length > 0 ? <div className={styles.tableWrap}><table data-testid="stock-purchase-supply-table">
      <thead><tr>
        <th scope="col">Kaynak / durum</th><th scope="col">Alış siparişi</th><th scope="col">Sipariş tarihi</th>
        <th scope="col">Satır</th><th scope="col">Tür</th><th scope="col">Tedarikçi</th><th scope="col">Ürün</th>
        <th scope="col">Birim</th><th scope="col">Onaylı</th><th scope="col">Kabul edilen</th><th scope="col">Kalan</th>
      </tr></thead>
      <tbody>{state.rows.map((row) => {
        const order = `Satın alma siparişi ${shortRef(row.orderId)}`
        return <tr key={row.lineId} data-purchase-line-id={row.lineId}>
          <td>{PURCHASE_SUPPLY_SOURCE_TEXT}</td>
          <td>{canOpenOrder ? <Link to={purchaseOrderDetailPath(row.orderId)}>{order}</Link> : order}</td>
          <td>{formatBusinessDate(row.orderEffectiveOn)}</td>
          <td>{row.lineNo}</td>
          <td>{PURCHASE_LINE_KIND_TEXT[row.kind] ?? row.kind}</td>
          <td>{row.supplierName}</td>
          <td>{canOpenProduct ? <Link to={productDetailPath(row.productId)}>Ürün kartı</Link> : <span>Ürün kartı</span>}</td>
          <td className={styles.mono}>{shortRef(row.unitId)}</td>
          <td>{row.committed}</td><td>{row.accepted}</td><td>{row.remaining}</td>
        </tr>
      })}</tbody>
    </table></div> : null}
    {state.type === 'ready' && state.nextCursor !== null
      ? <button type="button" onClick={loadMore} disabled={state.more === 'loading'}>
        {state.more === 'loading' ? 'Yükleniyor…' : 'Daha fazla alış satırı'}
      </button>
      : null}
    {state.type === 'ready' && state.more === 'failed'
      ? <p role="alert" className={styles.error}>Sonraki sayfa alınamadı. Gösterilen satırlar geçerli.</p>
      : null}
  </section>
}
