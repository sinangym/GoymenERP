import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useLocation, useParams } from 'react-router'
import { fetchPurchaseRequest } from './api'
import { purchaseOrderDetailPath, PURCHASING_CAPABILITY } from './capabilities'
import { submitPurchaseRequest, type RequestSubmissionOutcome } from './requestSubmissionApi'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { pendingPurchaseRequestSubmissionKey, recallPendingPurchaseRequestSubmission, releasePendingPurchaseRequestSubmission, reservePendingPurchaseRequestSubmission, type PendingPurchaseRequestSubmission } from './pendingRequestStore'
import styles from './purchasing.module.css'

/** Navigation hint only: backend authorization still owns both detail reads. */
function sourceOrderId(value: unknown, namespace: string | null, requestId: string | undefined): string | null {
  if (namespace === null || requestId === undefined || value === null || typeof value !== 'object' || Array.isArray(value)) return null
  const fields = Object.keys(value)
  if (fields.length !== 3 || !['ns', 'requestId', 'orderId'].every((field) => Object.prototype.hasOwnProperty.call(value, field))) return null
  const origin = value as Record<string, unknown>
  if (origin.ns !== namespace || origin.requestId !== requestId || typeof origin.orderId !== 'string') return null
  // Only a canonical non-empty Guid, never an arbitrary URL, query or returnTo.
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(origin.orderId)
    || origin.orderId === '00000000-0000-0000-0000-000000000000') return null
  return origin.orderId
}

export function PurchaseRequestDetailPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { requestId } = useParams()
  const location = useLocation()
  // Keep the entry generation outside the keyed detail. A stale detail remount
  // must not re-authorize a return hint left behind by an earlier entry.
  const [entry, setEntry] = useState(() => ({
    key: location.key, state: location.state as unknown, requestId, generation: shell.routeEntryGeneration,
  }))
  const sameEntry = entry.key === location.key && entry.state === location.state && entry.requestId === requestId
  if (!sameEntry) {
    setEntry({ key: location.key, state: location.state as unknown, requestId, generation: shell.routeEntryGeneration })
  }
  const returnOrderId = sameEntry && entry.generation === shell.routeEntryGeneration
    ? sourceOrderId(location.state as unknown, namespace, requestId)
    : null
  return <PurchaseRequestDetail key={`${namespace ?? 'no-context'}::${requestId ?? ''}::${shell.routeEntryGeneration}`} namespace={namespace} returnOrderId={returnOrderId} />
}

function PurchaseRequestDetail({ namespace, returnOrderId }: { readonly namespace: string | null; readonly returnOrderId: string | null }) {
  const shell = useShellState(); const boundary = useAuthBoundary(); const { requestId } = useParams()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const canSubmit = hasCapability(shell, PURCHASING_CAPABILITY.requestSubmit as never)
  const storeKey = pendingPurchaseRequestSubmissionKey(shell.sessionEpoch, actorId, companyId, requestId ?? '')
  const recalled = recallPendingPurchaseRequestSubmission(storeKey)
  const [attempt, setAttempt] = useState<
    | { readonly phase: 'idle' }
    | { readonly phase: 'submitting'; readonly snapshot: PendingPurchaseRequestSubmission }
    | { readonly phase: 'unknown'; readonly snapshot: PendingPurchaseRequestSubmission; readonly outcome: Extract<RequestSubmissionOutcome, { type: 'unknown' }> }
    | { readonly phase: 'applied'; readonly replayed: boolean }
    | { readonly phase: 'failed'; readonly status: number; readonly code: string | null }
  >(() => recalled === null ? { phase: 'idle' } : { phase: 'unknown', snapshot: recalled, outcome: { type: 'unknown', reason: 'network-error' } })
  const inFlight = useRef(false); const responseGeneration = useRef(0)
  useEffect(() => { inFlight.current = false; return () => { responseGeneration.current += 1 } }, [namespace, shell.routeEntryGeneration])
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'purchase-request', requestId ?? ''],
    enabled: namespace !== null && companyId !== null && (requestId ?? '').length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture(); const result = await fetchPurchaseRequest(companyId ?? '', requestId ?? '', signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
      return result
    },
  })
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<import('./contract').PurchaseRequestDetail>
  const send = useCallback(async (retry?: PendingPurchaseRequestSubmission) => {
    if (state.kind !== 'ready' || companyId === null || actorId === null || requestId === undefined || inFlight.current) return
    const snapshot = retry ?? { actorId, companyId, requestId, expectedVersion: state.data.request.version, idempotencyKey: globalThis.crypto.randomUUID() }
    if (snapshot.actorId !== actorId || snapshot.companyId !== companyId || snapshot.requestId !== requestId) return
    inFlight.current = true; const generation = ++responseGeneration.current; reservePendingPurchaseRequestSubmission(storeKey, snapshot); setAttempt({ phase: 'submitting', snapshot })
    const security = boundary.capture(); const result = await submitPurchaseRequest(companyId, requestId, snapshot.expectedVersion, snapshot.idempotencyKey)
    if (responseGeneration.current !== generation) return
    inFlight.current = false
    if (result.type === 'failed' && (result.status === 401 || result.status === 403)) boundary.report(result.status, security)
    if (result.type === 'unknown') { setAttempt({ phase: 'unknown', snapshot, outcome: result }); return }
    releasePendingPurchaseRequestSubmission(storeKey, snapshot.idempotencyKey)
    setAttempt(result.type === 'applied' ? { phase: 'applied', replayed: result.replayed } : { phase: 'failed', status: result.status, code: result.code })
    if (result.type === 'applied' || result.status === 409) await query.refetch()
  }, [actorId, boundary, companyId, query, requestId, state, storeKey])

  const frame = (child: ReactNode) => <main className={styles.page}><header><p className={styles.eyebrow}>P-01-DETAIL</p><h1>Satın alma talebi</h1></header>{child}</main>
  if (companyId === null) return frame(<StateBlock state={{ kind: 'no-authorized-company' }} subject="satın alma talebi" />)
  if (state.kind !== 'ready') return frame(<StateBlock state={state} subject="satın alma talebi" onRetry={() => void query.refetch()} />)
  const { request, lines } = state.data; const unresolved = attempt.phase === 'unknown' || attempt.phase === 'submitting'
  const notice = attempt.phase === 'applied' ? <p role="status" className={styles.success}>{attempt.replayed ? 'Önceki gönderim yanıtı yeniden gösterildi.' : 'Talep gönderildi ve yeniden okundu.'}</p> : attempt.phase === 'failed' ? <p role="alert" className={styles.error}>Talep gönderilmedi ({attempt.code ?? attempt.status}). Kayıt yeniden okundu.</p> : null
  const showOrderReturn = returnOrderId !== null && namespace !== null && request.id === requestId && request.companyId === companyId
    && hasCapability(shell, PURCHASING_CAPABILITY.requestDetail) && hasCapability(shell, PURCHASING_CAPABILITY.orderDetail)
  return frame(<>{showOrderReturn && returnOrderId !== null ? <nav aria-label="Siparişe geri dönüş"><Link to={purchaseOrderDetailPath(returnOrderId)} state={null}>Kaynak siparişe dön</Link></nav> : null}<dl className={styles.facts}><div><dt>Talep</dt><dd className={styles.mono}>{request.id}</dd></div><div><dt>Durum</dt><dd><span className={request.status === 'Submitted' ? styles.approved : styles.draft}>{request.status === 'Submitted' ? 'Gönderildi' : 'Taslak'}</span></dd></div><div><dt>Sürüm</dt><dd>{request.version}</dd></div><div><dt>İş tarihi</dt><dd>{request.effectiveOn}</dd></div></dl><section><h2>İhtiyaç satırları</h2><div className={styles.tableWrap}><table><thead><tr><th>No</th><th>Ürün</th><th>Birim</th><th>Miktar</th></tr></thead><tbody>{lines.map((line) => <tr key={line.lineId}><td>{line.lineNo}</td><td className={styles.mono}>…{line.productId.slice(-8)}</td><td className={styles.mono}>…{line.unitId.slice(-8)}</td><td>{line.quantity}</td></tr>)}</tbody></table></div></section>{canSubmit ? notice : null}{canSubmit && (request.status === 'Draft' || unresolved) ? <section className={styles.actionCard}><h2>Talebi ihtiyaç havuzuna gönder</h2><p>Gönderim yalnız talep durumunu değiştirir; sipariş, fiyat veya stok hareketi üretmez.</p>{attempt.phase === 'unknown' ? <p role="alert" className={styles.warning}>Sonuç bilinmiyor. Yeni işlem başlatmadan aynı anahtar ve sürümle yeniden gönderin.</p> : null}<button type="button" disabled={attempt.phase === 'submitting'} onClick={() => void send(attempt.phase === 'unknown' ? attempt.snapshot : undefined)}>{attempt.phase === 'submitting' ? 'Gönderiliyor…' : attempt.phase === 'unknown' ? 'Aynı gönderimi yeniden dene' : 'Gönder'}</button></section> : null}</>)
}
