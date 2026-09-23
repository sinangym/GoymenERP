import { Link } from 'react-router'
import { INVENTORY_OPENING_ROUTE, inventoryOpeningDetailPath } from './capabilities'
import { lastOpeningCountKey, recallLastOpeningCount } from './pendingOpeningStore'
import { useShellState } from '../shell/shellContext'
import styles from './inventory.module.css'

export function InventoryOpeningHomePage() {
  const shell = useShellState()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  if (companyId === null || actorId === null) {
    return <main className={styles.page}><p role="alert">Yetkili tek şirket bağlamı seçilmeden açılış stoku kullanılamaz.</p></main>
  }
  const last = recallLastOpeningCount(lastOpeningCountKey(shell.sessionEpoch, actorId, companyId))
  return <main className={styles.page}>
    <header className={styles.header}><div><p className={styles.eyebrow}>ST-05-OPENING</p><h1>İlk stok / açılış sayımı</h1></div></header>
    <p>Bir lokasyondaki fiziksel parçaları gerçek ürün, stok birimi ve etiket kimliğiyle tek atomik sayımda kaydedin.</p>
    <div className={styles.actions}>
      <Link className={styles.primaryLink} to={INVENTORY_OPENING_ROUTE.create}>Yeni açılış sayımı</Link>
      {last === null ? null : <Link to={inventoryOpeningDetailPath(last.countId)}>Son kaydı yeniden aç</Link>}
    </div>
  </main>
}
