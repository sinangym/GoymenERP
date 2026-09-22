import { useCallback, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useParams } from 'react-router'
import { approvePurchaseOrder, type ApprovalOutcome } from './approvalApi'
import { fetchPurchaseOrder } from './api'
import { PURCHASING_CAPABILITY } from './capabilities'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './purchasing.module.css'

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
  return <PurchaseOrderDetail key={`${namespace ?? 'no-context'}::${orderId ?? ''}`} namespace={namespace} />
}

function PurchaseOrderDetail({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const { orderId } = useParams()
  const companyId = shell.activeContext?.companyId ?? null
  const canApprove = hasCapability(shell, PURCHASING_CAPABILITY.orderApprove as never)
  const [outcome, setOutcome] = useState<ApprovalOutcome | null>(null)
  const [pendingKey, setPendingKey] = useState<string | null>(null)
  const [sending, setSending] = useState(false)
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
  const sendApproval = useCallback(async () => {
    if (state.kind !== 'ready' || companyId === null || orderId === undefined || sending) return
    const key = pendingKey ?? newId()
    setPendingKey(key); setSending(true)
    const snapshot = boundary.capture()
    const result = await approvePurchaseOrder(companyId, orderId, state.data.order.version, key)
    setSending(false); setOutcome(result)
    if (result.type === 'failed' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
    if (result.type !== 'unknown') setPendingKey(null)
    if (result.type === 'applied' || (result.type === 'failed' && result.status === 409)) await query.refetch()
  }, [boundary, companyId, orderId, pendingKey, query, sending, state])

  const frame = (child: React.ReactNode) => <main className={styles.page}><header><p className={styles.eyebrow}>P-03-DETAIL</p><h1>Satın alma siparişi</h1></header>{child}</main>
  if (companyId === null) return frame(<StateBlock state={{ kind: 'no-authorized-company' }} subject="satın alma siparişi" />)
  if (state.kind !== 'ready') return frame(<StateBlock state={state} subject="satın alma siparişi" onRetry={() => void query.refetch()} />)
  const { order, lines } = state.data
  return frame(<><dl className={styles.facts}><div><dt>Sipariş</dt><dd className={styles.mono}>{order.id}</dd></div><div><dt>Durum</dt><dd><span className={order.status === 'Approved' ? styles.approved : styles.draft}>{order.status === 'Approved' ? 'Onaylandı' : 'Taslak'}</span></dd></div><div><dt>Sürüm</dt><dd>{order.version}</dd></div><div><dt>Tedarikçi hesabı</dt><dd className={styles.mono}>{order.companyAccountId}</dd></div><div><dt>Kaynak talep</dt><dd className={styles.mono}>{order.sourceRequestId ?? 'Bağlı değil'}</dd></div><div><dt>İş tarihi</dt><dd>{order.effectiveOn}</dd></div></dl><section><h2>Taahhüt satırları</h2><div className={styles.tableWrap}><table><thead><tr><th>No</th><th>Ürün</th><th>Birim</th><th>Sipariş</th><th>Teknik</th><th>Taahhüt</th><th>Kabul</th><th>Kalan</th></tr></thead><tbody>{lines.map((line) => <tr key={line.lineId}><td>{line.lineNo}</td><td className={styles.mono}>…{line.productId.slice(-8)}</td><td className={styles.mono}>…{line.unitId.slice(-8)}</td><td>{line.ordered}</td><td>{line.technical}</td><td>{line.committed}</td><td>{line.accepted}</td><td>{line.remaining}</td></tr>)}</tbody></table></div></section>{canApprove && order.status === 'Draft' ? <section className={styles.actionCard}><h2>Siparişi onayla</h2><p>Onay, talep satırı paylarını sunucuda yeniden ölçer. Fiyat veya stok hareketi üretmez.</p>{outcome === null ? null : outcome.type === 'applied' ? <p role="status" className={styles.success}>{outcome.replayed ? 'Önceki onay yanıtı yeniden gösterildi.' : 'Sipariş onaylandı ve yeniden okundu.'}</p> : outcome.type === 'failed' ? <p role="alert" className={styles.error}>Onay kaydedilmedi ({outcome.code ?? outcome.status}). Kayıt yeniden okundu.</p> : <p role="alert" className={styles.warning}>Sonuç bilinmiyor. Yeni işlem başlatmadan aynı anahtarla yeniden gönderin.</p>}<button type="button" disabled={sending} onClick={() => void sendApproval()}>{sending ? 'Gönderiliyor…' : outcome?.type === 'unknown' ? 'Aynı onayı yeniden gönder' : 'Onayla'}</button></section> : null}</>)
}
