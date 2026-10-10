import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { approvePurchaseOrder, type ApprovalOutcome } from './approvalApi'
import { fetchPurchaseOrder } from './api'
import { purchaseRequestDetailPath, PURCHASING_CAPABILITY } from './capabilities'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { pendingApprovalKey, recallPendingApproval, releasePendingApproval, reservePendingApproval, type ApprovalSnapshot } from './pendingApprovalStore'
import styles from './purchasing.module.css'
import { GoodsReceiptPanel } from './GoodsReceiptPanel'
import { PURCHASE_GOODS_RECEIPT_CAPABILITY } from './goodsReceiptContract'
import { PURCHASE_ORDER_DETAIL_TEXT as T } from './purchaseOrderDetailText'
import { PurchaseOrderSupplierIdentity } from './PurchaseOrderListPage'
import { CAPABILITY } from '../shell/capabilities'
import { DocumentsPanel } from '../documents/DocumentsPanel'
import { OWNER_KIND } from '../documents/contract'
import { Button } from '../ui/Button'
import { StatusIndicator } from '../ui/StatusIndicator'

function newId(): string {
  if (typeof globalThis.crypto?.randomUUID === 'function') return globalThis.crypto.randomUUID()
  const bytes = Array.from({ length: 16 }, () => Math.floor(Math.random() * 256))
  bytes[6] = (bytes[6] & 0x0f) | 0x40; bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = bytes.map((value) => value.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

export function PurchaseOrderDetailPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { orderId } = useParams()
  return <PurchaseOrderDetail key={`${namespace ?? 'no-context'}::${orderId ?? ''}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function PurchaseOrderDetail({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const { orderId } = useParams()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const canApprove = hasCapability(shell, PURCHASING_CAPABILITY.orderApprove as never)
  const canReadSourceRequest = hasCapability(shell, PURCHASING_CAPABILITY.requestDetail as never)
  const canOpenParty = hasCapability(shell, CAPABILITY.cariPartiesDetail)
  const storeKey = pendingApprovalKey(shell.sessionEpoch, actorId, companyId, orderId ?? '')
  const [attempt, setAttempt] = useState<
    | { readonly phase: 'idle' }
    | { readonly phase: 'submitting'; readonly snapshot: ApprovalSnapshot }
    | { readonly phase: 'unknown'; readonly snapshot: ApprovalSnapshot; readonly outcome: Extract<ApprovalOutcome, { type: 'unknown' }> }
    | { readonly phase: 'applied'; readonly replayed: boolean }
    | { readonly phase: 'failed'; readonly status: number; readonly code: string | null }
  >(() => {
    const snapshot = recallPendingApproval(storeKey)
    return snapshot === null ? { phase: 'idle' } : { phase: 'unknown', snapshot, outcome: { type: 'unknown', reason: 'network-error' } }
  })
  const inFlight = useRef(false)
  const responseGeneration = useRef(0)
  useEffect(() => {
    inFlight.current = false
    return () => { responseGeneration.current += 1 }
  }, [namespace, shell.routeEntryGeneration])
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'purchase-order', orderId ?? ''],
    enabled: namespace !== null && companyId !== null && (orderId ?? '').length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await fetchPurchaseOrder(companyId ?? '', orderId ?? '', signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
      return result
    },
  })
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<import('./contract').PurchaseOrderDetail>
  const sendApproval = useCallback(async (retry?: ApprovalSnapshot) => {
    if (state.kind !== 'ready' || companyId === null || actorId === null || orderId === undefined || inFlight.current) return
    const approvalSnapshot: ApprovalSnapshot = retry ?? {
      actorId, companyId, orderId, expectedVersion: state.data.order.version, idempotencyKey: newId(),
    }
    if (approvalSnapshot.actorId !== actorId || approvalSnapshot.companyId !== companyId || approvalSnapshot.orderId !== orderId) return
    inFlight.current = true
    const generation = ++responseGeneration.current
    reservePendingApproval(storeKey, approvalSnapshot)
    setAttempt({ phase: 'submitting', snapshot: approvalSnapshot })
    const securitySnapshot = boundary.capture()
    const result = await approvePurchaseOrder(
      companyId,
      orderId,
      approvalSnapshot.expectedVersion,
      approvalSnapshot.idempotencyKey,
    )
    if (responseGeneration.current !== generation) return
    inFlight.current = false
    if (result.type === 'failed' && (result.status === 401 || result.status === 403)) boundary.report(result.status, securitySnapshot)
    if (result.type === 'unknown') {
      setAttempt({ phase: 'unknown', snapshot: approvalSnapshot, outcome: result })
      return
    }
    releasePendingApproval(storeKey, approvalSnapshot.idempotencyKey)
    setAttempt(result.type === 'applied'
      ? { phase: 'applied', replayed: result.replayed }
      : { phase: 'failed', status: result.status, code: result.code })
    if (result.type === 'applied' || result.status === 409) await query.refetch()
  }, [actorId, boundary, companyId, orderId, query, state, storeKey])

  const frame = (child: ReactNode, status?: ReactNode) => <main className={styles.order360Page}>
    <header className={styles.order360Header}>
      <div className={styles.order360HeaderCopy}>
        <p className={styles.order360Eyebrow}>{T.eyebrow}</p>
        <h1 className={styles.order360Title}>{T.heading}</h1>
      </div>
      {status}
    </header>
    {child}
  </main>
  if (companyId === null) return frame(<StateBlock state={{ kind: 'no-authorized-company' }} subject="satın alma siparişi" />)
  if (state.kind !== 'ready') return frame(<StateBlock state={state} subject="satın alma siparişi" onRetry={() => void query.refetch()} />)
  const { order, lines, acceptances } = state.data
  const isCurrentDetail = order.id === orderId && order.companyId === companyId
  const lineNoById = new Map(lines.map((line) => [line.lineId, line.lineNo] as const))
  const unresolved = attempt.phase === 'unknown' || attempt.phase === 'submitting'
  const outcomeNotice = attempt.phase === 'applied'
    ? <p role="status" className={styles.success}>{attempt.replayed ? 'Önceki onay yanıtı yeniden gösterildi.' : 'Sipariş onaylandı ve yeniden okundu.'}</p>
    : attempt.phase === 'failed'
      ? <p role="alert" className={styles.error}>Onay kaydedilmedi ({attempt.code ?? attempt.status}). Kayıt yeniden okundu.</p>
      : null
  return frame(<>
    <section className={styles.order360Section} aria-labelledby="purchase-order-summary-heading" data-testid="purchase-order-summary">
      <h2 id="purchase-order-summary-heading">{T.summaryHeading}</h2>
      <dl className={styles.order360Facts} data-testid="purchase-order-status-facts">
        <div><dt>{T.orderId}</dt><dd className={styles.order360Identifier}>{order.id}</dd></div>
        <div><dt>{T.version}</dt><dd>{order.version}</dd></div>
        <div><dt>{T.businessDate}</dt><dd>{order.effectiveOn}</dd></div>
        <div><dt>{T.sourceRequest}</dt><dd className={styles.order360Identifier}>{order.sourceRequestId === null ? T.sourceNotLinked : canReadSourceRequest && namespace !== null && order.id === orderId && order.companyId === companyId ? <Link className={styles.order360Link} to={purchaseRequestDetailPath(order.sourceRequestId)} state={{ ns: namespace, requestId: order.sourceRequestId, orderId: order.id }}>{order.sourceRequestId}</Link> : order.sourceRequestId}</dd></div>
      </dl>
      <h3>{T.supplierHeading}</h3>
      <dl className={styles.order360Facts} data-testid="purchase-order-supplier-facts">
        <div data-testid="purchase-order-supplier-identity"><dt>{T.supplierIdentity}</dt><dd><PurchaseOrderSupplierIdentity order={order} canOpenParty={canOpenParty && isCurrentDetail} /></dd></div>
        <div><dt>{T.supplierPartyId}</dt><dd className={styles.order360Identifier}>{order.supplierPartyId}</dd></div>
        <div><dt>{T.supplierAccountId}</dt><dd className={styles.order360Identifier}>{order.companyAccountId}</dd></div>
      </dl>
    </section>
    <section className={styles.order360Section} aria-labelledby="purchase-order-lines-heading" data-testid="purchase-order-lines">
      <h2 id="purchase-order-lines-heading">{T.linesHeading}</h2>
      <div className={styles.order360TableWrap}><table className={styles.order360Table} aria-label={T.linesCaption}><thead><tr><th>No</th><th>{T.productId}</th><th>{T.unitId}</th><th className={styles.order360Number}>{T.ordered}</th><th className={styles.order360Number}>{T.technical}</th><th className={styles.order360Number}>{T.committed}</th><th className={styles.order360Number}>{T.accepted}</th><th className={styles.order360Number}>{T.remaining}</th></tr></thead><tbody>{lines.map((line) => <tr key={line.lineId} data-testid={`purchase-order-line-${line.lineNo}`}><td>{line.lineNo}</td><td className={styles.order360Identifier}>{line.productId}</td><td className={styles.order360Identifier}>{line.unitId}</td><td className={styles.order360Number}>{line.ordered}</td><td className={styles.order360Number}>{line.technical}</td><td className={styles.order360Number}>{line.committed}</td><td className={styles.order360Number}>{line.accepted}</td><td className={styles.order360Number}>{line.remaining}</td></tr>)}</tbody></table></div>
    </section>
    {isCurrentDetail ? <section className={styles.order360Section} aria-labelledby="purchase-order-acceptance-history-heading" data-testid="purchase-order-acceptance-history">
      <h2 id="purchase-order-acceptance-history-heading">{T.acceptanceHistoryHeading}</h2>
      {acceptances.length === 0
        ? <p>{T.acceptanceHistoryEmpty}</p>
        : <div className={styles.order360TableWrap}><table className={styles.order360Table}><thead><tr><th>{T.acceptanceId}</th><th>{T.lineNo}</th><th className={styles.order360Number}>{T.quantity}</th><th>{T.recordedAtUtc}</th></tr></thead><tbody>{acceptances.map((acceptance) => <tr key={acceptance.acceptanceId} data-testid={`purchase-order-acceptance-${acceptance.acceptanceId}`}><td className={styles.order360Identifier}>{acceptance.acceptanceId}</td><td>{lineNoById.get(acceptance.lineId) ?? T.lineUnavailable}</td><td className={styles.order360Number}>{acceptance.quantity}</td><td><time dateTime={acceptance.recordedAtUtc}>{acceptance.recordedAtUtc}</time></td></tr>)}</tbody></table></div>}
    </section> : null}
    {isCurrentDetail && namespace !== null ? <DocumentsPanel
      ownerKind={OWNER_KIND.purchasingPurchaseOrder}
      ownerId={order.id}
      companyId={companyId}
      namespace={namespace}
      entryGeneration={shell.routeEntryGeneration}
    /> : null}
    {canApprove ? outcomeNotice : null}
    {canApprove && (order.status === 'Draft' || unresolved) ? <section className={styles.order360Section}><h2>{T.approvalHeading}</h2><p>{T.approvalNote}</p>{attempt.phase === 'unknown' ? <p role="alert" className={styles.warning}>Sonuç bilinmiyor. Yeni işlem başlatmadan aynı anahtar ve sürümle yeniden gönderin.</p> : null}<Button type="button" variant="primary" disabled={attempt.phase === 'submitting'} onClick={() => void sendApproval(attempt.phase === 'unknown' ? attempt.snapshot : undefined)}>{attempt.phase === 'submitting' ? 'Gönderiliyor…' : attempt.phase === 'unknown' ? 'Aynı onayı yeniden gönder' : 'Onayla'}</Button></section> : null}
    {namespace !== null && hasCapability(shell, PURCHASE_GOODS_RECEIPT_CAPABILITY as never)
      ? <div className={styles.order360Section} data-testid="purchase-order-receipt-action"><GoodsReceiptPanel enabled
          binding={namespace} detail={state.data} onRecorded={() => void query.refetch()} /></div>
      : null}
  </>, <StatusIndicator className={styles.order360Status} tone={order.status === 'Approved' ? 'success' : 'warning'}>{order.status === 'Approved' ? T.approved : T.draft}</StatusIndicator>)
}
