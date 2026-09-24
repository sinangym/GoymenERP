import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { fetchOpeningCount } from './api'
import { INVENTORY_OPENING_ROUTE } from './capabilities'
import { inventoryLabelDetailPath } from './labelDetailRoute'
import { CAPABILITY } from '../shell/capabilities'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import styles from './inventory.module.css'

export function InventoryOpeningDetailPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { countId } = useParams()
  return <InventoryOpeningDetail key={`${namespace ?? 'no-context'}::${countId ?? ''}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function InventoryOpeningDetail({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const { countId } = useParams()
  const boundary = useAuthBoundary()
  const canReadLabel = hasCapability(shell, CAPABILITY.inventoryStockPositionsList)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const query = useQuery({
    queryKey: ['inventory-opening-count', namespace, countId, shell.routeEntryGeneration],
    enabled: namespace !== null && companyId !== null && countId !== undefined,
    retry: false,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await fetchOpeningCount(companyId!, countId!, signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
      return result
    },
  })
  const state = (query.isPending
    ? { kind: 'initial-loading' }
    : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<import('./contract').OpeningCountView>
  if (companyId === null || namespace === null) return <main className={styles.page}><StateBlock state={{ kind: 'no-authorized-company' }} subject="açılış sayımı" /></main>
  if (state.kind !== 'ready') return <main className={styles.page}><StateBlock state={state} subject="açılış sayımı" onRetry={() => void query.refetch()} /></main>
  const count = state.data
  // A link is derived only from this entry's decoded server record, not a route hint.
  if (count.companyId !== companyId || count.countId !== countId) {
    return <main className={styles.page}><StateBlock state={{ kind: 'malformed-response' }} subject="açılış sayımı" onRetry={() => void query.refetch()} /></main>
  }
  return <main className={styles.page}>
    <header className={styles.header}><div><p className={styles.eyebrow}>ST-05-OPENING</p><h1>Açılış sayımı</h1></div><Link to={INVENTORY_OPENING_ROUTE.home}>← İlk stok</Link></header>
    <dl className={styles.facts}>
      <div><dt>Sayım</dt><dd className={styles.mono}>{count.countId}</dd></div>
      <div><dt>Lokasyon</dt><dd className={styles.mono}>{count.locationId}</dd></div>
      <div><dt>Tarih</dt><dd>{count.countedOn}</dd></div>
      <div><dt>Referans</dt><dd>{count.sheetReference}</dd></div>
    </dl>
    <section><h2>Fiziksel parçalar</h2><div className={styles.tableWrap}><table><thead><tr><th>Etiket</th><th>Ürün</th><th>Lot</th><th>Döküm / Rulo</th><th>Kg</th></tr></thead><tbody>
      {count.lines.map((line) => <tr key={line.lineId}><td>{canReadLabel ? <Link to={inventoryLabelDetailPath(line.labelId)}>{line.labelCode}</Link> : line.labelCode}<br /><span className={styles.mono}>…{line.labelId.slice(-8)}</span></td><td className={styles.mono}>…{line.productId.slice(-8)}</td><td className={styles.mono}>…{line.lotId.slice(-8)}</td><td>{line.heatNumber ?? '—'} / {line.coilNumber ?? '—'}</td><td>{line.quantityKg}</td></tr>)}
    </tbody></table></div></section>
  </main>
}
