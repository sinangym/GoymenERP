import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { productDetailPath } from '../product/capabilities'
import { CAPABILITY } from '../shell/capabilities'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { INVENTORY_LABEL_TRANSFORMATION_CAPABILITY } from './capabilities'
import { fetchInventoryLabelDetail } from './labelDetailApi'
import { LabelTransformationPanel } from './LabelTransformationPanel'
import type { InventoryLabelDetailWithMovementTimeline } from './labelDetailContract'
import { inventoryLabelDetailPath } from './labelDetailRoute'
import styles from './inventory.module.css'

const MOVEMENT_KIND_TEXT = {
  Receipt: 'Giriş',
  Issue: 'Çıkış',
  Block: 'Blokaj',
  Release: 'Serbest bırakma',
  Return: 'İade',
} as const

const DIRECTION_TEXT = { In: 'Giriş', Out: 'Çıkış' } as const

export function InventoryLabelDetailPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { labelId } = useParams()
  return <InventoryLabelDetailView
    key={`${namespace ?? 'no-context'}::${labelId ?? ''}::${shell.routeEntryGeneration}`}
    namespace={namespace}
    labelId={labelId}
  />
}

function InventoryLabelDetailView({ namespace, labelId }: {
  readonly namespace: string | null
  readonly labelId: string | undefined
}) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const canReadProduct = hasCapability(shell, CAPABILITY.productItemsDetail)
  const canReadLabel = hasCapability(shell, CAPABILITY.inventoryStockPositionsList)
  const canTransform = hasCapability(shell, INVENTORY_LABEL_TRANSFORMATION_CAPABILITY)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const query = useQuery({
    queryKey: ['inventory-label-detail', namespace, labelId, shell.routeEntryGeneration],
    enabled: namespace !== null && companyId !== null && labelId !== undefined,
    retry: false,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await fetchInventoryLabelDetail(companyId!, labelId!, signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
      return result
    },
  })
  const state = (query.isPending
    ? { kind: 'initial-loading' }
    : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<InventoryLabelDetailWithMovementTimeline>

  if (companyId === null || namespace === null) {
    return <main className={styles.page}><StateBlock state={{ kind: 'no-authorized-company' }} subject="etiket" /></main>
  }
  if (state.kind !== 'ready') {
    return <main className={styles.page}><StateBlock state={state} subject="etiket" onRetry={() => void query.refetch()} /></main>
  }

  const { labelVersion, identity, balances, genealogy, movementTimeline } = state.data
  if (identity.companyId !== companyId || identity.labelId !== labelId || balances.labelId !== labelId ||
    genealogy.labelId !== labelId || movementTimeline.labelId !== labelId) {
    return <main className={styles.page}><StateBlock state={{ kind: 'malformed-response' }} subject="etiket" onRetry={() => void query.refetch()} /></main>
  }
  return <main className={styles.page}>
    <header className={styles.header}>
      <div><p className={styles.eyebrow}>ST-02</p><h1>{identity.code}</h1></div>
      <span>{identity.documentStatus}</span>
    </header>

    <section aria-labelledby="label-identity-heading">
      <h2 id="label-identity-heading">Etiket ve lot kimliği</h2>
      <dl className={styles.facts}>
        <div><dt>Etiket kimliği</dt><dd className={styles.mono}>{identity.labelId}</dd></div>
        <div><dt>Etiket sürümü</dt><dd>{labelVersion}</dd></div>
        <div><dt>Ürün</dt><dd className={styles.mono}>{identity.productId}<br />{canReadProduct
          ? <Link to={productDetailPath(identity.productId)}>Ürün kartı</Link>
          : <span>Ürün kartı</span>}</dd></div>
        <div><dt>Lot</dt><dd className={styles.mono}>{identity.lotId}</dd></div>
        <div><dt>Döküm</dt><dd>{identity.heatNumber ?? identity.lot.heatNumber ?? '—'}</dd></div>
        <div><dt>Rulo</dt><dd>{identity.coilNumber ?? identity.lot.coilNumber ?? '—'}</dd></div>
        <div><dt>Fiziksel miktar</dt><dd>{balances.physicalKg} kg</dd></div>
        <div><dt>Serbest</dt><dd>{balances.freeKg} kg</dd></div>
        <div><dt>Bloklu</dt><dd>{balances.blockedKg} kg</dd></div>
      </dl>
    </section>

    <section aria-labelledby="label-position-heading">
      <h2 id="label-position-heading">Sahiplik ve konum</h2>
      {balances.positions.length === 0
        ? <p>Bu etiket için fiziksel stok konumu yok.</p>
        : <div className={styles.tableWrap}><table>
          <thead><tr><th>Sahip</th><th>Gözeten</th><th>Lokasyon</th><th>Durum</th><th>Miktar</th></tr></thead>
          <tbody>{balances.positions.map((position, index) => <tr key={`${position.locationId}:${position.ownerId}:${position.custodianId}:${position.block}:${index}`}>
            <td>{position.ownerKind}<br /><span className={styles.mono}>{position.ownerId}</span></td>
            <td>{position.custodianKind}<br /><span className={styles.mono}>{position.custodianId}</span></td>
            <td className={styles.mono}>{position.locationId}</td>
            <td>{position.block}</td>
            <td>{position.quantityKg} kg</td>
          </tr>)}</tbody>
        </table></div>}
    </section>

    <section aria-labelledby="label-movement-heading">
      <h2 id="label-movement-heading">Stok hareketleri</h2>
      {movementTimeline.entries.length === 0
        ? <p>Bu etiket için stok hareketi yok.</p>
        : <div className={styles.tableWrap}><table>
          <thead><tr><th>Zaman</th><th>Hareket</th><th>Kaynak</th><th>Yön</th><th>Konum</th><th>Durum</th><th>Miktar</th></tr></thead>
          <tbody>{movementTimeline.entries.flatMap((movement) => movement.lines.map((line) => <tr key={`${movement.movementId}:${line.sequence}`}>
            <td>{movement.occurredAtUtc}</td>
            <td>{MOVEMENT_KIND_TEXT[movement.kind]}<br /><span className={styles.mono}>…{movement.movementId.slice(-8)}</span></td>
            <td>{movement.sourceKind}<br /><span className={styles.mono}>…{movement.sourceId.slice(-8)}</span></td>
            <td>{DIRECTION_TEXT[line.direction]}</td>
            <td className={styles.mono}>{line.locationId}</td>
            <td>{line.block}</td>
            <td>{line.quantityKg} kg</td>
          </tr>))}</tbody>
        </table></div>}
      {movementTimeline.windowFull ? <p>Daha eski stok hareketleri bu görünümde yer almıyor.</p> : null}
    </section>

    <section aria-labelledby="label-genealogy-heading">
      <h2 id="label-genealogy-heading">Soy ağacı</h2>
      {genealogy.edges.length === 0
        ? <p>Bu etiket için bölme veya birleştirme kaydı yok.</p>
        : <div className={styles.tableWrap}><table>
          <thead><tr><th>İşlem</th><th>Tür</th><th>Zaman</th><th>Kaynak</th><th>Çıktı</th></tr></thead>
          <tbody>{genealogy.edges.map((edge) => <tr key={`${edge.transformationId}:${edge.sourceLabelId}:${edge.outputLabelId}`}>
            <td className={styles.mono}>…{edge.transformationId.slice(-8)}</td>
            <td>{edge.kind === 'Split' ? 'Bölme' : 'Birleştirme'}</td>
            <td>{edge.occurredAtUtc}</td>
            <td>{canReadLabel
              ? <Link to={inventoryLabelDetailPath(edge.sourceLabelId)}>{edge.sourceCode}</Link>
              : edge.sourceCode}<br />{edge.sourceQuantityKg} kg · sürüm {edge.sourceLabelVersion}</td>
            <td>{canReadLabel
              ? <Link to={inventoryLabelDetailPath(edge.outputLabelId)}>{edge.outputCode}</Link>
              : edge.outputCode}<br />{edge.outputQuantityKg} kg · sürüm {edge.outputLabelVersion}</td>
          </tr>)}</tbody>
        </table></div>}
      {genealogy.windowFull ? <p>Daha eski soy ağacı kayıtları bu görünümde yer almıyor.</p> : null}
    </section>

    {canTransform && actorId !== null
      ? <LabelTransformationPanel
        key={identity.labelId}
        label={{
          labelId: identity.labelId, labelVersion, code: identity.code, productId: identity.productId,
          lotId: identity.lotId, heatNumber: identity.heatNumber, coilNumber: identity.coilNumber,
          physicalKg: balances.physicalKg, positions: balances.positions,
        }}
        companyId={companyId}
        actorId={actorId}
        sessionEpoch={shell.sessionEpoch}
        canOpenLabels={canReadLabel}
        onRecorded={() => void query.refetch()}
      />
      : null}

    <section aria-labelledby="label-document-heading">
      <h2 id="label-document-heading">Belgeler ve basımlar</h2>
      <p>Gerekli belgeler: {identity.requiredDocuments.length === 0 ? 'Yok' : identity.requiredDocuments.join(', ')}</p>
      <p>Ekli belge: {identity.documents.length}</p>
      <p>Basım: {identity.prints.length}</p>
    </section>
  </main>
}
